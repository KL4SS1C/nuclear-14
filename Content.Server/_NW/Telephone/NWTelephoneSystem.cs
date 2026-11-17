using Content.Server.Chat.Managers;
using Content.Server.Hands.Systems;
using Content.Server.Speech;
using Content.Server.Speech.Components;
using Content.Shared._NW.Telephone;
using Content.Shared.Chat;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._NW.Telephone;

public sealed partial class NWTelephoneSystem : SharedNWTelephoneSystem
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NWTelephoneComponent, ListenEvent>(OnListen);
    }

    protected override void PickupPhone(Entity<NWRotaryPhoneComponent> rotary, EntityUid telephone, EntityUid user)
    {
        base.PickupPhone(rotary, telephone, user);
        if (_hands.IsHolding(user, telephone))
            EnsureComp<ActiveListenerComponent>(telephone);
    }

    private void OnListen(Entity<NWTelephoneComponent> ent, ref ListenEvent args)
    {
        if (HasComp<NWTelephoneComponent>(args.Source) || !_hands.IsHolding(args.Source, ent))
            return;
        if (ent.Comp.NWRotaryPhone is not { } rotary ||
            !TryGetOtherPhone(rotary, out var otherPhone) ||
            !_containers.TryGetContainingContainer((otherPhone, null, null), out var container) ||
            !_hands.IsHolding(container.Owner, otherPhone) ||
            !TryComp(container.Owner, out ActorComponent? actor))
            return;

        if (!TryComp<NWRotaryPhoneComponent>(rotary, out var sourceBase) ||
            !TryComp<NWTelephoneComponent>(otherPhone, out var remoteHandset) ||
            remoteHandset.NWRotaryPhone is not { } remoteBaseId ||
            !TryComp<NWRotaryPhoneComponent>(remoteBaseId, out var remoteBase) ||
            !SameNetwork(sourceBase, remoteBase) ||
            !CanUsePortable(rotary, args.Source) || !CanUsePortable(remoteBaseId, container.Owner))
            return;

        var name = FormattedMessage.EscapeText(GetPhoneName(rotary));
        var text = $"{name} says, \"{FormattedMessage.EscapeText(args.Message)}\"";
        _chatManager.ChatMessageToOne(ChatChannel.Local, args.Message, text, otherPhone,
            false, actor.PlayerSession.Channel, Color.FromHex("#9956D3"), true);
    }
}
