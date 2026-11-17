using Content.Client.UserInterface.ControlExtensions;
using Content.Shared._NW.Telephone;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._NW.Telephone;

public sealed class NWTelephoneBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly List<string> TabOrder = new() { "General", "Command", "Security", "Medical", "Engineering", "Offices" };

    private NWTelephoneWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NWTelephoneWindow>();
        _window.CreateNetwork.OnPressed += _ => SendMessage(new NWPhoneNetworkMessage(
            NWPhoneNetworkOperation.Create, _window.NetworkNameInput.Text, _window.NewNetworkCodeInput.Text));
        _window.JoinNetwork.OnPressed += _ => SendMessage(new NWPhoneNetworkMessage(
            NWPhoneNetworkOperation.Join, _window.NetworkCodeInput.Text));
        _window.LeaveNetwork.OnPressed += _ => SendMessage(new NWPhoneNetworkMessage(
            NWPhoneNetworkOperation.Leave, string.Empty));

        if (EntMan.TryGetComponent(Owner, out MetaDataComponent? metaData))
            _window.Title = metaData.EntityName;

        Refresh();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (_window is not { IsOpen: true })
            return;

        if (State is not NWTelephoneBuiState state)
            return;

        _window.NetworkLabel.Text = $"Network: {state.NetworkName}";
        _window.NetworkControls.Visible = state.CanConfigure;
        _window.InviteCode.Text = state.InviteCode;
        _window.InviteLabel.Visible = !string.IsNullOrEmpty(state.InviteCode);
        _window.InviteCode.Visible = !string.IsNullOrEmpty(state.InviteCode);
        _window.LeaveNetwork.Disabled = string.IsNullOrEmpty(state.InviteCode);

        _window.Tabs.DisposeAllChildren();
        if (state.Phones.Count == 0)
        {
            var empty = new BoxContainer { Orientation = LayoutOrientation.Vertical };
            empty.AddChild(new Label { Text = "No other telephones on this network." });
            _window.Tabs.AddChild(empty);
            TabContainer.SetTabTitle(empty, "Directory");
        }
        var tabs = new Dictionary<string, BoxContainer>();
        foreach (var phone in state.Phones)
        {
            if (!tabs.TryGetValue(phone.Category, out var tab))
            {
                tab = new BoxContainer { Orientation = LayoutOrientation.Vertical };
                tabs[phone.Category] = tab;

                var scroll = new ScrollContainer
                {
                    HScrollEnabled = false,
                    VScrollEnabled = true,
                    VerticalExpand = true,
                };

                var category = new BoxContainer { Orientation = LayoutOrientation.Vertical };
                scroll.AddChild(category);

                var searchBar = new LineEdit();
                tab.AddChild(searchBar);
                tab.AddChild(scroll);

                searchBar.OnTextChanged += args =>
                {
                    foreach (var scroll in _window.Tabs.GetControlOfType<ScrollContainer>())
                    {
                        foreach (var container in scroll.GetControlOfType<BoxContainer>())
                        {
                            foreach (var child in container.Children)
                            {
                                if (child is LineEdit otherBar)
                                {
                                    otherBar.SetText(args.Text, false);
                                }
                                else if (child is Button button)
                                {
                                    button.Visible = button.Text?.Contains(args.Text, StringComparison.OrdinalIgnoreCase) ?? false;
                                }
                            }
                        }
                    }
                };
            }

            foreach (var child in tab.Children)
            {
                if (child is not ScrollContainer scroll)
                    continue;

                foreach (var scrollChild in scroll.Children)
                {
                    if (scrollChild is not BoxContainer category)
                        continue;

                    var phoneButton = new Button
                    {
                        Text = phone.Name,
                        StyleClasses = { "OpenBoth" },
                    };
                    phoneButton.OnPressed += _ => SendMessage(new NWTelephoneCallBuiMsg(phone.Id));
                    category.AddChild(phoneButton);
                    break;
                }
            }
        }

        foreach (var categoryName in TabOrder)
        {
            if (tabs.Remove(categoryName, out var category))
            {
                _window.Tabs.AddChild(category);
                TabContainer.SetTabTitle(category, categoryName);
            }
        }

        foreach (var (categoryName, category) in tabs)
        {
            _window.Tabs.AddChild(category);
            TabContainer.SetTabTitle(category, categoryName);
        }

        _window.Buttons.DisposeAllChildren();
        var close = new Button { Text = "Close" };
        close.OnPressed += _ => _window.Close();
        _window.Buttons.AddChild(close);
        if (state.Dnd)
        {
            var disableDndButton = new Button
            {
                Text = Loc.GetString("nw-phone-dnd-button"),
                StyleClasses = { "OpenBoth", "Caution" },
                ToolTip = Loc.GetString("nw-phone-dnd-tooltip-enabled"),
            };
            disableDndButton.OnPressed += _ => SendMessage(new NWTelephoneDndBuiMsg(false));
            _window.Buttons.AddChild(disableDndButton);
        }
        else if (state.CanDnd)
        {
            var enableDndButton = new Button
            {
                Text = Loc.GetString("nw-phone-dnd-button"),
                StyleClasses = { "OpenBoth" },
                ToolTip = Loc.GetString("nw-phone-dnd-tooltip-disabled"),
            };
            enableDndButton.OnPressed += _ => SendMessage(new NWTelephoneDndBuiMsg(true));
            _window.Buttons.AddChild(enableDndButton);
        }
        else
        {
            var enableDndButton = new Button
            {
                Text = Loc.GetString("nw-phone-dnd-button"),
                StyleClasses = { "OpenBoth" },
                ToolTip = Loc.GetString("nw-phone-dnd-tooltip-locked"),
                Disabled = true,
            };
            _window.Buttons.AddChild(enableDndButton);
        }
    }
}
