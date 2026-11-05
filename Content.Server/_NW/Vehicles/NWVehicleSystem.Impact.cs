using System.Numerics;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.Destructible.Thresholds.Triggers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Content.Shared._NW.Vehicles;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Climbing.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Events;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;

    private void InitializeImpact()
    {
        SubscribeLocalEvent<NWGroundVehicleComponent, DestructionEventArgs>(OnVehicleDestroyed);
        SubscribeLocalEvent<NWGroundVehicleComponent, StartCollideEvent>(OnGroundImpact);
    }

    private void OnVehicleDestroyed(EntityUid uid, NWGroundVehicleComponent ground, DestructionEventArgs args)
    {
        if (ground.WreckPrototype is not { } prototype)
            return;

        if (TryComp<NWVehicleComponent>(uid, out var vehicle))
            ReturnIgnitionKey((uid, vehicle));

        ground.WreckPrototype = null;
        var wreck = Spawn(prototype, Transform(uid).Coordinates);
        _transform.SetWorldRotation(wreck, _transform.GetWorldRotation(uid));
        TransferCabinToWreck(uid, wreck);
    }

    private void OnGroundImpact(Entity<NWGroundVehicleComponent> ent, ref StartCollideEvent args)
    {
        if (!TryComp<NWVehicleComponent>(ent, out var vehicle) || vehicle.State != NWVehicleState.Running)
            return;

        // On a solid hull the solver may already have removed the incoming velocity.
        var impactVelocity = args.OurBody.LinearVelocity;
        if (vehicle.DriftVelocity.LengthSquared() > impactVelocity.LengthSquared())
            impactVelocity = vehicle.DriftVelocity;
        var speed = impactVelocity.Length();
        if (HasComp<MobStateComponent>(args.OtherEntity))
        {
            TryRunOver((ent.Owner, vehicle), ent.Comp, args.OtherEntity, speed, impactVelocity);
            return;
        }

        if (TryDamageObstacle((ent.Owner, vehicle), ent.Comp, args.OtherEntity, speed))
            return;

        if (!args.OurFixture.Hard || !args.OtherFixture.Hard || speed <= 4f ||
            _timing.CurTime < ent.Comp.NextImpactTime)
            return;
        ent.Comp.NextImpactTime = _timing.CurTime + TimeSpan.FromSeconds(1.25);
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(MathF.Min(18f, (speed - 4f) * 3f));
        _damageable.TryChangeDamage(ent.Owner, damage, origin: args.OtherEntity);
        vehicle.HeldInputs = NWVehicleControlInput.None;
        vehicle.DriftVelocity = Vector2.Zero;
        _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: args.OurBody);
    }

    private void UpdateRunOver(Entity<NWVehicleComponent> ent, NWGroundVehicleComponent ground, float frameTime)
    {
        var current = _transform.GetMapCoordinates(ent.Owner);
        var previous = ground.PreviousPosition;
        ground.PreviousPosition = current;
        foreach (var victim in ground.NextRunOver.Where(pair => pair.Value <= _timing.CurTime).Select(pair => pair.Key).ToArray())
            ground.NextRunOver.Remove(victim);

        foreach (var obstacle in ground.NextObstacleHit.Where(pair => pair.Value <= _timing.CurTime).Select(pair => pair.Key).ToArray())
            ground.NextObstacleHit.Remove(obstacle);

        if (ent.Comp.State != NWVehicleState.Running || previous is not { } start ||
            start.MapId != current.MapId || frameTime <= 0f)
            return;
        var travel = current.Position - start.Position;
        var distance = travel.Length();
        var speed = distance / frameTime;
        // Ignore teleports/map transfers and standing still; damage is based on actual travel.
        if (distance > (ent.Comp.MaxSpeed + ground.MaxReverseSpeed + 2f) * frameTime + 0.5f)
            return;
        if (distance > 0.0001f)
            PlowSnow(ent.Owner, current, travel);
        if (speed < MathF.Min(ground.RunOverMinimumSpeed, MathF.Min(ground.ObstacleMinimumSpeed, ground.BarrierMinimumSpeed)))
            return;
        var midpoint = new MapCoordinates((current.Position + start.Position) * 0.5f, current.MapId);
        var hull = _lookup.GetWorldAABB(ent.Owner);
        var halfSize = MathF.Abs(hull.Width - hull.Height) < 0.001f
            ? new Vector2(ground.RunOverRadius)
            : new Vector2(hull.Width, hull.Height) * 0.5f;
        var cornerRadius = halfSize.Length();
        var candidates = _lookup.GetEntitiesInRange(midpoint, distance * 0.5f + cornerRadius).ToHashSet();
        // Signs may only have an anchor, with no collision fixture to report a hit.
        var transform = Transform(ent.Owner);
        if (speed >= MathF.Min(ground.ObstacleMinimumSpeed, ground.BarrierMinimumSpeed) && (transform.GridUid ?? transform.MapUid) is { } grid &&
            TryComp<MapGridComponent>(grid, out var gridComp))
        {
            var first = _map.WorldToTile(grid, gridComp, start.Position);
            var last = _map.WorldToTile(grid, gridComp, current.Position);
            var padding = (int) MathF.Ceiling(cornerRadius / gridComp.TileSize);
            for (var x = Math.Min(first.X, last.X) - padding; x <= Math.Max(first.X, last.X) + padding; x++)
            for (var y = Math.Min(first.Y, last.Y) - padding; y <= Math.Max(first.Y, last.Y) + padding; y++)
            {
                var anchors = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, new Vector2i(x, y));
                while (anchors.MoveNext(out var anchor))
                {
                    if (HasComp<DestructibleComponent>(anchor.Value))
                        candidates.Add(anchor.Value);
                }
            }
        }

        foreach (var victim in candidates)
        {
            if (!Exists(victim) || Terminating(victim))
                continue;
            var point = _transform.GetMapCoordinates(victim);
            if (point.MapId != current.MapId ||
                !NWVehicleImpactMath.InSweptRectangle(point.Position, start.Position, current.Position, halfSize))
                continue;
            if (HasComp<MobStateComponent>(victim))
                TryRunOver(ent, ground, victim, speed, travel);
            else
                TryDamageObstacle(ent, ground, victim, speed);
        }
    }

    private void TryRunOver(Entity<NWVehicleComponent> ent, NWGroundVehicleComponent ground, EntityUid victim, float speed, Vector2 direction)
    {
        if (speed < ground.RunOverMinimumSpeed || victim == ent.Owner || !Exists(victim) ||
            _mobState.IsDead(victim) || !HasComp<DamageableComponent>(victim) ||
            ent.Comp.SeatOccupants.Contains(victim) ||
            TryComp<BuckleComponent>(victim, out var buckle) && buckle.BuckledTo == ent.Owner ||
            ground.NextRunOver.TryGetValue(victim, out var next) && _timing.CurTime < next)
            return;

        ground.NextRunOver[victim] = _timing.CurTime + TimeSpan.FromSeconds(ground.RunOverCooldown);
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(MathF.Min(ground.RunOverMaxDamage, speed * ground.RunOverDamagePerSpeed));
        _damageable.TryChangeDamage(victim, damage, origin: ent.Owner);
        if (!Exists(victim) || Terminating(victim) || direction.LengthSquared() <= 0.0001f ||
            !TryComp<PhysicsComponent>(victim, out var body) ||
            (body.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) == 0)
            return;

        // Use a contact impulse so a vehicle hit does not raise a character's ThrownEvent.
        // Retain the existing per-vehicle knockback tuning and let ground friction stop the body.
        var distance = MathF.Min(ground.MaxPushDistance, speed * ground.PushDistancePerSpeed);
        const float pushDuration = 0.4f;
        var targetSpeed = Math.Clamp(distance / pushDuration, 0f, 8f);
        var normal = Vector2.Normalize(direction);
        var deltaSpeed = targetSpeed - Vector2.Dot(body.LinearVelocity, normal);
        if (deltaSpeed > 0f && distance > 0f)
            _physics.ApplyLinearImpulse(victim, normal * (deltaSpeed * body.Mass), body: body);
    }

    private bool TryDamageObstacle(Entity<NWVehicleComponent> ent, NWGroundVehicleComponent ground, EntityUid obstacle, float speed)
    {
        if (obstacle == ent.Owner || !Exists(obstacle) ||
            Terminating(obstacle) || HasComp<MobStateComponent>(obstacle) || HasComp<NWVehicleComponent>(obstacle) ||
            !TryComp<DamageableComponent>(obstacle, out var damageable) || !TryComp<DestructibleComponent>(obstacle, out var destructible))
            return false;
        if (MetaData(obstacle).EntityPrototype is not { } prototype || HasComp<DoorComponent>(obstacle))
            return false;

        var barrier = HasComp<ClimbableComponent>(obstacle) || ground.FragileBarriers.Contains(prototype.ID);
        if (speed < (barrier ? ground.BarrierMinimumSpeed : ground.ObstacleMinimumSpeed) ||
            (!barrier && !ground.BreakableObstacles.Contains(prototype.ID)))
            return false;

        var limit = barrier ? ground.ObstacleMaxDamage : ground.BreakableMaxThreshold;
        var thresholdDamage = int.MaxValue;
        foreach (var threshold in destructible.Thresholds)
        {
            if (threshold.Trigger is DamageTrigger trigger && trigger.Damage <= limit &&
                threshold.Behaviors.Any(behavior => behavior is DoActsBehavior acts && acts.HasAct(ThresholdActs.Destruction)))
                thresholdDamage = Math.Min(thresholdDamage, trigger.Damage);
        }

        if (thresholdDamage == int.MaxValue)
            return false;
        if (ground.NextObstacleHit.TryGetValue(obstacle, out var next) && _timing.CurTime < next)
            return true;
        ground.NextObstacleHit[obstacle] = _timing.CurTime + TimeSpan.FromSeconds(1);
        var damage = new DamageSpecifier();
        var impactType = damageable.Damage.DamageDict.ContainsKey("Structural") ? "Structural" : "Blunt";
        damage.DamageDict[impactType] = FixedPoint2.New(barrier ? thresholdDamage : MathF.Min(ground.ObstacleMaxDamage, speed * ground.ObstacleDamagePerSpeed));
        _damageable.TryChangeDamage(obstacle, damage, ignoreResistances: true, origin: ent.Comp.Pilot ?? ent.Owner);
        // Keep throttle held. Normal collision physics still blocks until destruction removes the obstacle.
        return true;
    }
}
