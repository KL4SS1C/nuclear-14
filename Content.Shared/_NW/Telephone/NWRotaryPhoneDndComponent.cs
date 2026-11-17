using Robust.Shared.GameStates;

namespace Content.Shared._NW.Telephone;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedNWTelephoneSystem))]
public sealed partial class NWRotaryPhoneDndComponent : Component;
