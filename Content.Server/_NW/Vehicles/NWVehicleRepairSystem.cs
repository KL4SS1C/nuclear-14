using Content.Server.Administration.Logs;
using Content.Shared._NW.Vehicles;
using Content.Shared.FixedPoint;
using Content.Shared.Verbs;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;

namespace Content.Server._NW.Vehicles;

public sealed class NWVehicleRepairSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<NWVehicleRepairComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<NWVehicleRepairComponent, GetVerbsEvent<InteractionVerb>>(OnRepairVerb);
        SubscribeLocalEvent<NWVehicleRepairComponent, NWVehicleRepairFinishedEvent>(OnRepairFinished);
    }

    private void OnInteractUsing(Entity<NWVehicleRepairComponent> ent, ref InteractUsingEvent args)
    {
        if (!args.Handled && _tool.HasQuality(args.Used, ent.Comp.QualityNeeded))
            args.Handled = StartRepair(ent, args.User, args.Used);
    }

    private void OnRepairVerb(Entity<NWVehicleRepairComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } tool ||
            !_tool.HasQuality(tool, ent.Comp.QualityNeeded))
            return;

        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("nw-vehicle-weld-hull"),
            Act = () => StartRepair(ent, user, tool),
        });
    }

    private bool Parked(EntityUid uid) =>
        TryComp<NWVehicleComponent>(uid, out var vehicle) && vehicle.State == NWVehicleState.Parked;

    private bool StartRepair(Entity<NWVehicleRepairComponent> ent, EntityUid user, EntityUid tool)
    {
        if (!Parked(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("nw-vehicle-repair-park"), ent, user);
            return true;
        }

        if (!TryComp<DamageableComponent>(ent, out var damageable) || damageable.TotalDamage <= 0)
        {
            _popup.PopupEntity(Loc.GetString("nw-vehicle-repair-undamaged"), ent, user);
            return true;
        }

        _tool.UseTool(tool, user, ent.Owner, ent.Comp.DoAfterDelay, ent.Comp.QualityNeeded,
            new NWVehicleRepairFinishedEvent(), ent.Comp.FuelCost);
        return true;
    }

    private void OnRepairFinished(Entity<NWVehicleRepairComponent> ent, ref NWVehicleRepairFinishedEvent args)
    {
        if (args.Cancelled || !Parked(ent.Owner))
            return;

        if (!TryComp<DamageableComponent>(ent, out var damageable) || damageable.TotalDamage == 0)
            return;

        var repair = ent.Comp.Damage;
        if (repair == null)
        {
            repair = new DamageSpecifier();
            var remaining = FixedPoint2.New(ent.Comp.RepairPerPass);
            foreach (var (type, damage) in damageable.Damage.DamageDict)
            {
                if (damage <= 0)
                    continue;
                var amount = FixedPoint2.Min(damage, remaining);
                repair.DamageDict[type] = -amount;
                remaining -= amount;
                if (remaining <= 0)
                    break;
            }
        }

        var changed = _damageable.TryChangeDamage(ent.Owner, repair, true, false, origin: args.User);
        if (changed == null || changed.GetTotal() >= 0)
            return;
        _adminLogger.Add(LogType.Healed,
            $"{ToPrettyString(args.User):user} repaired {ToPrettyString(ent.Owner):target} by {-changed.GetTotal()}");
        _popup.PopupEntity(Loc.GetString("nw-vehicle-repair-result", ("amount", -changed.GetTotal())), ent, args.User);
    }
}
