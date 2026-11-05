using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.Input;
using Content.Shared.Movement.Systems;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._NW.Vehicles;

public sealed class NWVehiclePilotInputSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    public override void Initialize()
    {
        base.Initialize();

        var binds = CommandBinds.Builder;
        Bind(binds, EngineKeyFunctions.MoveUp, NWVehicleControlInput.Forward);
        Bind(binds, EngineKeyFunctions.MoveDown, NWVehicleControlInput.Back);
        Bind(binds, EngineKeyFunctions.MoveLeft, NWVehicleControlInput.Left);
        Bind(binds, EngineKeyFunctions.MoveRight, NWVehicleControlInput.Right);
        binds.Register<NWVehiclePilotInputSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<NWVehiclePilotInputSystem>();
    }

    private void Bind(CommandBinds.BindingsBuilder binds, BoundKeyFunction key, NWVehicleControlInput input)
    {
        binds.BindBefore(key, new NWVehicleMovementHandler(this, input), typeof(SharedMoverController));
    }

    private void SendInput(EntityUid? pilot, NWVehicleControlInput input, bool pressed)
    {
        if (pilot is not { } uid || _player.LocalEntity != uid ||
            !IsRunningPilot(uid))
            return;

        RaiseNetworkEvent(new NWVehicleControlInputMessage(input, pressed));
    }

    private bool IsRunningPilot(EntityUid pilot)
    {
        if (TryComp<NWVehicleCabinViewerComponent>(pilot, out var viewer) && !viewer.Outside) return false;
        var query = EntityQueryEnumerator<NWVehicleComponent>();
        while (query.MoveNext(out _, out var vehicle))
        {
            if (vehicle.Pilot == pilot && vehicle.State == NWVehicleState.Running)
                return true;
        }

        return false;
    }
    private sealed class NWVehicleMovementHandler(NWVehiclePilotInputSystem system, NWVehicleControlInput input)
        : InputCmdHandler
    {
        private bool _passedDown;

        public override bool HandleCmdMessage(
            IEntityManager entManager,
            ICommonSession? session,
            IFullInputCmdMessage message)
        {
            if (message.State == BoundKeyState.Down)
            {
                var block = session?.AttachedEntity is { } pilot && system.IsRunningPilot(pilot);
                _passedDown = !block;

                if (block)
                    system.SendInput(session!.AttachedEntity, input, true);

                return block;
            }

            if (message.State == BoundKeyState.Up && _passedDown)
            {
                _passedDown = false;
                return false;
            }

            if (session?.AttachedEntity is not { } releasePilot || !system.IsRunningPilot(releasePilot))
                return false;

            system.SendInput(releasePilot, input, false);
            return true;
        }
    }
}
