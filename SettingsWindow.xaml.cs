using System.Diagnostics;
using System.IO;
using System.Windows;
using AirGlass.Services;

namespace AirGlass;

/// <summary>Settings dialog. Edits the given <see cref="AppSettings"/> in place when saved.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<AppSettings.BlockedDevice> _blocked;
    private readonly string? _lastDeviceId;
    private readonly string? _lastDeviceName;

    public SettingsWindow(AppSettings settings,
        string? lastDeviceId = null, string? lastDeviceName = null)
    {
        InitializeComponent();
        _settings = settings;
        _lastDeviceId = lastDeviceId;
        _lastDeviceName = lastDeviceName;
        _blocked = settings.BlockedDevices
            .Select(d => new AppSettings.BlockedDevice { Id = d.Id, Name = d.Name })
            .ToList();

        NameBox.Text = settings.ReceiverName;
        NameHint.Text = $"Laisse vide pour utiliser le nom du PC ({AirPlayInfo.ReceiverName}). " +
                        "Le récepteur redémarre si tu changes le nom.";
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();
        StartMinimizedBox.IsChecked = settings.StartMinimized;
        MinimizeToTrayBox.IsChecked = settings.MinimizeToTray;
        KeepOnTopBox.IsChecked = settings.KeepOnTopWhileMirroring;
        NoHoldBox.IsChecked = settings.NewClientReplacesCurrent;
        Fps60Box.IsChecked = settings.Fps60;
        foreach (var option in QualityOptions())
            option.IsChecked = option.Tag as string == settings.QualityHeight.ToString();
        LowLatencyBox.IsChecked = settings.LowLatency;
        AudioOffBox.IsChecked = settings.AudioOff;
        UsePinBox.IsChecked = settings.UsePin;
        PinBox.Text = settings.Pin;
        RefreshBlocked();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var nameError = AppSettings.ValidateName(NameBox.Text);
        if (nameError is not null)
        {
            ShowError(nameError);
            return;
        }

        var usePin = UsePinBox.IsChecked == true;
        var pinError = AppSettings.ValidatePin(usePin, PinBox.Text);
        if (pinError is not null)
        {
            ShowError(pinError);
            return;
        }

        var wantStartup = StartWithWindowsBox.IsChecked == true;
        try
        {
            StartupRegistration.Set(wantStartup);
        }
        catch (Exception ex)
        {
            AppLog.Error("Démarrage avec Windows impossible", ex);
            ShowError("Impossible de modifier le démarrage avec Windows : " + ex.Message);
            return;
        }

        _settings.ReceiverName = NameBox.Text.Trim();
        _settings.StartWithWindows = wantStartup;
        _settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayBox.IsChecked == true;
        _settings.KeepOnTopWhileMirroring = KeepOnTopBox.IsChecked == true;
        _settings.NewClientReplacesCurrent = NoHoldBox.IsChecked == true;
        _settings.Fps60 = Fps60Box.IsChecked == true;
        var chosen = QualityOptions().FirstOrDefault(o => o.IsChecked == true);
        _settings.QualityHeight = chosen?.Tag is string tag && int.TryParse(tag, out var height) ? height : 1080;
        _settings.LowLatency = LowLatencyBox.IsChecked == true;
        _settings.AudioOff = AudioOffBox.IsChecked == true;
        _settings.UsePin = usePin;
        _settings.Pin = PinBox.Text.Trim();
        _settings.BlockedDevices = _blocked;

        if (!_settings.Save())
        {
            ShowError("Impossible d'enregistrer les réglages (voir les journaux).");
            return;
        }

        DialogResult = true;
    }

    private IEnumerable<System.Windows.Controls.RadioButton> QualityOptions() =>
        QualityPanel.Children.OfType<System.Windows.Controls.RadioButton>();

    private void OnTitleClose(object sender, RoutedEventArgs e) => Close();

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.DataDir);
            Process.Start(new ProcessStartInfo(AppLog.DataDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("Ouverture impossible : " + ex.Message);
        }
    }

    private void RefreshBlocked()
    {
        BlockedList.Items.Clear();
        foreach (var d in _blocked)
            BlockedList.Items.Add(string.IsNullOrWhiteSpace(d.Name) ? d.Id : $"{d.Name} ({d.Id})");

        var canBlock = !string.IsNullOrEmpty(_lastDeviceId) &&
                       !_blocked.Any(d => string.Equals(d.Id, _lastDeviceId, StringComparison.OrdinalIgnoreCase));
        BlockLastButton.IsEnabled = canBlock;
        BlockLastButton.Content = string.IsNullOrEmpty(_lastDeviceName)
            ? "Refuser le dernier appareil"
            : $"Refuser « {_lastDeviceName} »";
        UnblockButton.IsEnabled = _blocked.Count > 0;
    }

    private void OnBlockLast(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastDeviceId)) return;
        _blocked.Add(new AppSettings.BlockedDevice { Id = _lastDeviceId, Name = _lastDeviceName ?? "" });
        RefreshBlocked();
    }

    private void OnUnblock(object sender, RoutedEventArgs e)
    {
        var index = BlockedList.SelectedIndex;
        if (index < 0 || index >= _blocked.Count) return;
        _blocked.RemoveAt(index);
        RefreshBlocked();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
