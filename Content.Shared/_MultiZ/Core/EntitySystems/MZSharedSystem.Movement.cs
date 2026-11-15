// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
//   PR #1058 "Multi Z addition" & #1119 "Multi z fixes" by TheHellFireo
//   Based on Crystall Edge (crystallpunk-14) Multi-Z system
//   Performance refactors from TTMC (ttmc14)
// Ported to misfits-14 _MultiZ/ — renamed &amp; adapted
// #Cythisiax Ported — Multi-Z level support for misfits-14

using System.Numerics;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Stunnable;
using JetBrains.Annotations;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._MultiZ.Core.EntitySystems;

public abstract partial class MZSharedSystem
{
    public const int MaxZLevelsBelowRendering = 8;

    private const float ZGravityForce = 9.8f;
    private const float ZVelocityLimit = 20.0f;
    protected const float MinActiveZVelocity = 0.05f;

    /// <summary>
    /// Maximum height at which a player will automatically climb higher when stepping on a highground entity.
    /// </summary>
    private const float MaxStepHeight = 0.5f;

    private const float GroundSnapDistance = 0.05f;
    private const float ZPhysicsSleepDistance = 0.05f;
    private const float StickyMoveSnapUpTransitionHeight = 0.95f;

    /// <summary>
    /// How far past a tile edge high ground is allowed to support an entity.
    /// </summary>
    private const float HighGroundEdgeSupport = 0.35f;

    private const float ImpactVelocityLimit = 4.0f;
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<MZHighGroundComponent> _highgroundQuery;
    private readonly List<EntityUid> _zMovementUpdateQueue = new();
    private bool _movementInitialized;

    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private void InitMovement()
    {
        if (_movementInitialized)
            return;

        _movementInitialized = true;
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _highgroundQuery = GetEntityQuery<MZHighGroundComponent>();

    }

    [Dependency] private readonly SharedStunSystem _stun = default!;

    private void RaiseZLevelHit(EntityUid uid, float impactPower)
    {
        ApplyFallDamage(uid, impactPower);
        RaiseLocalEvent(uid, new MZLevelHitEvent(impactPower));
    }

    private void ApplyFallDamage(EntityUid uid, float impactPower)
    {
        if (!HasComp<DamageableComponent>(uid))
            return;

        // CMU's human landing behavior: blunt damage = speed squared, with a capped knockdown.
        // Do not duplicate damage in a second event handler.
        _stun.TryKnockdown(uid, TimeSpan.FromSeconds(MathF.Min(impactPower * 0.25f, 5f)), true);
        var damageType = _proto.Index(BluntDamageType);
        var damageAmount = MathF.Pow(impactPower, 2);
        if (damageAmount <= 0f)
            return;

        _damage.TryChangeDamage(uid, new DamageSpecifier(damageType, damageAmount));
    }

    // ── Wake / Sleep ─────────────────────────────────────────────────────

    public virtual void WakeZPhysics(Entity<MZPhysicsComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        if (!HasComp<MZFallingComponent>(ent))
            EnsureComp<MZFallingComponent>(ent);
    }

    protected static bool ShouldSleepZPhysics(
        float distanceToGround, bool stickyGround, float localPosition, float velocity)
    {
        if (MathF.Abs(velocity) > MinActiveZVelocity)
            return false;

        if (MathF.Abs(distanceToGround) > ZPhysicsSleepDistance)
            return false;

        return stickyGround || MathF.Abs(localPosition) <= ZPhysicsSleepDistance;
    }

    // ── Ground Contact ────────────────────────────────────────────────────

    private static bool ShouldSnapToGround(float distanceToGround, bool stickyGround)
    {
        if (stickyGround)
            return true;

        return distanceToGround >= -MaxStepHeight && distanceToGround <= GroundSnapDistance;
    }

    // CMU handles a fast fall that has crossed below the surface in a single tick.
    private static bool ShouldTreatAsFallingGroundContact(float distanceToGround, float velocity)
        => velocity <= 0f && distanceToGround < -MaxStepHeight;

    private static float GetGroundSnapDistance(float distanceToGround, bool stickyGround)
    {
        if (!ShouldSnapToGround(distanceToGround, stickyGround))
            return 0f;

        return distanceToGround;
    }

    // ── Distance To Ground ────────────────────────────────────────────────

    /// <summary>
    /// Returns the distance to the floor. Returns maxFloors if the distance is too great.
    /// </summary>
    [PublicAPI]
    public float DistanceToGround(Entity<MZPhysicsComponent?> target, out bool stickyGround, int maxFloors = 1)
    {
        if (!Resolve(target, ref target.Comp, false))
        {
            stickyGround = false;
            return 0;
        }

        var xform = Transform(target);
        var worldPos = _transform.GetWorldPosition(xform);
        return DistanceToGroundAtWorldPositionCore(target, xform.MapUid, worldPos, out stickyGround, maxFloors);
    }

    /// <summary>
    /// Returns the distance to ground for an arbitrary world-space sample.
    /// </summary>
    [PublicAPI]
    public float DistanceToGroundAtWorldPosition(
        Entity<MZPhysicsComponent?> target, Vector2 worldPosition,
        out bool stickyGround, int maxFloors = 1)
    {
        return DistanceToGroundAtWorldPositionCore(target, Transform(target).MapUid, worldPosition, out stickyGround, maxFloors);
    }

    private float DistanceToGroundAtWorldPositionCore(
        Entity<MZPhysicsComponent?> target, EntityUid? mapUid, Vector2 worldPos,
        out bool stickyGround, int maxFloors)
    {
        stickyGround = false;
        if (!Resolve(target, ref target.Comp, false))
            return 0;

        if (mapUid is not { } resolvedMap || !_zMapQuery.TryComp(resolvedMap, out var zMapComp))
            return 0;

        if (!GridQuery.TryComp(resolvedMap, out var mapGrid))
            return 0;

        Entity<MZMapComponent> checkingMap = (resolvedMap, zMapComp);
        MapGridComponent checkingGrid = mapGrid;

        for (var floor = 0; floor <= maxFloors; floor++)
        {
            if (floor != 0)
            {
                if (!TryMapDown((checkingMap.Owner, checkingMap.Comp), out var tempCheckingMap))
                    break;

                checkingMap = tempCheckingMap.Value;
                if (!GridQuery.TryComp(checkingMap.Owner, out var tempCheckingGrid))
                    continue;

                checkingGrid = tempCheckingGrid;
            }

            var checkingTile = _map.WorldToTile(checkingMap, checkingGrid, worldPos);

            // Check highground entities first
            if (TryGetHighGroundDistance(target, checkingMap, checkingGrid, checkingTile, worldPos, floor, out var highGroundDistance, ref stickyGround))
                return highGroundDistance;

            // Check floor tiles
            if (_map.TryGetTileRef(checkingMap, checkingGrid, checkingTile, out var tileRef) && !tileRef.Tile.IsEmpty)
                return target.Comp.LocalPosition + floor;
        }

        return maxFloors;
    }

    private bool TryGetHighGroundDistance(
        Entity<MZPhysicsComponent?> target,
        Entity<MZMapComponent> checkingMap, MapGridComponent checkingGrid,
        Vector2i checkingTile, Vector2 worldPos, int floor,
        out float distance, ref bool stickyGround)
    {
        distance = 0f;
        var found = false;
        var bestDistance = 0f;
        var bestSticky = false;
        var bestScore = float.MaxValue;
        var bestIsCurrentTile = false;
        var gridLocal = _map.WorldToLocal(checkingMap, checkingGrid, worldPos) / checkingGrid.TileSize;

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                var tile = checkingTile + new Vector2i(x, y);
                var isCurrentTile = x == 0 && y == 0;
                var query = _map.GetAnchoredEntitiesEnumerator(checkingMap, checkingGrid, tile);

                while (query.MoveNext(out var uid))
                {
                    if (!_highgroundQuery.TryComp(uid, out var heightComp))
                        continue;

                    if (floor == 0 && heightComp.SupportOnlyFromAbove)
                        continue;

                    if (heightComp.HeightCurve.Count == 0)
                        continue;

                    var local = gridLocal - new Vector2(tile.X, tile.Y);
                    if (!TryGetHighGroundCurveT(uid.Value, heightComp, local, isCurrentTile, out var t))
                        continue;

                    var candidateDistance = GetHighGroundDistance(target.Comp!, heightComp, t, floor);
                    var score = MathF.Abs(candidateDistance);

                    if (!ShouldReplaceHighGroundCandidate(isCurrentTile, score, found, bestIsCurrentTile, bestScore))
                        continue;

                    found = true;
                    bestScore = score;
                    bestIsCurrentTile = isCurrentTile;
                    bestDistance = candidateDistance;
                    bestSticky = ShouldUseStickyGround(isCurrentTile, target.Comp!.Velocity, heightComp);
                }
            }
        }

        if (!found)
            return false;

        distance = bestDistance;
        stickyGround = bestSticky;
        return true;
    }

    private bool TryGetHighGroundCurveT(
        EntityUid highGround, MZHighGroundComponent heightComp, Vector2 local, bool isCurrentTile, out float t)
    {
        t = 0f;

        if (isCurrentTile)
        {
            if (local.X < 0f || local.X > 1f || local.Y < 0f || local.Y > 1f)
                return false;

            t = GetHighGroundCurveT(highGround, heightComp, local);
            return true;
        }

        if (IsFlatHighGround(heightComp))
        {
            if (local.X < -HighGroundEdgeSupport || local.X > 1f + HighGroundEdgeSupport ||
                local.Y < -HighGroundEdgeSupport || local.Y > 1f + HighGroundEdgeSupport)
                return false;

            t = GetHighGroundCurveT(highGround, heightComp, local);
            return true;
        }

        if (heightComp.Corner)
        {
            if (local.X < -HighGroundEdgeSupport || local.X > 1f + HighGroundEdgeSupport ||
                local.Y < -HighGroundEdgeSupport || local.Y > 1f + HighGroundEdgeSupport)
                return false;

            t = GetHighGroundCurveT(highGround, heightComp, local);
            return IsNearHighGroundTopEdge(heightComp, t);
        }

        if (!TryGetHighGroundRampAxes(highGround, local, out var ramp, out var side))
            return false;

        if (!IsNearHighGroundTopEdge(heightComp, ramp) ||
            side < -HighGroundEdgeSupport || side > 1f + HighGroundEdgeSupport)
            return false;

        t = ramp;
        return true;
    }

    private float GetHighGroundCurveT(EntityUid highGround, MZHighGroundComponent heightComp, Vector2 local)
    {
        if (heightComp.Corner)
        {
            var dir = _transform.GetWorldRotation(highGround).GetCardinalDir();
            return dir switch
            {
                Direction.East => (local.X + 1f - local.Y) / 2f,
                Direction.West => (1f - local.X + local.Y) / 2f,
                Direction.North => (local.X + local.Y) / 2f,
                Direction.South => (1f - local.X + 1f - local.Y) / 2f,
                _ => 0.5f,
            };
        }

        if (TryGetHighGroundRampAxes(highGround, local, out var ramp, out _))
            return ramp;

        return 0.5f;
    }

    private bool TryGetHighGroundRampAxes(EntityUid highGround, Vector2 local, out float ramp, out float side)
    {
        var dir = _transform.GetWorldRotation(highGround).GetCardinalDir();
        (ramp, side) = dir switch
        {
            Direction.East => (local.X, local.Y),
            Direction.West => (1f - local.X, local.Y),
            Direction.North => (local.Y, local.X),
            Direction.South => (1f - local.Y, local.X),
            _ => (0.5f, 0.5f),
        };

        return dir is Direction.East or Direction.West or Direction.North or Direction.South;
    }

    private static bool IsFlatHighGround(MZHighGroundComponent heightComp)
    {
        if (heightComp.HeightCurve.Count <= 1)
            return true;

        var first = heightComp.HeightCurve[0];
        for (var i = 1; i < heightComp.HeightCurve.Count; i++)
        {
            if (MathF.Abs(heightComp.HeightCurve[i] - first) > 0.01f)
                return false;
        }

        return true;
    }

    private static bool IsNearHighGroundTopEdge(MZHighGroundComponent heightComp, float t)
    {
        if (heightComp.HeightCurve.Count <= 1)
            return t >= -HighGroundEdgeSupport && t <= 1f + HighGroundEdgeSupport;

        var first = heightComp.HeightCurve[0];
        var last = heightComp.HeightCurve[^1];

        if (first > last + 0.01f)
            return t >= -HighGroundEdgeSupport && t <= 0f;

        return t >= 1f && t <= 1f + HighGroundEdgeSupport;
    }

    private float GetHighGroundDistance(MZPhysicsComponent zPhysics, MZHighGroundComponent heightComp, float t, int floor)
    {
        t = Math.Clamp(t, 0f, 1f);

        var curve = heightComp.HeightCurve;
        if (curve.Count == 1)
            return zPhysics.LocalPosition + floor - curve[0];

        var step = 1f / (curve.Count - 1);
        var index = (int)(t / step);
        var frac = (t - index * step) / step;

        var y0 = curve[Math.Clamp(index, 0, curve.Count - 1)];
        var y1 = curve[Math.Clamp(index + 1, 0, curve.Count - 1)];

        return zPhysics.LocalPosition + floor - MathHelper.Lerp(y0, y1, frac);
    }

    private static bool ShouldUseStickyGround(bool isCurrentTile, float velocity, MZHighGroundComponent heightComp)
    {
        return velocity <= 0.01f && velocity > -4f && heightComp.Stick;
    }

    private static bool ShouldReplaceHighGroundCandidate(
        bool isCurrentTile, float score, bool found, bool bestIsCurrentTile, float bestScore)
    {
        if (!found)
            return true;

        if (isCurrentTile != bestIsCurrentTile)
            return isCurrentTile;

        return score < bestScore;
    }

    // ── Boundary Transitions ──────────────────────────────────────────────

    protected virtual bool CanProcessZLevelTransition(EntityUid ent, int offset)
    {
        return true;
    }

    private static bool ShouldProcessDownBoundary(float localPosition)
    {
        return localPosition < -ZPhysicsSleepDistance;
    }

    private void TryProcessZLevelBoundary(EntityUid uid, MZPhysicsComponent zPhys, bool stickyGround)
    {
        if (zPhys.LocalPosition < 0f && !ShouldProcessDownBoundary(zPhys.LocalPosition))
            return;

        if (zPhys.LocalPosition < 0) // Falling down to Z-level below
        {
            if (CanProcessZLevelTransition(uid, -1))
            {
                if (TryMoveDown(uid))
                {
                    zPhys.LocalPosition += 1;

                    if (!stickyGround)
                    {
                        var fallEv = new MZLevelFallEvent();
                        RaiseLocalEvent(uid, fallEv);
                    }

                    TryClampToGroundAfterDownTransition(uid, zPhys);
                }
            }
            else
            {
                zPhys.LocalPosition = 0;
            }
            return;
        }

        if (zPhys.LocalPosition < 1)
            return;

        if (HasTileAbove(uid)) // Hit roof
        {
            if (MathF.Abs(zPhys.Velocity) >= ImpactVelocityLimit)
            {
                RaiseZLevelHit(uid, MathF.Abs(zPhys.Velocity));
            }

            zPhys.LocalPosition = 1;
            zPhys.Velocity = -zPhys.Velocity * zPhys.Bounciness;
            return;
        }

        if (CanProcessZLevelTransition(uid, 1))
        {
            if (TryMoveUp(uid))
                zPhys.LocalPosition -= 1;
        }
        else
        {
            zPhys.LocalPosition = 1;
        }
    }

    private bool TryClampToGroundAfterDownTransition(EntityUid uid, MZPhysicsComponent zPhys)
    {
        var distanceToGround = DistanceToGround((uid, zPhys), out var stickyGround);
        if (distanceToGround > GroundSnapDistance)
            return false;

        if (zPhys.LocalPosition >= 0f ||
            (!ShouldSnapToGround(distanceToGround, stickyGround) &&
             !ShouldTreatAsFallingGroundContact(distanceToGround, zPhys.Velocity)))
            return false;

        zPhys.LocalPosition -= distanceToGround;

        if (TryComp<PhysicsComponent>(uid, out var physics) && physics.BodyStatus != BodyStatus.OnGround)
            _physics.SetBodyStatus(uid, physics, BodyStatus.OnGround);

        if (MathF.Abs(zPhys.Velocity) >= ImpactVelocityLimit)
            RaiseZLevelHit(uid, MathF.Abs(zPhys.Velocity));

        if (stickyGround)
            zPhys.Velocity = 0f;
        else
            zPhys.Velocity = -zPhys.Velocity * zPhys.Bounciness;

        if (MathF.Abs(zPhys.Velocity) <= MinActiveZVelocity)
        {
            zPhys.Velocity = 0f;
            if (ShouldSleepZPhysics(0f, stickyGround, zPhys.LocalPosition, zPhys.Velocity))
                RemComp<MZFallingComponent>(uid);
        }

        return true;
    }

    private void StopZMovement(EntityUid uid, MZPhysicsComponent zPhys)
    {
        zPhys.Velocity = 0;
        zPhys.LocalPosition = 0;
        RemComp<MZFallingComponent>(uid);
    }

    // ── Main Update Loop ──────────────────────────────────────────────────

    protected void UpdateZMovement(float frameTime)
    {
        _zMovementUpdateQueue.Clear();
        var query = EntityQueryEnumerator<MZPhysicsComponent, MZFallingComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out _, out _, out _))
        {
            _zMovementUpdateQueue.Add(uid);
        }

        foreach (var uid in _zMovementUpdateQueue)
        {
            if (!TryComp<MZPhysicsComponent>(uid, out var zPhys) ||
                !HasComp<MZFallingComponent>(uid) ||
                !TryComp(uid, out TransformComponent? xform) ||
                !TryComp<PhysicsComponent>(uid, out var physics))
                continue;

            if (xform.ParentUid != xform.MapUid)
            {
                StopZMovement(uid, zPhys);
                continue;
            }

            if (!_zMapQuery.HasComp(xform.MapUid))
            {
                StopZMovement(uid, zPhys);
                continue;
            }

            // Vertical gravity must continue after leaving the edge of a floor.
            zPhys.Velocity -= ZGravityForce * frameTime;

            // Apply velocity
            zPhys.LocalPosition += zPhys.Velocity * frameTime;

            var distanceToGround = DistanceToGround((uid, zPhys), out var stickyGround);
            var hasGroundContact = ShouldSnapToGround(distanceToGround, stickyGround);
            var fallingGroundContact = !hasGroundContact &&
                ShouldTreatAsFallingGroundContact(distanceToGround, zPhys.Velocity);
            var groundSnapDistance = fallingGroundContact
                ? distanceToGround
                : GetGroundSnapDistance(distanceToGround, stickyGround);
            hasGroundContact |= fallingGroundContact;

            if (hasGroundContact && physics.BodyStatus != BodyStatus.OnGround)
                _physics.SetBodyStatus(uid, physics, BodyStatus.OnGround);

            if (hasGroundContact)
            {
                zPhys.LocalPosition -= groundSnapDistance;
                if (stickyGround)
                    zPhys.Velocity = 0;
            }

            if (hasGroundContact && zPhys.Velocity <= 0f) // Hit ground
            {
                if (MathF.Abs(zPhys.Velocity) >= ImpactVelocityLimit)
                {
                    RaiseZLevelHit(uid, MathF.Abs(zPhys.Velocity));
                }

                zPhys.Velocity = -zPhys.Velocity * zPhys.Bounciness;

                if (MathF.Abs(zPhys.Velocity) <= MinActiveZVelocity)
                {
                    zPhys.Velocity = 0;
                    if (ShouldSleepZPhysics(0f, stickyGround, zPhys.LocalPosition, zPhys.Velocity))
                    {
                        // A sticky ramp can put us on the next floor while velocity is zero.
                        // Process the boundary before sleeping, including on the way down.
                        TryProcessZLevelBoundary(uid, zPhys, stickyGround);
                        Dirty(uid, zPhys);
                        RemComp<MZFallingComponent>(uid);
                        continue;
                    }
                }
            }

            TryProcessZLevelBoundary(uid, zPhys, stickyGround);

            if (Math.Abs(zPhys.Velocity) > ZVelocityLimit)
                zPhys.Velocity = MathF.Sign(zPhys.Velocity) * ZVelocityLimit;

            Dirty(uid, zPhys);
        }

        _zMovementUpdateQueue.Clear();
    }

    // ── Velocity / Position Setters ───────────────────────────────────────

    [PublicAPI]
    public void SetZVelocity(Entity<MZPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.Velocity = newVelocity;
        WakeZPhysics(ent);
    }

    [PublicAPI]
    public void SetZLocalPosition(Entity<MZPhysicsComponent?> ent, float localPosition)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        if (Math.Abs(ent.Comp.LocalPosition - localPosition) <= 0.01f)
            return;

        ent.Comp.LocalPosition = localPosition;
        WakeZPhysics(ent);
    }

    [PublicAPI]
    public void AddZVelocity(Entity<MZPhysicsComponent?> ent, float delta)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        ent.Comp.Velocity += delta;
        WakeZPhysics(ent);
    }
}
