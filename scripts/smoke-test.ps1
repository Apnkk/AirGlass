$ErrorActionPreference = "Continue"
Set-Location (Split-Path -Parent $PSScriptRoot)

$exe = "bin\Debug\net10.0-windows\AirGlass.exe"
Get-Process AirGlass, uxplay -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1

$app = Start-Process -FilePath $exe -PassThru
Start-Sleep 8

"== 1. Demarrage"
"app vivante : " + (-not $app.HasExited)
$ux = @(Get-Process uxplay -ErrorAction SilentlyContinue)
"uxplay PID  : " + (($ux | ForEach-Object { $_.Id }) -join ",")

"== 2. Ports"
netstat -ano | Select-String -Pattern ':(7000|7001|7002|5353)\s' | ForEach-Object { $_.Line.Trim() }

"== 3. Regles pare-feu"
Get-NetFirewallRule -DisplayName 'AirGlass*' -ErrorAction SilentlyContinue |
    ForEach-Object { "{0} | profil={1} | actif={2}" -f $_.DisplayName, $_.Profile, $_.Enabled }

"== 4. Job Object (kill brutal de l'app)"
Stop-Process -Id $app.Id -Force
Start-Sleep 3
$left = @(Get-Process uxplay -ErrorAction SilentlyContinue)
"uxplay restants : " + $left.Count
$left | Stop-Process -Force -ErrorAction SilentlyContinue