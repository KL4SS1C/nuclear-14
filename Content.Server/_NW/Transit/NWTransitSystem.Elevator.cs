using Content.Shared._NW.Transit;
using Content.Shared.ListViewSelector;

namespace Content.Server._NW.Transit;

public sealed partial class NWTransitSystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    private void InitializeElevators()
    {
        Subs.BuiEvents<NWElevatorStopComponent>(ListViewSelectorUiKey.Key, subs =>
        {
            subs.Event<ListViewItemSelectedMessage>(OnSelectFloor);
            subs.Event<NWElevatorRefreshMessage>(OnRefreshFloors);
        });
    }

    private bool IsElevatorStop(EntityUid uid) =>
        TryComp<NWElevatorStopComponent>(uid, out var stop) && !string.IsNullOrWhiteSpace(stop.ElevatorId);

    private SortedDictionary<int, EntityUid>? GetFloors(EntityUid source, EntityUid user)
    {
        if (!IsElevatorStop(source) || !TryComp<NWTransitLinkComponent>(source, out var origin))
            return null;
        var network = Comp<NWElevatorStopComponent>(source).ElevatorId.Trim();
        var map = Transform(source).MapUid;
        if (map == null)
            return null;

        var floors = new SortedDictionary<int, EntityUid>();
        var query = EntityQueryEnumerator<NWElevatorStopComponent, NWTransitLinkComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var stop, out var link, out var xform))
        {
            if (TerminatingOrDeleted(uid) || xform.MapUid == null ||
                origin.SameMapOnly != link.SameMapOnly || (link.SameMapOnly && xform.MapUid != map) ||
                !string.Equals(stop.ElevatorId?.Trim(), network, StringComparison.Ordinal))
                continue;

            if (floors.ContainsKey(stop.Floor))
            {
                _popup.PopupEntity(Loc.GetString("nw-elevator-duplicate", ("floor", stop.Floor)), source, user);
                return null;
            }
            floors.Add(stop.Floor, uid);
        }
        if (floors.Count >= 2 && floors.ContainsValue(source))
            return floors;

        _popup.PopupEntity(Loc.GetString("nw-elevator-alone"), source, user);
        return null;
    }

    private string StopId(EntityUid uid, int floor) => $"nw-stop-{GetNetEntity(uid)}-{floor}";

    private void OnRefreshFloors(Entity<NWElevatorStopComponent> ent, ref NWElevatorRefreshMessage message)
    {
        if (!CanTravel(ent.Owner, message.Actor))
            return;
        PublishFloors(ent.Owner, message.Actor);
    }

    private void ShowFloors(EntityUid source, EntityUid user)
    {
        if (PublishFloors(source, user))
            _ui.OpenUi(source, ListViewSelectorUiKey.Key, user);
    }

    private bool PublishFloors(EntityUid source, EntityUid user)
    {
        var floors = GetFloors(source, user);
        if (floors == null)
        {
            // Replace stale destinations; GetFloors also explains the configuration error.
            _ui.SetUiState(source, ListViewSelectorUiKey.Key,
                new ListViewSelectorState(new List<ListViewSelectorEntry>()));
            return false;
        }
        var entries = new List<ListViewSelectorEntry>();
        foreach (var (floor, uid) in floors)
        {
            if (uid == source)
                continue;
            var label = Comp<NWElevatorStopComponent>(uid).FloorName?.Trim();
            var text = string.IsNullOrEmpty(label)
                ? Loc.GetString("nw-elevator-floor", ("floor", floor))
                : Loc.GetString("nw-elevator-named", ("floor", floor), ("name", label));
            entries.Add(new ListViewSelectorEntry(StopId(uid, floor), text));
        }
        _ui.SetUiState(source, ListViewSelectorUiKey.Key, new ListViewSelectorState(entries));
        return true;
    }

    private void OnSelectFloor(Entity<NWElevatorStopComponent> ent, ref ListViewItemSelectedMessage message)
    {
        var user = message.Actor;
        if (!CanTravel(ent.Owner, user))
            return;
        var floors = GetFloors(ent.Owner, user);
        if (floors == null)
            return;
        // Re-resolve the selected entity; neither the client index nor its text is authoritative.
        foreach (var (floor, target) in floors)
        {
            if (target != ent.Owner && StopId(target, floor) == message.SelectedItem?.Id)
            {
                Travel(user, target);
                return;
            }
        }
        _popup.PopupEntity(Loc.GetString("nw-elevator-changed"), ent.Owner, user);
    }
}
