using Content.Shared._NW.Tents;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client._NW.Tents;

public sealed class NWTentRoofSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<NWTentComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tent, out var sprite, out var xform))
        {
            var inside = false;
            if (_player.LocalEntity is { } player && TryComp<TransformComponent>(player, out var playerXform)
                && playerXform.MapID == xform.MapID)
            {
                var local = (-_transform.GetWorldRotation(uid)).RotateVec(
                    _transform.GetWorldPosition(player) - _transform.GetWorldPosition(uid));
                inside = tent.Footprint.Contains(local);
            }
            foreach (var key in tent.RoofLayers)
            {
                if (!sprite.LayerMapTryGet(key, out var index) || !sprite.TryGetLayer(index, out var layer))
                    continue;
                var target = inside ? 0f : 1f;
                var alpha = layer.Color.A;
                var step = 6f * frameTime;
                alpha = target < alpha ? Math.Max(target, alpha - step) : Math.Min(target, alpha + step);
                sprite.LayerSetColor(index, layer.Color.WithAlpha(alpha));
            }
        }
    }
}
