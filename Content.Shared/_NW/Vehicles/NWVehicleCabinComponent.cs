using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._NW.Vehicles;

/// <summary>One independent map per exterior. A wreck can inherit the same map.</summary>
[RegisterComponent]
public sealed partial class NWVehicleCabinComponent : Component
{
    [DataField] public string InteriorPath = "/Maps/_NW/Vehicles/humvee.yml";
    [DataField] public Vector2[] EntryPoints = [];
    [DataField] public EntityUid? Map;
    [DataField] public EntityUid? Grid;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NWVehicleCabinLinkComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Exterior;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NWVehicleCabinSeatComponent : Component
{
    [DataField] public int SeatIndex;
    [DataField, AutoNetworkedField] public EntityUid? Exterior;
}

[RegisterComponent]
public sealed partial class NWVehicleCabinExitComponent : Component
{
    [DataField] public int EntryIndex;
}

[RegisterComponent]
public sealed partial class NWVehicleCabinViewportComponent : Component;

[RegisterComponent]
public sealed partial class NWVehicleCabinAmmoFeedComponent : Component;

/// <summary>Fixed interior furnishings, excluded from emergency evacuation of loose contents.</summary>
[RegisterComponent]
public sealed partial class NWVehicleCabinFixtureComponent : Component;

/// <summary>Control actions stay on the person, including when their eye is outside.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NWVehicleCabinViewerComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Exterior;
    [DataField, AutoNetworkedField] public bool Outside;
    [DataField, AutoNetworkedField] public float Fuel;
    [DataField, AutoNetworkedField] public float FuelCapacity;
    [DataField, AutoNetworkedField] public float Integrity;
    [DataField, AutoNetworkedField] public float MaxIntegrity;
    [DataField] public EntityUid? ViewAction;
    [DataField] public EntityUid? IgnitionAction;
    [DataField] public EntityUid? GearAction;
    [DataField] public EntityUid? LightsAction;
}

public sealed partial class NWVehicleCabinViewActionEvent : InstantActionEvent;
public sealed partial class NWVehicleIgnitionActionEvent : InstantActionEvent;
public sealed partial class NWVehicleCabinGearActionEvent : InstantActionEvent;
public sealed partial class NWVehicleCabinLightsActionEvent : InstantActionEvent;

[ByRefEvent]
public readonly record struct NWVehicleTurretAimEvent(Angle Bearing);
