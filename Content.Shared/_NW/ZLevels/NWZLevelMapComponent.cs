using Robust.Shared.GameStates;

namespace Content.Shared._NW.ZLevels;

/// <summary>Opt-in for the NW walking integration. Add to every map of one Z network.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NWZLevelMapComponent : Component;
