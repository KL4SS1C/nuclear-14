using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._NW.Telephone;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedNWTelephoneSystem))]
public sealed partial class NWTelephoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? NWRotaryPhone;

    [DataField, AutoNetworkedField]
    public SoundSpecifier SpeakSound = new SoundCollectionSpecifier("NWPhoneSpeak", AudioParams.Default.WithVolume(-3));
}
