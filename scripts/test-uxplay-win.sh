#!/usr/bin/env bash
#
# test-uxplay-win.sh — verifie que uxplay.exe demarre de maniere AUTONOME
# depuis external/uxplay-win/, c.-a-d. en n'utilisant QUE les DLL embarquees
# (sans /mingw64 dans le PATH natif du processus uxplay). Confirme que le
# paquet distribuable ne depend plus de MSYS2.
#
# Les outils du script (head, grep, rm...) restent accessibles via /usr/bin ;
# seul le PATH *natif Windows* passe a uxplay.exe est reduit a System32, via
# un lancement par cmd.exe avec un PATH dedie, pour ne PAS exposer /mingw64.
#
# Usage (depuis PowerShell) :
#   & "C:\msys64\usr\bin\bash.exe" -lc "bash /p/AirServer/scripts/test-uxplay-win.sh"
#
set -uo pipefail

# Outils MSYS pour le script lui-meme.
export PATH="/usr/bin:/bin:$PATH"

OUT=/p/AirServer/external/uxplay-win

# cmd.exe par chemin absolu : il n'est PAS dans /usr/bin, donc on le reference
# explicitement (il reste accessible meme avec un PATH MSYS reduit).
CMD_EXE="/c/Windows/System32/cmd.exe"
if [ ! -x "$CMD_EXE" ]; then
  echo "!! cmd.exe introuvable a $CMD_EXE" >&2
  exit 1
fi

if [ ! -f "$OUT/uxplay.exe" ]; then
  echo "!! $OUT/uxplay.exe introuvable — lance collect-uxplay-win.sh d'abord." >&2
  exit 1
fi

cd "$OUT"

GST_WIN=$(cygpath -w "$PWD/lib/gstreamer-1.0")

# PATH natif Windows volontairement reduit a System32 pour le PROCESSUS
# uxplay.exe uniquement : s'il demarre ainsi, c'est qu'il trouve toutes ses
# dependances dans son propre dossier (le repertoire courant). On passe par
# cmd.exe avec un PATH natif dedie, pour ne PAS exposer /mingw64/bin.
WINPATH='C:\Windows\System32;C:\Windows'

run_isolated() {
  # $1 = arguments passes a uxplay.exe. Sortie combinee stdout+stderr.
  "$CMD_EXE" /d /s /c "set PATH=$WINPATH&& set GST_PLUGIN_PATH=$GST_WIN&& set GST_PLUGIN_SYSTEM_PATH=$GST_WIN&& uxplay.exe $1" 2>&1
}

echo "=== 1. Demarrage isole (PATH natif = System32 seulement) ==="
out=$(run_isolated "-h")
echo "$out" | head -n 3

# Un demarrage reussi affiche la banniere "UxPlay <version>".
if echo "$out" | grep -qiE 'was not found|cannot proceed|\.dll|is missing|point d.entree'; then
  echo ""
  echo "!! Une DLL semble manquante au demarrage isole. Message complet :"
  echo "$out" | head -n 30
  echo "   (ajoute la DLL nommee ci-dessus dans collect-uxplay-win.sh)"
  exit 1
fi
if ! echo "$out" | grep -qi 'UxPlay'; then
  echo ""
  echo "!! uxplay.exe n'a PAS affiche sa banniere — demarrage anormal. Sortie :"
  echo "$out" | head -n 30
  exit 1
fi
echo "   -> banniere UxPlay detectee : demarrage isole OK"

echo ""
echo "=== 2. Scan du registre GStreamer (plugins reellement charges) ==="
GST_DEBUG=GST_PLUGIN_LOADING:3 run_isolated "-h" > gstload.log 2>&1 || true
grep -oiE 'plugin "[^"]+" loaded' gstload.log | sort -u | head -n 40 || true
echo ""
echo "   plugins charges : $(grep -coiE 'plugin "[^"]+" loaded' gstload.log 2>/dev/null || echo 0)"
rm -f gstload.log

echo ""
echo "=== 3. Resume ==="
echo "    OK : uxplay.exe demarre en isolation depuis $OUT"
echo "    Le paquet est autonome (ne depend plus de /mingw64)."
