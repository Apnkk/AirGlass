"== processus"
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -match 'AirServer|AirGlass|uxplay' } |
    ForEach-Object { "{0} PID={1} parent={2}" -f $_.Name, $_.ProcessId, $_.ParentProcessId }

"== profil reseau"
Get-NetConnectionProfile | ForEach-Object {
    "{0} | categorie={1} | interface={2}" -f $_.Name, $_.NetworkCategory, $_.InterfaceAlias
}

"== regles AirGlass"
Get-NetFirewallRule -DisplayName 'AirGlass*' -ErrorAction SilentlyContinue |
    ForEach-Object { "{0} | profil={1} | actif={2}" -f $_.DisplayName, $_.Profile, $_.Enabled }

"== log (60 dernieres lignes)"
$log = Join-Path $env:APPDATA 'AirGlass\airglass.log'
if (Test-Path $log) { Get-Content $log -Tail 60 } else { "log absent" }