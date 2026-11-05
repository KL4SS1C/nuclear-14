using System.Numerics;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._NW.Transit;

[RegisterComponent]
public sealed partial class NWTransitLinkComponent : Component
{
    [DataField] public string Id = string.Empty;
    [DataField] public bool SameMapOnly;
    [DataField] public Vector2 ArrivalOffset;
    [DataField] public float Delay;
}

[Serializable, NetSerializable]
public sealed partial class NWClimbLinkEvent : DoAfterEvent
{
    public NetEntity Destination;

    public NWClimbLinkEvent(NetEntity destination)
    {
        Destination = destination;
    }

    public override DoAfterEvent Clone() => new NWClimbLinkEvent(Destination);
}
