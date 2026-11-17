using Robust.Shared.GameStates;

namespace Content.Shared._NW.ZMapping;

/// <summary>Persistent project metadata; runtime Z-links are rebuilt when opening the project.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NWZEditorFloorComponent : Component
{
    [DataField] public string Project = "Building";
    [DataField] public int Depth;
    [DataField] public string Label = "Ground floor";
    /// <summary>Native runtime export basename, persisted in the editable bundle.</summary>
    [DataField] public string RuntimeFile = "";
}
