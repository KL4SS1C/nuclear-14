using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Buckle.Components;
using Content.Shared.Projectiles;
using Content.Shared.Movement.Events;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Shared._NW.Vehicles;

/// <summary>
/// Shared NWVehicle rules that both sides need to agree on: collision handling for
/// weapons fired from inside the cabin, and whether a crate may be loaded into the
/// cargo bay (the client uses it to decide if a drag is a valid drop, the server to
/// validate the drop it is handed).
/// </summary>
public sealed class SharedNWVehicleSystem : EntitySystem
{
    /// <summary>Container holding crates loaded into the cargo bay.</summary>
    public const string CargoContainerId = "nw-vehicle-cargo";

    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedEntityStorageSystem _entityStorage = default!;

    /// <summary>
    /// A closed crate can be loaded while the bay still has room. Open crates are
    /// refused: their contents are sitting on the floor, so only the empty shell
    /// would travel.
    /// </summary>
    public bool CanStoreCargo(Entity<NWVehicleComponent> vehicle, EntityUid crate)
    {
        SharedEntityStorageComponent? storage = null;
        if (!_entityStorage.ResolveStorage(crate, ref storage) || storage.Open)
            return false;

        return GetCargo(vehicle).Count < vehicle.Comp.CargoCapacity;
    }

    /// <summary>
    /// Crates currently in the bay. Empty until something has been loaded, since the
    /// container is only created on the first insertion.
    /// </summary>
    public IReadOnlyList<EntityUid> GetCargo(EntityUid vehicle)
    {
        return _container.TryGetContainer(vehicle, CargoContainerId, out var container)
            ? container.ContainedEntities
            : Array.Empty<EntityUid>();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NWVehicleComponent, SolutionTransferAttemptEvent>(OnFuelTransferAttempt);
        SubscribeLocalEvent<NWVehicleComponent, TileFrictionEvent>(OnTileFriction);
        // PreventCollideEvent directly (the event bus allows only one directed subscription per
        // component/event pair, which the base SharedProjectileSystem already owns).
        SubscribeLocalEvent<ProjectilePreventCollideEvent>(OnProjectilePreventCollide);
    }

    private void OnFuelTransferAttempt(Entity<NWVehicleComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (args.To != ent.Owner)
            return;

        if (ent.Comp.State != NWVehicleState.Parked)
        {
            args.Cancel(Loc.GetString("nw-vehicle-fuel-refuel-running"));
            return;
        }

        if (!_solution.TryGetDrainableSolution(args.From, out _, out var source))
            return;

        var fuel = source.GetTotalPrototypeQuantity(ent.Comp.FuelReagent);
        if (fuel != source.Volume)
            args.Cancel(Loc.GetString("nw-vehicle-fuel-contaminated"));
    }

    private void OnTileFriction(Entity<NWVehicleComponent> ent, ref TileFrictionEvent args)
    {
        // The drivetrain already applies rolling resistance and terrain-specific speed limits.
        if (ent.Comp.State == NWVehicleState.Running)
            args.Modifier = 0f;
    }

    private void OnProjectilePreventCollide(ref ProjectilePreventCollideEvent args)
    {
        var projectile = args.Projectile;

        // broad fixture and strike the occupants sharing its cabin origin.
        if (projectile.Comp.Shooter is not { } shooter ||
            !TryComp<BuckleComponent>(shooter, out var shooterBuckle) ||
            shooterBuckle.BuckledTo is not { } seat)
        {
            return;
        }

        var vehicle = TryComp<NWVehicleCabinSeatComponent>(seat, out var cabinSeat) ? cabinSeat.Exterior ?? seat : seat;
        if (!HasComp<NWVehicleComponent>(vehicle)) return;
        if (args.OtherEntity == vehicle)
        {
            args.Cancelled = true;
            return;
        }

        if (TryComp<BuckleComponent>(args.OtherEntity, out var otherBuckle) &&
            otherBuckle.BuckledTo == vehicle)
        {
            args.Cancelled = true;
        }
    }
}
