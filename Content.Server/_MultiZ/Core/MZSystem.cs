// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
//   PR #1058 "Multi Z addition" & #1119 "Multi z fixes" by TheHellFireo
//   Based on Crystall Edge (crystallpunk-14) Multi-Z system
//   Performance refactors from TTMC (ttmc14)
// Ported to misfits-14 _MultiZ/ — renamed &amp; adapted
// #Cythisiax Ported — Multi-Z level support for misfits-14

using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Core.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._MultiZ.Core;

/// <summary>
/// Server-side Multi-Z system. Hooks PostGameMapLoad to create Z-level networks
/// from GameMapPrototype.MapsAbove / MapsBelow.
/// Handles Z-physics movement updates and waking sleeping Z physics.
/// </summary>
public sealed partial class MZSystem : MZSharedSystem
{
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private TransformSystem _xformSystem = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private bool _zLevelsEnabled;
    private readonly HashSet<EntityUid> _createdMaps = new();
    private readonly HashSet<EntityUid> _createdNetworks = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, MZCVars.Enabled, v => _zLevelsEnabled = v, true);
        SubscribeLocalEvent<PostGameMapLoad>(OnGameMapLoad, after: [typeof(StationSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_zLevelsEnabled)
            return;

        UpdateZMovement(frameTime);
    }

    public override void WakeZPhysics(Entity<MZPhysicsComponent?> ent)
    {
        base.WakeZPhysics(ent);

        if (!Resolve(ent, ref ent.Comp, false) || HasComp<MZFallingComponent>(ent))
            return;

        EnsureComp<MZFallingComponent>(ent);
    }

    /// <summary>
    /// Fired after a game map is loaded. If the GameMapPrototype has MapsAbove or MapsBelow,
    /// creates a Z-network entity, loads the additional maps, and wires everything together.
    /// </summary>
    private void OnGameMapLoad(PostGameMapLoad ev)
    {
        if (!_zLevelsEnabled)
            return;

        // #Cythisiax Ported — Check for multi-Z maps on the gameMap prototype
        if (ev.GameMap.MapsAbove.Count == 0 && ev.GameMap.MapsBelow.Count == 0)
            return;

        var stationNetwork = CreateZNetwork();
        _meta.SetEntityName(stationNetwork, $"Station z-Network: {ev.GameMap.MapName}");

        var mainMap = _mapSystem.GetMap(ev.Map);
        Dictionary<EntityUid, int> mapDepths = new();
        mapDepths.Add(mainMap, 0);

        // Collect stations from loaded grids
        var stationsById = new Dictionary<string, EntityUid>(StringComparer.OrdinalIgnoreCase);
        var stations = new HashSet<EntityUid>();
        foreach (var grid in ev.Grids)
        {
            if (_station.GetOwningStation(grid) is not { } station)
                continue;

            stations.Add(station);
            if (TryComp<BecomesStationComponent>(grid, out var becomesStation))
                stationsById[becomesStation.Id] = station;
        }

        // Apply shared component overrides to the main map
        EntityManager.AddComponents(mainMap, ev.GameMap.ZLevelsComponentOverrides);

        // ── Load maps below (depth -1, -2, ...) ──────────────────────────
        var depth = -1;
        foreach (var mapBelow in ev.GameMap.MapsBelow)
        {
            if (!_mapLoader.TryLoadMap(mapBelow, out var mapEnt, out var grids))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            _createdMaps.Add(mapEnt.Value.Owner);

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            EntityManager.AddComponents(mapEnt.Value, ev.GameMap.ZLevelsComponentOverrides);
            AddZLevelGridsToStations(grids, stationsById, stations);
            _mapSystem.InitializeMap(mapEnt.Value.Comp.MapId);
            _meta.SetEntityName(mapEnt.Value, $"{ev.GameMap.MapName} [{depth}]");
            mapDepths.Add(mapEnt.Value, depth);
            depth--;
        }

        // ── Load maps above (depth +1, +2, ...) ──────────────────────────
        depth = 1;
        foreach (var mapAbove in ev.GameMap.MapsAbove)
        {
            if (!_mapLoader.TryLoadMap(mapAbove, out var mapEnt, out var grids))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            _createdMaps.Add(mapEnt.Value.Owner);

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            EntityManager.AddComponents(mapEnt.Value, ev.GameMap.ZLevelsComponentOverrides);
            AddZLevelGridsToStations(grids, stationsById, stations);
            _mapSystem.InitializeMap(mapEnt.Value.Comp.MapId);
            _meta.SetEntityName(mapEnt.Value, $"{ev.GameMap.MapName} [{depth}]");
            mapDepths.Add(mapEnt.Value, depth);
            depth++;
        }

        TryAddMapsIntoZNetwork(stationNetwork, mapDepths);
    }

    /// <summary>
    /// Associates grids from a loaded Z-level map with their corresponding stations.
    /// </summary>
    private void AddZLevelGridsToStations(
        HashSet<Entity<MapGridComponent>> grids,
        IReadOnlyDictionary<string, EntityUid> stationsById,
        IReadOnlySet<EntityUid> stations)
    {
        foreach (var grid in grids)
        {
            EntityUid? station = null;
            if (TryComp<BecomesStationComponent>(grid, out var becomesStation) &&
                stationsById.TryGetValue(becomesStation.Id, out var matchingStation))
            {
                station = matchingStation;
            }
            else if (grids.Count == 1 && stations.Count == 1)
            {
                station = stations.First();
            }

            if (station is not { } resolvedStation)
            {
                Log.Warning($"Could not associate Z-level grid {ToPrettyString(grid)} with a station.");
                continue;
            }

            _station.AddGridToStation(resolvedStation, grid);
        }
    }

    /// <summary>
    /// Creates a Z-network entity in nullspace and registers all map→depth mappings.
    /// </summary>
    private EntityUid CreateZNetwork()
    {
        var network = Spawn(null, MapCoordinates.Nullspace);
        EnsureComp<MZNetworkComponent>(network);
        _createdNetworks.Add(network);
        return network;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var map in _createdMaps)
        {
            if (!TerminatingOrDeleted(map))
                QueueDel(map);
        }

        foreach (var network in _createdNetworks)
        {
            if (!TerminatingOrDeleted(network))
                QueueDel(network);
        }

        _createdMaps.Clear();
        _createdNetworks.Clear();
    }

    /// <summary>
    /// Populates the Z-network with map→depth mappings and wires up MapAbove/MapBelow references.
    /// </summary>
    private void TryAddMapsIntoZNetwork(EntityUid network, Dictionary<EntityUid, int> mapDepths)
    {
        if (!TryComp<MZNetworkComponent>(network, out var netComp))
            return;

        // Register all maps in the network
        foreach (var (mapUid, mapDepth) in mapDepths)
        {
            netComp.ZLevels[mapDepth] = mapUid;
            netComp.ZLevelByEntity[mapUid] = mapDepth;

            var zMapComp = EnsureComp<MZMapComponent>(mapUid);
            zMapComp.NetworkUid = network;
            zMapComp.Depth = mapDepth;
        }

        // Wire up MapAbove / MapBelow references
        foreach (var (mapUid, mapDepth) in mapDepths)
        {
            var zMapComp = Comp<MZMapComponent>(mapUid);

            if (netComp.ZLevels.TryGetValue(mapDepth + 1, out var aboveUid) && aboveUid is { } above)
                zMapComp.MapAbove = above;

            if (netComp.ZLevels.TryGetValue(mapDepth - 1, out var belowUid) && belowUid is { } below)
                zMapComp.MapBelow = below;

            Dirty(mapUid, zMapComp);
        }

        Dirty(network, netComp);
    }
}
