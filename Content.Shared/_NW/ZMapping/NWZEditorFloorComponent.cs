using Robust.Shared.GameStates;

namespace Content.Shared._NW.ZMapping;

/// <summary>Persistent project metadata; runtime Z-links are rebuilt when opening the project.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NWZEditorFloorComponent : Component
{
    [DataField] public string Project = "Building";
    [DataField] public int Depth;
    [DataField] public string Label = "Ground floor";
}
