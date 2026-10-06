using System.Diagnostics;
using System.Windows;
using AirGlass.Services;

namespace AirGlass;

/// <summary>Dialog to enter and validate a license key. DialogResult = true when activated.</summary>
public partial class ActivationWindow : Window
{
    public ActivationWindow()
    {
        InitializeComponent();
        BuyButton.Visibility = string.IsNullOrWhiteSpace(LicenseService.PurchaseUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
        Loaded += (_, _) => KeyBox.Focus();
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        if (!LicenseService.TryActivate(KeyBox.Text, out var error, out var email))
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        MessageBox.Show(this, $"Licence activée pour {email}. Merci !",
            "AirGlass", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void OnTitleClose(object sender, RoutedEventArgs e) => Close();

    private void OnBuy(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(LicenseService.PurchaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ouverture impossible : " + ex.Message,
                "AirGlass", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
