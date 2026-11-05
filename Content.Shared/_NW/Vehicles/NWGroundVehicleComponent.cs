using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._NW.Vehicles;

/// <summary>Opt-in road movement using the native vehicle seats, fuel and input protocol.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NWGroundVehicleComponent : Component
{
    [DataField] public float MaxReverseSpeed = 3f;
    [DataField] public float Braking = 10f;
    [DataField] public float RollingResistance = 12f;
    /// <summary>Minimum interval between cardinal turns, matching the RMC control style.</summary>
    [DataField] public float TurnDelay = 0.08f;
    [ViewVariables] public TimeSpan NextTurnTime;
    [DataField] public EntProtoId? WreckPrototype;
    [DataField] public HashSet<string> BreakableObstacles = new();
    [DataField] public float MaxIntegrity = 400f;
    [DataField] public float RunOverMinimumSpeed = 2f;
    [DataField] public float RunOverRadius = 0.9f;
    [DataField] public float RunOverDamagePerSpeed = 5f;
    [DataField] public float RunOverMaxDamage = 60f;
    [DataField] public float RunOverCooldown = 1f;
    [ViewVariables] public MapCoordinates? PreviousPosition;
    [ViewVariables] public TimeSpan NextImpactTime;
    [DataField] public float PushDistancePerSpeed = 0.25f;
    [DataField] public float MaxPushDistance = 3f;
    [DataField] public float BreakableMaxThreshold = 250f;
    [DataField] public float BarrierMinimumSpeed = 2f;
    [DataField] public HashSet<string> FragileBarriers = new();
    [DataField] public float ObstacleMinimumSpeed = 4f;
    [DataField] public float ObstacleDamagePerSpeed = 32f;
    [DataField] public float ObstacleMaxDamage = 300f;
    public readonly Dictionary<EntityUid, TimeSpan> NextObstacleHit = new();
    public readonly Dictionary<EntityUid, TimeSpan> NextRunOver = new();
}
