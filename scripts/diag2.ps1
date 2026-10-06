Get-CimInstance Win32_Process |
    Where-Object { $_.Name -match 'AirServer|AirGlass|uxplay' } |
    ForEach-Object {
        "{0} PID={1} parent={2} path={3}" -f $_.Name, $_.ProcessId, $_.ParentProcessId, $_.ExecutablePath
    }
"== fin liste"
Get-NetFirewallRule -DisplayName 'AirServer*', 'AirGlass*' -ErrorAction SilentlyContinue |
    ForEach-Object { "{0} | profil={1} | actif={2}" -f $_.DisplayName, $_.Profile, $_.Enabled }
"== fin regles"