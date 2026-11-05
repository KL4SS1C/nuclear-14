using Content.Client.Buckle;
using Content.Shared._NW.Vehicles;
using Content.Shared.DragDrop;

namespace Content.Client._NW.Vehicles;

/// <summary>
/// Drag validity lives on the client because <see cref="CanDragEvent"/> and
/// <see cref="CanDropTargetEvent"/> are only ever raised there. The server re-checks
/// the drop it receives; this only decides what the cursor lets you attempt.
/// </summary>
public sealed class NWVehicleCargoDragSystem : EntitySystem
{
    [Dependency] private readonly SharedNWVehicleSystem _vehicle = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Crate drag eligibility is already registered globally by the base game.
        // Register only this vehicle's drop target; directed event pairs must be unique.
        // overwrites CanDrop when it does, so this has to land after it and OR in.
        SubscribeLocalEvent<NWVehicleComponent, CanDropTargetEvent>(OnCanDropTarget,
            after: [typeof(BuckleSystem)]);
    }

    private void OnCanDropTarget(Entity<NWVehicleComponent> ent, ref CanDropTargetEvent args)
    {
        if (!_vehicle.CanStoreCargo(ent, args.Dragged))
            return;

        args.CanDrop = true;
        args.Handled = true;
    }
}
