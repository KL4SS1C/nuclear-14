using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Verbs;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    [Dependency] private readonly MapSystem _cabinMaps = default!;
    [Dependency] private readonly SharedGunSystem _cabinGuns = default!;
    [Dependency] private readonly MapLoaderSystem _cabinLoader = default!;
    [Dependency] private readonly ViewSubscriberSystem _cabinViews = default!;
    [Dependency] private readonly PullingSystem _cabinPulling = default!;
    [Dependency] private readonly SharedHandsSystem _cabinHands = default!;

    private void InitializeCabins()
    {
        // Robust requires identical ordering for every subscription to the same event in this system.
        // Cabin activation runs before gun activation so boarding the M60 does not operate its bolt.
        SubscribeLocalEvent<NWVehicleCabinComponent, ActivateInWorldEvent>(OnEnterCabin, before: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<NWVehicleCabinComponent, GetVerbsEvent<InteractionVerb>>(OnCabinEnterVerb);
        SubscribeLocalEvent<NWVehicleCabinComponent, GetVerbsEvent<Verb>>(OnGhostCabinEnterVerb);
        SubscribeLocalEvent<NWVehicleCabinExitComponent, GetVerbsEvent<Verb>>(OnGhostCabinExitVerb);
        SubscribeLocalEvent<NWVehicleCabinComponent, ComponentShutdown>(OnCabinShutdown);
        SubscribeLocalEvent<NWVehicleCabinExitComponent, ActivateInWorldEvent>(OnLeaveCabin, before: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<NWVehicleCabinViewportComponent, ActivateInWorldEvent>(OnCabinViewport, before: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<NWVehicleCabinAmmoFeedComponent, ActivateInWorldEvent>(OnCabinAmmoEject, before: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<NWVehicleCabinAmmoFeedComponent, InteractUsingEvent>(OnCabinAmmoInsert);
        SubscribeLocalEvent<NWVehicleCabinSeatComponent, StrapAttemptEvent>(OnCabinStrapAttempt);
        SubscribeLocalEvent<NWVehicleCabinSeatComponent, StrappedEvent>(OnCabinStrapped);
        SubscribeLocalEvent<NWVehicleCabinSeatComponent, UnstrappedEvent>(OnCabinUnstrapped);
        SubscribeLocalEvent<NWVehicleCabinViewerComponent, NWVehicleCabinViewActionEvent>(OnCabinViewAction);
        SubscribeLocalEvent<NWVehicleCabinViewerComponent, NWVehicleIgnitionActionEvent>(OnIgnitionAction);
        SubscribeLocalEvent<NWVehicleCabinViewerComponent, NWVehicleCabinGearActionEvent>(OnCabinGearAction);
        SubscribeLocalEvent<NWVehicleCabinViewerComponent, NWVehicleCabinLightsActionEvent>(OnCabinLightsAction);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnCabinPlayerAttached);
    }

    private bool CabinExterior(EntityUid inside, out EntityUid exterior)
    {
        exterior = default;
        if (Transform(inside).MapUid is not { } map ||
            !TryComp<NWVehicleCabinLinkComponent>(map, out var link) ||
            link.Exterior is not { } hull || TerminatingOrDeleted(hull))
            return false;
        exterior = hull;
        return true;
    }

    private bool CabinParked(EntityUid exterior)
    {
        return (!TryComp<NWVehicleComponent>(exterior, out var vehicle) || vehicle.State == NWVehicleState.Parked) &&
               (!TryComp<PhysicsComponent>(exterior, out var body) || body.LinearVelocity.LengthSquared() < 0.25f);
    }

    private void CabinNotice(EntityUid user, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), user, user);
    }

    private bool EnsureCabin(Entity<NWVehicleCabinComponent> ent)
    {
        if (ent.Comp.Map is { } existing && Exists(existing) && ent.Comp.Grid is { } grid && Exists(grid))
        {
            PrepareCabin(ent.Owner, ent.Comp);
            return true;
        }
        if (ent.Comp.EntryPoints.Length == 0 ||
            !_cabinLoader.TryLoadMap(new Robust.Shared.Utility.ResPath(ent.Comp.InteriorPath), out var loadedMap, out var grids))
        {
            Log.Error($"NW cabin failed to load: {ent.Comp.InteriorPath}");
            return false;
        }
        if (loadedMap == null || grids == null || grids.Count != 1)
        {
            if (loadedMap != null) QueueDel(loadedMap.Value.Owner);
            Log.Error($"NW cabin must contain exactly one grid: {ent.Comp.InteriorPath}");
            return false;
        }
        ent.Comp.Map = loadedMap.Value.Owner;
        foreach (var loadedGrid in grids) ent.Comp.Grid = loadedGrid.Owner;
        PrepareCabin(ent.Owner, ent.Comp);
        return true;
    }

    private void PrepareCabin(EntityUid exterior, NWVehicleCabinComponent cabin)
    {
        if (cabin.Map is not { } map || !TryComp<MapComponent>(map, out var mapComponent)) return;
        // Queries skip paused entities. Initialize/unpause BEFORE resolving the interior seats.
        if (!_cabinMaps.IsInitialized(mapComponent.MapId))
            _cabinMaps.InitializeMap(mapComponent.MapId);
        _cabinMaps.SetPaused(mapComponent.MapId, false);
        BindCabin(exterior, cabin);

        // Loaded prototype entities can have their unchanged component ticks cleared by the loader.
        // Include metadata when these runtime-created entities first enter a client's PVS.
        Dirty(map, MetaData(map));
        var query = EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var xform, out var metadata))
        {
            if (xform.MapUid == map) Dirty(uid, metadata);
        }
    }

    private void BindCabin(EntityUid exterior, NWVehicleCabinComponent cabin)
    {
        if (cabin.Map is not { } map) return;
        var link = EnsureComp<NWVehicleCabinLinkComponent>(map);
        link.Exterior = exterior;
        Dirty(map, link);
        var seats = EntityQueryEnumerator<NWVehicleCabinSeatComponent, TransformComponent>();
        while (seats.MoveNext(out var uid, out var seat, out var xform))
        {
            if (xform.MapUid != map) continue;
            if (seat.SeatIndex == 0 && TryComp<NWVehicleComponent>(exterior, out var vehicle))
                EnsureVehicleKey((exterior, vehicle), xform.Coordinates);
            seat.Exterior = exterior;
            Dirty(uid, seat);
        }
    }

    private bool GhostNearCabinDoor(EntityUid user, EntityUid door)
    {
        if (!HasComp<GhostComponent>(user)) return false;
        var a = _transform.GetMapCoordinates(user);
        var b = _transform.GetMapCoordinates(door);
        return a.MapId == b.MapId && Vector2.DistanceSquared(a.Position, b.Position) <= 9f;
    }

    private void OnGhostCabinEnterVerb(Entity<NWVehicleCabinComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!GhostNearCabinDoor(args.User, ent.Owner)) return;
        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("nw-cabin-ghost-enter"),
            Act = () =>
            {
                if (!GhostNearCabinDoor(user, ent.Owner)) return;
                var activation = new ActivateInWorldEvent(user, ent.Owner, true);
                OnEnterCabin(ent, ref activation);
            }
        });
    }

    private void OnGhostCabinExitVerb(Entity<NWVehicleCabinExitComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!GhostNearCabinDoor(args.User, ent.Owner)) return;
        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("nw-cabin-ghost-exit"),
            Act = () =>
            {
                if (!GhostNearCabinDoor(user, ent.Owner)) return;
                var activation = new ActivateInWorldEvent(user, ent.Owner, true);
                OnLeaveCabin(ent, ref activation);
            }
        });
    }

    private void OnCabinEnterVerb(Entity<NWVehicleCabinComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess) return;
        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("nw-cabin-enter"),
            Act = () =>
            {
                var activation = new ActivateInWorldEvent(user, ent.Owner, true);
                OnEnterCabin(ent, ref activation);
            }
        });
    }

    private void OnEnterCabin(Entity<NWVehicleCabinComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || _mobState.IsIncapacitated(args.User)) return;
        args.Handled = true;
        if (!HasComp<GhostComponent>(args.User) && !CabinParked(ent.Owner)) { CabinNotice(args.User, "nw-cabin-stop-first"); return; }
        if (TryComp<BuckleComponent>(args.User, out var buckle) && buckle.BuckledTo != null)
        { CabinNotice(args.User, "nw-cabin-unbuckle-first"); return; }
        // Never put a vehicle inside another vehicle or create cyclic interiors.
        if (_cabinPulling.TryGetPulledEntity(args.User, out var pulled) && HasComp<NWVehicleCabinComponent>(pulled))
        { CabinNotice(args.User, "nw-cabin-no-nesting"); return; }
        if (!EnsureCabin(ent)) { CabinNotice(args.User, "nw-cabin-load-failed"); return; }
        var relative = _transform.GetMapCoordinates(args.User).Position - _transform.GetMapCoordinates(ent.Owner).Position;
        var heading = _transform.GetWorldRotation(ent.Owner).GetCardinalDir().ToIntVec();
        var forward = new Vector2(heading.X, heading.Y);
        var left = Vector2.Dot(relative, new Vector2(-forward.Y, forward.X)) >= 0f;
        // RMC humvee: lower rear/right (0), lower front/right (1), upper rear/left (2), upper front/left (3).
        var index = ent.Comp.EntryPoints.Length == 4
            ? (left ? 2 : 0) + (Vector2.Dot(relative, forward) >= 0f ? 1 : 0)
            : Vector2.Dot(relative, forward) < -0.6f ? 2 : left ? 0 : 1;
        index = Math.Clamp(index, 0, ent.Comp.EntryPoints.Length - 1);
        MoveThroughCabinDoor(args.User, new EntityCoordinates(ent.Comp.Grid!.Value, ent.Comp.EntryPoints[index]));
    }

    private void OnLeaveCabin(Entity<NWVehicleCabinExitComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || _mobState.IsIncapacitated(args.User) || !CabinExterior(ent, out var exterior)) return;
        args.Handled = true;
        if (!HasComp<GhostComponent>(args.User) && !CabinParked(exterior)) { CabinNotice(args.User, "nw-cabin-stop-first"); return; }
        if (TryComp<BuckleComponent>(args.User, out var buckle) && buckle.BuckledTo != null)
        { CabinNotice(args.User, "nw-cabin-unbuckle-first"); return; }
        if (HasComp<GhostComponent>(args.User))
        {
            ClearCabinViewer(args.User);
            MoveThroughCabinDoor(args.User, Transform(exterior).Coordinates);
            return;
        }
        var cabin = Comp<NWVehicleCabinComponent>(exterior);
        var seat = cabin.EntryPoints.Length == 4 ? ent.Comp.EntryIndex switch { 0 => 3, 1 => 1, 2 => 2, _ => 0 } : ent.Comp.EntryIndex;
        if (!TryGetExit(exterior, args.User, seat, out var exit))
        { CabinNotice(args.User, "nw-vehicle-exit-blocked"); return; }
        // Use the larger of the person/crate footprints when selecting a doorway.
        if (_cabinPulling.TryGetPulledEntity(args.User, out var pulled) && !TryGetExit(exterior, pulled.Value, seat, out exit))
        { CabinNotice(args.User, "nw-vehicle-exit-blocked"); return; }
        ClearCabinViewer(args.User);
        MoveThroughCabinDoor(args.User, exit);
    }

    private void MoveThroughCabinDoor(EntityUid user, EntityCoordinates destination)
    {
        _cabinPulling.TryGetPulledEntity(user, out var pulled);
        if (pulled is { } item && !_cabinPulling.TryStopPull(item, user: user)) return;
        if (_cabinPulling.IsPulled(user) && !_cabinPulling.TryStopPull(user, user: user)) return;
        var destinationMap = _transform.ToMapCoordinates(destination);
        ClearCabinViewer(user);
        if (TryComp<EyeComponent>(user, out var eye))
        {
            _eye.SetTarget(user, null, eye);
            _eye.SetOffset(user, Vector2.Zero, eye);
        }
        // Follow RMC's map-space transfer, resolving the destination grid through the engine.
        _transform.SetMapCoordinates(user, destinationMap);
        if (TryComp<PhysicsComponent>(user, out var body)) _physics.SetLinearVelocity(user, Vector2.Zero, body: body);
        if (pulled is not { } cargo) return;
        _transform.SetMapCoordinates(cargo, destinationMap);
        if (TryComp<PhysicsComponent>(cargo, out var cargoBody)) _physics.SetLinearVelocity(cargo, Vector2.Zero, body: cargoBody);
        _cabinPulling.TryStartPull(user, cargo);
    }

    private void OnCabinStrapAttempt(Entity<NWVehicleCabinSeatComponent> ent, ref StrapAttemptEvent args)
    {
        if (ent.Comp.Exterior is not { } hull) { args.Cancelled = true; return; }
        if (!TryComp<NWVehicleComponent>(hull, out var vehicle)) return; // Wreck seats are ordinary seats.
        if (!IsValidSeat(vehicle, ent.Comp.SeatIndex) || vehicle.SeatOccupants[ent.Comp.SeatIndex] != null)
            args.Cancelled = true;
    }

    private void OnCabinStrapped(Entity<NWVehicleCabinSeatComponent> ent, ref StrappedEvent args)
    {
        if (ent.Comp.Exterior is not { } hull || !TryComp<NWVehicleComponent>(hull, out var vehicle) ||
            !IsValidSeat(vehicle, ent.Comp.SeatIndex)) return;
        var user = args.Buckle.Owner;
        vehicle.SeatOccupants[ent.Comp.SeatIndex] = user;
        if (ent.Comp.SeatIndex == 0)
        {
            vehicle.Pilot = user;
            AddPilotActions(user, (hull, vehicle));
        }
        if (ent.Comp.SeatIndex == vehicle.GunnerSeat) vehicle.Gunner = user;
        var viewer = SetupCabinViewer(user, hull);
        if (ent.Comp.SeatIndex == 0)
        {
            _actions.AddAction(user, ref viewer.GearAction, "ActionNWVehicleCabinGear", user);
            _actions.AddAction(user, ref viewer.IgnitionAction, "ActionNWVehicleIgnition", user);
        }
        if (ent.Comp.SeatIndex is 0 or 1 && vehicle.HasOverlights)
            _actions.AddAction(user, ref viewer.LightsAction, "ActionNWVehicleCabinLights", user);
        SetCabinView(user, viewer, ent.Comp.SeatIndex == 0 || ent.Comp.SeatIndex == vehicle.GunnerSeat);
        Dirty(hull, vehicle);
        UpdateUi((hull, vehicle));
    }

    private void OnCabinUnstrapped(Entity<NWVehicleCabinSeatComponent> ent, ref UnstrappedEvent args)
    {
        var user = args.Buckle.Owner;
        ClearCabinViewer(user);
        if (ent.Comp.Exterior is not { } hull || !TryComp<NWVehicleComponent>(hull, out var vehicle)) return;
        if (IsValidSeat(vehicle, ent.Comp.SeatIndex) && vehicle.SeatOccupants[ent.Comp.SeatIndex] == user)
            vehicle.SeatOccupants[ent.Comp.SeatIndex] = null;
        if (vehicle.Gunner == user) vehicle.Gunner = null;
        if (vehicle.Pilot == user)
        {
            RemovePilotAction(user, vehicle);
            vehicle.Pilot = null;
            if (!Terminating(hull)) StopGroundEngine((hull, vehicle));
        }
        Dirty(hull, vehicle);
        // Native unbuckling keeps the body next to the INTERIOR chair. Only doors exit the cabin.
        if (!Terminating(hull)) UpdateUi((hull, vehicle));
    }

    private NWVehicleCabinViewerComponent SetupCabinViewer(EntityUid user, EntityUid exterior)
    {
        var viewer = EnsureComp<NWVehicleCabinViewerComponent>(user);
        viewer.Exterior = exterior;
        RefreshDashboard(user, viewer, exterior);
        _actions.AddAction(user, ref viewer.ViewAction, "ActionNWVehicleCabinView", user);
        if (TryComp<ActorComponent>(user, out var actor))
        {
            _cabinViews.AddViewSubscriber(exterior, actor.PlayerSession);
            _cabinViews.AddViewSubscriber(user, actor.PlayerSession);
        }
        Dirty(user, viewer);
        return viewer;
    }

    private void SetCabinView(EntityUid user, NWVehicleCabinViewerComponent viewer, bool outside)
    {
        viewer.Outside = outside;
        if (!outside && viewer.Exterior is { } hull && TryComp<NWVehicleComponent>(hull, out var vehicle) && vehicle.Pilot == user)
            vehicle.HeldInputs = NWVehicleControlInput.None;
        if (TryComp<EyeComponent>(user, out var eye))
        {
            _eye.SetTarget(user, outside ? viewer.Exterior : null, eye);
            _eye.SetOffset(user, Vector2.Zero, eye);
        }
        Dirty(user, viewer);
    }

    private void ClearCabinViewer(EntityUid user)
    {
        if (!TryComp<NWVehicleCabinViewerComponent>(user, out var viewer)) return;
        SetCabinView(user, viewer, false);
        if (viewer.Exterior is { } exterior && Exists(exterior) && TryComp<ActorComponent>(user, out var actor))
        {
            _cabinViews.RemoveViewSubscriber(exterior, actor.PlayerSession);
            _cabinViews.RemoveViewSubscriber(user, actor.PlayerSession);
        }
        _actions.RemoveAction(user, viewer.ViewAction);
        _actions.RemoveAction(user, viewer.GearAction);
        _actions.RemoveAction(user, viewer.IgnitionAction);
        _actions.RemoveAction(user, viewer.LightsAction);
        RemComp<NWVehicleCabinViewerComponent>(user);
    }

    private void OnCabinViewport(Entity<NWVehicleCabinViewportComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !CabinExterior(ent, out var hull)) return;
        args.Handled = true;
        var viewer = SetupCabinViewer(args.User, hull);
        SetCabinView(args.User, viewer, !viewer.Outside);
    }

    private void OnCabinViewAction(Entity<NWVehicleCabinViewerComponent> ent, ref NWVehicleCabinViewActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || !CabinExterior(ent, out var hull) || ent.Comp.Exterior != hull) return;
        args.Handled = true;
        SetCabinView(ent, ent.Comp, !ent.Comp.Outside);
        // No unattended driving while the driver's camera is inside.
        if (TryComp<NWVehicleComponent>(hull, out var vehicle) && vehicle.Pilot == ent.Owner)
            vehicle.HeldInputs = NWVehicleControlInput.None;
    }

    private void OnIgnitionAction(Entity<NWVehicleCabinViewerComponent> ent, ref NWVehicleIgnitionActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || _mobState.IsIncapacitated(ent) ||
            !CabinExterior(ent, out var hull) || ent.Comp.Exterior != hull ||
            !TryComp<NWVehicleComponent>(hull, out var vehicle) || vehicle.Pilot != ent.Owner)
            return;

        args.Handled = true;
        if (vehicle.State != NWVehicleState.Parked)
        {
            CabinNotice(ent, "nw-vehicle-key-stop-first");
            return;
        }

        if (InsertedKey(hull) != null)
            ReturnIgnitionKey((hull, vehicle), ent.Owner);
        else
            InsertIgnitionKey((hull, vehicle));
    }

    private void OnCabinGearAction(Entity<NWVehicleCabinViewerComponent> ent, ref NWVehicleCabinGearActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || _mobState.IsIncapacitated(ent) ||
            !CabinExterior(ent, out var hull) || !TryComp<NWVehicleComponent>(hull, out var vehicle) || vehicle.Pilot != ent.Owner) return;
        args.Handled = true;
        vehicle.LowGear = !vehicle.LowGear;
        Dirty(hull, vehicle);
        CabinNotice(ent, vehicle.LowGear ? "nw-cabin-low-gear" : "nw-cabin-high-gear");
    }

    private void OnCabinLightsAction(Entity<NWVehicleCabinViewerComponent> ent, ref NWVehicleCabinLightsActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || _mobState.IsIncapacitated(ent) ||
            !CabinExterior(ent, out var hull) || !TryComp<NWVehicleComponent>(hull, out var vehicle) ||
            !vehicle.HasOverlights || GetSeatIndex(vehicle, ent) is not (0 or 1)) return;
        args.Handled = true;
        vehicle.LightsEnabled = !vehicle.LightsEnabled;
        _vehicleLights.SetEnabled(hull, vehicle.LightsEnabled);
        Dirty(hull, vehicle);
        CabinNotice(ent, vehicle.LightsEnabled ? "nw-cabin-lights-on" : "nw-cabin-lights-off");
    }

    private void OnCabinPlayerAttached(PlayerAttachedEvent args)
    {
        if (!TryComp<NWVehicleCabinViewerComponent>(args.Entity, out var viewer) || viewer.Exterior is not { } hull || !Exists(hull)) return;
        _cabinViews.AddViewSubscriber(hull, args.Player);
        _cabinViews.AddViewSubscriber(args.Entity, args.Player);
        SetCabinView(args.Entity, viewer, viewer.Outside);
    }

    private void OnCabinAmmoInsert(Entity<NWVehicleCabinAmmoFeedComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !CabinExterior(ent, out var hull) || !TryComp<NWVehicleComponent>(hull, out var vehicle) || vehicle.GunnerSeat < 0) return;
        if (!_equipmentSlots.TryGetSlot(hull, "gun_magazine", out var slot) ||
            !_cabinHands.TryGetActiveItem(args.User, out var item) || item != args.Used ||
            !_equipmentSlots.TryInsertFromHand(hull, slot, args.User)) return;
        args.Handled = true;
        // Closing the feed chambers a fresh round, including after the magazine ran dry.
        if (TryComp<ChamberMagazineAmmoProviderComponent>(hull, out var provider))
        {
            _cabinGuns.SetBoltClosed(hull, provider, false, args.User);
            _cabinGuns.SetBoltClosed(hull, provider, true, args.User);
        }
    }

    private void OnCabinAmmoEject(Entity<NWVehicleCabinAmmoFeedComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !CabinExterior(ent, out var hull) || !TryComp<NWVehicleComponent>(hull, out var vehicle) || vehicle.GunnerSeat < 0) return;
        args.Handled = true;
        if (_equipmentSlots.TryEject(hull, "gun_magazine", args.User, out var magazine))
        {
            _transform.SetCoordinates(magazine.Value, Transform(args.User).Coordinates);
            _cabinHands.PickupOrDrop(args.User, magazine.Value);
        }
    }

    private void TransferCabinToWreck(EntityUid exterior, EntityUid wreck)
    {
        if (!TryComp<NWVehicleCabinComponent>(exterior, out var cabin) || cabin.Map is not { } map) return;
        UnseatCabin(map);
        if (cabin.Grid is { } grid && _container.TryGetContainer(exterior, SharedNWVehicleSystem.CargoContainerId, out var cargo))
        {
            foreach (var item in cargo.ContainedEntities.ToArray())
                _container.Remove(item, cargo, destination: new EntityCoordinates(grid, cabin.EntryPoints[0]));
        }
        var wreckCabin = EnsureComp<NWVehicleCabinComponent>(wreck);
        wreckCabin.Map = cabin.Map;
        wreckCabin.Grid = cabin.Grid;
        wreckCabin.EntryPoints = cabin.EntryPoints;
        wreckCabin.InteriorPath = cabin.InteriorPath;
        BindCabin(wreck, wreckCabin);
        cabin.Map = null;
        cabin.Grid = null;
    }

    private void UnseatCabin(EntityUid map)
    {
        var bodies = new List<EntityUid>();
        var query = EntityQueryEnumerator<BuckleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var buckle, out var xform))
            if (xform.MapUid == map && buckle.BuckledTo != null) bodies.Add(uid);
        foreach (var uid in bodies) _buckle.Unbuckle((uid, Comp<BuckleComponent>(uid)), null);
        var viewers = new List<EntityUid>();
        var viewQuery = EntityQueryEnumerator<NWVehicleCabinViewerComponent, TransformComponent>();
        while (viewQuery.MoveNext(out var uid, out _, out var xform))
            if (xform.MapUid == map) viewers.Add(uid);
        foreach (var uid in viewers) ClearCabinViewer(uid);
    }

    private void OnCabinShutdown(Entity<NWVehicleCabinComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Map is not { } map || TerminatingOrDeleted(map)) return;
        // Destruction normally transfers ownership to the wreck first. Admin deletion instead evacuates loose contents.
        UnseatCabin(map);
        var occupants = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
            if (xform.MapUid == map && uid != map && uid != ent.Comp.Grid &&
                (xform.ParentUid == map || xform.ParentUid == ent.Comp.Grid) && !HasComp<NWVehicleCabinFixtureComponent>(uid))
                occupants.Add(uid);
        foreach (var uid in occupants)
        {
            if (TryGetExit(ent.Owner, uid, 0, out var exit) || TryGetExit(ent.Owner, uid, 1, out exit))
                _transform.SetCoordinates(uid, exit);
            else
                _transform.SetCoordinates(uid, Transform(ent.Owner).Coordinates);
        }
        ent.Comp.Map = null;
        ent.Comp.Grid = null;
        QueueDel(map);
    }

    private TimeSpan _nextDashboardUpdate;

    private void RefreshDashboard(EntityUid user, NWVehicleCabinViewerComponent viewer, EntityUid hull)
    {
        if (!TryComp<NWVehicleComponent>(hull, out var vehicle) ||
            !TryComp<NWGroundVehicleComponent>(hull, out var ground))
            return;

        TryGetFuel((hull, vehicle), out var fuel, out var capacity);
        var health = MathF.Max(0f, ground.MaxIntegrity -
            (TryComp<DamageableComponent>(hull, out var damage) ? damage.TotalDamage.Float() : 0f));
        if (viewer.Fuel == fuel.Float() && viewer.FuelCapacity == capacity.Float() &&
            viewer.Integrity == health && viewer.MaxIntegrity == ground.MaxIntegrity)
            return;

        viewer.Fuel = fuel.Float();
        viewer.FuelCapacity = capacity.Float();
        viewer.Integrity = health;
        viewer.MaxIntegrity = ground.MaxIntegrity;
        Dirty(user, viewer);
    }

    private void UpdateCabinViewers()
    {
        var refresh = _timing.CurTime >= _nextDashboardUpdate;
        if (refresh)
            _nextDashboardUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var invalid = new List<EntityUid>();
        var query = EntityQueryEnumerator<NWVehicleCabinViewerComponent>();
        while (query.MoveNext(out var uid, out var viewer))
        {
            if (!CabinExterior(uid, out var hull) || viewer.Exterior != hull || _mobState.IsIncapacitated(uid))
                invalid.Add(uid);
            else if (refresh)
                RefreshDashboard(uid, viewer, hull);
        }
        foreach (var uid in invalid)
        {
            if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.BuckledTo is { } seat && HasComp<NWVehicleCabinSeatComponent>(seat))
                _buckle.Unbuckle((uid, buckle), null);
            ClearCabinViewer(uid);
        }
    }
}
