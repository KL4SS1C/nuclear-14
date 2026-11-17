using System.Numerics;
using Robust.Shared.Maths;
using Content.Shared._NW.Vehicles;
using Content.Shared.CombatMode;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._NW.Vehicles;

public sealed class NWVehicleTurretSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NWVehicleTurretComponent, ComponentStartup>(OnStartup);
        SubscribeNetworkEvent<NWVehicleAimRequest>(OnAim);
    }

    private void OnStartup(Entity<NWVehicleTurretComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Initialized) return;
        ent.Comp.Initialized = true;
        ent.Comp.WorldYaw = _transform.GetWorldRotation(ent.Owner);
        Dirty(ent);
    }

    private bool CanAim(EntityUid user, EntityUid hull, NWVehicleComponent vehicle) =>
        vehicle.Gunner == user && !_mobs.IsIncapacitated(user) &&
        TryComp<CombatModeComponent>(user, out var combat) && combat.IsInCombatMode &&
        _guns.TryGetGun(user, out var gun, out _) && gun == hull;

    private void OnAim(NWVehicleAimRequest message, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is not { } user ||
            !float.IsFinite(message.Position.X) || !float.IsFinite(message.Position.Y)) return;
        var hull = GetEntity(message.Vehicle);
        if (!TryComp<NWVehicleTurretComponent>(hull, out var turret) ||
            !TryComp<NWVehicleComponent>(hull, out var vehicle) || !CanAim(user, hull, vehicle) ||
            _timing.CurTime < turret.NextAim) return;
        var delta = message.Position - _transform.GetMapCoordinates(hull).Position;
        if (delta.LengthSquared() < 0.0001f || delta.LengthSquared() > 128f * 128f) return;
        turret.Target = message.Position;
        turret.AimUser = user;
        turret.NextAim = _timing.CurTime + TimeSpan.FromSeconds(0.04);
        turret.AimExpires = _timing.CurTime + TimeSpan.FromSeconds(0.5);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<NWVehicleTurretComponent, NWVehicleComponent>();
        while (query.MoveNext(out var uid, out var turret, out var vehicle))
        {
            if (turret.Target is not { } target) continue;
            if (turret.AimUser is not { } user || !CanAim(user, uid, vehicle) ||
                _timing.CurTime > turret.AimExpires)
            {
                turret.Target = null;
                turret.AimUser = null;
                continue;
            }
            var delta = target - _transform.GetMapCoordinates(uid).Position;
            if (delta.LengthSquared() < 0.0001f) continue;
            var yaw = NWVehicleTurretMath.Step(turret.WorldYaw, delta.ToWorldAngle(),
                Math.Clamp(turret.DegreesPerSecond, 1f, 360f) * Math.PI / 180 * frameTime);
            if (yaw.Equals(turret.WorldYaw)) continue;
            turret.WorldYaw = yaw;
            Dirty(uid, turret);
        }
    }
}
