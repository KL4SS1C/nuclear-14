namespace Content.Shared._NW.Vehicles;

[RegisterComponent]
public sealed partial class NWVehicleIgnitionKeyComponent : Component
{
    [DataField] public string? LockCode;
}
