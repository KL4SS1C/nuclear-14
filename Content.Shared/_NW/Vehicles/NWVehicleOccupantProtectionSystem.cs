using Content.Shared.Body.Part;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;

namespace Content.Shared._NW.Vehicles;

/// <summary>Protect seated occupants without granting persistent godmode.</summary>
public sealed class NWVehicleOccupantProtectionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
    }

    private void OnBeforeDamage(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // Healing is allowed. Reject positive damage before Shitmed distributes it to limbs.
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;

        // Direct damage to an attached limb must use its owning body's seat as well.
        var occupant = ent.Owner;
        if (TryComp<BodyPartComponent>(occupant, out var part) && part.Body is { } body)
            occupant = body;

        if (!TryComp<BuckleComponent>(occupant, out var buckle) ||
            buckle.BuckledTo is not { } vehicle ||
            !HasComp<NWGroundVehicleComponent>(vehicle) ||
            !HasComp<NWVehicleComponent>(vehicle) ||
            Terminating(vehicle))
            return;

        // BuckledTo is networked and authoritative; do not rely on the server-only seat array.
        args.Cancelled = true;
    }
}
