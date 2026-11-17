using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._NW.Telephone;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedNWTelephoneSystem))]
public sealed partial class NWRotaryPhoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Category = "General";

    [DataField, AutoNetworkedField]
    public string NetworkId = "public";

    // Configurable devices may create or join player networks; faction endpoints remain locked by default.
    [DataField] public bool AllowNetworkChanges;
    [DataField] public string NetworkName = string.Empty;
    // Not an AutoNetworkedField: invitation codes are only supplied to the device's configuration UI.
    [DataField] public string NetworkInviteCode = string.Empty;

    [DataField, AutoNetworkedField]
    public bool CanDnd = true;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? DialingSound = new SoundPathSpecifier("/Audio/_NW/Telephone/Phone/dial.ogg", AudioParams.Default.WithVolume(-3));

    [DataField, AutoNetworkedField]
    public SoundSpecifier? DialingIdleSound = new SoundPathSpecifier("/Audio/_NW/Telephone/Phone/ring_outgoing.ogg", AudioParams.Default.WithVolume(-3));

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ReceivingSound = new SoundPathSpecifier("/Audio/_NW/Telephone/Phone/telephone_ring.ogg", AudioParams.Default.WithVolume(-3));

    [DataField, AutoNetworkedField]
    public SoundSpecifier? GrabSound = new SoundCollectionSpecifier("NWRadioTelephoneGrab");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? VoicemailSound = new SoundPathSpecifier("/Audio/_NW/Telephone/Machines/Phone/voicemail.ogg", AudioParams.Default.WithVolume(-3));

    [DataField, AutoNetworkedField]
    public EntProtoId<NWTelephoneComponent> PhoneId = "NWTelephone";

    [DataField, AutoNetworkedField]
    public bool PhoneCanBeTransferred = false;

    [DataField, AutoNetworkedField]
    public string ContainerId = "nw_rotary_phone_telephone";

    [DataField, AutoNetworkedField]
    public EntityUid? Phone;

    [DataField, AutoNetworkedField]
    public EntityUid? Sound;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LastCall;

    [DataField, AutoNetworkedField]
    public TimeSpan CallCooldown = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public TimeSpan DialingIdleDelay = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public TimeSpan VoicemailDelay = TimeSpan.FromSeconds(30);

    [DataField, AutoNetworkedField]
    public TimeSpan VoicemailTimeoutDelay = TimeSpan.FromSeconds(6);

    [DataField]
    public EntityUid? VoicemailSoundEntity;

    [DataField, AutoNetworkedField]
    public bool Idle;

    [DataField, AutoNetworkedField]
    public bool TryGetHolderName = true;

    /// <summary>
    /// Should admins be notified when being called.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public bool NotifyAdmins { get; set; } = false;
}

[Serializable, NetSerializable]
public enum NWRotaryPhoneLayers
{
    Layer,
}

[Serializable, NetSerializable]
public enum NWRotaryPhoneVisuals
{
    Base,
    Ring,
    Ear,
}
