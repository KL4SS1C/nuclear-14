// Adapted from CMUZLevelBlurOverlay, AU-14/ColonialMarinesUniverse
// commit 723924c7fab5e0f6b9078226d2994ef66821b8c6. CMU changes: AGPL-3.0.
using System.Numerics;
using Content.Shared._MultiZ;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._NW.ZLevels;

public sealed class NWZDepthOverlay : Overlay
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    private ShaderInstance? _shader;
    private static readonly ProtoId<ShaderPrototype> Shader = "NWZDepthBlur";
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => Enabled;
    private float Strength
    {
        get
        {
            var value = _config.GetCVar(NWZVisualCVars.DepthBlur);
            return float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1f;
        }
    }
    private bool Enabled => _config.GetCVar(MZCVars.BlurEnabled) && Strength > 0;

    public NWZDepthOverlay() => IoCManager.InjectDependencies(this);

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Enabled && args.MapId != MapId.Nullspace &&
            args.Viewport.Eye is NWZDepthEye { Depth: < 0 };
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye is not NWZDepthEye { Depth: < 0 }) return;
        var ambient = new Vector3(0f, 0f, 1f);
        if (_entities.TryGetComponent<MapLightComponent>(args.MapUid, out var light))
            ambient = new Vector3(light.AmbientLightColor.R, light.AmbientLightColor.G, light.AmbientLightColor.B);
        _shader ??= _prototypes.Index(Shader).InstanceUnique();
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("BLUR_COLOR", ambient);
        _shader.SetParameter("BLUR_RADIUS", Strength);
        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        try
        {
            handle.DrawRect(args.WorldBounds, Color.White);
            NWZDepthDiagnostics.FilterPasses++;
        }
        finally { handle.UseShader(null); }
    }
}

public sealed class NWZDepthOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    private NWZDepthOverlay? _overlay;
    public override void Initialize()
    {
        base.Initialize();
        _overlay = new NWZDepthOverlay();
        _overlays.AddOverlay(_overlay);
    }
    public override void Shutdown()
    {
        if (_overlay != null) _overlays.RemoveOverlay(_overlay);
        _overlay = null;
        NWZDepthDiagnostics.StackChecks = 0;
        NWZDepthDiagnostics.FilterPasses = 0;
        NWZDepthDiagnostics.LastPasses = 0;
        NWZDepthDiagnostics.LastMap = "none";
        base.Shutdown();
    }
}
