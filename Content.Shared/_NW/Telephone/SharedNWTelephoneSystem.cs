using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Audio;
using Content.Shared.Database;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._NW.Telephone;

public abstract partial class SharedNWTelephoneSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    [Dependency] private readonly InventorySystem _inventory = default!;

    private static readonly SoundSpecifier RemotePickupSound = new SoundPathSpecifier("/Audio/_NW/Telephone/Phone/remote_pickup.ogg");
    private static readonly SoundSpecifier RemoteHangupSound = new SoundPathSpecifier("/Audio/_NW/Telephone/Phone/remote_hangup.ogg");
    private static readonly SoundSpecifier BusySound = new SoundPathSpecifier("/Audio/_NW/Telephone/Phone/phone_busy.ogg");

    public override void Initialize()
    {
        SubscribeLocalEvent<NWRotaryPhoneComponent, MapInitEvent>(OnNWRotaryPhoneMapInit);
        SubscribeLocalEvent<NWRotaryPhoneComponent, BeforeActivatableUIOpenEvent>(OnNWRotaryPhoneBeforeOpen);
        SubscribeLocalEvent<NWRotaryPhoneComponent, ComponentShutdown>(OnNWRotaryPhoneTerminating);
        SubscribeLocalEvent<NWRotaryPhoneComponent, EntityTerminatingEvent>(OnNWRotaryPhoneTerminating);

        SubscribeLocalEvent<NWRotaryPhoneDialingComponent, InteractUsingEvent>(OnNWRotaryPhoneDialingInteractUsing);

        SubscribeLocalEvent<NWRotaryPhoneReceivingComponent, InteractHandEvent>(OnNWRotaryPhoneReceivingInteractHand, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<NWRotaryPhoneReceivingComponent, InteractUsingEvent>(OnNWRotaryPhoneReceivingInteractUsing);

        SubscribeLocalEvent<NWTelephoneComponent, ComponentShutdown>(OnTelephoneTerminating);
        SubscribeLocalEvent<NWTelephoneComponent, EntityTerminatingEvent>(OnTelephoneTerminating);

        SubscribeLocalEvent<NWPickedUpPhoneComponent, GettingPickedUpAttemptEvent>(OnPickedUpPhoneGettingPickedUpAttempt);
        SubscribeLocalEvent<NWPickedUpPhoneComponent, ContainerGettingInsertedAttemptEvent>(OnPickedUpPhoneGettingInsertedAttempt);


        SubscribeLocalEvent<NWPhoneBackpackComponent, GetItemActionsEvent>(OnBackpackGetActions);
        SubscribeLocalEvent<NWPhoneBackpackComponent, NWPhoneBackpackActionEvent>(OnBackpackAction);

        Subs.BuiEvents<NWRotaryPhoneComponent>(NWTelephoneUiKey.Key,
            subs =>
            {
                subs.Event<NWPhoneNetworkMessage>(OnNetworkMessage);
                subs.Event<NWTelephoneCallBuiMsg>(OnTelephoneCallMsg);
                subs.Event<NWTelephoneDndBuiMsg>(OnTelephoneDndMsg);
            });
    }

    private void OnNWRotaryPhoneMapInit(Entity<NWRotaryPhoneComponent> ent, ref MapInitEvent args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerId);

        if (!TrySpawnInContainer(ent.Comp.PhoneId, ent, ent.Comp.ContainerId, out var phone))
            return;

        ent.Comp.Phone = phone.Value;
        Dirty(ent);

        if (TryComp(phone, out NWTelephoneComponent? phoneComp))
        {
            phoneComp.NWRotaryPhone = ent;
            Dirty(phone.Value, phoneComp);
        }
    }

    private void OnNWRotaryPhoneBeforeOpen(Entity<NWRotaryPhoneComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        SendUIState(ent);
    }

    private void OnNWRotaryPhoneTerminating<T>(Entity<NWRotaryPhoneComponent> ent, ref T args)
    {
        if (_net.IsServer)
        {
            EntityUid? other = null;
            if (TryComp<NWRotaryPhoneDialingComponent>(ent, out var dialing))
                other = dialing.Other;
            else if (TryComp<NWRotaryPhoneReceivingComponent>(ent, out var receiving))
                other = receiving.Other;
            if (other is { } remote && !TerminatingOrDeleted(remote))
            {
                RemComp<NWRotaryPhoneDialingComponent>(remote);
                RemComp<NWRotaryPhoneReceivingComponent>(remote);
                StopSound(remote);
                if (TryComp<NWRotaryPhoneComponent>(remote, out var remoteBase) && remoteBase.Phone is { } remoteHandset)
                    ReturnPhone(remote, remoteHandset, null);
                UpdateAppearance(remote, true);
            }
            if (ent.Comp.Phone is { } handset && !TerminatingOrDeleted(handset))
                QueueDel(handset);
        }
        if (TryComp(ent.Comp.Phone, out NWTelephoneComponent? phone))
        {
            phone.NWRotaryPhone = null;
            Dirty(ent.Comp.Phone.Value, phone);
        }
    }



    private void OnNWRotaryPhoneDialingInteractUsing(Entity<NWRotaryPhoneDialingComponent> ent, ref InteractUsingEvent args)
    {
        if (HangUpDialing(ent, args.Used, args.User))
            args.Handled = true;
    }

    private void OnNWRotaryPhoneReceivingInteractHand(Entity<NWRotaryPhoneReceivingComponent> ent, ref InteractHandEvent args)
    {
        if (_timing.ApplyingState)
            return;

        args.Handled = true;
        PickupReceiving(ent, args.User);
    }

    private void OnNWRotaryPhoneReceivingInteractUsing(Entity<NWRotaryPhoneReceivingComponent> ent, ref InteractUsingEvent args)
    {
        if (HangUpReceiving(ent, args.Used, args.User))
            args.Handled = true;
    }

    private void OnTelephoneTerminating<T>(Entity<NWTelephoneComponent> ent, ref T args)
    {
        if (TryComp(ent.Comp.NWRotaryPhone, out NWRotaryPhoneComponent? phone))
        {
            phone.Phone = null;
            Dirty(ent.Comp.NWRotaryPhone.Value, phone);
        }
    }

    private void OnPickedUpPhoneGettingPickedUpAttempt(Entity<NWPickedUpPhoneComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (CanTransferPickedUpPhone(ent, args.User))
            return;

        args.Cancel();
    }

    private void OnPickedUpPhoneGettingInsertedAttempt(Entity<NWPickedUpPhoneComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (ent.Comp.CanTransfer)
            return;

        if (TryComp(ent, out NWTelephoneComponent? phone) &&
            phone.NWRotaryPhone == args.Container.Owner)
        {
            return;
        }

        if (ent.Comp.OriginalHolder is { } holder &&
            args.Container.Owner == holder &&
            TryComp(holder, out HandsComponent? hands) &&
            _hands.TryGetHand(holder, args.Container.ID, out _, hands))
        {
            return;
        }

        args.Cancel();
    }





    private void OnTelephoneCallMsg(Entity<NWRotaryPhoneComponent> ent, ref NWTelephoneCallBuiMsg args)
    {
        if (!CanUsePortable(ent, args.Actor))
            return;
        var time = _timing.CurTime;
        if (time < ent.Comp.LastCall + ent.Comp.CallCooldown)
            return;

        _ui.CloseUi(ent.Owner, NWTelephoneUiKey.Key);

        if (_net.IsClient)
            return;

        if (GetEntity(args.Id) is not { Valid: true } target ||
            ent.Owner == target ||
            !TryComp(target, out NWRotaryPhoneComponent? targetNWRotaryPhone))
        {
            return;
        }

        if (HasComp<NWPhoneBackpackComponent>(target) && !TryGetBackpackHolder(target, out _))
            return;
        if (IsPhoneBusy(ent) || !SameNetwork(ent.Comp, targetNWRotaryPhone))
            return;
        if (ent.Comp.Phone is not { } handset || !IsStoredInNWRotaryPhone(ent, handset) ||
            !_hands.TryGetEmptyHand(args.Actor, out _))
        {
            _popup.PopupEntity("You need a free hand to use the telephone.", args.Actor, args.Actor);
            return;
        }

        var user = args.Actor;
        if (IsPhoneBusy(target))
        {
            _popup.PopupEntity("That phone is busy!", user, user, PopupType.MediumCaution);
            return;
        }


        // Emit the popup on a successful call.
        if (TryComp(user, out MetaDataComponent? marineMeta) &&
            TryComp(ent, out MetaDataComponent? phoneMeta))
        {
            _popup.PopupEntity($"{marineMeta.EntityName} dials a number on the {phoneMeta.EntityName}.", ent);
        }

        ent.Comp.Idle = false;
        ent.Comp.LastCall = time;
        Dirty(ent);

        var dialing = EnsureComp<NWRotaryPhoneDialingComponent>(ent);
        dialing.Other = target;
        Dirty(ent, dialing);

        var receiving = EnsureComp<NWRotaryPhoneReceivingComponent>(target);
        receiving.Other = ent;
        Dirty(target, receiving);

        if (_net.IsServer)
        {
            if (ent.Comp.DialingSound is { } dialingSound)
            {
                var selfSound = EnsureComp<AmbientSoundComponent>(ent);
                _ambientSound.SetSound(ent, dialingSound, selfSound);
                _ambientSound.SetRange(ent, 16, selfSound);
                _ambientSound.SetVolume(ent, dialingSound.Params.Volume, selfSound);
                _ambientSound.SetAmbience(ent, true, selfSound);
            }

            if (targetNWRotaryPhone.ReceivingSound is { } receivingSound)
            {
                var otherSound = EnsureComp<AmbientSoundComponent>(target);
                _ambientSound.SetSound(target, receivingSound, otherSound);
                _ambientSound.SetRange(target, 16, otherSound);
                _ambientSound.SetVolume(target, receivingSound.Params.Volume, otherSound);
                _ambientSound.SetAmbience(target, true, otherSound);
                var ev = new NWTelephoneRingEvent(target, ent, args.Actor);
                RaiseLocalEvent(ref ev);
            }
        }

        if (ent.Comp.Phone is { } phone)
            PickupPhone(ent, phone, user);

        UpdateAppearance((ent, ent));
        UpdateAppearance((target, targetNWRotaryPhone));

        _adminLog.Add(LogType.Action, $"{ToPrettyString(args.Actor)} started calling {ToPrettyString(target)} using {ToPrettyString(ent)}");
    }


    private void OnTelephoneDndMsg(Entity<NWRotaryPhoneComponent> ent, ref NWTelephoneDndBuiMsg args)
    {
        if (!CanUsePortable(ent, args.Actor))
            return;
        if (args.Dnd && ent.Comp.CanDnd)
        {
            EnsureComp<NWRotaryPhoneDndComponent>(ent);
        }
        else
        {
            RemComp<NWRotaryPhoneDndComponent>(ent);
        }
        SendUIState(ent);
    }

    private bool IsPhoneBusy(EntityUid ent)
    {
        return HasComp<NWRotaryPhoneDialingComponent>(ent) || HasComp<NWRotaryPhoneReceivingComponent>(ent) || HasComp<NWRotaryPhoneDndComponent>(ent);
    }

    private void UpdateAppearance(Entity<NWRotaryPhoneComponent?> phone, bool forceNotRinging = false)
    {
        if (!Resolve(phone, ref phone.Comp, false))
            return;

        var visual = NWRotaryPhoneVisuals.Base;
        if (!_container.TryGetContainer(phone, phone.Comp.ContainerId, out var container) ||
            container.ContainedEntities.Count == 0)
        {
            visual = NWRotaryPhoneVisuals.Ear;
        }
        else if (HasComp<NWRotaryPhoneReceivingComponent>(phone) && !forceNotRinging)
        {
            visual = NWRotaryPhoneVisuals.Ring;
        }

        _appearance.SetData(phone, NWRotaryPhoneLayers.Layer, visual);
    }

    protected virtual void PickupPhone(Entity<NWRotaryPhoneComponent> rotary, EntityUid telephone, EntityUid user)
    {
        var pickedUp = EnsureComp<NWPickedUpPhoneComponent>(telephone);
        pickedUp.CanTransfer = rotary.Comp.PhoneCanBeTransferred;
        pickedUp.OriginalHolder = user;
        Dirty(telephone, pickedUp);

        if (_container.TryGetContainer(rotary, rotary.Comp.ContainerId, out var container))
            _container.Remove(telephone, container);

        _hands.TryPickupAnyHand(user, telephone);
        PlayGrabSound(rotary);
    }

    private void ReturnPhone(EntityUid rotary, EntityUid telephone, EntityUid? user)
    {
        if (!TryComp(rotary, out NWRotaryPhoneComponent? rotaryPhone) ||
            rotaryPhone.Phone != telephone ||
            !_container.TryGetContainer(rotary, rotaryPhone.ContainerId, out var container))
        {
            return;
        }

        if (user != null)
        {
            if (_hands.TryDropIntoContainer(user.Value, telephone, container))
                PlayGrabSound(rotary);
        }
        else
        {
            if (_container.Insert(telephone, container))
                PlayGrabSound(rotary);
        }
    }

    private void HangUp(EntityUid self, EntityUid other, EntityUid? user)
    {
        StopSound(self);

        if (!HasComp<NWRotaryPhoneDialingComponent>(other) &&
            !HasComp<NWRotaryPhoneReceivingComponent>(other))
        {
            StopSound(other);
            return;
        }

        if (_net.IsServer)
        {
            _ambientSound.SetSound(other, BusySound);
            _ambientSound.SetVolume(other, BusySound.Params.Volume);
            _ambientSound.SetAmbience(other, true);
        }

        if (!HasPickedUp(other))
            return;

        if (_net.IsServer)
            _audio.PlayPvs(RemoteHangupSound, other);

        _adminLog.Add(LogType.Action, $"{ToPrettyString(user)} hung up {ToPrettyString(self)} while calling {ToPrettyString(other)}");
    }

    private void StopSound(EntityUid ent)
    {
        _ambientSound.SetAmbience(ent, false);
    }

    private void PlayGrabSound(EntityUid rotary)
    {
        if (_net.IsClient)
            return;

        if (!TryComp(rotary, out NWRotaryPhoneComponent? comp))
            return;

        _audio.PlayPvs(comp.GrabSound, rotary);
        _audio.Stop(comp.VoicemailSoundEntity);
    }

    protected bool TryGetOtherPhone(EntityUid rotary, out EntityUid other)
    {
        if (TryComp(rotary, out NWRotaryPhoneDialingComponent? dialing) &&
            TryComp(dialing.Other, out NWRotaryPhoneComponent? otherRotary) &&
            otherRotary.Phone != null)
        {
            other = otherRotary.Phone.Value;
            return true;
        }
        else if (TryComp(rotary, out NWRotaryPhoneReceivingComponent? receiving) &&
                 TryComp(receiving.Other, out otherRotary) &&
                 otherRotary.Phone != null)
        {
            other = otherRotary.Phone.Value;
            return true;
        }

        other = default;
        return false;
    }

    private bool IsCorrectPhone(Entity<NWRotaryPhoneComponent?> rotary, EntityUid phone)
    {
        return Resolve(rotary, ref rotary.Comp, false) && rotary.Comp.Phone == phone;
    }

    private bool HasPickedUp(Entity<NWRotaryPhoneComponent?, NWRotaryPhoneReceivingComponent?> receiving)
    {
        return Resolve(receiving, ref receiving.Comp1, ref receiving.Comp2, false) &&
               _container.TryGetContainer(receiving, receiving.Comp1.ContainerId, out var container) &&
               container.ContainedEntities.Count == 0;
    }



    private bool TryGetPhoneHandHolder(EntityUid phone, out EntityUid holder)
    {
        holder = default;
        if (!_container.TryGetContainingContainer((phone, null, null), out var container))
            return false;

        if (!_hands.IsHolding(container.Owner, phone))
            return false;

        holder = container.Owner;
        return true;
    }

    private bool CanTransferPickedUpPhone(Entity<NWPickedUpPhoneComponent> pickedUp, EntityUid user)
    {
        return pickedUp.Comp.CanTransfer ||
               pickedUp.Comp.OriginalHolder == null ||
               pickedUp.Comp.OriginalHolder == user;
    }

    private bool IsStoredInNWRotaryPhone(EntityUid rotary, EntityUid phone, NWRotaryPhoneComponent? rotaryPhone = null)
    {
        if (!Resolve(rotary, ref rotaryPhone, false))
            return false;

        return _container.TryGetContainer(rotary, rotaryPhone.ContainerId, out var container) &&
               container.Contains(phone);
    }

    private bool ShouldReturnPickedUpPhone(EntityUid phone, NWPickedUpPhoneComponent pickedUp, EntityUid rotary)
    {
        if (pickedUp.CanTransfer)
            return false;

        if (IsStoredInNWRotaryPhone(rotary, phone))
            return false;

        return pickedUp.OriginalHolder == null ||
               !TryGetPhoneHandHolder(phone, out var holder) ||
               holder != pickedUp.OriginalHolder;
    }

    private bool ReturnPickedUpPhone(EntityUid rotary, EntityUid phone, EntityUid? user)
    {
        if (TryComp(rotary, out NWRotaryPhoneDialingComponent? dialing))
            return HangUpDialing((rotary, dialing), phone, user);

        if (TryComp(rotary, out NWRotaryPhoneReceivingComponent? receiving))
            return HangUpReceiving((rotary, receiving), phone, user);

        ReturnPhone(rotary, phone, user);
        return true;
    }

    protected string GetPhoneName(Entity<NWRotaryPhoneComponent?> phone)
    {
        if (HasComp<NWPhoneBackpackComponent>(phone) && TryGetBackpackHolder(phone, out var holder))
            return $"{Name(holder)} — {Name(phone)}";
        return Name(phone);
    }

    protected static bool SameNetwork(NWRotaryPhoneComponent a, NWRotaryPhoneComponent b) =>
        !string.IsNullOrWhiteSpace(a.NetworkId) && !string.IsNullOrWhiteSpace(b.NetworkId) &&
        string.Equals(a.NetworkId.Trim(), b.NetworkId.Trim(), StringComparison.Ordinal);

    private bool TryGetBackpackHolder(EntityUid backpack, out EntityUid holder)
    {
        holder = default;
        if (!_container.TryGetContainingContainer((backpack, null, null), out var container))
            return false;
        if (!(HasComp<HandsComponent>(container.Owner) && _hands.IsHolding(container.Owner, backpack)) &&
            !(HasComp<InventoryComponent>(container.Owner) &&
              _inventory.TryGetSlotEntity(container.Owner, "back", out var worn) && worn == backpack))
            return false;
        holder = container.Owner;
        return true;
    }

    protected bool CanUsePortable(EntityUid phone, EntityUid user) =>
        !HasComp<NWPhoneBackpackComponent>(phone) ||
        (TryGetBackpackHolder(phone, out var holder) && holder == user);

    private void OnBackpackGetActions(EntityUid uid, NWPhoneBackpackComponent component, GetItemActionsEvent args)
    {
        if (!args.InHands && (args.SlotFlags & SlotFlags.BACK) == 0)
            return;
        var previous = component.Action;
        args.AddAction(ref component.Action, "NWActionPhoneBackpack", uid);
        if (previous != component.Action)
            Dirty(uid, component);
    }

    private void OnBackpackAction(Entity<NWPhoneBackpackComponent> ent, ref NWPhoneBackpackActionEvent args)
    {
        if (args.Handled || !CanUsePortable(ent, args.Performer))
            return;
        args.Handled = true;
        if (_net.IsClient)
            return;
        if (TryComp<NWRotaryPhoneComponent>(ent, out var portable) && portable.Phone is { } handset &&
            _hands.IsHolding(args.Performer, handset))
        {
            ReturnPickedUpPhone(ent, handset, args.Performer);
            return;
        }
        if (TryComp<NWRotaryPhoneReceivingComponent>(ent, out var receiving))
        {
            PickupReceiving((ent, receiving), args.Performer);
            return;
        }
        if (HasComp<NWRotaryPhoneDialingComponent>(ent))
            return;
        SendUIState(ent);
        _ui.TryOpenUi(ent.Owner, NWTelephoneUiKey.Key, args.Performer);
    }

    private void SendUIState(EntityUid phone)
    {
        if (_net.IsClient)
            return;

        var phones = new List<NWPhone>();
        var phonesQuery = EntityQueryEnumerator<NWRotaryPhoneComponent>();
        while (phonesQuery.MoveNext(out var otherId, out var otherComp))
        {
            if (otherId == phone || !SameNetwork(Comp<NWRotaryPhoneComponent>(phone), otherComp))
                continue;

            if (HasComp<NWPhoneBackpackComponent>(otherId) && !TryGetBackpackHolder(otherId, out _))
                continue;
            var name = GetPhoneName((otherId, otherComp));
            phones.Add(new NWPhone(GetNetEntity(otherId), otherComp.Category, name));
        }
        var canDnd = Comp<NWRotaryPhoneComponent>(phone).CanDnd;
        var dnd = HasComp<NWRotaryPhoneDndComponent>(phone);

        var settings = Comp<NWRotaryPhoneComponent>(phone);
        var label = string.IsNullOrWhiteSpace(settings.NetworkName) ? settings.NetworkId : settings.NetworkName;
        var code = settings.AllowNetworkChanges && settings.NetworkId.StartsWith("player:", StringComparison.Ordinal)
            ? settings.NetworkInviteCode : string.Empty;
        _ui.SetUiState(phone, NWTelephoneUiKey.Key,
            new NWTelephoneBuiState(phones, canDnd, dnd, settings.AllowNetworkChanges, label, code));
    }

    private void PickupReceiving(Entity<NWRotaryPhoneReceivingComponent> receiving, EntityUid user)
    {
        if (!CanUsePortable(receiving, user))
            return;
        if (!_hands.TryGetEmptyHand(user, out _))
        {
            _popup.PopupEntity("You need a free hand to answer the telephone.", user, user);
            return;
        }
        if (TryComp(receiving, out NWRotaryPhoneComponent? rotaryPhone) &&
            rotaryPhone.Phone is { } phone)
        {
            PickupPhone((receiving, rotaryPhone), phone, user);
        }

        if (rotaryPhone?.Phone is not { } pickedUp || !_hands.IsHolding(user, pickedUp))
            return;

        StopSound(receiving);

        if (receiving.Comp.Other is { } other)
        {
            StopSound(other);

            if (_net.IsServer)
                _audio.PlayPvs(RemotePickupSound, other);
        }

        UpdateAppearance((receiving, rotaryPhone));
        _adminLog.Add(LogType.Action, $"{ToPrettyString(user)} picked up {ToPrettyString(receiving)}");
    }



    private bool HangUpDialing(Entity<NWRotaryPhoneDialingComponent> ent, EntityUid phone, EntityUid? user)
    {
        if (!IsCorrectPhone(ent.Owner, phone))
            return false;

        RemCompDeferred<NWRotaryPhoneDialingComponent>(ent);
        ReturnPhone(ent.Owner, phone, user);
        StopSound(ent.Owner);

        if (ent.Comp.Other is { } other)
        {
            StopSound(other);
            HangUp(ent, other, user);

            if (!HasPickedUp(other))
            {
                RemCompDeferred<NWRotaryPhoneReceivingComponent>(other);
                StopSound(other);
            }

            UpdateAppearance(other, true);
        }

        UpdateAppearance(ent.Owner, true);
        return true;
    }

    private bool HangUpReceiving(Entity<NWRotaryPhoneReceivingComponent> ent, EntityUid used, EntityUid? user)
    {
        if (!IsCorrectPhone(ent.Owner, used))
            return false;

        RemCompDeferred<NWRotaryPhoneReceivingComponent>(ent);
        ReturnPhone(ent.Owner, used, user);

        if (ent.Comp.Other is { } other)
        {
            if (TryComp<NWRotaryPhoneDialingComponent>(other, out var dialing))
            {
                dialing.Other = null;
                Dirty(other, dialing);
            }

            HangUp(ent, other, user);

            if (!HasPickedUp(other))
                RemCompDeferred<NWRotaryPhoneReceivingComponent>(other);
        }

        UpdateAppearance(ent.Owner, true);
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var dialingQuery = EntityQueryEnumerator<NWRotaryPhoneDialingComponent, NWRotaryPhoneComponent>();
        while (dialingQuery.MoveNext(out var uid, out var dialing, out var phone))
        {
            if (phone.Phone == null)
                continue;

            // Play busy sound after voicemail ends
            if (time > dialing.LastVoicemail + phone.VoicemailTimeoutDelay && dialing.DidVoicemail && !dialing.DidVoicemailTimeout)
            {
                dialing.DidVoicemailTimeout = true;
                Dirty(uid, dialing);

                _ambientSound.SetSound(uid, BusySound);
                _ambientSound.SetVolume(uid, BusySound.Params.Volume);
                _ambientSound.SetAmbience(uid, true);
            }

            if (dialing.Other is not { } other)
                continue;

            if (!TryComp<NWRotaryPhoneReceivingComponent>(other, out var receiving))
                continue;

            if (!TryComp<NWRotaryPhoneComponent>(other, out var receivingPhone))
                continue;

            if (receivingPhone.Phone == null)
                continue;

            if (HasPickedUp(other))
                continue;

            if (phone.Idle)
            {
                if (time > phone.LastCall + phone.VoicemailDelay && !dialing.DidVoicemail)
                {
                    if (HangUpReceiving((other, receiving), receivingPhone.Phone.Value, null))
                    {
                        StopSound(other);
                        StopSound(uid);
                    }

                    phone.VoicemailSoundEntity = _audio.PlayPvs(phone.VoicemailSound, phone.Phone.Value)?.Entity;
                    dialing.DidVoicemail = true;
                    dialing.LastVoicemail = time;
                    Dirty(uid, dialing);
                    Dirty(uid, phone);
                }

                continue;
            }

            if (time > phone.LastCall + phone.DialingIdleDelay && phone.DialingIdleSound is { } sound)
            {
                phone.Idle = true;
                Dirty(uid, phone);

                _ambientSound.SetSound(uid, sound);
                _ambientSound.SetVolume(uid, sound.Params.Volume);
                _ambientSound.SetAmbience(uid, true);
            }
        }

        var pickedUpPhonesQuery = EntityQueryEnumerator<NWPickedUpPhoneComponent, NWTelephoneComponent>();
        while (pickedUpPhonesQuery.MoveNext(out var uid, out var pickedUp, out var telephone))
        {
            if (telephone.NWRotaryPhone is not { } rotary)
                continue;

            if (ShouldReturnPickedUpPhone(uid, pickedUp, rotary))
            {
                if (ReturnPickedUpPhone(rotary, uid, null))
                    _popup.PopupEntity($"The {Name(uid)} snaps back to the {Name(rotary)}!", uid, PopupType.MediumCaution);

                continue;
            }

            void PhoneSnapBackPopup()
            {
                _popup.PopupEntity($"The {Name(uid)} snaps back to the {Name(rotary)}!", uid, PopupType.MediumCaution);
            }

            var phonePosition = _transform.GetMoverCoordinates(uid);
            var rotaryPosition = _transform.GetMoverCoordinates(rotary);
            if (!phonePosition.TryDistance(EntityManager, _transform, rotaryPosition, out var distance) ||
                distance > pickedUp.Range)
            {
                if (TryComp(rotary, out NWRotaryPhoneDialingComponent? dialing))
                {
                    if (HangUpDialing((rotary, dialing), uid, null))
                        PhoneSnapBackPopup();
                }
                else if (TryComp(rotary, out NWRotaryPhoneReceivingComponent? receiving))
                {
                    if (HangUpReceiving((rotary, receiving), uid, null))
                        PhoneSnapBackPopup();
                }
            }
        }
    }
}
