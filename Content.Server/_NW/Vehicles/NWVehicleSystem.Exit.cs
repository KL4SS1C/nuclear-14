using System.Numerics;
using Content.Shared._NW.Vehicles;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    private void OnExitAttempt(Entity<NWVehicleComponent> ent, ref UnstrapAttemptEvent args)
    {
        if (args.Cancelled || GetSeatIndex(ent.Comp, args.Buckle.Owner) is not { } seat ||
            TryGetExit(ent, args.Buckle.Owner, seat, out _))
            return;

        args.Cancelled = true;
        if (args.Popup && args.User is { } user)
            _popup.PopupEntity(Loc.GetString("nw-vehicle-exit-blocked"), ent.Owner, user);
    }

    private bool TryGetExit(EntityUid ent, EntityUid occupant, int seat, out EntityCoordinates exit)
    {
        exit = default;
        var center = _transform.GetMapCoordinates(ent);
        var facing = _transform.GetWorldRotation(ent).GetCardinalDir().ToIntVec();
        var forward = new Vector2(facing.X, facing.Y);
        var side = new Vector2(-forward.Y, forward.X) * (seat % 2 == 0 ? 1f : -1f);
        var bounds = _lookup.GetWorldAABB(occupant);
        var clearance = MathF.Max(0.36f, MathF.Max(bounds.Width, bounds.Height) * 0.5f);
        var hull = _lookup.GetWorldAABB(ent);
        var hullHalfWidth = MathF.Abs(hull.Width - hull.Height) < 0.001f
            ? 0.9f
            : (side.X != 0f ? hull.Width : hull.Height) * 0.5f;
        // Start at this seat's row and try the other two door positions on the same side.
        var primaryRow = seat / 2;
        if (TryComp<NWVehicleComponent>(ent, out var vehicle) && seat == vehicle.GunnerSeat)
            primaryRow = 1;
        else if ((vehicle == null || vehicle.SeatCount == 4 || vehicle.GunnerSeat >= 0) && primaryRow == 1)
            primaryRow = 2;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = (primaryRow + attempt) % 3;
            var door = center.Position + forward * (0.75f - row * 0.75f);
            var edge = door + side * (hullHalfWidth + 0.03f);
            var target = edge + side * (clearance + 0.12f);
            var extent = new Vector2(clearance);
            var area = new Box2(Vector2.Min(edge, target - extent), Vector2.Max(edge, target + extent));
            if (!ExitIsClear(ent, occupant, center.MapId, area))
                continue;

            exit = _transform.ToCoordinates(Transform(ent).ParentUid, new MapCoordinates(target, center.MapId));
            return true;
        }
        return false;
    }

    private bool ExitIsClear(EntityUid vehicle, EntityUid occupant, MapId map, Box2 area)
    {
        foreach (var other in _lookup.GetEntitiesIntersecting(map, area))
        {
            if (other == vehicle || other == occupant || HasComp<MapGridComponent>(other) ||
                !TryComp<PhysicsComponent>(other, out var body) || !body.CanCollide ||
                !TryComp<FixturesComponent>(other, out var fixtures))
                continue;

            if (HasComp<MobStateComponent>(other) || fixtures.Fixtures.Values.Any(fixture =>
                    fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.MobMask) != 0))
                return false;
        }
        return true;
    }
}
