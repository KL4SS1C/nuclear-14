using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.CombatMode;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._NW.Vehicles;

/// <summary>Continuous cursor input and local, independently rotated turret sprites.</summary>
public sealed class NWVehicleTurretSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;
    private readonly Dictionary<EntityUid, EntityUid> _visuals = new();
    private readonly Dictionary<EntityUid, Angle> _yaw = new();
    private TimeSpan _nextAim;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NWVehicleTurretComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<NWVehicleTurretComponent> ent, ref ComponentShutdown args)
    {
        if (_visuals.Remove(ent.Owner, out var visual) && Exists(visual)) QueueDel(visual);
        _yaw.Remove(ent.Owner);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        SendAim();
        var query = EntityQueryEnumerator<NWVehicleTurretComponent>();
        while (query.MoveNext(out var uid, out var turret))
        {
            if (!TryComp<SpriteComponent>(uid, out var hullSprite)) continue;
            if (!_visuals.TryGetValue(uid, out var visual) || !Exists(visual))
            {
                visual = Spawn(turret.Visual, new EntityCoordinates(uid, Vector2.Zero));
                _visuals[uid] = visual;
                _yaw[uid] = turret.WorldYaw;
            }
            // Smooth packet arrivals over about 50 ms; never rotate the hull's sprite layers.
            var old = _yaw[uid];
            var difference = Math.Abs(Math.Atan2(Math.Sin(turret.WorldYaw.Theta - old.Theta),
                Math.Cos(turret.WorldYaw.Theta - old.Theta)));
            var rendered = NWVehicleTurretMath.Step(old, turret.WorldYaw,
                difference * (1 - Math.Exp(-20 * frameTime)));
            _yaw[uid] = rendered;
            var hullRotation = _transform.GetWorldRotation(uid);
            var eyeRotation = _eye.CurrentEye.Rotation;
            // RMC's Humvee sheets use a different roof anchor in each hull direction.
            // Keep the anchor tied to the hull, not to the independently rotating gun.
            var localAnchor = Vector2.Zero;
            if (turret.Visual.Id == "NWvehicleM60TurretVisual")
            {
                var facing = GetRenderAlignedCardinalDir(hullRotation + eyeRotation);
                var pixels = facing switch
                {
                    Direction.North => new Vector2(0, 4),
                    Direction.East => new Vector2(-6, 16),
                    Direction.West => new Vector2(6, 16),
                    _ => new Vector2(0, 15)
                };
                localAnchor = (-hullRotation).RotateVec((-eyeRotation).RotateVec(pixels / 32f));
            }
            _transform.SetLocalPositionNoLerp(visual, localAnchor);
            _transform.SetLocalRotationNoLerp(visual, rendered - hullRotation);
            if (!TryComp<SpriteComponent>(visual, out var sprite)) continue;
            _sprites.SetVisible((visual, sprite), hullSprite.Visible);
            if (!turret.TankBarrelOffset) continue;
            // RMC's LTB barrel sheet is separate from the turret and has directional anchors.
            var dir = (rendered + _eye.CurrentEye.Rotation).GetCardinalDir();
            var offset = dir switch
            {
                Direction.North => new Vector2(0, 30),
                Direction.East => new Vector2(30, 0),
                Direction.West => new Vector2(-30, 0),
                _ => new Vector2(0, -50)
            };
            sprite.LayerSetOffset(1, (-dir.ToAngle()).RotateVec(offset / 32f));
        }
    }

    // Match the four-direction sprite selection, including its diagonal boundary bias.
    // Derived from RMC VehicleTurretDirectionHelpers.
    private static Direction GetRenderAlignedCardinalDir(Angle facing)
    {
        var angle = facing.Reduced().FlipPositive();
        var mod = (Math.Floor(angle.Theta / MathHelper.PiOver2) % 2) - 0.5;
        var theta = angle.Theta + mod * -0.05;
        return ((int) Math.Round(theta / MathHelper.PiOver2) % 4) switch
        {
            0 => Direction.South,
            1 => Direction.East,
            2 => Direction.North,
            _ => Direction.West
        };
    }

    private void SendAim()
    {
        if (_timing.CurTime < _nextAim || _player.LocalEntity is not { } user ||
            !TryComp<CombatModeComponent>(user, out var combat) || !combat.IsInCombatMode ||
            !_guns.TryGetGun(user, out var gun, out _) || !HasComp<NWVehicleTurretComponent>(gun)) return;
        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouse.MapId == MapId.Nullspace || mouse.MapId != Transform(gun).MapID) return;
        RaiseNetworkEvent(new NWVehicleAimRequest(GetNetEntity(gun), mouse.Position));
        _nextAim = _timing.CurTime + TimeSpan.FromSeconds(0.05);
    }

    public override void Shutdown()
    {
        foreach (var visual in _visuals.Values)
            if (Exists(visual)) QueueDel(visual);
        _visuals.Clear();
        _yaw.Clear();
        _nextAim = TimeSpan.Zero;
        base.Shutdown();
    }
}
