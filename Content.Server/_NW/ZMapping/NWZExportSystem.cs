using System.Globalization;
using System.IO;
using System.Linq;
using Content.Shared._NW.ZMapping;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server._NW.ZMapping;

/// <summary>One immutable serialization snapshot produces the editable bundle and native floor files.</summary>
public sealed class NWZExportSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly NWZEditorSystem _editor = default!;
    [Dependency] private readonly IResourceManager _resources = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MapSaveToPathEvent>(OnSave);
    }

    private static bool IsBasename(string name) =>
        !string.IsNullOrWhiteSpace(name) && name is not "." and not ".." &&
        !name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) &&
        !name.EndsWith(' ') &&
        (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private void OnSave(MapSaveToPathEvent ev)
    {
        if (ev.Handled || !HasComp<NWZEditorFloorComponent>(ev.Map)) return;
        ev.Handled = true;
        try
        {
            var floors = _editor.GetFloors(ev.Map) ?? throw new InvalidOperationException("Building links are missing.");
            var target = ev.Path.ToRootedPath();
            var filename = target.Filename;
            if (!IsBasename(filename)) throw new InvalidOperationException("Choose a .yml or .yaml bundle filename.");
            var stem = Path.GetFileNameWithoutExtension(filename);
            var paths = new Dictionary<int, ResPath>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { target.ToString() };
            foreach (var (depth, uid) in floors)
            {
                var tag = Comp<NWZEditorFloorComponent>(uid);
                var name = tag.RuntimeFile;
                if (string.IsNullOrWhiteSpace(name))
                {
                    // Compatibility with the Detroit bundle shipped before runtime filenames existed.
                    name = tag.Project == "Detroit — Enclave Hotel" ? depth switch
                    {
                        0 => "DetroitGround.yml",
                        1 => "EnclaveRooms.yml",
                        2 => "EnclaveSuites.yml",
                        _ => $"{stem}_Z{depth.ToString(CultureInfo.InvariantCulture)}.yml"
                    } : $"{stem}_Z{depth.ToString(CultureInfo.InvariantCulture)}.yml";
                }
                if (!IsBasename(name)) throw new InvalidOperationException($"Invalid runtime filename for Z {depth}: {name}");
                var path = target.WithName(name);
                if (!unique.Add(path.ToString()))
                    throw new InvalidOperationException($"Output filename collision: {name}. Use a different bundle name.");
                paths.Add(depth, path);
            }
            // Store names in the bundle for subsequent saves, even if the bundle itself is renamed.
            foreach (var (depth, uid) in floors) Comp<NWZEditorFloorComponent>(uid).RuntimeFile = paths[depth].Filename;

            var options = ev.Options ?? SerializationOptions.Default;
            options.Category = FileCategory.Map;
            var (bundle, category) = _loader.SerializeEntitiesRecursive(new HashSet<EntityUid> { ev.Map }, options);
            if (category != FileCategory.Map || !bundle.TryGet("nwZBundle", out _) ||
                !bundle.TryGet("floors", out var list) || list is not SequenceDataNode sequence || sequence.Count != floors.Count)
                throw new InvalidOperationException("The serializer did not return a complete NW Z bundle.");

            var documents = new List<(ResPath Path, MappingDataNode Node)> { (target, bundle) };
            var seen = new HashSet<int>();
            foreach (var node in sequence)
            {
                if (node is not MappingDataNode floor || !floor.TryGet("z", out var zNode) || zNode is not ValueDataNode z ||
                    !int.TryParse(z.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth) ||
                    !paths.TryGetValue(depth, out var path) || !seen.Add(depth) ||
                    !floor.TryGet("map", out var mapNode) || mapNode is not MappingDataNode map)
                    throw new InvalidOperationException("Invalid or duplicate serialized floor.");
                var native = map.Copy();
                StripEditorTag(native);
                documents.Add((path, native));
            }
            WriteSet(documents);
            ev.Success = true;
            foreach (var document in documents) Log.Info($"NW Z saved: {document.Path}");
        }
        catch (Exception e)
        {
            ev.Success = false;
            Log.Error($"NW Z save/export failed: {e}");
        }
    }

    private static void StripEditorTag(MappingDataNode map)
    {
        if (!map.TryGet("meta", out _) || !map.TryGet("maps", out var maps) || maps is not SequenceDataNode roots || roots.Count != 1 ||
            !map.TryGet("entities", out var entities) || entities is not SequenceDataNode groups)
            throw new InvalidOperationException("A native floor document is incomplete.");
        foreach (var groupNode in groups)
        {
            if (groupNode is not MappingDataNode group || !group.TryGet("entities", out var entriesNode) || entriesNode is not SequenceDataNode entries)
                throw new InvalidOperationException("Invalid entity group.");
            foreach (var entityNode in entries)
            {
                if (entityNode is not MappingDataNode entity) throw new InvalidOperationException("Invalid entity record.");
                if (!entity.TryGet("components", out var componentsNode)) continue;
                if (componentsNode is not SequenceDataNode components) throw new InvalidOperationException("Invalid component list.");
                var retained = new SequenceDataNode();
                foreach (var component in components)
                {
                    if (component is MappingDataNode c && c.TryGet("type", out var type) &&
                        type is ValueDataNode { Value: "NWZEditorFloor" }) continue;
                    retained.Add(component);
                }
                entity["components"] = retained;
            }
        }
    }

    private void WriteSet(List<(ResPath Path, MappingDataNode Node)> documents)
    {
        var fs = _resources.UserData;
        var token = Guid.NewGuid().ToString("N");
        var staged = new List<(ResPath Final, ResPath Temp, ResPath Backup)>();
        var backedUp = new HashSet<ResPath>();
        var committed = new HashSet<ResPath>();
        try
        {
            // No existing file is touched until all YAML streams have been written and closed.
            foreach (var (path, node) in documents)
            {
                fs.CreateDir(path.Directory);
                if (fs.IsDir(path)) throw new IOException($"The output path is a directory: {path}");
                var temp = path.WithName(path.Filename + ".nwztmp-" + token);
                var backup = path.WithName(path.Filename + ".nwzbak-" + token);
                staged.Add((path, temp, backup));
                using var writer = fs.OpenWriteText(temp);
                new YamlStream(new YamlDocument(node.ToYaml())).Save(new YamlMappingFix(new Emitter(writer)), false);
            }
            foreach (var file in staged)
            {
                if (!fs.Exists(file.Final)) continue;
                fs.Rename(file.Final, file.Backup);
                backedUp.Add(file.Final);
            }
            foreach (var file in staged)
            {
                fs.Rename(file.Temp, file.Final);
                committed.Add(file.Final);
            }
        }
        catch
        {
            // Best-effort rollback. A disk/permission failure can also prevent restoration;
            // retain backup files and report their exact paths rather than deleting them.
            foreach (var file in staged.AsEnumerable().Reverse())
            {
                try
                {
                    if (committed.Contains(file.Final)) fs.Delete(file.Final);
                    if (backedUp.Contains(file.Final)) fs.Rename(file.Backup, file.Final);
                }
                catch (Exception rollback) { Log.Error($"NW Z restore failed; previous file may remain at {file.Backup}: {rollback}"); }
            }
            throw;
        }
        finally
        {
            foreach (var file in staged)
            {
                try { if (fs.Exists(file.Temp)) fs.Delete(file.Temp); }
                catch (Exception cleanup) { Log.Warning($"NW Z temporary file retained at {file.Temp}: {cleanup.Message}"); }
            }
        }
        foreach (var file in staged)
        {
            if (!backedUp.Contains(file.Final)) continue;
            var previous = file.Final.WithName(file.Final.Filename + ".previous");
            try
            {
                if (fs.Exists(previous)) fs.Delete(previous);
                fs.Rename(file.Backup, previous);
            }
            catch (Exception backup) { Log.Warning($"NW Z previous revision retained at {file.Backup}: {backup.Message}"); }
        }
    }
}
