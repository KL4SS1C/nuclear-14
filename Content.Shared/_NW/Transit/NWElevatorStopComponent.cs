namespace Content.Shared._NW.Transit;

[RegisterComponent]
public sealed partial class NWElevatorStopComponent : Component
{
    [DataField] public string ElevatorId = string.Empty;
    [DataField] public int Floor;
    [DataField] public string FloorName = string.Empty;
}
