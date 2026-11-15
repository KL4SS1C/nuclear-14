// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
// Ported to misfits-14
// #Cythisiax Ported — Multi-Z viewport rendering
//
// Main-viewport multi-pass approach matching CMU's RenderZLevelPasses.
// Each Z-level renders through the main viewport's full pipeline (entities,
// FOV, lights all work). Lowest level renders first with clear to black,
// higher levels render without clearing so empty space reveals the level below.

using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Core.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private readonly IConfigurationManager _mzCfg = default!;
    [Dependency] private readonly IEntityManager _mzEntMan = default!;

    private bool _mzSkipNormalRender;

    private void MultiZBeforeRender()
    {
        _mzSkipNormalRender = false;

        if (_viewport == null || _eye == null)
            return;

        if (!_mzCfg.GetCVar(MZCVars.Enabled) || !_mzCfg.GetCVar(MZCVars.RenderEnabled))
            return;

        var playerManager = IoCManager.Resolve<IPlayerManager>();
        var player = playerManager.LocalSession?.AttachedEntity;
        if (player == null)
            return;

        if (!_mzEntMan.TryGetComponent<TransformComponent>(player, out var xform))
            return;

        if (xform.MapUid is not { } mapUid)
            return;

        if (!_mzEntMan.TryGetComponent<MZMapComponent>(mapUid, out var zMap))
            return;

        // NW floors need the same lowest-to-highest composition as CMU, even with grids.
        if (_mzEntMan.HasComponent<Content.Shared._NW.ZLevels.NWZLevelMapComponent>(mapUid) &&
            TryRenderNWZStack(mapUid, zMap, _eye))
            return;

        var zSystem = _mzEntMan.System<MZSharedSystem>();
        var eye = _eye;

        // Only empty sky/observation layers should replace the normal viewport
        // render with a lower-level pass. Normal maps need the stock render path.
        if (HasRenderableGrids(mapUid))
            return;

        if (!zSystem.TryMapDown((mapUid, zMap), out var belowMap))
            return;

        if (!_mzEntMan.TryGetComponent<MapComponent>(belowMap.Value, out var belowMC))
            return;

        var savedEye = _viewport.Eye;

        // Pass 1: render the below map with a plain eye so the upper sky layer
        // cannot blank it out through FOV / lighting state.
        var belowCoords = new MapCoordinates(eye.Position.Position, belowMC.MapId);
        var altitudeZoom = MathF.Max(_mzCfg.GetCVar(MZCVars.SkyAltitudeZoom), 1f);
        _viewport.Eye = CloneEye(eye, belowCoords, drawFov: false, drawLight: true, zoomMultiplier: altitudeZoom);
        _viewport.ClearColor = Color.Black;
        _viewport.Render();

        // #Cythisiax Add - empty sky layers should behave like a fogged observation layer,
        // not a hard black/space clear that erases the lower map.
        if (_mzBlurBuffer != null)
        {
            _clyde.BlurRenderTarget(_viewport, _viewport.RenderTarget, _mzBlurBuffer, eye, 10f);
        }

        _viewport.Eye = savedEye;
        _viewport.ClearColor = Color.Black;
        _mzSkipNormalRender = true;
    }

    private static Robust.Shared.Graphics.Eye CloneEye(
        IEye source,
        MapCoordinates position,
        bool drawFov,
        bool drawLight,
        float zoomMultiplier = 1f)
    {
        return new Robust.Shared.Graphics.Eye
        {
            Position = position,
            Rotation = source.Rotation,
            Scale = source.Scale,
            Zoom = source.Zoom * zoomMultiplier,
            Offset = source.Offset,
            DrawFov = drawFov,
            DrawLight = drawLight,
        };
    }

    private bool HasRenderableGrids(EntityUid mapUid)
    {
        var query = _mzEntMan.EntityQueryEnumerator<TransformComponent, MapGridComponent>();
        while (query.MoveNext(out _, out var xform, out _))
        {
            if (xform.MapUid == mapUid)
                return true;
        }

        return false;
    }
}
