using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    [Dependency] private readonly SharedInteractionSystem _inspectionInteraction = default!;
    [Dependency] private readonly ActionBlockerSystem _inspectionBlocker = default!;
    [Dependency] private readonly NWVehicleRepairSystem _inspectionRepair = default!;
    private TimeSpan _nextInspectionUpdate;

    private void InitializeInspection()
    {
        SubscribeLocalEvent<NWVehicleInspectionComponent, InteractHandEvent>(OnInspectionHand);
        SubscribeLocalEvent<NWVehicleInspectionComponent, GetVerbsEvent<InteractionVerb>>(OnInspectionVerb);
        SubscribeLocalEvent<NWVehicleInspectionComponent, NWVehicleInspectionMessage>(OnInspectionCommand);
    }

    private bool InspectionHull(EntityUid target, out EntityUid hull)
    {
        hull = target;
        return HasComp<NWVehicleComponent>(target) ||
               HasComp<NWVehicleCabinAmmoFeedComponent>(target) && CabinExterior(target, out hull);
    }

    private void OnInspectionHand(Entity<NWVehicleInspectionComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled) return;
        if (HasComp<NWVehicleCabinAmmoFeedComponent>(ent) || IsAtInspectionHood(ent.Owner, args.User))
        {
            args.Handled = OpenInspection(ent.Owner, args.User);
            return;
        }

        // Side/rear clicks enter the cabin instead of opening maintenance.
        if (!_inspectionBlocker.CanInteract(args.User, ent.Owner) ||
            !_inspectionInteraction.InRangeAndAccessible(args.User, ent.Owner) ||
            !TryComp<NWVehicleCabinComponent>(ent.Owner, out var cabin)) return;
        var activation = new ActivateInWorldEvent(args.User, ent.Owner, true);
        OnEnterCabin((ent.Owner, cabin), ref activation);
        args.Handled = activation.Handled;
    }

    private void OnInspectionVerb(Entity<NWVehicleInspectionComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !HasComp<NWVehicleCabinAmmoFeedComponent>(ent) && !IsAtInspectionHood(ent.Owner, args.User)) return;
        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = HasComp<NWVehicleCabinAmmoFeedComponent>(ent) ? "Inspect ammunition feed" : "Inspect hood",
            Act = () => OpenInspection(ent.Owner, user),
        });
    }

    private bool IsAtInspectionHood(EntityUid target, EntityUid user)
    {
        if (!HasComp<NWVehicleComponent>(target)) return false;
        var vehiclePosition = _transform.GetMapCoordinates(target);
        var userPosition = _transform.GetMapCoordinates(user);
        if (vehiclePosition.MapId != userPosition.MapId) return false;
        // Match the forward direction used by vehicle movement and the snowplow.
        var heading = _transform.GetWorldRotation(target).GetCardinalDir().ToIntVec();
        var forward = new Vector2(heading.X, heading.Y);
        var delta = userPosition.Position - vehiclePosition.Position;
        var front = Vector2.Dot(delta, forward);
        var side = MathF.Abs(Vector2.Dot(delta, new Vector2(-forward.Y, forward.X)));
        return front > 0.35f && front > side;
    }

    private bool OpenInspection(EntityUid target, EntityUid user)
    {
        if (!_inspectionBlocker.CanInteract(user, target) || !_inspectionInteraction.InRangeAndAccessible(user, target) ||
            !InspectionHull(target, out var hull) || !HasComp<NWVehicleComponent>(hull) ||
            !HasComp<NWVehicleCabinAmmoFeedComponent>(target) && !IsAtInspectionHood(target, user)) return false;
        // Send state before opening: the first opening must not be blank.
        RefreshInspection(target, hull);
        return _ui.TryOpenUi(target, NWVehicleInspectionUiKey.Key, user);
    }

    private void UpdateInspection()
    {
        if (_timing.CurTime < _nextInspectionUpdate) return;
        _nextInspectionUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var query = EntityQueryEnumerator<NWVehicleInspectionComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, NWVehicleInspectionUiKey.Key)) continue;
            if (!InspectionHull(uid, out var hull) || !HasComp<NWVehicleComponent>(hull))
                _ui.CloseUi(uid, NWVehicleInspectionUiKey.Key);
            else RefreshInspection(uid, hull);
        }
    }

    private void RefreshInspection(EntityUid target, EntityUid hull)
    {
        var vehicle = Comp<NWVehicleComponent>(hull);
        TryGetFuel((hull, vehicle), out var fuel, out var capacity);
        var maximum = TryComp<NWGroundVehicleComponent>(hull, out var ground) ? ground.MaxIntegrity : 0f;
        var state = new NWVehicleInspectionState
        {
            Title = Name(hull),
            Ammunition = HasComp<NWVehicleCabinAmmoFeedComponent>(target),
            Vehicle = GetNetEntity(hull),
            MaxIntegrity = maximum,
            Integrity = MathF.Max(0, maximum - (TryComp<DamageableComponent>(hull, out var damage) ? damage.TotalDamage.Float() : 0)),
            Fuel = fuel.Float(), FuelCapacity = capacity.Float(),
            Parked = vehicle.State == NWVehicleState.Parked,
            Armed = vehicle.GunnerSeat >= 0,
            Overlights = vehicle.HasOverlights,
        };
        if (_equipmentSlots.TryGetSlot(hull, "nw_plow", out var plow))
        {
            state.HasPlowSlot = true;
            if (plow.Item is { } item) state.Plow = GetNetEntity(item);
        }
        if (_equipmentSlots.TryGetSlot(hull, "gun_magazine", out var magazine) && magazine.Item is { } mag)
        {
            state.Magazine = GetNetEntity(mag);
            state.MagazineName = Name(mag);
            var count = new GetAmmoCountEvent();
            RaiseLocalEvent(mag, ref count);
            state.Ammo = count.Count;
            state.Capacity = count.Capacity;
        }
        if (_equipmentSlots.TryGetSlot(hull, "gun_chamber", out var chamber) && chamber.Item is { } round)
            state.ChamberLoaded = TryComp<CartridgeAmmoComponent>(round, out var cartridge) && !cartridge.Spent;
        state.BoltClosed = TryComp<ChamberMagazineAmmoProviderComponent>(hull, out var provider) && provider.BoltClosed != false;
        _ui.SetUiState(target, NWVehicleInspectionUiKey.Key, state);
    }

    private void OnInspectionCommand(Entity<NWVehicleInspectionComponent> ent, ref NWVehicleInspectionMessage args)
    {
        var user = args.Actor;
        if (!_inspectionBlocker.CanInteract(user, ent.Owner) || !_ui.IsUiOpen(ent.Owner, NWVehicleInspectionUiKey.Key, user) ||
            !_inspectionInteraction.InRangeAndAccessible(user, ent.Owner) || _mobState.IsIncapacitated(user) ||
            !InspectionHull(ent.Owner, out var hull) || !TryComp<NWVehicleComponent>(hull, out var vehicle)) return;
        var feed = HasComp<NWVehicleCabinAmmoFeedComponent>(ent);
        if (!feed && !IsAtInspectionHood(ent.Owner, user))
        {
            _ui.CloseUi(ent.Owner, NWVehicleInspectionUiKey.Key, user);
            return;
        }
        var ok = false;
        switch (args.Command)
        {
            case NWVehicleInspectionCommand.Repair when !feed:
                if (_cabinHands.TryGetActiveItem(user, out var tool) && tool is { } heldTool)
                    ok = _inspectionRepair.TryRepairFromPanel(hull, user, heldTool);
                break;
            case NWVehicleInspectionCommand.InstallPlow when !feed && vehicle.State == NWVehicleState.Parked:
                if (_equipmentSlots.TryGetSlot(hull, "nw_plow", out var emptyPlow))
                    ok = _equipmentSlots.TryInsertFromHand(hull, emptyPlow, user);
                break;
            case NWVehicleInspectionCommand.RemovePlow when !feed && vehicle.State == NWVehicleState.Parked:
                ok = EjectInspectionItem(hull, "nw_plow", user);
                break;
            case NWVehicleInspectionCommand.LoadMagazine when feed:
                if (_equipmentSlots.TryGetSlot(hull, "gun_magazine", out var slot))
                {
                    ok = _equipmentSlots.TryInsertFromHand(hull, slot, user);
                    // Magazine insertion deliberately leaves the bolt unchanged.
                }
                break;
            case NWVehicleInspectionCommand.EjectMagazine when feed:
                ok = EjectInspectionItem(hull, "gun_magazine", user);
                break;
        }
        if (!ok) _popup.PopupEntity("Cannot perform this action. Check the held item, slot and vehicle state.", ent.Owner, user);
        RefreshInspection(ent.Owner, hull);
    }

    private bool EjectInspectionItem(EntityUid hull, string slot, EntityUid user)
    {
        if (!_equipmentSlots.TryEject(hull, slot, user, out var item)) return false;
        _transform.SetCoordinates(item.Value, Transform(user).Coordinates);
        _cabinHands.PickupOrDrop(user, item.Value);
        return true;
    }
}
