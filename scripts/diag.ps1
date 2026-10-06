Set-Location (Split-Path -Parent $PSScriptRoot)
Get-Process uxplay, AirGlass -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1

$app = Start-Process -FilePath 'bin\Debug\net10.0-windows\AirGlass.exe' -PassThru
Start-Sleep 8
"app PID: " + $app.Id
Get-CimInstance Win32_Process -Filter "Name='AirGlass.exe'" | ForEach-Object {
    "AirGlass PID={0} parent={1}" -f $_.ProcessId, $_.ParentProcessId
}
Get-CimInstance Win32_Process -Filter "Name='uxplay.exe'" | ForEach-Object {
    "uxplay PID={0} parent={1} start={2} cmd={3}" -f $_.ProcessId, $_.ParentProcessId, $_.CreationDate, $_.CommandLine
}

Stop-Process -Id $app.Id -Force
Start-Sleep 3
$left = @(Get-Process uxplay -ErrorAction SilentlyContinue)
"restants: " + $left.Count
$left | Stop-Process -Force -ErrorAction SilentlyContinue

$roots = @((Join-Path $env:APPDATA 'AirGlass'), (Join-Path $env:LOCALAPPDATA 'AirGlass'))
foreach ($d in $roots) {
    if (Test-Path $d) {
        "== dossier: " + $d
        Get-ChildItem $d -Recurse -File | ForEach-Object { $_.FullName }
        Get-ChildItem $d -Recurse -Filter *.log | ForEach-Object {
            "--- " + $_.Name
            Get-Content $_.FullName -Tail 50
        }
    } else {
        "absent: " + $d
    }
}