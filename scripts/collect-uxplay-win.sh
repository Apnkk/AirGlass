#!/usr/bin/env bash
#
# collect-uxplay-win.sh — rassemble uxplay.exe, ses DLL de dependance directe
# et les plugins GStreamer necessaires au mirroring AirPlay dans un dossier
# distribuable (external/uxplay-win/). A lancer dans l'environnement MINGW64.
#
# Usage (depuis PowerShell) :
#   & "C:\msys64\usr\bin\bash.exe" -lc "export PATH=/mingw64/bin:/usr/bin:/bin && bash /p/AirServer/scripts/collect-uxplay-win.sh"
#
set -euo pipefail

MINGW=/mingw64
SRC=/p/AirServer/external/uxplay/build-win
OUT=/p/AirServer/external/uxplay-win

if [ ! -f "$SRC/uxplay.exe" ]; then
  echo "!! uxplay.exe introuvable dans $SRC — compile d'abord UxPlay." >&2
  exit 1
fi

echo "==> Nettoyage de $OUT"
rm -rf "$OUT"
mkdir -p "$OUT" "$OUT/lib/gstreamer-1.0"

# --- 1. L'executable --------------------------------------------------------
echo "==> Copie de uxplay.exe"
cp "$SRC/uxplay.exe" "$OUT/"

# --- 2. DLL de dependance directe ------------------------------------------
# Liste issue de `ldd uxplay.exe` (dependances directes dans /mingw64/bin).
echo "==> Copie des DLL de dependance directe"
DIRECT_DLLS=(
  libcrypto-3-x64
  libffi-8
  libgcc_s_seh-1
  libglib-2.0-0
  libgmodule-2.0-0
  libgobject-2.0-0
  libgstapp-1.0-0
  libgstbase-1.0-0
  libgstreamer-1.0-0
  libgstvideo-1.0-0
  libiconv-2
  libintl-8
  liborc-0.4-0
  libpcre2-8-0
  libplist-2.0
  libstdc++-6
  libwinpthread-1
)
for d in "${DIRECT_DLLS[@]}"; do
  if cp "$MINGW/bin/$d.dll" "$OUT/" 2>/dev/null; then
    echo "    DLL  $d.dll"
  else
    echo "    !!   $d.dll absente de $MINGW/bin (ignoree)"
  fi
done

# --- 3. Plugins GStreamer necessaires au mirroring AirPlay ------------------
# UxPlay a besoin de : reception/parsing (app, coreelements), decodage H264,
# conversion/affichage video, et decodage/sortie audio. On embarque un
# sous-ensemble cible plutot que les ~400 Mo du dossier complet.
echo "==> Copie des plugins GStreamer necessaires"
GST_SRC="$MINGW/lib/gstreamer-1.0"
GST_OUT="$OUT/lib/gstreamer-1.0"
PLUGINS=(
  libgstapp               # appsrc/appsink (UxPlay injecte les frames par la)
  libgstcoreelements      # queue, tee, capsfilter, fakesink...
  libgstplayback          # playbin/decodebin
  libgsttypefindfunctions # detection de type
  libgstvideoconvertscale # videoconvert + videoscale
  libgstvideoparsersbad   # h264parse / h265parse
  libgstlibav             # avdec_h264 / avdec_h265 (decodage logiciel)
  libgstopenh264          # fallback decodeur H264 si present
  libgstd3d11             # sortie video Direct3D11 (Windows natif)
  libgstd3d               # support d3d commun (selon version)
  libgstwinscreencap      # (optionnel) capture — inoffensif si absent
  libgstautodetect        # autovideosink / autoaudiosink
  libgstvideofilter       # filtres video de base
  libgstaudioconvert      # conversion audio
  libgstaudioresample     # reechantillonnage audio
  libgstaudioparsers      # aacparse / alaw...
  libgstwasapi            # sortie audio Windows (WASAPI)
  libgstwasapi2           # sortie audio Windows (WASAPI2, selon version)
  libgstfaad              # decodage AAC si present
  libgstalaw              # (optionnel)
  libgstaudiofx           # (optionnel)
)
copied=0
for p in "${PLUGINS[@]}"; do
  if cp "$GST_SRC/$p.dll" "$GST_OUT/" 2>/dev/null; then
    echo "    PLUGIN  $p.dll"
    copied=$((copied + 1))
  else
    echo "    (absent) $p.dll — ignore"
  fi
done
echo "==> $copied plugin(s) GStreamer copie(s)"

# --- 4. DLL transitives des plugins (fermeture des dependances) -------------
# Les plugins GStreamer tirent d'autres DLL (codecs, av*, etc.) qui ne sont
# pas dans ldd(uxplay.exe). On resout la fermeture avec ntldd si disponible,
# sinon on copie les familles de DLL connues necessaires au decodage.
echo "==> Resolution des DLL transitives des plugins"
if command -v ntldd >/dev/null 2>&1; then
  for dll in "$GST_OUT"/*.dll "$OUT"/uxplay.exe; do
    ntldd -R "$dll" 2>/dev/null \
      | grep -io "$MINGW/bin/[^ ]*\.dll" \
      | while read -r dep; do
          base=$(basename "$dep")
          if [ ! -f "$OUT/$base" ]; then
            cp "$dep" "$OUT/" 2>/dev/null && echo "    DEP  $base"
          fi
        done
  done
else
  echo "    ntldd absent — copie des familles de DLL connues (libav, codecs)"
  for d in avcodec-* avformat-* avutil-* swresample-* swscale-* \
           libx264-* libx265-* libopus-* libvorbis* libogg-* \
           libfaad* libmp3lame-* libtheora* libgstpbutils-1.0-0 \
           libgstaudio-1.0-0 libgsttag-1.0-0 libgstriff-1.0-0 \
           libgstnet-1.0-0 libgstsdp-1.0-0 libgstrtp-1.0-0 \
           libgstpbutils-1.0-0 libgstcodecparsers-1.0-0 libgstd3d11-1.0-0 \
           libzstd libbz2-* liblzma-* zlib* libwinpthread-1; do
    for f in "$MINGW"/bin/$d.dll; do
      [ -e "$f" ] || continue
      base=$(basename "$f")
      if [ ! -f "$OUT/$base" ]; then
        cp "$f" "$OUT/" 2>/dev/null && echo "    DEP  $base"
      fi
    done
  done
fi

# --- 5. Resume --------------------------------------------------------------
echo ""
echo "==> Resume de $OUT"
echo "    DLL/exe racine : $(find "$OUT" -maxdepth 1 -type f | wc -l) fichier(s)"
echo "    plugins gst    : $(find "$GST_OUT" -type f | wc -l) fichier(s)"
echo "    taille totale  : $(du -sh "$OUT" | cut -f1)"
echo ""
echo "Termine. Verifie le lancement avec :"
echo "  cd $OUT && GST_PLUGIN_PATH=\$PWD/lib/gstreamer-1.0 ./uxplay.exe -h"
