using Content.Shared._NW.Telephone;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._NW.Telephone;

public sealed class NWTelephoneOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entity = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public NWTelephoneOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var containerSystem = _entity.System<ContainerSystem>();
        var transformSystem = _entity.System<TransformSystem>();
        var handle = args.WorldHandle;

        var rotarys = _entity.EntityQueryEnumerator<NWRotaryPhoneComponent>();
        while (rotarys.MoveNext(out var rotaryId, out var rotary))
        {
            if (rotary.Phone is not { Valid: true } phone ||
                !containerSystem.TryGetContainer(rotaryId, rotary.ContainerId, out var container) ||
                container.ContainedEntities.Count > 0 ||
                !_entity.TryGetComponent(rotaryId, out TransformComponent? rotaryTransform) ||
                rotaryTransform.MapID == MapId.Nullspace ||
                !_entity.TryGetComponent(phone, out TransformComponent? phoneTransform) ||
                phoneTransform.MapID == MapId.Nullspace)
            {
                continue;
            }

            var rotaryPosition = transformSystem.GetMapCoordinates(rotaryId);
            var phonePosition = transformSystem.GetMapCoordinates(phone);
            if (rotaryPosition.MapId != args.MapId || phonePosition.MapId != args.MapId)
                continue;
            handle.DrawLine(rotaryPosition.Position, phonePosition.Position, Color.Black);
        }
    }
}
