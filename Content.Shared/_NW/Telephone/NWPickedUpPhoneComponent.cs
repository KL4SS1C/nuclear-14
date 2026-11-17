using Robust.Shared.GameStates;

namespace Content.Shared._NW.Telephone;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedNWTelephoneSystem))]
public sealed partial class NWPickedUpPhoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Range = 7;

    [DataField, AutoNetworkedField]
    public bool CanTransfer = true;

    [DataField, AutoNetworkedField]
    public EntityUid? OriginalHolder;
}
