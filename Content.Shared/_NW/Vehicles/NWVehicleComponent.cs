using System.Numerics;
using System;
using Content.Shared.Actions;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NW.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NWVehicleComponent : Component
{
    [DataField, AutoNetworkedField] public NWVehicleState State = NWVehicleState.Parked;
    [DataField, AutoNetworkedField] public EntityUid? Pilot;
    [DataField, AutoNetworkedField] public bool LowGear = true;
    [DataField, AutoNetworkedField] public bool HasOverlights;
    [DataField, AutoNetworkedField] public bool LightsEnabled;
    // Assigned on first cabin preparation. Also records that the physical key was issued.
    [DataField] public string? IgnitionLockCode;
    [DataField] public int GunnerSeat = -1;
    [DataField, AutoNetworkedField] public EntityUid? Gunner;
    [DataField, AutoNetworkedField] public Angle TurretBearing;
    [DataField] public int SeatCount = 4;
    [ViewVariables] public EntityUid?[] SeatOccupants = [];
    [DataField] public int CargoCapacity = 2;
    [DataField, AutoNetworkedField] public EntityUid? StartActionEntity;
    [DataField, AutoNetworkedField] public EntityUid? StopActionEntity;
    [DataField] public EntProtoId StartAction = "ActionNWVehicleStart";
    [DataField] public EntProtoId StopAction = "ActionNWVehicleStop";
    [DataField] public float StartupDuration = 3f;
    [ViewVariables] public TimeSpan StartupFinishedAt;
    [DataField] public SoundSpecifier? StartupSound;
    [DataField] public SoundSpecifier? EngineLoopSound;
    [ViewVariables] public EntityUid? StartupSoundStream;
    [ViewVariables] public EntityUid? EngineSoundStream;
    [DataField] public float ThrustAcceleration = 7f;
    [DataField] public float ReverseAcceleration = 4f;
    [DataField] public float MaxSpeed = 11f;
    [DataField] public string FuelSolution = "nwVehicleFuel";
    [DataField] public ProtoId<ReagentPrototype> FuelReagent = "WeldingFuel";
    [DataField] public FixedPoint2 FuelUsePerSecond = FixedPoint2.New(0.2);
    [DataField] public FixedPoint2 MinimumStartFuel = FixedPoint2.New(1);
    [ViewVariables] public float FuelAccumulator;
    [ViewVariables] public TimeSpan NextFuelUiUpdate;
    [ViewVariables] public Vector2 DriftVelocity;
    [ViewVariables] public NWVehicleControlInput HeldInputs;
    [DataField] public string WindowTitleLocId = "ground-vehicle-console";
}

[Flags]
public enum NWVehicleControlInput : byte
{
    None = 0,
    Forward = 1 << 0,
    Back = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
}

[RegisterComponent, NetworkedComponent]
public sealed partial class NWVehicleHiddenOccupantComponent : Component
{
    [DataField]
    public bool HadStealth;

    [DataField]
    public bool PreviousCanCollide = true;

    [DataField]
    public float PreviousVisibility = 1f;
}

[Serializable, NetSerializable]
public enum NWVehicleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class NWVehicleSeatBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool LowGear;
    public readonly bool HasOverlights;
    public readonly bool LightsEnabled;
    public readonly string Title;
    public readonly NWVehicleState State;
    public readonly float Fuel;
    public readonly float MaxFuel;
    public readonly float StructuralIntegrity;
    public readonly float MaxStructuralIntegrity;
    public readonly NWVehicleSeatUiState[] Seats;
    public readonly NWVehicleCargoUiState[] Cargo;
    public readonly int CargoCapacity;

    public NWVehicleSeatBoundUserInterfaceState(
        string title,
        NWVehicleState flightState,
        float fuel,
        float maxFuel,
        float structuralIntegrity,
        float maxStructuralIntegrity,
        NWVehicleSeatUiState[] seats,
        NWVehicleCargoUiState[] cargo,
        int cargoCapacity,
        bool lowGear,
        bool hasOverlights,
        bool lightsEnabled)
    {
        LowGear = lowGear;
        HasOverlights = hasOverlights;
        LightsEnabled = lightsEnabled;
        Cargo = cargo;
        CargoCapacity = cargoCapacity;
        Title = title;
        State = flightState;
        Fuel = fuel;
        MaxFuel = maxFuel;
        StructuralIntegrity = structuralIntegrity;
        MaxStructuralIntegrity = maxStructuralIntegrity;
        Seats = seats;
    }
}

[Serializable, NetSerializable]
public readonly record struct NWVehicleSeatUiState(int Index, string Name, string? OccupantName, bool RequiresPilotPerk);

[Serializable, NetSerializable]
public readonly record struct NWVehicleCargoUiState(NetEntity Crate, string Name);

/// <summary>
/// Loads whatever crate the actor is pulling. The console cannot reach out and pick a
/// crate for them, so the one they hauled over is the one that goes in.
/// </summary>
[Serializable, NetSerializable]
public sealed class NWVehicleLoadCargoMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class NWVehicleUnloadCargoMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Crate;

    public NWVehicleUnloadCargoMessage(NetEntity crate)
    {
        Crate = crate;
    }
}

[Serializable, NetSerializable]
public sealed class NWVehicleSelectSeatMessage : BoundUserInterfaceMessage
{
    public readonly int SeatIndex;

    public NWVehicleSelectSeatMessage(int seatIndex)
    {
        SeatIndex = seatIndex;
    }
}

[Serializable, NetSerializable]
public sealed class NWVehicleControlInputMessage : EntityEventArgs
{
    public NWVehicleControlInput Input;
    public bool Pressed;

    public NWVehicleControlInputMessage(NWVehicleControlInput input, bool pressed)
    {
        Input = input;
        Pressed = pressed;
    }
}

[Serializable, NetSerializable]
public sealed partial class NWVehicleBoardDoAfterEvent : DoAfterEvent
{
    public int SeatIndex;

    public NWVehicleBoardDoAfterEvent(int seatIndex)
    {
        SeatIndex = seatIndex;
    }

    public override DoAfterEvent Clone() => new NWVehicleBoardDoAfterEvent(SeatIndex);
}

/// <summary>
/// Loading a crate into the cargo bay, or hauling one back out. Which one it is
/// depends on whether the crate is already in the bay when the do-after lands.
/// The crate rides in the event rather than in DoAfterArgs.Used, because once it is
/// in the bay it has no broadphase for the args' range check to work against.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class NWVehicleCargoDoAfterEvent : DoAfterEvent
{
    public NetEntity Crate;

    public NWVehicleCargoDoAfterEvent(NetEntity crate)
    {
        Crate = crate;
    }

    public override DoAfterEvent Clone() => new NWVehicleCargoDoAfterEvent(Crate);
}

[Serializable, NetSerializable]
public sealed class NWVehicleSetGearMessage(bool lowGear) : BoundUserInterfaceMessage
{
    public readonly bool LowGear = lowGear;
}

[Serializable, NetSerializable]
public sealed class NWVehicleSetLightsMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

public enum NWVehicleState : byte { Parked, Starting, Running }
public sealed partial class NWVehicleStartActionEvent : InstantActionEvent;
public sealed partial class NWVehicleStopActionEvent : InstantActionEvent;
