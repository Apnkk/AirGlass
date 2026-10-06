$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '..\MainWindow.xaml.cs'
$path = (Resolve-Path $path).Path

$bytes = [System.IO.File]::ReadAllBytes($path)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$enc = New-Object System.Text.UTF8Encoding($hasBom)

$lines = [System.Collections.Generic.List[string]]::new([System.IO.File]::ReadAllLines($path, $enc))

# Guards (1-based line numbers)
if ($lines[462].Trim() -ne 'System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;') { throw "Garde 1: ligne 463 inattendue : $($lines[462])" }
if ($lines[463].Trim() -ne '_launcher.NoHold = _settings.NewClientReplacesCurrent;') { throw "Garde 2: ligne 464 inattendue : $($lines[463])" }
if ($lines[476].Trim() -ne 'private void OnNetworkAddressChanged(object? sender, EventArgs e) => OnUi(RestartNetworkTimer);') { throw "Garde 3: ligne 477 inattendue : $($lines[476])" }

$replacement = @(
  '        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;',
  '    }',
  '',
  '    private void OnNetworkAddressChanged(object? sender, EventArgs e) => OnUi(RestartNetworkTimer);'
)

# Remplace les lignes 464..477 (index 463..476, soit 14 lignes)
$lines.RemoveRange(463, 14)
$lines.InsertRange(463, [string[]]$replacement)

[System.IO.File]::WriteAllLines($path, $lines, $enc)
Write-Host "OK : MainWindow.xaml.cs réparé (BOM=$hasBom, $($lines.Count) lignes)"