using Robust.Shared.Containers;
using Robust.Server.GameObjects;
using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.Chemistry.Components;
using Content.Shared.Movement.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitions = default!;

    private const string IgnitionSlot = "nw-vehicle-ignition";

    [Dependency] private readonly MetaDataSystem _keyMetadata = default!;

    private EntityUid? InsertedKey(EntityUid hull)
    {
        return _container.TryGetContainer(hull, IgnitionSlot, out var slot) && slot is ContainerSlot ignition
            ? ignition.ContainedEntity : null;
    }

    private bool FitsIgnition(NWVehicleComponent vehicle, NWVehicleIgnitionKeyComponent key) =>
        vehicle.IgnitionLockCode is { Length: > 0 } code && key.LockCode == code;

    private bool HasIgnitionKey(EntityUid hull) =>
        InsertedKey(hull) is { } inserted &&
        TryComp<NWVehicleIgnitionKeyComponent>(inserted, out var key) &&
        FitsIgnition(Comp<NWVehicleComponent>(hull), key);

    private void EnsureVehicleKey(Entity<NWVehicleComponent> ent, EntityCoordinates seat)
    {
        if (!string.IsNullOrEmpty(ent.Comp.IgnitionLockCode))
            return;

        // Adopt an uncut key already in a pre-0.6.6 ignition; otherwise leave the
        // original at the driver seat. Re-entering or losing the key never issues a spare.
        EntityUid key;
        if (InsertedKey(ent.Owner) is { } previous &&
            TryComp<NWVehicleIgnitionKeyComponent>(previous, out var oldKey) &&
            string.IsNullOrEmpty(oldKey.LockCode))
            key = previous;
        else
            key = Spawn("NWvehicleIgnitionKey", seat);

        var code = System.Guid.NewGuid().ToString("N");
        ent.Comp.IgnitionLockCode = code;
        Comp<NWVehicleIgnitionKeyComponent>(key).LockCode = code;
        var label = Name(ent.Owner);
        var serial = code[..8].ToUpperInvariant();
        _keyMetadata.SetEntityName(ent.Owner, Loc.GetString("nw-vehicle-numbered-name",
            ("model", label), ("serial", serial)));
        _keyMetadata.SetEntityName(key, Loc.GetString("nw-vehicle-numbered-key",
            ("model", label), ("serial", serial)));
    }

    private bool InsertIgnitionKey(Entity<NWVehicleComponent> ent)
    {
        if (ent.Comp.Pilot is not { } driver)
            return false;

        if (!_cabinHands.TryGetActiveItem(driver, out var held) || held is not { } key ||
            !TryComp<NWVehicleIgnitionKeyComponent>(key, out var blade))
        {
            _popup.PopupEntity(Loc.GetString("nw-vehicle-key-required"), driver, driver);
            return false;
        }

        if (!FitsIgnition(ent.Comp, blade))
        {
            _popup.PopupEntity(Loc.GetString("nw-vehicle-key-wrong-lock"), driver, driver);
            return false;
        }

        var slot = _container.EnsureContainer<ContainerSlot>(ent.Owner, IgnitionSlot);
        if (slot.ContainedEntity != null || !_cabinHands.TryDropIntoContainer(driver, key, slot) ||
            slot.ContainedEntity != key)
        {
            _popup.PopupEntity(Loc.GetString("nw-vehicle-key-insert-failed"), driver, driver);
            return false;
        }

        _popup.PopupEntity(Loc.GetString("nw-vehicle-key-inserted"), driver, driver);
        return true;
    }

    private void ReturnIgnitionKey(Entity<NWVehicleComponent> ent, EntityUid? holder = null)
    {
        if (!_container.TryGetContainer(ent.Owner, IgnitionSlot, out var container) ||
            container is not ContainerSlot { ContainedEntity: { } key })
            return;

        var canReturn = holder is { } user && !TerminatingOrDeleted(user);
        var destination = canReturn ? Transform(holder!.Value).Coordinates : Transform(ent.Owner).Coordinates;
        if (!_container.Remove(key, container, destination: destination))
            return;

        if (canReturn)
            _cabinHands.PickupOrDrop(holder!.Value, key);
    }

    private void StartGroundEngine(Entity<NWVehicleComponent> ent)
    {
        if (ent.Comp.State != NWVehicleState.Parked)
            return;
        if (!HasMinimumStartFuel(ent))
        {
            WarnPilot(ent, "ground-vehicle-no-fuel");
            return;
        }
        if (!HasIgnitionKey(ent.Owner))
        {
            if (ent.Comp.Pilot is { } driver)
                _popup.PopupEntity(Loc.GetString("nw-vehicle-key-empty"), driver, driver);
            return;
        }

        ent.Comp.State = NWVehicleState.Starting;
        ent.Comp.HeldInputs = NWVehicleControlInput.None;
        ent.Comp.StartupFinishedAt = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.StartupDuration);
        if (ent.Comp.StartupSound != null)
            ent.Comp.StartupSoundStream = _audio.PlayPvs(ent.Comp.StartupSound, ent.Owner)?.Entity;
        Dirty(ent);
        UpdateUi(ent);
    }

    private void StopGroundEngine(Entity<NWVehicleComponent> ent)
    {
        ent.Comp.StartupSoundStream = _audio.Stop(ent.Comp.StartupSoundStream);
        StopEngineLoop(ent.Comp);
        ent.Comp.State = NWVehicleState.Parked;
        ent.Comp.HeldInputs = NWVehicleControlInput.None;
        ent.Comp.DriftVelocity = Vector2.Zero;
        if (TryComp<PhysicsComponent>(ent, out var body))
        {
            _physics.SetLinearVelocity(ent, Vector2.Zero, body: body);
            _physics.SetAngularVelocity(ent, 0f, body: body);
        }
        if (ent.Comp.Pilot is { } pilot)
            ResetDriverView(pilot);
        Dirty(ent);
        UpdateUi(ent);
    }

    private void UpdateGroundVehicle(Entity<NWVehicleComponent> ent, NWGroundVehicleComponent ground,
        PhysicsComponent physics, float frameTime)
    {
        UpdateFuelUi(ent);
        if (ent.Comp.State == NWVehicleState.Parked)
            return;
        if (ent.Comp.Pilot == null || !Exists(ent.Comp.Pilot.Value) || _mobState.IsIncapacitated(ent.Comp.Pilot.Value))
        {
            StopGroundEngine(ent);
            return;
        }
        if (!HasIgnitionKey(ent.Owner))
        {
            StopGroundEngine(ent);
            return;
        }

        if (!TryConsumeFuel(ent, ent.Comp, frameTime))
        {
            WarnPilot(ent, "ground-vehicle-no-fuel");
            StopGroundEngine(ent);
            return;
        }
        if (ent.Comp.State == NWVehicleState.Starting)
        {
            if (_timing.CurTime < ent.Comp.StartupFinishedAt)
                return;
            ent.Comp.StartupSoundStream = _audio.Stop(ent.Comp.StartupSoundStream);
            ent.Comp.State = NWVehicleState.Running;
            ent.Comp.HeldInputs = NWVehicleControlInput.None;
            StartEngineLoop(ent, ent.Comp);
            Dirty(ent);
            UpdateUi(ent);
        }
        if (ent.Comp.State != NWVehicleState.Running)
        {
            StopGroundEngine(ent);
            return;
        }

        var rotation = _transform.GetWorldRotation(ent.Owner);
        var facing = rotation.GetCardinalDir().ToIntVec();
        var forward = new Vector2(facing.X, facing.Y);
        rotation = forward.ToWorldAngle();
        // Read post-collision speed, never restore a cached velocity through a wall.
        var speed = Vector2.Dot(physics.LinearVelocity, forward);
        var input = NWVehicleMotion.GetInputDirection(ent.Comp.HeldInputs);
        var hasInput = input != Vector2i.Zero;
        var turnRequested = hasInput && input != facing && input != -facing;

        if (turnRequested)
        {
            if (_timing.CurTime < ground.NextTurnTime || !HasSpaceToTurn(ent.Owner))
            {
                // No sideways acceleration while waiting for the next legal turn.
                hasInput = false;
            }
            else
            {
                facing = input;
                forward = new Vector2(facing.X, facing.Y);
                rotation = forward.ToWorldAngle();
                ground.NextTurnTime = _timing.CurTime + TimeSpan.FromSeconds(ground.TurnDelay);
            }
        }

        var profile = new NWVehicleMotion.DriveProfile(ent.Comp.MaxSpeed,
            ground.MaxReverseSpeed, ent.Comp.ThrustAcceleration, ent.Comp.ReverseAcceleration,
            ground.Braking);
        var terrainFactor = GetTerrainSpeedFactor(ent.Owner) * (ent.Comp.LowGear ? 0.5f : 1f);
        speed = NWVehicleMotion.StepTerrainSpeed(speed, profile, facing, input, hasInput,
            ground.RollingResistance, terrainFactor, frameTime);

        ent.Comp.DriftVelocity = forward * speed;
        _physics.SetLinearVelocity(ent, ent.Comp.DriftVelocity, body: physics);
        _physics.SetAngularVelocity(ent, 0f, body: physics);
        _transform.SetWorldRotation(ent.Owner, rotation);
    }

    private bool HasSpaceToTurn(EntityUid uid)
    {
        var hull = _lookup.GetWorldAABB(uid);
        if (MathF.Abs(hull.Width - hull.Height) < 0.001f)
            return true;

        // A quarter-turn exchanges the two extents. Exclude the contact skin so
        // touching a wall is allowed when the new hull fits alongside it.
        var center = _transform.GetMapCoordinates(uid);
        var extent = new Vector2(hull.Height, hull.Width) * 0.5f - new Vector2(0.015f);
        var target = new Robust.Shared.Maths.Box2(center.Position - extent, center.Position + extent);
        return ExitIsClear(uid, uid, center.MapId, target);
    }

    private float GetTerrainSpeedFactor(EntityUid uid)
    {
        var xform = Transform(uid);
        var gridUid = xform.GridUid ?? xform.MapUid;
        if (gridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return 1f;

        var indices = _map.LocalToTile(grid, gridComp, xform.Coordinates);
        var factor = 1f;
        if (_map.TryGetTileRef(grid, gridComp, indices, out var tile) && !tile.Tile.IsEmpty)
        {
            factor = _tileDefinitions[tile.Tile.TypeId].ID switch
            {
                "WaterDeep" => 0.2f,
                "WaterMedium" => 0.4f,
                "WaterShallow" or "FloorSwampland" => 0.6f,
                "WaterRiver" => 0.5f,
                _ => 1f,
            };
        }

        // Nuclear-14 also maps water as anchored floor entities over ordinary tiles.
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, indices);
        while (anchored.MoveNext(out var entity))
        {
            if (!HasComp<FloorOccluderComponent>(entity.Value) ||
                !TryComp<DrainableSolutionComponent>(entity.Value, out var drain) ||
                drain.Solution != "pool" ||
                !TryComp<SpeedModifierContactsComponent>(entity.Value, out var water))
                continue;

            if (float.IsFinite(water.WalkSpeedModifier))
                factor = MathF.Min(factor, Math.Clamp(water.WalkSpeedModifier, 0f, 1f));
        }

        return factor;
    }
}
