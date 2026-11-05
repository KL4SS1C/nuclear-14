// crew can work faster, which plain RepairableSystem gives no hook for.
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Tools;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NW.Vehicles;

[RegisterComponent]
public sealed partial class NWVehicleRepairComponent : Component
{
    /// <summary>
    /// Damage healed per completed repair. Negative values heal.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public int FuelCost = 5;

    [DataField]
    public int RepairPerPass = 40;

    [DataField]
    public ProtoId<ToolQualityPrototype> QualityNeeded = "Welding";

    /// <summary>
    /// Base do-after seconds for someone with no vehicle training.
    /// </summary>
    [DataField]
    public float DoAfterDelay = 8f;
}

[Serializable, NetSerializable]
public sealed partial class NWVehicleRepairFinishedEvent : SimpleDoAfterEvent;
