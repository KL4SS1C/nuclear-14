namespace Content.Client._NW.ZLevels;

/// <summary>Identifies lower-floor passes without altering the active player's eye.</summary>
public sealed class NWZDepthEye : global::Robust.Shared.Graphics.Eye
{
    public int Depth;
}

public static class NWZDepthDiagnostics
{
    public static long StackChecks;
    public static long FilterPasses;
    public static int LastPasses;
    public static string LastMap = "none";
}
