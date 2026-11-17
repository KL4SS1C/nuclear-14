using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._NW.Telephone;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedNWTelephoneSystem))]
public sealed partial class NWRotaryPhoneDialingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Other;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LastVoicemail;

    [DataField, AutoNetworkedField]
    public bool DidVoicemail = false;

    [DataField, AutoNetworkedField]
    public bool DidVoicemailTimeout = false;
}
