using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._NW.Vehicles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._NW.Vehicles;

public sealed class NWVehicleInspectionWindow : FancyWindow
{
    [Dependency] private readonly IEntityManager _entities = default!;
    public event Action<NWVehicleInspectionCommand>? Command;
    private readonly BoxContainer _body;
    private string? _renderedState;
    private static readonly Color Green = Color.FromHex("#4BCD86");
    private static readonly Color Yellow = Color.FromHex("#E0BC58");

    public NWVehicleInspectionWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = "Vehicle inspection";
        MinSize = new Vector2(480, 300);
        SetSize = new Vector2(650, 600);
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10, Margin = new Thickness(12) };
        scroll.AddChild(_body);
        ContentsContainer.AddChild(scroll);
    }

    public void SetState(NWVehicleInspectionState s)
    {
        // Preserve button focus and scroll position when a polling update has no changes.
        var key = $"{s.Title}|{s.Ammunition}|{s.Vehicle}|{s.Integrity}|{s.MaxIntegrity}|{s.Fuel}|{s.FuelCapacity}|{s.Parked}|{s.HasPlowSlot}|{s.Plow}|{s.Armed}|{s.Overlights}|{s.Magazine}|{s.MagazineName}|{s.Ammo}|{s.Capacity}|{s.ChamberLoaded}|{s.BoltClosed}";
        if (_renderedState == key) return;
        _renderedState = key;
        Title = s.Ammunition ? $"{s.Title} ammo loader" : s.Title;
        _body.RemoveAllChildren();
        if (s.Ammunition) Ammo(s); else Maintenance(s);
    }

    private BoxContainer Card(string heading, NetEntity? icon = null)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#0D1C2B") },
            HorizontalExpand = true,
        };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(12), SeparationOverride = 8, HorizontalExpand = true };
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12 };
        if (icon is { } entity)
            row.AddChild(new SpriteView(entity, _entities) { SetSize = new Vector2(80, 80),
                OverrideDirection = Direction.South });
        row.AddChild(new Label { Text = heading, VerticalAlignment = VAlignment.Center });
        column.AddChild(row);
        panel.AddChild(column);
        _body.AddChild(panel);
        return column;
    }

    private static void Meter(BoxContainer column, string name, float value, float maximum, Color color)
    {
        column.AddChild(new Label { Text = $"{name}    {value:0}/{maximum:0}" });
        column.AddChild(new ProgressBar
        {
            MinValue = 0, MaxValue = MathF.Max(1, maximum), Value = Math.Clamp(value, 0, MathF.Max(1, maximum)),
            SetHeight = 18, HorizontalExpand = true,
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = color },
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#233246") },
        });
    }

    private void ActionButton(BoxContainer row, string text, NWVehicleInspectionCommand command, bool enabled)
    {
        var button = new Button { Text = text, Disabled = !enabled, HorizontalExpand = true };
        button.OnPressed += _ => Command?.Invoke(command);
        row.AddChild(button);
    }

    private void Maintenance(NWVehicleInspectionState s)
    {
        var frame = Card("Vehicle Hardpoints", s.Vehicle);
        Meter(frame, "Frame Integrity", s.Integrity, s.MaxIntegrity, Green);
        Meter(frame, "Fuel", s.Fuel, s.FuelCapacity, Yellow);
        ActionButton(frame, "Repair frame (welder)", NWVehicleInspectionCommand.Repair,
            s.Parked && s.Integrity < s.MaxIntegrity);
        frame.AddChild(new Label { Text = s.Parked ? "Engine off — maintenance available" : "Stop the engine before maintenance" });

        if (s.Armed)
        {
            var gun = Card("Mounted weapon / turret", s.Vehicle);
            gun.AddChild(new Label { Text = "Fixed assembly — uses frame integrity" });
            gun.AddChild(new Label { Text = "Service ammunition at the interior feed" });
        }
        if (s.HasPlowSlot)
        {
            var front = Card(s.Plow == null ? "Empty — front attachment" : "Snowplow — front attachment", s.Plow);
            var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
            ActionButton(buttons, "Install held plow", NWVehicleInspectionCommand.InstallPlow, s.Parked && s.Plow == null);
            ActionButton(buttons, "Remove", NWVehicleInspectionCommand.RemovePlow, s.Parked && s.Plow != null);
            front.AddChild(buttons);
        }
        if (s.Overlights)
            Card("Overlights").AddChild(new Label { Text = "Built-in equipment — controlled from the driver's actions" });
    }

    private void Ammo(NWVehicleInspectionState s)
    {
        Card("Mounted weapon ammunition", s.Vehicle);
        var magazine = Card(s.MagazineName, s.Magazine);
        Meter(magazine, "Magazine", s.Ammo, s.Capacity, Green);
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        ActionButton(buttons, "Load held magazine", NWVehicleInspectionCommand.LoadMagazine, s.Magazine == null);
        ActionButton(buttons, "Eject magazine", NWVehicleInspectionCommand.EjectMagazine, s.Magazine != null);
        magazine.AddChild(buttons);
        var chamber = Card("Chamber / bolt");
        chamber.AddChild(new Label { Text = s.ChamberLoaded ? "Chamber: loaded" : "Chamber: empty / spent" });
        chamber.AddChild(new Label { Text = s.BoltClosed ? "Bolt: closed" : "Bolt: open" });
        chamber.AddChild(new Label { Text = "Use Ready mounted gun from the gunner's seat" });
    }
}
