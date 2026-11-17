using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Content.Shared._MultiZ;

namespace Content.Client._NW.ZLevels;

public sealed class NWZRenderCommand : IConsoleCommand
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    public string Command => "nwzrender";
    public string Description => "Print NW Z renderer settings and pass counters.";
    public string Help => "nwzrender — run twice while viewing a lower floor to compare counters.";
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine("NWZLevels 1.0.0 — active-floor placement fix");
        shell.WriteLine($"Enabled: {_config.GetCVar(MZCVars.Enabled)}; render: {_config.GetCVar(MZCVars.RenderEnabled)}; max depth: {_config.GetCVar(MZCVars.MaxRenderDepth)}");
        shell.WriteLine($"Offset: {_config.GetCVar(NWZVisualCVars.DepthOffset)}; blur: {_config.GetCVar(NWZVisualCVars.DepthBlur)}; blur enabled: {_config.GetCVar(MZCVars.BlurEnabled)}");
        shell.WriteLine($"Last stack map: {NWZDepthDiagnostics.LastMap}; passes (including current): {NWZDepthDiagnostics.LastPasses}");
        shell.WriteLine($"Stack checks: {NWZDepthDiagnostics.StackChecks}; filter draws: {NWZDepthDiagnostics.FilterPasses}");
        shell.WriteLine("Counters are cumulative. Run twice after closing the console briefly; active rendering increases them.");
    }
}
