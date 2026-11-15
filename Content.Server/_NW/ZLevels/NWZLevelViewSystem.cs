using System.Linq;
using Content.Server._MultiZ.Core;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._NW.ZLevels;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._NW.ZLevels;

/// <summary>Sends nearby lower-floor entities to clients viewing a stacked NW map.</summary>
public sealed class NWZLevelViewSystem : EntitySystem
{
    [Dependency] private readonly MZSystem _levels = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedEyeSystem _eyes = default!;
    [Dependency] private readonly ViewSubscriberSystem _views = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly HashSet<ICommonSession> _sessions = new();
    private readonly Dictionary<ICommonSession, Dictionary<MapId, EntityUid>> _relays = new();
    private float _elapsed;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnDetached);
        // ActorComponent + EntParentChangedMessage is owned by the native MZPvsSystem.
        // Refresh lower-floor relays through attachment events and the existing 0.1 s poll.
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    private void OnAttached(PlayerAttachedEvent ev)
    {
        _sessions.Add(ev.Player);
        Refresh(ev.Player);
    }

    private void OnDetached(PlayerDetachedEvent ev)
    {
        _sessions.Remove(ev.Player);
        Clear(ev.Player);
    }

    private void OnRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var session in _relays.Keys.ToArray())
            Clear(session);
        _sessions.Clear();
        _elapsed = 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _elapsed += frameTime;
        if (_elapsed < 0.1f)
            return;
        _elapsed = 0;
        foreach (var session in _sessions.ToArray())
        {
            if (session.Status == SessionStatus.Disconnected)
            {
                _sessions.Remove(session);
                Clear(session);
            }
            else
                Refresh(session);
        }
    }

    private void Refresh(ICommonSession session)
    {
        if (!_cfg.GetCVar(MZCVars.Enabled) || session.AttachedEntity is not { } uid ||
            !TryComp(uid, out TransformComponent? xform) || xform.MapUid is not { } mapUid ||
            !HasComp<NWZLevelMapComponent>(mapUid) ||
            !TryComp<MZMapComponent>(mapUid, out var zMap))
        {
            Clear(session);
            return;
        }

        if (!_relays.TryGetValue(session, out var relays))
            _relays[session] = relays = new Dictionary<MapId, EntityUid>();

        var position = _transform.GetMapCoordinates(xform).Position;
        var scale = 1f;
        if (TryComp<EyeComponent>(uid, out var eye))
        {
            position += eye.Offset;
            scale = MathF.Max(eye.PvsScale, 1f);
        }
        var desired = new HashSet<MapId>();
        Entity<MZMapComponent> current = (mapUid, zMap);
        var depthLimit = Math.Clamp(_cfg.GetCVar(MZCVars.MaxRenderDepth), 0, 8);
        for (var i = 0; i < depthLimit; i++)
        {
            if (!_levels.TryMapDown((current.Owner, current.Comp), out var below) ||
                !TryComp<MapComponent>(below.Value.Owner, out var map) ||
                map.MapId == xform.MapID || !desired.Add(map.MapId))
                break;
            current = below.Value;
            var coords = new MapCoordinates(position, map.MapId);
            if (!relays.TryGetValue(map.MapId, out var relay) || TerminatingOrDeleted(relay))
            {
                relay = Spawn(null, coords);
                relays[map.MapId] = relay;
                var relayEye = EnsureComp<EyeComponent>(relay);
                _eyes.SetPvsScale((relay, relayEye), scale);
                _views.AddViewSubscriber(relay, session);
            }
            else
            {
                _transform.SetMapCoordinates(relay, coords);
                _eyes.SetPvsScale((relay, Comp<EyeComponent>(relay)), scale);
            }
        }
        foreach (var (map, relay) in relays.ToArray())
        {
            if (desired.Contains(map))
                continue;
            _views.RemoveViewSubscriber(relay, session);
            if (!TerminatingOrDeleted(relay))
                QueueDel(relay);
            relays.Remove(map);
        }
    }

    private void Clear(ICommonSession session)
    {
        if (!_relays.Remove(session, out var relays))
            return;
        foreach (var relay in relays.Values)
        {
            _views.RemoveViewSubscriber(relay, session);
            if (!TerminatingOrDeleted(relay))
                QueueDel(relay);
        }
    }
}
