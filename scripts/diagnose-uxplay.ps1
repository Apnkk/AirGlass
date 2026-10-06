#Requires -Version 5.1
<#
.SYNOPSIS
    Diagnostic de la viabilité du paquet UxPlay natif Windows (external/uxplay-win).

.DESCRIPTION
    Ce script reproduit À L'IDENTIQUE l'environnement mis en place par
    Services/UxPlayLauncher.cs (WorkingDirectory, GST_PLUGIN_PATH,
    GST_PLUGIN_SYSTEM_PATH, GST_REGISTRY, PATH préfixé par le dossier du paquet),
    puis valide les trois points encore incertains du portage :

      1. Les 21 plugins GStreamer embarqués se chargent-ils réellement ?
         -> on force la (re)construction du registre GStreamer et on inspecte
            la liste des plugins vus, en cherchant ceux qui sont indispensables
            au pipeline AirPlay (openh264/libav pour H.264, faad/alaw pour
            l'audio, wasapi pour la sortie son, playback/coreelements/app...).

      2. L'annonce mDNS interne d'UxPlay fonctionne-t-elle sans WSL/avahi ?
         -> on lance uxplay.exe en mode verbeux (-vv) pendant quelques secondes
            et on scrute sa sortie pour les traces d'enregistrement mDNS/Bonjour
            et d'écoute réseau, puis on vérifie les ports 7000/7001/7002.

      3. Les ports 7000/7001/7002 sont-ils ouverts/écoutés et autorisés par le
         pare-feu Windows ?
         -> on relève les ports en écoute et les règles de pare-feu existantes.

    Le script est NON destructif : il ne modifie aucun fichier du projet
    (le registre GStreamer de test est écrit dans un dossier temporaire isolé),
    ne touche pas au pare-feu, et nettoie ses fichiers temporaires à la fin.

.PARAMETER DurationSeconds
    Durée (secondes) pendant laquelle uxplay.exe tourne pour le test runtime.
    Défaut : 8. Au-delà de ~5 s, l'annonce mDNS et l'ouverture des ports ont eu
    le temps de se produire.

.PARAMETER ReceiverName
    Nom du récepteur passé à uxplay (-n). Défaut : "AirGlass-Diag".

.PARAMETER KeepLogs
    Conserve le dossier de logs temporaire au lieu de le supprimer en fin de run
    (utile pour joindre les logs à un rapport de bug).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\diagnose-uxplay.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\diagnose-uxplay.ps1 -DurationSeconds 15 -KeepLogs
#>

[CmdletBinding()]
param(
    [ValidateRange(3, 120)]
    [int]$DurationSeconds = 8,

    [string]$ReceiverName = "AirGlass-Diag",

    [switch]$KeepLogs
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Mise en forme console -------------------------------------------------
function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host ("=" * 72) -ForegroundColor DarkCyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ("=" * 72) -ForegroundColor DarkCyan
}
function Write-Ok   ([string]$Msg) { Write-Host "  [OK]   $Msg" -ForegroundColor Green }
function Write-Warn2([string]$Msg) { Write-Host "  [WARN] $Msg" -ForegroundColor Yellow }
function Write-Fail ([string]$Msg) { Write-Host "  [FAIL] $Msg" -ForegroundColor Red }
function Write-Info ([string]$Msg) { Write-Host "  [..]   $Msg" -ForegroundColor Gray }

# Compteur global de problèmes bloquants pour le code de sortie final.
$script:FailCount = 0
function Note-Fail { $script:FailCount++ }

# --- 1. Résolution du paquet (même logique que UxPlayLauncher.ResolvePackageDir) ---
Write-Section "1. Localisation du paquet UxPlay natif"

# Racine du dépôt = parent du dossier 'scripts' qui contient ce fichier.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent $scriptDir

$packageDir = Join-Path $repoRoot "external\uxplay-win"
$exePath    = Join-Path $packageDir "uxplay.exe"
$pluginDir  = Join-Path $packageDir "lib\gstreamer-1.0"

if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Fail "uxplay.exe introuvable : $exePath"
    Write-Info "Vérifiez que external/uxplay-win est bien présent dans le dépôt."
    exit 2
}
Write-Ok "uxplay.exe : $exePath"

if (-not (Test-Path -LiteralPath $pluginDir)) {
    Write-Fail "Dossier des plugins introuvable : $pluginDir"
    exit 2
}
$pluginFiles = @(Get-ChildItem -LiteralPath $pluginDir -Filter "*.dll" -File)
Write-Ok "Dossier plugins : $pluginDir ($($pluginFiles.Count) DLL)"

# Dossier de logs temporaire isolé (jamais dans le profil ni dans le paquet).
$logDir = Join-Path ([System.IO.Path]::GetTempPath()) ("uxplay-diag-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$testRegistry = Join-Path $logDir "gstreg-diag.bin"

# --- 2. Construction de l'environnement (copie exacte du launcher) ----------
Write-Section "2. Environnement GStreamer (identique au launcher C#)"

$childEnv = @{
    'GST_PLUGIN_PATH'        = $pluginDir
    'GST_PLUGIN_SYSTEM_PATH' = $pluginDir
    'GST_REGISTRY'           = $testRegistry
    # Le launcher préfixe le PATH par le dossier du paquet pour la résolution
    # des DLL par les plugins chargés dynamiquement.
    'PATH'                   = $packageDir + [System.IO.Path]::PathSeparator + $env:PATH
    # Niveau de debug GStreamer modéré : montre le scan/chargement des plugins
    # sans noyer la sortie (3 = FIXME/ERROR/WARNING/INFO).
    'GST_DEBUG'              = '3'
}
foreach ($k in ($childEnv.Keys | Sort-Object)) {
    if ($k -eq 'PATH') {
        Write-Info "PATH    = <dossier paquet>;<PATH système>"
    } else {
        Write-Info "$k = $($childEnv[$k])"
    }
}

# Applique un hashtable d'env au process courant et renvoie les anciennes
# valeurs pour pouvoir les restaurer ensuite.
function Set-ChildEnv([hashtable]$Env) {
    $old = @{}
    foreach ($k in $Env.Keys) {
        $old[$k] = [System.Environment]::GetEnvironmentVariable($k, 'Process')
        [System.Environment]::SetEnvironmentVariable($k, $Env[$k], 'Process')
    }
    return $old
}
function Restore-ChildEnv([hashtable]$Old) {
    foreach ($k in $Old.Keys) {
        [System.Environment]::SetEnvironmentVariable($k, $Old[$k], 'Process')
    }
}

# --- 3. Chargement réel des plugins -----------------------------------------
Write-Section "3. Inspection des plugins GStreamer réellement chargés"

# Plugins indispensables au pipeline AirPlay (clé = nom de DLL embarquée,
# valeur = rôle). Un plugin manquant OU présent mais non chargé est bloquant.
$requiredPlugins = [ordered]@{
    'libgstcoreelements.dll'      = 'éléments de base (queue, tee, fakesink...)'
    'libgstapp.dll'               = 'appsrc/appsink (injection des flux par UxPlay)'
    'libgstplayback.dll'          = 'decodebin / playbin'
    'libgsttypefindfunctions.dll' = 'détection de type de flux'
    'libgstvideoparsersbad.dll'   = 'parsing H.264 (h264parse)'
    'libgstopenh264.dll'          = 'décodage H.264 (openh264)'
    'libgstlibav.dll'             = 'décodage H.264 de secours (avdec_h264)'
    'libgstaudioparsers.dll'      = 'parsing audio (aacparse)'
    'libgstfaad.dll'              = 'décodage AAC/ALAC (faad)'
    'libgstalaw.dll'              = 'décodage A-law'
    'libgstvolume.dll'            = 'élément volume (contrôle de gain du pipeline audio)'
    'libgstlevel.dll'             = 'élément level (mesure de niveau du pipeline audio)'
    'libgstaudioconvert.dll'      = 'conversion de format audio'
    'libgstaudioresample.dll'     = 'rééchantillonnage audio'
    'libgstwasapi.dll'            = 'sortie audio Windows (WASAPI)'
    'libgstvideoconvertscale.dll' = 'conversion/scale vidéo'
    'libgstautodetect.dll'        = 'autovideosink / autoaudiosink'
}

$missingFiles = @()
foreach ($dll in $requiredPlugins.Keys) {
    if (-not (Test-Path -LiteralPath (Join-Path $pluginDir $dll))) {
        $missingFiles += $dll
    }
}
if ($missingFiles.Count -gt 0) {
    foreach ($m in $missingFiles) {
        Write-Fail "Plugin requis absent du paquet : $m ($($requiredPlugins[$m]))"
        Note-Fail
    }
} else {
    Write-Ok "Les $($requiredPlugins.Count) plugins requis sont présents comme fichiers dans le paquet."
}

# Force la (re)construction du registre et liste les plugins vus par GStreamer.
# gst-inspect-1.0.exe n'est PAS garanti dans le paquet (UxPlay n'en a pas
# besoin) ; on tente de le trouver, sinon on retombe sur l'analyse de la sortie
# verbeuse d'uxplay (étape 4).
$gstInspect = Join-Path $packageDir "gst-inspect-1.0.exe"
$inspectAvailable = Test-Path -LiteralPath $gstInspect

$old = Set-ChildEnv $childEnv
try {
    if ($inspectAvailable) {
        Write-Info "gst-inspect-1.0.exe trouvé : reconstruction du registre et liste des plugins."
        # Supprime le registre de test pour forcer un scan complet et propre.
        if (Test-Path -LiteralPath $testRegistry) { Remove-Item -LiteralPath $testRegistry -Force }
        $inspectOut = & $gstInspect 2>&1 | Out-String
        $inspectLog = Join-Path $logDir "gst-inspect.log"
        $inspectOut | Set-Content -LiteralPath $inspectLog -Encoding UTF8

        # gst-inspect liste "nom_du_plugin:  élément" ; on vérifie plutôt les
        # noms d'éléments clés qui doivent apparaître.
        $requiredElements = @(
            'h264parse', 'avdec_h264', 'openh264dec', 'faad', 'aacparse',
            'audioconvert', 'audioresample', 'wasapisink', 'appsrc',
            'decodebin', 'queue', 'videoconvert'
        )
        $missingElems = @()
        foreach ($el in $requiredElements) {
            # Un élément apparaît en début de ligne "plugin:  element: desc".
            if ($inspectOut -notmatch "(?m):\s+$([regex]::Escape($el))\b") {
                $missingElems += $el
            }
        }
        if ($missingElems.Count -eq 0) {
            Write-Ok "Tous les éléments GStreamer clés sont exposés par le registre."
        } else {
            foreach ($el in $missingElems) {
                Write-Fail "Élément GStreamer introuvable dans le registre : $el"
                Note-Fail
            }
            Write-Info "Détail complet : $inspectLog"
        }

        # Signale les plugins qui ont ÉCHOUÉ au chargement (lignes 'blacklisted'
        # ou erreurs de chargement dans la sortie debug).
        $blacklisted = @(Select-String -InputObject $inspectOut -Pattern 'blacklist' -AllMatches)
        if ($blacklisted.Matches.Count -gt 0) {
            Write-Warn2 "Des plugins ont été blacklistés (voir $inspectLog)."
        }
    } else {
        Write-Warn2 "gst-inspect-1.0.exe absent du paquet : le chargement des plugins sera vérifié"
        Write-Info  "via la sortie verbeuse d'uxplay.exe à l'étape 4 (GST_DEBUG=3)."
    }
}
finally {
    Restore-ChildEnv $old
}

# --- 4. Test runtime d'uxplay.exe (mDNS + ports + pipeline) -----------------
Write-Section "4. Exécution d'uxplay.exe en mode debug ($DurationSeconds s)"

$stdoutLog = Join-Path $logDir "uxplay-stdout.log"
$stderrLog = Join-Path $logDir "uxplay-stderr.log"

# -d = debug logging (UxPlay ; -vv n'existe pas). -n = nom. -p 7000 = ports
# fixes 7000/7001/7002 (exactement comme le launcher). On passe l'environnement
# via le process courant puis Start-Process hérite de cet environnement.
$old = Set-ChildEnv $childEnv
$proc = $null
try {
    Write-Info "Lancement : uxplay.exe -n $ReceiverName -p 7000 -d"
    $proc = Start-Process -FilePath $exePath `
        -ArgumentList @('-n', $ReceiverName, '-p', '7000', '-d') `
        -WorkingDirectory $packageDir `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError  $stderrLog `
        -WindowStyle Hidden `
        -PassThru

    # Laisse le temps à l'init GStreamer, à l'annonce mDNS et à l'ouverture
    # des ports de se produire.
    $elapsed = 0
    while ($elapsed -lt $DurationSeconds -and -not $proc.HasExited) {
        Start-Sleep -Milliseconds 500
        $elapsed += 0.5
    }

    if ($proc.HasExited) {
        Write-Fail "uxplay.exe s'est arrêté prématurément (code $($proc.ExitCode)) après $elapsed s."
        Write-Info "Cela indique un échec d'initialisation (DLL/plugin manquant). Voir logs ci-dessous."
        Note-Fail
    } else {
        Write-Ok "uxplay.exe tourne toujours après $DurationSeconds s (init réussie)."
    }

    # --- 4b. Analyse de la sortie -------------------------------------------
    $out = ''
    if (Test-Path -LiteralPath $stdoutLog) { $out += (Get-Content -LiteralPath $stdoutLog -Raw -ErrorAction SilentlyContinue) }
    if (Test-Path -LiteralPath $stderrLog) { $out += "`n" + (Get-Content -LiteralPath $stderrLog -Raw -ErrorAction SilentlyContinue) }

    Write-Host ""
    Write-Host "  --- Dernières lignes de la sortie uxplay ---" -ForegroundColor DarkGray
    $out -split "`r?`n" | Where-Object { $_ -ne '' } | Select-Object -Last 20 |
        ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

    # Erreurs de chargement de plugin / DLL (le symptôme n°1 d'un paquet incomplet).
    $loadErrors = @($out -split "`r?`n" | Where-Object {
        $_ -match 'no element|not-linked|could not (load|link|create)|missing (plugin|element)|cannot load|failed to load|No such file|\.dll' -and
        $_ -match 'error|fail|missing|no element|not-linked' -and
        $_ -notmatch 'autodetect'   # bruit bénin fréquent
    })
    if ($loadErrors.Count -gt 0) {
        Write-Warn2 "Traces d'erreurs de chargement/lien GStreamer détectées :"
        $loadErrors | Select-Object -First 8 | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
        Write-Info "Un 'no element `"xxx`"' signifie qu'un plugin manque au paquet."
    } else {
        Write-Ok "Aucune erreur évidente de chargement de plugin GStreamer."
    }

    # Annonce mDNS / Bonjour.
    if ($out -match 'mDNS|Bonjour|DNS-SD|_airplay|_raop|register.*service|dns_sd') {
        Write-Ok "Trace d'annonce mDNS/Bonjour détectée dans la sortie uxplay."
    } else {
        Write-Warn2 "Aucune trace explicite d'annonce mDNS dans la sortie."
        Write-Info  "Cela n'est pas forcément bloquant (UxPlay peut rester silencieux),"
        Write-Info  "la validation finale reste la visibilité réelle depuis un iPhone."
    }
}
finally {
    # Arrêt best-effort du process et de son arbre.
    if ($proc -and -not $proc.HasExited) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            # Tue aussi d'éventuels enfants (équivalent de Kill(entireProcessTree)).
            Get-CimInstance Win32_Process -Filter "ParentProcessId=$($proc.Id)" -ErrorAction SilentlyContinue |
                ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        } catch { }
    }
    Restore-ChildEnv $old
}

# --- 5. Ports en écoute -----------------------------------------------------
Write-Section "5. Ports réseau 7000 / 7001 / 7002"

# Relance brièvement uxplay le temps de relever les ports écoutés, car il doit
# tourner pour qu'ils soient ouverts.
$old = Set-ChildEnv $childEnv
$portProc = $null
try {
    $portProc = Start-Process -FilePath $exePath `
        -ArgumentList @('-n', $ReceiverName, '-p', '7000') `
        -WorkingDirectory $packageDir `
        -RedirectStandardOutput (Join-Path $logDir "uxplay-ports-out.log") `
        -RedirectStandardError  (Join-Path $logDir "uxplay-ports-err.log") `
        -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3

    foreach ($port in 7000, 7001, 7002) {
        $tcp = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
        $udp = @(Get-NetUDPEndpoint -LocalPort $port -ErrorAction SilentlyContinue)
        if ($tcp.Count -gt 0) {
            Write-Ok "Port TCP $port en écoute (PID $($tcp[0].OwningProcess))."
        } elseif ($udp.Count -gt 0) {
            Write-Ok "Port UDP $port ouvert (PID $($udp[0].OwningProcess))."
        } else {
            Write-Warn2 "Port $port : aucune écoute TCP/UDP détectée (peut n'être ouvert qu'à la connexion d'un client)."
        }
    }
}
catch {
    Write-Warn2 "Impossible de relever les ports : $($_.Exception.Message)"
}
finally {
    if ($portProc -and -not $portProc.HasExited) {
        try {
            Stop-Process -Id $portProc.Id -Force -ErrorAction SilentlyContinue
            Get-CimInstance Win32_Process -Filter "ParentProcessId=$($portProc.Id)" -ErrorAction SilentlyContinue |
                ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        } catch { }
    }
    Restore-ChildEnv $old
}

# --- 6. Règles de pare-feu Windows ------------------------------------------
Write-Section "6. Règles de pare-feu Windows"

try {
    # Cherche les règles entrantes qui autorisent 7000/7001/7002.
    $wanted = @('7000', '7001', '7002')
    $found  = @{}
    $rules  = @(Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue)
    foreach ($rule in $rules) {
        $pf = $rule | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue
        if ($pf -and $pf.LocalPort) {
            foreach ($p in $wanted) {
                # On ne compte QUE les règles qui citent explicitement le port.
                # Les règles 'Any' (comme « Pilote WFD ») ne prouvent rien sur
                # l'ouverture ciblée de 7000/7001/7002 et donnaient un faux positif.
                if ($pf.LocalPort -contains $p) {
                    if (-not $found.ContainsKey($p)) { $found[$p] = $rule.DisplayName }
                }
            }
        }
    }
    foreach ($p in $wanted) {
        if ($found.ContainsKey($p)) {
            Write-Ok "Port $p autorisé en entrée par la règle « $($found[$p]) »."
        } else {
            Write-Warn2 "Aucune règle de pare-feu entrante trouvée pour le port $p."
            Write-Info  "L'installeur (AirGlass.iss) doit créer les 3 règles TCP+UDP, ou"
            Write-Info  "exécuter : New-NetFirewallRule -DisplayName 'AirGlass $p' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $p"
        }
    }
}
catch {
    Write-Warn2 "Lecture des règles de pare-feu impossible (droits insuffisants ?) : $($_.Exception.Message)"
}

# --- 7. Synthèse + nettoyage -------------------------------------------------
Write-Section "7. Synthèse"

if ($script:FailCount -eq 0) {
    Write-Ok "Aucun problème bloquant détecté : le paquet UxPlay natif semble viable."
    Write-Info "Validation finale restante : connexion réelle depuis un iPhone (visibilité AirPlay)."
} else {
    Write-Fail "$($script:FailCount) problème(s) bloquant(s) détecté(s) - voir les lignes [FAIL] ci-dessus."
}

Write-Host ""
if ($KeepLogs) {
    Write-Info "Logs conservés dans : $logDir"
} else {
    try {
        Remove-Item -LiteralPath $logDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Info "Logs temporaires supprimés (relancez avec -KeepLogs pour les garder)."
    } catch {
        Write-Info "Logs dans : $logDir (suppression impossible)."
    }
}

exit ([int]($script:FailCount -gt 0))
