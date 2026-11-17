using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._NW.ZLevels;

[CVarDefs]
public sealed class NWZVisualCVars : CVars
{
    /// <summary>Visual offset per lower floor, in world units. Zero preserves mapping alignment.</summary>
    public static readonly CVarDef<float> DepthOffset =
        CVarDef.Create("nw_z.depth_offset", 0.75f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Blur applied between lower-floor passes; the active floor stays sharp.</summary>
    public static readonly CVarDef<float> DepthBlur =
        CVarDef.Create("nw_z.depth_blur", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
