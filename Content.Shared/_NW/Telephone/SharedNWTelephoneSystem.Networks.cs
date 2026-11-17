using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;

namespace Content.Shared._NW.Telephone;

public abstract partial class SharedNWTelephoneSystem
{
    [Dependency] private readonly SharedInteractionSystem _networkInteraction = default!;
    [Dependency] private readonly MobStateSystem _networkMob = default!;

    private void NetworkNotice(EntityUid user, string message) => _popup.PopupEntity(message, user, user);

    private static bool ValidPlayerCode(string code)
    {
        if (code.Length < 3 || code.Length > 64)
            return false;
        foreach (var character in code)
        {
            if (char.IsControl(character))
                return false;
        }
        return true;
    }

    private void OnNetworkMessage(Entity<NWRotaryPhoneComponent> ent, ref NWPhoneNetworkMessage args)
    {
        if (_net.IsClient || !ent.Comp.AllowNetworkChanges || _networkMob.IsIncapacitated(args.Actor) ||
            !CanUsePortable(ent, args.Actor))
            return;
        if (!HasComp<NWPhoneBackpackComponent>(ent) && !_networkInteraction.InRangeUnobstructed(args.Actor, ent.Owner))
            return;
        if (HasComp<NWRotaryPhoneDialingComponent>(ent) || HasComp<NWRotaryPhoneReceivingComponent>(ent))
        {
            NetworkNotice(args.Actor, "Hang up before changing networks.");
            return;
        }

        var value = args.Value?.Trim() ?? string.Empty;
        switch (args.Operation)
        {
            case NWPhoneNetworkOperation.Create:
                if (value.Length < 3 || value.Length > 48)
                {
                    NetworkNotice(args.Actor, "Use a network name between 3 and 48 characters.");
                    return;
                }
                foreach (var character in value)
                {
                    if (char.IsControl(character))
                    {
                        NetworkNotice(args.Actor, "The network name contains invalid characters.");
                        return;
                    }
                }
                var requestedCode = args.InviteCode?.Trim() ?? string.Empty;
                if (!ValidPlayerCode(requestedCode))
                {
                    NetworkNotice(args.Actor, "Choose an invitation code between 3 and 64 characters, without control characters.");
                    return;
                }
                var existingPhones = EntityQueryEnumerator<NWRotaryPhoneComponent>();
                while (existingPhones.MoveNext(out var existingUid, out var existingPhone))
                {
                    if (!TerminatingOrDeleted(existingUid) &&
                        existingPhone.NetworkId.StartsWith("player:", StringComparison.Ordinal) &&
                        string.Equals(existingPhone.NetworkInviteCode.Trim(), requestedCode, StringComparison.OrdinalIgnoreCase))
                    {
                        NetworkNotice(args.Actor, "That invitation code is already in use. Choose another code or join the existing network.");
                        return;
                    }
                }
                ent.Comp.NetworkId = "player:" + Guid.NewGuid().ToString("N");
                ent.Comp.NetworkName = value;
                ent.Comp.NetworkInviteCode = requestedCode;
                break;

            case NWPhoneNetworkOperation.Join:
                var code = value;
                if (!ValidPlayerCode(code))
                {
                    NetworkNotice(args.Actor, "Invalid invitation code.");
                    return;
                }
                NWRotaryPhoneComponent? target = null;
                var query = EntityQueryEnumerator<NWRotaryPhoneComponent>();
                while (query.MoveNext(out var uid, out var other))
                {
                    if (!TerminatingOrDeleted(uid) && other.NetworkId.StartsWith("player:", StringComparison.Ordinal) &&
                        string.Equals(other.NetworkInviteCode.Trim(), code, StringComparison.OrdinalIgnoreCase))
                    {
                        target = other;
                        break;
                    }
                }
                if (target == null)
                {
                    NetworkNotice(args.Actor, "No existing player network matches that invitation code.");
                    return;
                }
                ent.Comp.NetworkId = target.NetworkId;
                ent.Comp.NetworkName = target.NetworkName;
                ent.Comp.NetworkInviteCode = target.NetworkInviteCode;
                break;

            case NWPhoneNetworkOperation.Leave:
                ent.Comp.NetworkId = "public";
                ent.Comp.NetworkName = string.Empty;
                ent.Comp.NetworkInviteCode = string.Empty;
                break;

            default:
                return;
        }

        Dirty(ent);
        SendUIState(ent);
        // Refresh open directories so a device moved to another network disappears immediately.
        var openPhones = EntityQueryEnumerator<NWRotaryPhoneComponent>();
        while (openPhones.MoveNext(out var uid, out _))
        {
            if (uid != ent.Owner && _ui.IsUiOpen(uid, NWTelephoneUiKey.Key))
                SendUIState(uid);
        }
        NetworkNotice(args.Actor, "Telephone network updated. Share its invitation code only with your contacts.");
    }
}
