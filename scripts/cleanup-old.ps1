Get-Process AirServer, uxplay -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
"processus restants : " + @(Get-Process AirServer, uxplay -ErrorAction SilentlyContinue).Count

$names = @(
    'AirServer (mDNS 5353 UDP)',
    'AirServer (AirPlay 7000 TCP)',
    'AirServer (AirPlay 7000-7002 TCP)',
    'AirServer (AirPlay 7000-7002 UDP)',
    'AirServer (RAOP 5000 TCP)'
)
foreach ($n in $names) {
    $found = @(Get-NetFirewallRule -DisplayName $n -ErrorAction SilentlyContinue)
    "{0} : {1} regle(s)" -f $n, $found.Count
}