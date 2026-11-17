using Content.Shared._NW.Telephone;
using Robust.Client.Graphics;

namespace Content.Client._NW.Telephone;

public sealed class NWTelephoneSystem : SharedNWTelephoneSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        if (!_overlay.HasOverlay<NWTelephoneOverlay>())
            _overlay.AddOverlay(new NWTelephoneOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<NWTelephoneOverlay>();
    }
}
