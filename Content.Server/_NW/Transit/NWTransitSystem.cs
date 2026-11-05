using System.Numerics;
using Content.Shared._NW.Transit;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._NW.Transit;

public sealed partial class NWTransitSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeElevators();
        SubscribeLocalEvent<NWTransitLinkComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<NWTransitLinkComponent, NWClimbLinkEvent>(OnClimb);
    }

    // Resolve on use: map edits, unloading and replacement require no cache invalidation.
    private EntityUid? Partner(Entity<NWTransitLinkComponent> source, EntityUid user)
    {
        var id = source.Comp.Id?.Trim();
        if (string.IsNullOrEmpty(id))
            return Fail("nw-transit-unconfigured", source.Owner, user);

        var map = Transform(source.Owner).MapUid;
        if (map == null)
            return Fail("nw-transit-missing", source.Owner, user);
        EntityUid? match = null;
        var query = EntityQueryEnumerator<NWTransitLinkComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var link, out var xform))
        {
            if (IsElevatorStop(uid) || uid == source.Owner || TerminatingOrDeleted(uid) || xform.MapUid == null ||
                link.SameMapOnly != source.Comp.SameMapOnly ||
                (link.SameMapOnly && xform.MapUid != map) ||
                !string.Equals(link.Id?.Trim(), id, StringComparison.Ordinal))
                continue;

            if (match != null)
                return Fail("nw-transit-ambiguous", source.Owner, user);
            match = uid;
        }
        return match ?? Fail("nw-transit-missing", source.Owner, user);
    }

    private EntityUid? Fail(string key, EntityUid source, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString(key), source, user);
        return null;
    }

    private bool CanTravel(EntityUid source, EntityUid user)
    {
        if (_mob.IsIncapacitated(user) || !_interaction.InRangeUnobstructed(user, source))
            return false;
        if (!TryComp<BuckleComponent>(user, out var buckle) || buckle.BuckledTo == null)
            return true;
        _popup.PopupEntity(Loc.GetString("nw-transit-unbuckle"), source, user);
        return false;
    }

    private void OnActivate(Entity<NWTransitLinkComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (!CanTravel(ent.Owner, args.User))
            return;
        if (IsElevatorStop(ent.Owner))
        {
            ShowFloors(ent.Owner, args.User);
            return;
        }
        if (Partner(ent, args.User) is not { } target)
            return;
        if (ent.Comp.Delay <= 0)
        {
            Travel(args.User, target);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay,
            new NWClimbLinkEvent(GetNetEntity(target)), ent.Owner, target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BlockDuplicate = true,
        });
    }

    private void OnClimb(Entity<NWTransitLinkComponent> ent, ref NWClimbLinkEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;
        args.Handled = true;
        if (CanTravel(ent.Owner, args.User) && Partner(ent, args.User) is { } target &&
            GetNetEntity(target) == args.Destination)
            Travel(args.User, target);
    }

    private bool CanArrive(EntityUid user, EntityUid target, MapCoordinates destination)
    {
        // Endpoints are mapped on a grid. Fail closed at its edge and on empty tiles.
        if (Transform(target).GridUid is not { } grid ||
            !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;
        var tile = _maps.WorldToTile(grid, gridComp, destination.Position);
        if (!_maps.TryGetTileRef(grid, gridComp, tile, out var floor) || floor.Tile.IsEmpty)
            return false;

        var bounds = _lookup.GetWorldAABB(user);
        var origin = _transform.GetMapCoordinates(user).Position;
        var extent = new Vector2(0.25f);
        var area = new Box2(destination.Position + Vector2.Min(bounds.BottomLeft - origin, -extent),
            destination.Position + Vector2.Max(bounds.TopRight - origin, extent));
        foreach (var other in _lookup.GetEntitiesIntersecting(destination.MapId, area))
        {
            if (other == user || HasComp<MapGridComponent>(other) ||
                !TryComp<PhysicsComponent>(other, out var body) || !body.CanCollide ||
                !TryComp<FixturesComponent>(other, out var fixtures))
                continue;
            if (fixtures.Fixtures.Values.Any(f => f.Hard &&
                    (f.CollisionLayer & (int) CollisionGroup.MobMask) != 0))
                return false;
        }
        return true;
    }

    private void Travel(EntityUid user, EntityUid target)
    {
        var destination = new EntityCoordinates(target, Comp<NWTransitLinkComponent>(target).ArrivalOffset);
        var arrival = _transform.ToMapCoordinates(destination);
        if (!CanArrive(user, target, arrival))
        {
            _popup.PopupEntity(Loc.GetString("nw-transit-blocked-arrival"), user, user);
            return;
        }
        if (_pulling.TryGetPulledEntity(user, out var cargo) && !_pulling.TryStopPull(cargo.Value, user: user))
            return;
        if (_pulling.IsPulled(user) && !_pulling.TryStopPull(user, user: user))
            return;
        _transform.SetMapCoordinates(user, arrival);
        if (TryComp<PhysicsComponent>(user, out var body))
            _physics.SetLinearVelocity(user, Vector2.Zero, body: body);
    }
}
