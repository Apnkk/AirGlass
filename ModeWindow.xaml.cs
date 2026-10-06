using System.Windows;

namespace AirGlass;

/// <summary>Startup chooser: Apple (AirPlay / UxPlay) or Android (scrcpy).</summary>
public partial class ModeWindow : Window
{
    public const string Apple = "apple";
    public const string Android = "android";

    /// <summary>"apple", "android", or null when the window was closed without a choice.</summary>
    public string? Choice { get; private set; }

    public bool Remember => RememberBox.IsChecked == true;

    public ModeWindow()
    {
        InitializeComponent();
    }

    private void OnAppleClick(object sender, RoutedEventArgs e) => Pick(Apple);

    private void OnAndroidClick(object sender, RoutedEventArgs e) => Pick(Android);

    private void OnTitleClose(object sender, RoutedEventArgs e) => Close();

    private void Pick(string choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}