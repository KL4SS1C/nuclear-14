using System.Globalization;
using System.Linq;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._NW.ZLevels;
using Content.Shared._NW.ZMapping;
using Robust.Server.Placement;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Server._NW.ZMapping;

/// <summary>One mapping file containing independent native map documents, linked at load time.</summary>
public sealed class NWZEditorSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly IPlacementManager _placement = default!;
    private readonly HashSet<EntityUid> _networks = new();
    private Func<MsgPlacement, bool>? _previousGate;
    private bool _installed;
    private bool _savingBundle;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AfterSerializationEvent>(OnAfterSerialization);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        _loader.OnIsSerializable += OnSerializable;
    }

    public override void Shutdown()
    {
        _loader.OnIsSerializable -= OnSerializable;
        if (_installed && _placement.AllowPlacementFunc == GuardPlacement)
            _placement.AllowPlacementFunc = _previousGate;
        base.Shutdown();
    }

    private static string Scalar(MappingDataNode node, string key)
    {
        if (!node.TryGet(key, out var value) || value is not ValueDataNode scalar)
            throw new InvalidOperationException($"Missing scalar: {key}.");
        return scalar.Value;
    }

    public bool TryLoadBundle(ResPath path, MapId groundId, out bool recognized, out string error)
    {
        recognized = false;
        error = string.Empty;
        var loaded = new List<LoadResult>();
        EntityUid? networkUid = null;
        try
        {
            if (!_loader.TryReadFile(path, out var data) || !data.Has("nwZBundle"))
                return false;
            recognized = true;
            if (Scalar(data, "nwZBundle") != "1")
                throw new InvalidOperationException("Unsupported NW Z bundle version.");
            var name = Scalar(data, "name");
            if (name.Length is < 1 or > 128)
                throw new InvalidOperationException("Invalid building name.");
            if (!data.TryGet("floors", out var list) || list is not SequenceDataNode sequence || sequence.Count is < 1 or > 16)
                throw new InvalidOperationException("Expected 1 to 16 floors.");
            var definitions = new SortedDictionary<int, (string Name, MappingDataNode Map)>();
            foreach (var node in sequence)
            {
                if (node is not MappingDataNode floor ||
                    !int.TryParse(Scalar(floor, "z"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth) ||
                    depth is < -32 or > 32 || !floor.TryGet("map", out var mapNode) || mapNode is not MappingDataNode map)
                    throw new InvalidOperationException("Invalid floor definition.");
                if (!definitions.TryAdd(depth, (Scalar(floor, "name"), map)))
                    throw new InvalidOperationException("Duplicate floor index.");
            }
            if (!definitions.ContainsKey(0) || definitions.Keys.Last() - definitions.Keys.First() != definitions.Count - 1)
                throw new InvalidOperationException("Floors must be consecutive and include Z 0.");
            var floors = new SortedDictionary<int, EntityUid>();
            // Reserve the requested map ID before auto-allocating the upper floors.
            foreach (var depth in definitions.Keys.OrderBy(z => z == 0 ? 0 : 1).ThenBy(z => z))
            {
                var definition = definitions[depth];
                var options = new MapLoadOptions
                {
                    ForceMapId = depth == 0 ? groundId : null,
                    DeserializationOptions = new DeserializationOptions
                    {
                        StoreYamlUids = true, InitializeMaps = false, PauseMaps = true
                    }
                };
                if (!_loader.TryLoadGeneric(definition.Map, $"{path} [Z {depth}]", out var result, options))
                    throw new InvalidOperationException($"Could not load Z {depth}.");
                loaded.Add(result);
                if (result.Maps.Count != 1)
                    throw new InvalidOperationException("Each floor must contain one map.");
                var uid = result.Maps.Single().Owner;
                if (!HasComp<MapGridComponent>(uid) || _maps.IsInitialized(Comp<MapComponent>(uid).MapId))
                    throw new InvalidOperationException("Each floor must be a pre-init Map + MapGrid root.");
                var tag = EnsureComp<NWZEditorFloorComponent>(uid);
                tag.Project = name;
                tag.Depth = depth;
                tag.Label = definition.Name;
                EnsureComp<NWZLevelMapComponent>(uid);
                _maps.SetPaused(uid, true);
                floors.Add(depth, uid);
            }
            networkUid = Spawn(null, MapCoordinates.Nullspace);
            var network = EnsureComp<MZNetworkComponent>(networkUid.Value);
            foreach (var (depth, uid) in floors)
            {
                network.ZLevels[depth] = uid;
                network.ZLevelByEntity[uid] = depth;
                var z = EnsureComp<MZMapComponent>(uid);
                z.NetworkUid = networkUid.Value;
                z.Depth = depth;
                z.MapAbove = floors.TryGetValue(depth + 1, out var above) ? above : null;
                z.MapBelow = floors.TryGetValue(depth - 1, out var below) ? below : null;
                Dirty(uid, z);
            }
            Dirty(networkUid.Value, network);
            _networks.Add(networkUid.Value);
            if (!_installed)
            {
                _previousGate = _placement.AllowPlacementFunc;
                _placement.AllowPlacementFunc = GuardPlacement;
                _installed = true;
            }
            return true;
        }
        catch (Exception e)
        {
            recognized = true;
            if (networkUid is { } net && Exists(net)) Del(net);
            foreach (var result in loaded) _loader.Delete(result);
            error = e.Message;
            Log.Error($"NW Z bundle load failed: {e}");
            return false;
        }
    }

    public SortedDictionary<int, EntityUid>? GetFloors(EntityUid map)
    {
        if (!HasComp<NWZEditorFloorComponent>(map) || !TryComp<MZMapComponent>(map, out var level) ||
            !TryComp<MZNetworkComponent>(level.NetworkUid, out var network))
            return null;
        var floors = new SortedDictionary<int, EntityUid>();
        foreach (var (depth, uid) in network.ZLevels)
        {
            if (uid is not { } floor || !Exists(floor) || !HasComp<NWZEditorFloorComponent>(floor))
                throw new InvalidOperationException("A building floor is missing; save aborted.");
            floors.Add(depth, floor);
        }
        if (!floors.ContainsKey(0) || floors.Keys.Last() - floors.Keys.First() != floors.Count - 1)
            throw new InvalidOperationException("The building floor sequence is incomplete.");
        return floors;
    }

    private void OnAfterSerialization(AfterSerializationEvent ev)
    {
        if (_savingBundle || ev.Category != FileCategory.Map || ev.Entities.Count != 1)
            return;
        var root = ev.Entities.Single();
        if (!TryComp<NWZEditorFloorComponent>(root, out var tag)) return;
        var floors = GetFloors(root) ?? throw new InvalidOperationException("Building links are missing; save aborted.");
        var bundle = new MappingDataNode();
        bundle.Add("nwZBundle", new ValueDataNode("1"));
        bundle.Add("name", new ValueDataNode(tag.Project));
        var sequence = new SequenceDataNode();
        _savingBundle = true;
        try
        {
            foreach (var (depth, floor) in floors)
            {
                var entry = new MappingDataNode();
                entry.Add("z", new ValueDataNode(depth.ToString(CultureInfo.InvariantCulture)));
                entry.Add("name", new ValueDataNode(Comp<NWZEditorFloorComponent>(floor).Label));
                // Copy the original node: inserting it directly would create a recursive YAML tree.
                var map = floor == root ? ev.Node.Copy() :
                    _loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { floor }).Node;
                entry.Add("map", map);
                sequence.Add(entry);
            }
        }
        finally { _savingBundle = false; }
        bundle.Add("floors", sequence);
        ev.Node.Clear();
        foreach (var (key, value) in bundle.Children) ev.Node.Add(key, value);
    }

    private void OnSerializable(Entity<MetaDataComponent> ent, ref bool serializable)
    {
        if (Transform(ent).MapUid is not { } map || !HasComp<NWZEditorFloorComponent>(map)) return;
        if (HasComp<ActorComponent>(ent) || HasComp<GhostComponent>(ent) ||
            (ent.Comp.EntityPrototype == null && HasComp<EyeComponent>(ent) && !HasComp<MapComponent>(ent)))
            serializable = false;
    }

    public bool CanEdit(ICommonSession session, EntityUid coordinateEntity)
    {
        if (!TryComp(coordinateEntity, out TransformComponent? target)) return false;
        var current = session.AttachedEntity is { } actor ? Transform(actor).MapUid : null;
        if (HasComp<NWZEditorFloorComponent>(target.MapUid) || HasComp<NWZEditorFloorComponent>(current))
            return current != null && current == target.MapUid;
        return true;
    }

    private bool GuardPlacement(MsgPlacement msg)
    {
        if (_previousGate != null && !_previousGate(msg)) return false;
        if (!_players.TryGetSessionById(msg.MsgChannel.UserId, out var session)) return false;
        EntityUid target;
        if (msg.PlaceType == PlacementManagerMessage.RequestEntRemove)
        {
            target = GetEntity(msg.EntityUid);
            if (HasComp<NWZEditorFloorComponent>(target)) return false;
        }
        else if (msg.PlaceType is PlacementManagerMessage.RequestPlacement or PlacementManagerMessage.RequestRectRemove)
            target = GetCoordinates(msg.NetCoordinates).EntityId;
        else return true;
        return CanEdit(session, target);
    }

    public bool MoveToFloor(ICommonSession session, int depth)
    {
        if (session.AttachedEntity is not { } actor || !HasComp<GhostComponent>(actor) ||
            Transform(actor).MapUid is not { } map || GetFloors(map) is not { } floors ||
            !floors.TryGetValue(depth, out var destination)) return false;
        var position = _transform.GetMapCoordinates(actor).Position;
        _transform.SetMapCoordinates(actor, new MapCoordinates(position, Comp<MapComponent>(destination).MapId));
        return true;
    }

    private void OnRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var net in _networks)
            if (!TerminatingOrDeleted(net)) QueueDel(net);
        _networks.Clear();
    }
}
