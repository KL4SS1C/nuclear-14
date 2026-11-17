using Content.Client.Decals;
using Content.Client.Mapping;
using Content.Shared._NW.ZMapping;
using Robust.Client.Placement;
using Robust.Client.Player;

namespace Content.Client._NW.ZMapping;

/// <summary>No custom window. Uses the normal mapping save dialog and tools.</summary>
public sealed class NWZEditorClientSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly MappingManager _mapping = default!;
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly DecalPlacementSystem _decals = default!;
    private EntityUid? _lastMap;
    private EntityUid? CurrentMap => _players.LocalEntity is { } actor ? Transform(actor).MapUid : null;
    public bool Active => HasComp<NWZEditorFloorComponent>(CurrentMap);
    public async void SaveProject() => await _mapping.SaveMap();

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var current = CurrentMap;
        if (current == _lastMap) return;
        if (Active || HasComp<NWZEditorFloorComponent>(_lastMap))
        {
            _placement.Clear();
            _decals.CancelForNWZEditor();
        }
        _lastMap = current;
    }
}
