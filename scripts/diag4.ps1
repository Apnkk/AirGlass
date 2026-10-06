"== processus"
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -match 'AirServer|AirGlass|uxplay' } |
    ForEach-Object { "{0} PID={1} parent={2}" -f $_.Name, $_.ProcessId, $_.ParentProcessId }

"== profil Wi-Fi"
Get-NetConnectionProfile -InterfaceAlias 'Wi-Fi' |
    ForEach-Object { "{0} | categorie={1}" -f $_.Name, $_.NetworkCategory }

"== admin ?"
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)