using Robust.Shared.Serialization;

namespace Content.Shared._NW.Vehicles;

[RegisterComponent]
public sealed partial class NWVehicleInspectionComponent : Component;

[Serializable, NetSerializable]
public enum NWVehicleInspectionUiKey : byte { Key }

[Serializable, NetSerializable]
public enum NWVehicleInspectionCommand : byte { Repair, InstallPlow, RemovePlow, LoadMagazine, EjectMagazine }

[Serializable, NetSerializable]
public sealed class NWVehicleInspectionMessage(NWVehicleInspectionCommand command) : BoundUserInterfaceMessage
{
    public readonly NWVehicleInspectionCommand Command = command;
}

[Serializable, NetSerializable]
public sealed class NWVehicleInspectionState : BoundUserInterfaceState
{
    public string Title = "";
    public bool Ammunition;
    public NetEntity Vehicle;
    public float Integrity;
    public float MaxIntegrity;
    public float Fuel;
    public float FuelCapacity;
    public bool Parked;
    public bool HasPlowSlot;
    public NetEntity? Plow;
    public bool Armed;
    public bool Overlights;
    public NetEntity? Magazine;
    public string MagazineName = "Empty";
    public int Ammo;
    public int Capacity;
    public bool ChamberLoaded;
    public bool BoltClosed;
}
