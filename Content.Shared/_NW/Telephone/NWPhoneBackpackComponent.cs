using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._NW.Telephone;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NWPhoneBackpackComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Action;
}

public sealed partial class NWPhoneBackpackActionEvent : InstantActionEvent;
