using Content.Shared._NW.Vehicles;
using Robust.Server.GameObjects;

namespace Content.Server._NW.Vehicles;

public sealed partial class NWVehicleSystem
{
    [Dependency] private readonly SharedPointLightSystem _vehicleLights = default!;

    private void OnSetGear(Entity<NWVehicleComponent> ent, ref NWVehicleSetGearMessage args)
    {
        if (ent.Comp.Pilot != args.Actor || _mobState.IsIncapacitated(args.Actor))
            return;

        ent.Comp.LowGear = args.LowGear;
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnSetLights(Entity<NWVehicleComponent> ent, ref NWVehicleSetLightsMessage args)
    {
        if (!ent.Comp.HasOverlights || _mobState.IsIncapacitated(args.Actor) ||
            GetSeatIndex(ent.Comp, args.Actor) is not (0 or 1))
            return;

        ent.Comp.LightsEnabled = args.Enabled;
        _vehicleLights.SetEnabled(ent.Owner, args.Enabled);
        Dirty(ent);
        UpdateUi(ent);
    }
}
