using System.Numerics;
using Content.Shared._NW.Tents;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._NW.Tents;

public sealed class NWTentSystem : EntitySystem
{
    private const string KitContainer = "nw-tent-kit";
    private const string TentContainer = "nw-packed-tent";
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NWTentKitComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<NWTentKitComponent, NWTentDeployDoAfterEvent>(OnDeploy);
        SubscribeLocalEvent<NWTentDeployedComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<NWTentDeployedComponent, NWTentPackDoAfterEvent>(OnPack);
        SubscribeLocalEvent<NWTentDeployedComponent, ComponentShutdown>(OnTentShutdown);
    }

    private void OnTentShutdown(Entity<NWTentDeployedComponent> ent, ref ComponentShutdown args)
    {
        // A destroyed shelter must not release a reusable kit that generates a fresh tent.
        if (MetaData(ent).EntityLifeStage >= EntityLifeStage.Terminating && Exists(ent.Comp.Kit))
            QueueDel(ent.Comp.Kit);
    }

    private void Notice(EntityUid user, string message) => _popup.PopupEntity(Loc.GetString(message), user, user);

    private void OnUse(Entity<NWTentKitComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled) return;
        args.Handled = true;
        if (!TryPlacement(ent, args.User, out _)) return;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
            TimeSpan.FromSeconds(ent.Comp.DeployTime), new NWTentDeployDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true, NeedHand = true
        });
    }

    private static Box2 Translate(Box2 b, Vector2 p) => new(b.Left + p.X, b.Bottom + p.Y, b.Right + p.X, b.Top + p.Y);

    private bool TryPlacement(Entity<NWTentKitComponent> ent, EntityUid user, out MapCoordinates location)
    {
        location = default;
        var xform = Transform(user);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
        { Notice(user, "nw-tent-ground"); return false; }
        var tile = _map.WorldToTile(gridUid, grid, _transform.GetWorldPosition(user));
        var center = _map.GridTileToWorldPos(gridUid, grid, tile) + new Vector2(0, 3);
        var area = Translate(ent.Comp.Footprint, center);
        // Check the full footprint, not only the tile under the deploying player.
        for (var x = area.Left + 0.05f; x < area.Right; x += 0.5f)
        for (var y = area.Bottom + 0.05f; y < area.Top; y += 0.5f)
        {
            var indices = _map.WorldToTile(gridUid, grid, new Vector2(x, y));
            if (!_map.TryGetTileRef(gridUid, grid, indices, out var floor) || floor.Tile.IsEmpty)
            { Notice(user, "nw-tent-ground"); return false; }
        }
        foreach (var other in _lookup.GetEntitiesIntersecting(xform.MapID, area))
        {
            if (other == ent.Owner || other == user || other == gridUid || other == xform.MapUid
                || HasComp<GhostComponent>(other) || _containers.IsEntityInContainer(other)) continue;
            if (HasComp<NWTentComponent>(other) || HasComp<MobStateComponent>(other)
                || (TryComp<PhysicsComponent>(other, out var body) && body.CanCollide && body.Hard))
            { Notice(user, "nw-tent-blocked"); return false; }
        }
        location = new MapCoordinates(center, xform.MapID);
        return true;
    }

    private void OnDeploy(Entity<NWTentKitComponent> ent, ref NWTentDeployDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled) return;
        args.Handled = true;
        // Another completed deployment must never produce a second tent from this kit.
        if (_containers.TryGetContainingContainer(ent.Owner, out var ownerContainer)
            && ownerContainer.ID == KitContainer) return;
        if (!TryPlacement(ent, args.Args.User, out var location)) return;

        EntityUid tent;
        var reused = false;
        if (_containers.TryGetContainer(ent.Owner, TentContainer, out var packed) && packed.ContainedEntities.Count > 0)
        {
            tent = packed.ContainedEntities[0];
            if (!_containers.Remove(tent, packed)) return;
            _transform.SetMapCoordinates(tent, location);
            reused = true;
        }
        else
            tent = Spawn(ent.Comp.Tent, location);

        _transform.SetWorldRotation(tent, Angle.Zero);
        var storage = _containers.EnsureContainer<Container>(tent, KitContainer);
        if (!_containers.Insert(ent.Owner, storage))
        {
            if (reused)
                _containers.Insert(tent, _containers.EnsureContainer<Container>(ent.Owner, TentContainer));
            else QueueDel(tent);
            Notice(args.Args.User, "nw-tent-failed");
            return;
        }
        EnsureComp<NWTentDeployedComponent>(tent).Kit = ent.Owner;
        Notice(args.Args.User, "nw-tent-deployed");
    }

    private void OnVerbs(Entity<NWTentDeployedComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null) return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("nw-tent-pack"),
            Act = () => StartPack(ent, user)
        });
    }

    private bool CanPack(Entity<NWTentDeployedComponent> ent, EntityUid user)
    {
        if (!Exists(ent.Comp.Kit) || !TryComp<NWTentComponent>(ent, out var tent)
            || !_containers.TryGetContainer(ent.Owner, KitContainer, out var storage)
            || !storage.Contains(ent.Comp.Kit)) return false;
        var area = Translate(tent.Footprint, _transform.GetWorldPosition(ent));
        foreach (var other in _lookup.GetEntitiesIntersecting(Transform(ent).MapID, area))
        {
            if (other == user || HasComp<GhostComponent>(other)) continue;
            if (HasComp<MobStateComponent>(other))
            { Notice(user, "nw-tent-occupied"); return false; }
        }
        return true;
    }

    private void StartPack(Entity<NWTentDeployedComponent> ent, EntityUid user)
    {
        if (!CanPack(ent, user)) return;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user,
            TimeSpan.FromSeconds(Comp<NWTentComponent>(ent).PackTime), new NWTentPackDoAfterEvent(), ent.Owner, target: ent.Owner)
        {
            BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true, NeedHand = true
        });
    }

    private void OnPack(Entity<NWTentDeployedComponent> ent, ref NWTentPackDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled) return;
        args.Handled = true;
        if (!CanPack(ent, args.Args.User)) return;
        var kit = ent.Comp.Kit;
        if (!_containers.TryGetContainer(ent.Owner, KitContainer, out var storage)
            || !_containers.Remove(kit, storage)) return;
        _transform.SetMapCoordinates(kit, _transform.GetMapCoordinates(args.Args.User));
        var packed = _containers.EnsureContainer<Container>(kit, TentContainer);
        if (!_containers.Insert(ent.Owner, packed))
        {
            _containers.Insert(kit, storage);
            Notice(args.Args.User, "nw-tent-failed");
            return;
        }
        Notice(args.Args.User, "nw-tent-packed");
    }
}
