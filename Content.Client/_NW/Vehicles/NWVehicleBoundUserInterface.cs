using Content.Shared._NW.Vehicles;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._NW.Vehicles;

[UsedImplicitly]
public sealed class NWVehicleBoundUserInterface : BoundUserInterface
{
    private NWVehicleWindow? _window;

    public NWVehicleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<NWVehicleWindow>();
        _window.OnGearSelected += lowGear => SendMessage(new NWVehicleSetGearMessage(lowGear));
        _window.OnLightsSelected += enabled => SendMessage(new NWVehicleSetLightsMessage(enabled));
        _window.OnSeatSelected += seatIndex => SendMessage(new NWVehicleSelectSeatMessage(seatIndex));
        _window.OnLoadCargo += () => SendMessage(new NWVehicleLoadCargoMessage());
        _window.OnUnloadCargo += crate => SendMessage(new NWVehicleUnloadCargoMessage(crate));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not NWVehicleSeatBoundUserInterfaceState vehicleState)
            return;

        _window?.SetState(vehicleState);
    }
}
