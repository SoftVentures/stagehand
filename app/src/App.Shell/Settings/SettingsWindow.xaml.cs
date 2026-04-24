using System.Windows;
using App.Core.Branding;

namespace App.Shell.Settings;

/// <summary>
/// Placeholder settings window. Plan 04 will replace its body with the real
/// preferences UI; until then it exists so the tray "Settings…" command has a
/// window to open.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>Initialises the component.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        Title = BrandConstants.SettingsWindowTitle;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
