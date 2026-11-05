// Derived seating/cargo behavior from Nuclear-14; independent NW vehicle systems.
using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem : EntitySystem
{
    private const float BoardingDuration = 5f;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    private readonly Dictionary<EntityUid, int> _pendingSeatSelections = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NWVehicleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NWVehicleComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<NWVehicleComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<NWVehicleComponent, UnstrapAttemptEvent>(OnExitAttempt);
        SubscribeLocalEvent<NWVehicleComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleStartActionEvent>(OnStartAction);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleStopActionEvent>(OnStopAction);
        SubscribeLocalEvent<NWVehicleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NWVehicleComponent, AfterActivatableUIOpenEvent>(OnAfterUiOpen);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleSelectSeatMessage>(OnSelectSeat);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleBoardDoAfterEvent>(OnBoardDoAfter);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeNetworkEvent<NWVehicleControlInputMessage>(OnControlInput);
        InitializeCargo();
        InitializeImpact();
        InitializeCabins();
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleSetGearMessage>(OnSetGear);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleSetLightsMessage>(OnSetLights);
        SubscribeLocalEvent<NWVehicleComponent, NWVehicleTurretAimEvent>(OnMountedGunAim);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateCabinViewers();
        var query = EntityQueryEnumerator<NWVehicleComponent, NWGroundVehicleComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var vehicle, out var ground, out var physics))
        {
            UpdateRunOver((uid, vehicle), ground, frameTime);
            UpdateGroundVehicle((uid, vehicle), ground, physics, frameTime);
        }
    }

    private void OnStartAction(Entity<NWVehicleComponent> ent, ref NWVehicleStartActionEvent args)
    {
        if (args.Handled || ent.Comp.Pilot != args.Performer) return;
        args.Handled = true;
        StartGroundEngine(ent);
    }

    private void OnStopAction(Entity<NWVehicleComponent> ent, ref NWVehicleStopActionEvent args)
    {
        if (args.Handled || ent.Comp.Pilot != args.Performer) return;
        args.Handled = true;
        StopGroundEngine(ent);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (TryComp<NWVehicleCabinViewerComponent>(args.Entity, out var viewer) && viewer.Exterior is { } hull && Exists(hull))
        {
            _cabinViews.RemoveViewSubscriber(hull, args.Player);
            _cabinViews.RemoveViewSubscriber(args.Entity, args.Player);
        }
        var query = EntityQueryEnumerator<NWVehicleComponent>();
        while (query.MoveNext(out var uid, out var vehicle))
            if (vehicle.Pilot == args.Entity) StopGroundEngine((uid, vehicle));
    }

    private void ResetDriverView(EntityUid pilot)
    {
        if (TryComp<EyeComponent>(pilot, out var eye))
        {
            _eye.SetOffset(pilot, Vector2.Zero, eye);
            _eye.SetPvsScale((pilot, eye), 1f);
        }
    }

    private void AddPilotActions(EntityUid pilot, Entity<NWVehicleComponent> ent)
    {
        _actions.AddAction(pilot, ref ent.Comp.StartActionEntity, ent.Comp.StartAction, ent.Owner);
        _actions.AddAction(pilot, ref ent.Comp.StopActionEntity, ent.Comp.StopAction, ent.Owner);
        ResetDriverView(pilot);
    }

    private void RemovePilotAction(EntityUid pilot, NWVehicleComponent vehicle)
    {
        _actions.RemoveAction(pilot, vehicle.StartActionEntity);
        _actions.RemoveAction(pilot, vehicle.StopActionEntity);
        vehicle.StartActionEntity = null;
        vehicle.StopActionEntity = null;
    }
    private void OnStartup(Entity<NWVehicleComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.HasOverlights)
            _vehicleLights.SetEnabled(ent.Owner, ent.Comp.LightsEnabled);
        // Seat capacity is per-vehicle; resize the
        // server-side seat array to match the prototype's SeatCount.
        if (ent.Comp.SeatOccupants.Length != ent.Comp.SeatCount)
            ent.Comp.SeatOccupants = new EntityUid?[ent.Comp.SeatCount];
    }

    private void OnStrapAttempt(Entity<NWVehicleComponent> ent, ref StrapAttemptEvent args)
    {
        var occupant = args.Buckle.Owner;

        if (!_pendingSeatSelections.TryGetValue(occupant, out var seatIndex))
        {
            args.Cancelled = true;
            _popup.PopupEntity(Loc.GetString("nw-vehicle-use-seat-manifest"), ent, occupant);
            return;
        }

        if (!IsValidSeat(ent.Comp, seatIndex) || ent.Comp.SeatOccupants[seatIndex] != null)
        {
            args.Cancelled = true;
            return;
        }

    }

    private void OnStrapped(Entity<NWVehicleComponent> ent, ref StrappedEvent args)
    {
        var occupant = args.Buckle.Owner;
        if (!_pendingSeatSelections.Remove(occupant, out var seatIndex) || !IsValidSeat(ent.Comp, seatIndex))
            return;

        ent.Comp.SeatOccupants[seatIndex] = occupant;
        if (seatIndex == ent.Comp.GunnerSeat)
            ent.Comp.Gunner = occupant;
        HideOccupant(occupant);

        if (seatIndex == 0)
        {
            ent.Comp.Pilot = occupant;
            AddPilotActions(occupant, ent);
        }


        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnUnstrapped(Entity<NWVehicleComponent> ent, ref UnstrappedEvent args)
    {
        var occupant = args.Buckle.Owner;
        if (GetSeatIndex(ent.Comp, occupant) is { } seat)
        {
            if (TryGetExit(ent, occupant, seat, out var exit) ||
                TryGetExit(ent, occupant, seat ^ 1, out exit))
                _transform.SetCoordinates(occupant, exit);

            ent.Comp.SeatOccupants[seat] = null;
        }
        if (ent.Comp.Gunner == occupant)
            ent.Comp.Gunner = null;
        UnhideOccupant(occupant);

        if (ent.Comp.Pilot == occupant)
        {
            RemovePilotAction(occupant, ent.Comp);
            ent.Comp.Pilot = null;
            if (!Terminating(ent.Owner))
                StopGroundEngine(ent);
        }

        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnSelectSeat(Entity<NWVehicleComponent> ent, ref NWVehicleSelectSeatMessage args)
    {
        if (HasComp<NWVehicleCabinComponent>(ent)) return;
        var user = args.Actor;
        var seatIndex = args.SeatIndex;
        if (HasComp<NWGroundVehicleComponent>(ent) && ent.Comp.State != NWVehicleState.Parked)
            return;

        if (!IsValidSeat(ent.Comp, seatIndex))
            return;

        // Reach is the real gate: the buckle and do-after range checks already stop
        // anyone climbing aboard a craft they cannot physically get to.
        if (ent.Comp.SeatOccupants[seatIndex] != null)
            return;


        var currentSeat = GetSeatIndex(ent.Comp, user);
        if (currentSeat != null)
        {
            ent.Comp.SeatOccupants[currentSeat.Value] = null;
            ent.Comp.SeatOccupants[seatIndex] = user;
            if (currentSeat == ent.Comp.GunnerSeat || seatIndex == ent.Comp.GunnerSeat)
                ent.Comp.Gunner = seatIndex == ent.Comp.GunnerSeat ? user : null;

            if (currentSeat.Value == 0)
            {
                RemovePilotAction(user, ent.Comp);
                ent.Comp.Pilot = null;
            }

            if (seatIndex == 0)
            {
                ent.Comp.Pilot = user;
                AddPilotActions(user, ent);
            }


            Dirty(ent);
            UpdateUi(ent);
            return;
        }

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            BoardingDuration,
            new NWVehicleBoardDoAfterEvent(seatIndex),
            ent.Owner,
            target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 2f,
            BlockDuplicate = true,
        };

        _doAfter.TryStartDoAfter(doAfter);

        UpdateUi(ent);
    }

    private void OnBoardDoAfter(Entity<NWVehicleComponent> ent, ref NWVehicleBoardDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target != ent.Owner)
            return;

        args.Handled = true;
        var user = args.User;
        var seatIndex = args.SeatIndex;
        if (HasComp<NWGroundVehicleComponent>(ent) && ent.Comp.State != NWVehicleState.Parked)
            return;

        if (!IsValidSeat(ent.Comp, seatIndex) ||
            ent.Comp.SeatOccupants[seatIndex] != null ||
            GetSeatIndex(ent.Comp, user) != null)
        {
            UpdateUi(ent);
            return;
        }

        _pendingSeatSelections[user] = seatIndex;
        if (!_buckle.TryBuckle(user, user, ent.Owner))
            _pendingSeatSelections.Remove(user);

        UpdateUi(ent);
    }

    private void HideOccupant(EntityUid occupant)
    {
        if (HasComp<NWVehicleHiddenOccupantComponent>(occupant))
            return;

        var hidden = EnsureComp<NWVehicleHiddenOccupantComponent>(occupant);
        if (TryComp<PhysicsComponent>(occupant, out var body))
        {
            hidden.PreviousCanCollide = body.CanCollide;
            _physics.SetCanCollide(occupant, false, body: body);
        }
        hidden.HadStealth = TryComp<StealthComponent>(occupant, out var stealth);
        hidden.PreviousVisibility = hidden.HadStealth && stealth != null
            ? _stealth.GetVisibility(occupant, stealth)
            : 1f;

        stealth ??= EnsureComp<StealthComponent>(occupant);
        _stealth.SetVisibility(occupant, -1f, stealth);
    }

    private void UnhideOccupant(EntityUid occupant)
    {
        if (!TryComp<NWVehicleHiddenOccupantComponent>(occupant, out var hidden))
            return;

        if (TryComp<PhysicsComponent>(occupant, out var body))
            _physics.SetCanCollide(occupant, hidden.PreviousCanCollide, body: body);
        if (hidden.HadStealth)
        {
            if (TryComp<StealthComponent>(occupant, out var stealth))
                _stealth.SetVisibility(occupant, hidden.PreviousVisibility, stealth);
        }
        else
        {
            RemComp<StealthComponent>(occupant);
        }

        RemComp<NWVehicleHiddenOccupantComponent>(occupant);
    }

    private static bool IsValidSeat(NWVehicleComponent vehicle, int seatIndex)
    {
        return seatIndex >= 0 && seatIndex < vehicle.SeatOccupants.Length;
    }

    private static int? GetSeatIndex(NWVehicleComponent vehicle, EntityUid occupant)
    {
        for (var i = 0; i < vehicle.SeatOccupants.Length; i++)
        {
            if (vehicle.SeatOccupants[i] == occupant)
                return i;
        }

        return null;
    }

    private void UpdateUi(Entity<NWVehicleComponent> ent)
    {
        var state = BuildUiState(ent);
        _ui.SetUiState(ent.Owner, NWVehicleUiKey.Key, state);

    }

    private NWVehicleSeatBoundUserInterfaceState BuildUiState(Entity<NWVehicleComponent> ent)
    {
        var vehicle = ent.Comp;
        var seats = new NWVehicleSeatUiState[vehicle.SeatOccupants.Length];
        for (var i = 0; i < seats.Length; i++)
        {
            var occupant = vehicle.SeatOccupants[i];
            var seatName = i switch
            {
                0 => Loc.GetString("ground-vehicle-driver"),
                1 => Loc.GetString("nw-vehicle-seat-crew-chief"),
                _ when i == vehicle.GunnerSeat => Loc.GetString("nw-vehicle-seat-gunner"),
                _ => Loc.GetString("nw-vehicle-seat-passenger", ("number", i - 1)),
            };

            seats[i] = new NWVehicleSeatUiState(
                i,
                seatName,
                occupant == null ? null : Identity.Name(occupant.Value, EntityManager),
                false);
        }

        TryGetFuel(ent, out var fuel, out var capacity);
        var maxIntegrity = Comp<NWGroundVehicleComponent>(ent).MaxIntegrity;
        var integrity = MathF.Max(0f, maxIntegrity -
            (TryComp<DamageableComponent>(ent, out var hull) ? hull.TotalDamage.Float() : 0f));

        var stored = _sharedNWVehicle.GetCargo(ent);
        var cargo = new NWVehicleCargoUiState[stored.Count];
        for (var i = 0; i < cargo.Length; i++)
        {
            cargo[i] = new NWVehicleCargoUiState(GetNetEntity(stored[i]), Name(stored[i]));
        }

        return new NWVehicleSeatBoundUserInterfaceState(
            Loc.GetString(vehicle.WindowTitleLocId),
            vehicle.State,
            fuel.Float(),
            capacity.Float(),
            integrity,
            maxIntegrity,
            seats,
            cargo,
            vehicle.CargoCapacity,
            vehicle.LowGear,
            vehicle.HasOverlights,
            vehicle.LightsEnabled);
    }

    private void OnControlInput(NWVehicleControlInputMessage message, EntitySessionEventArgs session)
    {
        if (message.Input is not (NWVehicleControlInput.Forward or NWVehicleControlInput.Back or
            NWVehicleControlInput.Left or NWVehicleControlInput.Right))
        {
            return;
        }

        if (session.SenderSession.AttachedEntity is not { } pilot || _mobState.IsIncapacitated(pilot) ||
            TryComp<NWVehicleCabinViewerComponent>(pilot, out var viewer) && !viewer.Outside)
            return;

        var query = EntityQueryEnumerator<NWVehicleComponent>();
        while (query.MoveNext(out var uid, out var vehicle))
        {
            if (vehicle.Pilot != pilot || vehicle.State != NWVehicleState.Running)
                continue;

            if (message.Pressed)
                vehicle.HeldInputs |= message.Input;
            else
                vehicle.HeldInputs &= ~message.Input;

            return;
        }
    }

    private void OnShutdown(Entity<NWVehicleComponent> ent, ref ComponentShutdown args)
    {
        ReturnIgnitionKey(ent);
        ent.Comp.StartupSoundStream = _audio.Stop(ent.Comp.StartupSoundStream);
        StopEngineLoop(ent.Comp);

        foreach (var occupant in ent.Comp.SeatOccupants)
        {
            if (occupant != null)
                UnhideOccupant(occupant.Value);
        }
    }

    private void OnAfterUiOpen(Entity<NWVehicleComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        UpdateUi(ent);
    }

    private bool TryConsumeFuel(EntityUid uid, NWVehicleComponent vehicle, float frameTime)
    {
        if (!_solution.TryGetSolution(uid, vehicle.FuelSolution, out var fuelEntity, out var fuelSolution))
            return false;

        var available = fuelSolution.GetTotalPrototypeQuantity(vehicle.FuelReagent);
        if (available <= FixedPoint2.Zero)
            return false;

        var accumulated = vehicle.FuelUsePerSecond.Float() * frameTime + vehicle.FuelAccumulator;
        var wholeFuel = FixedPoint2.New(MathF.Floor(accumulated));
        vehicle.FuelAccumulator = accumulated - wholeFuel.Float();

        if (wholeFuel <= FixedPoint2.Zero)
            return true;

        var consumed = FixedPoint2.Min(wholeFuel, available);
        if (!_solution.RemoveReagent(fuelEntity.Value, vehicle.FuelReagent, consumed))
            return false;

        return available > consumed;
    }

    private bool HasMinimumStartFuel(Entity<NWVehicleComponent> ent)
    {
        return TryGetFuel(ent, out var fuel, out _) && fuel >= ent.Comp.MinimumStartFuel;
    }

    private bool TryGetFuel(Entity<NWVehicleComponent> ent, out FixedPoint2 fuel, out FixedPoint2 capacity)
    {
        fuel = FixedPoint2.Zero;
        capacity = FixedPoint2.Zero;

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.FuelSolution, out _, out var solution))
            return false;

        fuel = solution.GetTotalPrototypeQuantity(ent.Comp.FuelReagent);
        capacity = solution.MaxVolume;
        return true;
    }

    private void UpdateFuelUi(Entity<NWVehicleComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.NextFuelUiUpdate)
            return;

        ent.Comp.NextFuelUiUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        UpdateUi(ent);
    }

    private void WarnPilot(Entity<NWVehicleComponent> ent, string message)
    {
        if (ent.Comp.Pilot is { } pilot && Exists(pilot))
            _popup.PopupEntity(Loc.GetString(message), ent, pilot, PopupType.LargeCaution);
    }

    private void StartEngineLoop(EntityUid uid, NWVehicleComponent vehicle)
    {
        if (vehicle.EngineSoundStream != null || vehicle.EngineLoopSound == null)
            return;

        vehicle.EngineSoundStream = _audio.PlayPvs(vehicle.EngineLoopSound, uid)?.Entity;
    }

    private void StopEngineLoop(NWVehicleComponent vehicle)
    {
        vehicle.EngineSoundStream = _audio.Stop(vehicle.EngineSoundStream);
    }
}
