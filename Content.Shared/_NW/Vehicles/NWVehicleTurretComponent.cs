using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NW.Vehicles;

/// <summary>Server-owned traverse. World yaw keeps the turret independent of hull turns.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NWVehicleTurretComponent : Component
{
    [DataField, AutoNetworkedField] public Angle WorldYaw;
    [DataField] public float DegreesPerSecond = 120f;
    [DataField] public EntProtoId Visual = "NWvehicleM60TurretVisual";
    [DataField] public bool TankBarrelOffset;
    [DataField] public bool Initialized;
    public Vector2? Target;
    public EntityUid? AimUser;
    public TimeSpan NextAim;
    public TimeSpan AimExpires;
}

[Serializable, NetSerializable]
public sealed class NWVehicleAimRequest(NetEntity vehicle, Vector2 position) : EntityEventArgs
{
    public readonly NetEntity Vehicle = vehicle;
    public readonly Vector2 Position = position;
}

public static class NWVehicleTurretMath
{
    public static Angle Step(Angle current, Angle target, double maximumRadians)
    {
        if (!double.IsFinite(current.Theta) || !double.IsFinite(target.Theta) ||
            !double.IsFinite(maximumRadians) || maximumRadians <= 0)
            return current;
        var delta = Math.Atan2(Math.Sin(target.Theta - current.Theta), Math.Cos(target.Theta - current.Theta));
        return new Angle(current.Theta + Math.Clamp(delta, -maximumRadians, maximumRadians)).Reduced();
    }
}
