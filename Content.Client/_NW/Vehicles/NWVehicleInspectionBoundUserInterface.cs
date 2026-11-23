using Content.Shared._NW.Vehicles;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._NW.Vehicles;

[UsedImplicitly]
public sealed class NWVehicleInspectionBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private NWVehicleInspectionWindow? _window;
    private NWVehicleInspectionState? _state;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NWVehicleInspectionWindow>();
        _window.Command += command => SendMessage(new NWVehicleInspectionMessage(command));
        if (_state != null) _window.SetState(_state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NWVehicleInspectionState inspection)
        {
            _state = inspection;
            _window?.SetState(inspection);
        }
    }
}
