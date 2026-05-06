using System.Windows;
using CctvVms.App.Models;

namespace CctvVms.App.Views.Dialogs;

public partial class AddNvrDialog : Window
{
    public AddNvrDialog()
    {
        InitializeComponent();
    }

    public DeviceConnectionInput? Input { get; private set; }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        var ip = IpBox.Text.Trim();
        var username = UserBox.Text.Trim();
        var password = PasswordBox.Password;
        var devicePortText = DevicePortBox.Text.Trim();
        var channelCountText = ChannelCountBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show(this, "IP and Username are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(devicePortText, out var devicePort) || devicePort <= 0 || devicePort > 65535)
        {
            MessageBox.Show(this, "Enter a valid device port.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(channelCountText, out var maxChannels) || maxChannels <= 0 || maxChannels > 128)
        {
            MessageBox.Show(this, "Enter a valid max channel count.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Input = new DeviceConnectionInput
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"NVR ({ip})" : NameBox.Text.Trim(),
            IpAddress = ip,
            AddMode = (AddModeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "IP/Domain Name",
            DevicePort = devicePort,
            Username = username,
            Password = string.IsNullOrWhiteSpace(password) ? "admin123" : password,
            NvrType = "Dahua",
            MaxChannels = maxChannels
        };

        DialogResult = true;
        Close();
    }
}
