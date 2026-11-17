// Adapted from CMU ScalingViewport.CMUZLevels: lowest-to-highest viewport passes.
// Depth filter/projection: AU-14/ColonialMarinesUniverse,
// 723924c7fab5e0f6b9078226d2994ef66821b8c6 (_CMU14/ZLevels), AGPL-3.0.
// This smaller integration omits CMU's look-up, sprite-culling and projected-light features.
using Content.Client._MultiZ.Core;
using Robust.Shared.Maths;
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
        NWZDepthDiagnostics.StackChecks++;
        NWZDepthDiagnostics.LastMap = eye.Position.MapId.ToString();
        NWZDepthDiagnostics.LastPasses = _nwZPasses.Count;
        if (_nwZPasses.Count == 1)
            return false;

        // CMU projects lower floors using a per-depth eye offset. This is visual only:
        // the maps, collision bodies and elevator destinations retain their world coordinates.
        var offsetSetting = _mzCfg.GetCVar(NWZVisualCVars.DepthOffset);
        var offset = float.IsFinite(offsetSetting) ? Math.Clamp(offsetSetting, 0f, 1f) : 0.75f;
        Angle projectionRotation = eye.Rotation * -1;
        var offsetPerFloor = projectionRotation.ToWorldVec() * offset;
        var placement = IoCManager.Resolve<Robust.Client.Placement.IPlacementManager>();
        var savedMode = placement.CurrentMode;
        var savedEye = _viewport.Eye;
        var savedClear = _viewport.ClearColor;
        var savedParallax = NWZRenderContext.SkipParallax;
        try
        {
            for (var i = _nwZPasses.Count - 1; i >= 0; i--)
            {
                var lowest = i == _nwZPasses.Count - 1;
                // PlacementManager's mode belongs to the active map. Drawing it during a
                // lower pass makes SnapgridCenter.MapToGrid mix two different MapIds.
                // This setter only changes the render mode, not placement permissions or
                // selection. Restore it before the active pass and unconditionally on exit.
                placement.CurrentMode = i == 0 ? savedMode : null;
                // Higher solid tiles mask lower maps. Empty tiles keep the lower image.
                // Keep the current eye for its FOV, blindness and screen effects.
                if (i == 0)
                    _viewport.Eye = eye;
                else
                {
                    var lowerEye = new NWZDepthEye
                    {
                        Position = new MapCoordinates(eye.Position.Position, _nwZPasses[i]),
                        Rotation = eye.Rotation,
                        Scale = eye.Scale,
                        DrawFov = false,
                        DrawLight = eye.DrawLight,
                        Depth = -i
                    };
                    // Negative depth matches CMU's lower-floor projection convention.
                    lowerEye.Offset = eye.Offset - offsetPerFloor * i;
                    _viewport.Eye = lowerEye;
                }
                NWZRenderContext.SkipParallax = !lowest;
                _viewport.ClearColor = lowest ? Color.Black : null;
                _viewport.Render();

            }
            _mzSkipNormalRender = true;
            return true;
        }
        finally
        {
            placement.CurrentMode = savedMode;
            _viewport.Eye = savedEye;
            _viewport.ClearColor = savedClear;
            NWZRenderContext.SkipParallax = savedParallax;
            _nwZPasses.Clear();
        }
    }
}
