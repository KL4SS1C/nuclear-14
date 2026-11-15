using Content.Server._MultiZ.Core;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Ghost;
using Content.Shared._NW.ZLevels;
using Content.Shared.Buckle.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._NW.ZLevels;

/// <summary>
/// Wakes native vertical physics after walking. Restricted to explicitly opted-in maps;
/// contained/buckled actors are never moved independently of their vehicle or container.
/// </summary>
public sealed class NWZLevelMovementSystem : EntitySystem
{
    [Dependency] private readonly MZSystem _levels = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private readonly List<EntityUid> _work = new();
    private float _floorCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateComponent, MoveEvent>(OnMobMove);
        SubscribeLocalEvent<MobStateComponent, ComponentStartup>(OnMobStartup);
        SubscribeLocalEvent<GhostComponent, MoveEvent>(OnGhostMove);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    private void OnMobMove(Entity<MobStateComponent> ent, ref MoveEvent args) => _pending.Add(ent.Owner);
    private void OnMobStartup(Entity<MobStateComponent> ent, ref ComponentStartup args) => _pending.Add(ent.Owner);
    private void OnGhostMove(Entity<GhostComponent> ent, ref MoveEvent args) => _pending.Add(ent.Owner);
    private void OnAttached(PlayerAttachedEvent ev)
    {
        if (ev.Player.AttachedEntity is { } uid)
            _pending.Add(uid);
    }

    private void OnRestart(RoundRestartCleanupEvent ev)
    {
        _pending.Clear();
        _work.Clear();
        _floorCheck = 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Recheck stationary participants, including a floor removed underneath a mob.
        _floorCheck += frameTime;
        if (_floorCheck >= 0.2f)
        {
            _floorCheck = 0;
            var query = EntityQueryEnumerator<NWZLevelParticipantComponent>();
            while (query.MoveNext(out var uid, out _))
                _pending.Add(uid);
        }

        _work.Clear();
        _work.AddRange(_pending);
        _pending.Clear();
        foreach (var uid in _work)
            Refresh(uid);
        _work.Clear();
    }

    private void Refresh(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || !TryComp(uid, out TransformComponent? xform))
            return;

        if (!_cfg.GetCVar(MZCVars.Enabled) || xform.MapUid is not { } map ||
            !HasComp<NWZLevelMapComponent>(map) || !HasComp<MZMapComponent>(map))
        {
            Cleanup(uid);
            return;
        }

        if (HasComp<GhostComponent>(uid))
        {
            var ghostState = EnsureComp<NWZLevelParticipantComponent>(uid);
            if (!HasComp<MZGhostMoverComponent>(uid))
            {
                EnsureComp<MZGhostMoverComponent>(uid);
                ghostState.AddedGhostMover = true;
            }
            return;
        }

        if (!HasComp<MobStateComponent>(uid) || !HasComp<PhysicsComponent>(uid) ||
            xform.ParentUid != map ||
            (TryComp<BuckleComponent>(uid, out var buckle) && buckle.BuckledTo != null))
        {
            Cleanup(uid);
            return;
        }

        var state = EnsureComp<NWZLevelParticipantComponent>(uid);
        if (!TryComp<MZPhysicsComponent>(uid, out var physics))
        {
            physics = EnsureComp<MZPhysicsComponent>(uid);
            // Living actors should settle rather than bounce on each stair.
            physics.Bounciness = 0;
            Dirty(uid, physics);
            state.AddedPhysics = true;
        }
        _levels.WakeZPhysics((uid, physics));
    }

    private void Cleanup(EntityUid uid)
    {
        if (!TryComp<NWZLevelParticipantComponent>(uid, out var state))
            return;
        if (state.AddedPhysics)
        {
            RemComp<MZFallingComponent>(uid);
            RemComp<MZPhysicsComponent>(uid);
        }
        if (state.AddedGhostMover)
            RemComp<MZGhostMoverComponent>(uid);
        RemComp<NWZLevelParticipantComponent>(uid);
    }
}

[RegisterComponent, UnsavedComponent]
public sealed partial class NWZLevelParticipantComponent : Component
{
    public bool AddedPhysics;
    public bool AddedGhostMover;
}
