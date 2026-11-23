using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NW.Tents;

[RegisterComponent]
public sealed partial class NWTentKitComponent : Component
{
    [DataField(required: true)] public EntProtoId Tent;
    [DataField] public Box2 Footprint = new(-1.5f, -2.5f, 0.5f, 1.5f);
    [DataField] public float DeployTime = 10f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class NWTentComponent : Component
{
    [DataField] public Box2 Footprint = new(-1.5f, -2.5f, 0.5f, 1.5f);
    [DataField] public string[] RoofLayers = ["standard_roof"];
    [DataField] public float PackTime = 10f;
}

// Server-only link, serialized so saving a deployed tent preserves its original kit.
[RegisterComponent]
public sealed partial class NWTentDeployedComponent : Component
{
    [DataField] public EntityUid Kit;
}

[Serializable, NetSerializable]
public sealed partial class NWTentDeployDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class NWTentPackDoAfterEvent : SimpleDoAfterEvent;
