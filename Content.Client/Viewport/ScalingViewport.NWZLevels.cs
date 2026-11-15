// Adapted from CMU ScalingViewport.CMUZLevels: lowest-to-highest viewport passes.
// Source: KL4SS1C/ColonialMarinesUniverse-for-import, 06e5156ada6a04e111fb2dd4b2c2f07561aae0d2.
// This smaller integration omits CMU's look-up, sprite-culling and projected-light features.
using Content.Client._MultiZ.Core;
using Content.Client._NW.ZLevels;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core.Components;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    private readonly List<MapId> _nwZPasses = new();

    private bool TryRenderNWZStack(EntityUid mapUid, MZMapComponent zMap, IEye eye)
    {
        if (_viewport == null ||
            !_mzEntMan.TryGetComponent<MapComponent>(mapUid, out var activeMap) ||
            eye.Position.MapId != activeMap.MapId)
            return false;

        _nwZPasses.Clear();
        _nwZPasses.Add(eye.Position.MapId);
        var system = _mzEntMan.System<MZClientSystem>();
        Entity<MZMapComponent> current = (mapUid, zMap);
        var maxDepth = Math.Clamp(_mzCfg.GetCVar(MZCVars.MaxRenderDepth), 0, 8);
        for (var depth = 0; depth < maxDepth; depth++)
        {
            if (!system.TryMapDown((current.Owner, current.Comp), out var below) ||
                !_mzEntMan.TryGetComponent<MapComponent>(below.Value.Owner, out var map))
                break;
            // A malformed network must not keep rendering the same map.
            if (_nwZPasses.Contains(map.MapId))
                break;
            _nwZPasses.Add(map.MapId);
            current = below.Value;
        }
        if (_nwZPasses.Count == 1)
            return false;

        var savedEye = _viewport.Eye;
        var savedClear = _viewport.ClearColor;
        var savedParallax = NWZRenderContext.SkipParallax;
        try
        {
            for (var i = _nwZPasses.Count - 1; i >= 0; i--)
            {
                var lowest = i == _nwZPasses.Count - 1;
                // Higher solid tiles mask lower maps. Empty tiles keep the lower image.
                // Keep the current eye for its FOV, blindness and screen effects.
                _viewport.Eye = i == 0 ? eye : CloneEye(eye,
                    new MapCoordinates(eye.Position.Position, _nwZPasses[i]),
                    drawFov: false, drawLight: eye.DrawLight);
                NWZRenderContext.SkipParallax = !lowest;
                _viewport.ClearColor = lowest ? Color.Black : null;
                _viewport.Render();
            }
            _mzSkipNormalRender = true;
            return true;
        }
        finally
        {
            _viewport.Eye = savedEye;
            _viewport.ClearColor = savedClear;
            NWZRenderContext.SkipParallax = savedParallax;
            _nwZPasses.Clear();
        }
    }
}
