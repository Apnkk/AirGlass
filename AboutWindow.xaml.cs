using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace AirGlass;

/// <summary>
/// Écran « À propos » : crédite UxPlay et donne accès aux licences tierces.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "Version inconnue" : $"Version {version.ToString(3)}";
    }

    private void OnLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenWithShell(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OnOpenNotices(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "Fichier de licences introuvable :\n" + path,
                "AirGlass", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        OpenWithShell(path);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OpenWithShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ouverture impossible : " + ex.Message,
                "AirGlass", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
