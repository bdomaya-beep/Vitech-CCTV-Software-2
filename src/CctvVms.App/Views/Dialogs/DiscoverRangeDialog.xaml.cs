using System.Windows;

namespace CctvVms.App.Views.Dialogs;

public partial class DiscoverRangeDialog : Window
{
    public DiscoverRangeDialog()
    {
        InitializeComponent();
    }

    public string SubnetOrRange { get; private set; } = "192.168.1.1,192.168.100.1";

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Scan_OnClick(object sender, RoutedEventArgs e)
    {
        var value = RangeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(this, "Enter a subnet or range.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SubnetOrRange = value;
        DialogResult = true;
        Close();
    }
}
