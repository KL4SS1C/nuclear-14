using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    [Dependency] private readonly ItemSlotsSystem _equipmentSlots = default!;

    private void OnMountedGunAim(Entity<NWVehicleComponent> ent, ref NWVehicleTurretAimEvent args)
    {
        ent.Comp.TurretBearing = args.Bearing - _transform.GetWorldRotation(ent.Owner);
        Dirty(ent);
    }

    private void PlowSnow(EntityUid vehicle, MapCoordinates position, Vector2 travel)
    {
        if (!TryComp<ItemSlotsComponent>(vehicle, out var slots) ||
            !_equipmentSlots.TryGetSlot(vehicle, "nw_plow", out var slot, slots) || !slot.HasItem)
            return;

        var transform = Transform(vehicle);
        if ((transform.GridUid ?? transform.MapUid) is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var heading = _transform.GetWorldRotation(vehicle).GetCardinalDir().ToIntVec();
        var forward = new Vector2(heading.X, heading.Y);
        if (Vector2.Dot(travel, forward) <= 0f)
            return;

        var blade = position.Position + forward * 0.9f;
        var side = new Vector2(-forward.Y, forward.X);
        for (var offset = -1; offset <= 1; offset++)
        {
            var indices = _map.WorldToTile(grid, gridComp, blade + side * (offset * 0.85f));
            if (_map.TryGetTileRef(grid, gridComp, indices, out var tile) &&
                _tileDefinitions[tile.Tile.TypeId].ID == "FloorSnow")
                _map.SetTile(grid, gridComp, indices, new Tile(_tileDefinitions["FloorSnowDug"].TileId));
        }
    }
}
