$ErrorActionPreference = 'Stop'
$path = (Resolve-Path (Join-Path $PSScriptRoot '..\SettingsWindow.xaml')).Path

$bytes = [System.IO.File]::ReadAllBytes($path)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$enc = New-Object System.Text.UTF8Encoding($hasBom)

$lines = [System.Collections.Generic.List[string]]::new([System.IO.File]::ReadAllLines($path, $enc))

# Guards (1-based line numbers)
if ($lines[156].Trim() -ne '>>>>>>> REPLACE') { throw "Garde 1: ligne 157 inattendue : $($lines[156])" }
if ($lines[157].Trim() -ne '<<<<<<< SEARCH') { throw "Garde 2: ligne 158 inattendue : $($lines[157])" }
if ($lines[161].Trim() -ne '=======') { throw "Garde 3: ligne 162 inattendue : $($lines[161])" }
if ($lines[175] -notmatch 'x:Name="SecurityLockNote"') { throw "Garde 4: ligne 176 inattendue : $($lines[175])" }
if ($lines[177] -notmatch 'Margin="0,0,0,12" />') { throw "Garde 5: ligne 178 inattendue : $($lines[177])" }

# Later block first so earlier indexes stay valid
$lines.RemoveRange(175, 3)   # lines 176-178 : SecurityLockNote
$lines.RemoveRange(156, 6)   # lines 157-162 : stray markers + duplicated note

[System.IO.File]::WriteAllLines($path, $lines, $enc)
Write-Host "OK : SettingsWindow.xaml repare (BOM=$hasBom, $($lines.Count) lignes)"