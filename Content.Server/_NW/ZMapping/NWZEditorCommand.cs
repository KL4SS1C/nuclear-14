using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._NW.ZMapping;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._NW.ZMapping;

[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed class NWZEditorCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    public string Command => "nwzfloor";
    public string Description => "List building floors or change floor at the same X/Y coordinates.";
    public string Help => "nwzfloor [z]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } session || session.AttachedEntity is not { } actor || args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }
        try
        {
            var system = _entities.System<NWZEditorSystem>();
            if (args.Length == 1 && (!int.TryParse(args[0], out var requestedDepth) || !system.MoveToFloor(session, requestedDepth)))
            {
                shell.WriteError("Use a valid floor index while inside a building as an admin ghost.");
                return;
            }
            var current = _entities.GetComponent<TransformComponent>(actor).MapUid;
            if (current is not { } map || system.GetFloors(map) is not { } floors)
            {
                shell.WriteError("This map is not an NW Z mapping bundle.");
                return;
            }
            foreach (var (depth, floor) in floors)
                shell.WriteLine($"{(floor == map ? "[ACTIVE] " : "")}Z {depth}: {_entities.GetComponent<NWZEditorFloorComponent>(floor).Label} — map {_entities.GetComponent<MapComponent>(floor).MapId}");
        }
        catch (Exception e) { shell.WriteError(e.Message); }
    }
}
