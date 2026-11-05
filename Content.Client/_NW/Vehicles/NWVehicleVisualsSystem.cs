using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Content.Shared._NW.Vehicles;
using Robust.Client.GameObjects;
using Robust.Shared.Physics.Components;
namespace Content.Client._NW.Vehicles;
public sealed class NWVehicleVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    private readonly Dictionary<EntityUid, bool> _hiddenOccupantVisibility = new();
    private readonly Dictionary<EntityUid, bool> _wheelAnimation = new();
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NWVehicleComponent, ComponentShutdown>(OnVehicleShutdown);
        SubscribeLocalEvent<NWVehicleHiddenOccupantComponent, ComponentStartup>(OnOccupantHidden);
        SubscribeLocalEvent<NWVehicleHiddenOccupantComponent, ComponentShutdown>(OnOccupantRevealed);
    }
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<NWVehicleComponent, SpriteComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var vehicle, out var sprite, out var body))
        {
            if (vehicle.HasOverlights && sprite.LayerMapTryGet("nw-overlights", out var lights))
                sprite.LayerSetState(lights, vehicle.LightsEnabled ? "overlight_on" : "overlight_off");
            if (vehicle.GunnerSeat >= 0)
            {
                sprite.LayerSetRotation(2, vehicle.TurretBearing);
                sprite.LayerSetRotation(3, vehicle.TurretBearing);
            }
            if (sprite.LayerMapTryGet("nw-humvee-wheels", out var wheelIndex) &&
                _sprite.TryGetLayer((uid, sprite), wheelIndex, out var wheels, false))
            {
                var facing = (_transform.GetWorldRotation(uid) + _eye.CurrentEye.Rotation).GetCardinalDir();
                // North turns local +X to screen left. Its eight RSI frames sit one pixel right.
                var offset = facing == Direction.North ? new Vector2(1f / 32f, 0f) : Vector2.Zero;
                if (wheels.Offset != offset)
                    sprite.LayerSetOffset(wheelIndex, offset);
            }

            var moving = body.LinearVelocity.LengthSquared() > 0.01f;
            if (_wheelAnimation.TryGetValue(uid, out var previous) && previous == moving) continue;
            if (!_sprite.TryGetLayer((uid, sprite), 1, out var layer, false)) continue;
            _sprite.LayerSetAutoAnimated(layer, moving);
            if (!moving) sprite.LayerSetAnimationTime(1, 0f);
            _wheelAnimation[uid] = moving;
        }
    }

    private void OnVehicleShutdown(Entity<NWVehicleComponent> ent, ref ComponentShutdown args)
    {
        _wheelAnimation.Remove(ent.Owner);
    }

    private void OnOccupantHidden(Entity<NWVehicleHiddenOccupantComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        _hiddenOccupantVisibility.TryAdd(ent.Owner, sprite.Visible);
        _sprite.SetVisible((ent.Owner, sprite), false);
    }
    private void OnOccupantRevealed(Entity<NWVehicleHiddenOccupantComponent> ent, ref ComponentShutdown args)
    {
        if (!_hiddenOccupantVisibility.Remove(ent.Owner, out var wasVisible) ||
            !TryComp<SpriteComponent>(ent, out var sprite))
        {
            return;
        }

        _sprite.SetVisible((ent.Owner, sprite), wasVisible);
    }
}
