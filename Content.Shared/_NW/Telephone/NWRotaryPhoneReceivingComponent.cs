using Robust.Shared.GameStates;

namespace Content.Shared._NW.Telephone;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedNWTelephoneSystem))]
public sealed partial class NWRotaryPhoneReceivingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Other;
}
