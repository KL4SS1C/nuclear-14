using Robust.Shared.Serialization;

namespace Content.Shared._NW.Telephone;

[Serializable, NetSerializable]
public enum NWTelephoneUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class NWTelephoneBuiState(List<NWPhone> phones, bool canDnd, bool dnd, bool canConfigure = false, string networkName = "", string inviteCode = "") : BoundUserInterfaceState
{
    public readonly List<NWPhone> Phones = phones;
    public readonly bool CanDnd = canDnd;
    public readonly bool Dnd = dnd;
    public readonly bool CanConfigure = canConfigure;
    public readonly string NetworkName = networkName;
    public readonly string InviteCode = inviteCode;
}

[Serializable, NetSerializable]
public sealed class NWTelephoneCallBuiMsg(NetEntity id) : BoundUserInterfaceMessage
{
    public readonly NetEntity Id = id;
}

[Serializable, NetSerializable]
public sealed class NWTelephoneDndBuiMsg(bool dnd) : BoundUserInterfaceMessage
{
    public readonly bool Dnd = dnd;
}

[Serializable, NetSerializable]
public readonly record struct NWPhone(NetEntity Id, string Category, string Name);


[Serializable, NetSerializable]
public enum NWPhoneNetworkOperation : byte
{
    Create,
    Join,
    Leave,
}

[Serializable, NetSerializable]
public sealed class NWPhoneNetworkMessage(NWPhoneNetworkOperation operation, string value, string inviteCode = "") : BoundUserInterfaceMessage
{
    public readonly NWPhoneNetworkOperation Operation = operation;
    public readonly string Value = value;
    public readonly string InviteCode = inviteCode;
}
