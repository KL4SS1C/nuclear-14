using Content.Shared.ListViewSelector;
using Content.Shared._NW.Transit;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._NW.Transit;

[UsedImplicitly]
public sealed class NWElevatorBoundUserInterface : BoundUserInterface
{
    private NWElevatorWindow? _window;
    private ListViewSelectorState? _floors;

    public NWElevatorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NWElevatorWindow>();
        _window.FloorSelected += SelectFloor;
        // The UI component may already contain state before this window exists.
        if (UiSystem.TryGetUiState<ListViewSelectorState>(Owner, UiKey, out var current))
        {
            _floors = current;
            _window.SetFloors(current.Items);
        }
        else if (_floors != null)
        {
            _window.SetFloors(_floors.Items);
        }

        // Rebuild from the loaded map, even when the initial state was missed.
        SendMessage(new NWElevatorRefreshMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not ListViewSelectorState floors)
            return;
        _floors = floors;
        _window?.SetFloors(floors.Items);
    }

    private void SelectFloor(int index)
    {
        if (_floors == null || (uint)index >= (uint)_floors.Items.Count)
            return;
        SendMessage(new ListViewItemSelectedMessage(_floors.Items[index], index, _floors.MetaData));
        _window?.Close();
    }
}
