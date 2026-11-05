using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Content.Client.UserInterface.Controls;
using Content.Shared._NW.Vehicles;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._NW.Vehicles;

public sealed class NWVehicleDashboardSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    private BoxContainer? _panel;
    private PanelContainer? _fuel;
    private PanelContainer? _integrity;
    private Label? _fuelText;
    private Label? _integrityText;
    private Vector4 _last;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_players.LocalEntity is not { } user ||
            !TryComp<NWVehicleCabinViewerComponent>(user, out var viewer) ||
            _ui.ActiveScreen?.GetWidget<MainViewport>()?.Parent is not LayoutContainer viewport)
        {
            ClearPanel();
            return;
        }

        var values = new Vector4(viewer.Fuel, viewer.FuelCapacity, viewer.Integrity, viewer.MaxIntegrity);
        if (_panel == null || _panel.Disposed || _panel.Parent != viewport)
        {
            ClearPanel();
            _panel = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical,
                MouseFilter = Control.MouseFilterMode.Ignore, MinWidth = 164, SeparationOverride = 2 };
            viewport.AddChild(_panel);
            LayoutContainer.SetAnchorPreset(_panel, LayoutContainer.LayoutPreset.BottomRight);
            LayoutContainer.SetMarginTop(_panel, -172);
            LayoutContainer.SetMarginBottom(_panel, -100);
            LayoutContainer.SetMarginLeft(_panel, -260);
            LayoutContainer.SetMarginRight(_panel, -96);
            (_fuel, _fuelText) = AddGauge(_panel, Color.FromHex("#F0BD42"));
            (_integrity, _integrityText) = AddGauge(_panel, Color.FromHex("#64CA78"));
        }
        else if (_last == values)
            return;

        _last = values;
        SetGauge(_fuel!, _fuelText!, "nw-dashboard-fuel", values.X, values.Y);
        SetGauge(_integrity!, _integrityText!, "nw-dashboard-integrity", values.Z, values.W);
    }

    private static (PanelContainer, Label) AddGauge(
        BoxContainer panel, Color color)
    {
        var label = new Label { MouseFilter = Control.MouseFilterMode.Ignore };
        panel.AddChild(label);

        var track = new LayoutContainer
        {
            MinWidth = 164,
            MinHeight = 5,
            MouseFilter = Control.MouseFilterMode.Ignore
        };
        panel.AddChild(track);

        var background = MakeStrip(Color.FromHex("#24282C"));
        track.AddChild(background);
        LayoutContainer.SetAnchorPreset(background, LayoutContainer.LayoutPreset.Wide);

        var fill = MakeStrip(color);
        track.AddChild(fill);
        LayoutContainer.SetAnchorPreset(fill, LayoutContainer.LayoutPreset.TopLeft);
        LayoutContainer.SetMarginBottom(fill, 5);
        return (fill, label);
    }

    private static PanelContainer MakeStrip(Color color)
    {
        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = color },
            MouseFilter = Control.MouseFilterMode.Ignore
        };
    }

    private void SetGauge(PanelContainer bar, Label text, string key, float current, float max)
    {
        var fraction = max > 0 ? Math.Clamp(current / max, 0f, 1f) : 0f;
        bar.Visible = fraction > 0f;
        LayoutContainer.SetMarginRight(bar, MathF.Round(164f * fraction));
        text.Text = max <= 0f ? Loc.GetString("nw-dashboard-connecting") : Loc.GetString(key, ("current", (int)MathF.Ceiling(current)), ("max", (int)MathF.Ceiling(max)));
    }

    private void ClearPanel()
    {
        if (_panel is { Disposed: false })
            _panel.Dispose();
        _panel = null;
        _fuel = _integrity = null;
        _fuelText = _integrityText = null;
    }

    public override void Shutdown()
    {
        ClearPanel();
        base.Shutdown();
    }
}
