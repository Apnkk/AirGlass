#!/usr/bin/env bash
#
# setup-uxplay.sh — installe les dependances, compile et installe UxPlay
# dans Ubuntu (WSL2). Idempotent : relancable sans danger.
#
# Usage : bash scripts/setup-uxplay.sh
#
set -euo pipefail

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
err() { printf '\n\033[1;31m!!  %s\033[0m\n' "$*" >&2; }

# --- 0. Pre-requis : on ne tourne que sous Linux (WSL inclus) ----------------
if ! grep -qiE 'microsoft|wsl' /proc/version 2>/dev/null; then
  err "Ce script est prevu pour WSL/Ubuntu. Abandon."
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive

# --- 1. Dependances de build + runtime GStreamer ----------------------------
# Liste tiree du README officiel UxPlay pour Debian/Ubuntu.
PKGS=(
  cmake
  build-essential
  pkg-config
  git
  libssl-dev
  libplist-dev
  libavahi-compat-libdnssd-dev
  avahi-daemon
  libgstreamer1.0-dev
  libgstreamer-plugins-base1.0-dev
  gstreamer1.0-plugins-base
  gstreamer1.0-plugins-good
  gstreamer1.0-plugins-bad
  gstreamer1.0-plugins-ugly
  gstreamer1.0-libav
  gstreamer1.0-vaapi
  gstreamer1.0-tools
  gstreamer1.0-gl
  gstreamer1.0-gtk3
)

log "Mise a jour de l'index apt"
sudo apt-get update -y

log "Installation des dependances (${#PKGS[@]} paquets)"
sudo apt-get install -y "${PKGS[@]}"

# --- 2. Avahi (mDNS) : UxPlay en a besoin pour l'annonce Bonjour ------------
log "Activation du demon Avahi (mDNS)"
sudo service dbus start 2>/dev/null || true
sudo service avahi-daemon start 2>/dev/null || true

# --- 3. Recuperation des sources UxPlay -------------------------------------
SRC_DIR="$HOME/uxplay-src"
if [ -d "$SRC_DIR/.git" ]; then
  log "Sources deja presentes, mise a jour (git pull)"
  git -C "$SRC_DIR" pull --ff-only || true
else
  log "Clonage des sources UxPlay"
  git clone --depth 1 https://github.com/FDH2/UxPlay.git "$SRC_DIR"
fi

# --- 4. Compilation ---------------------------------------------------------
log "Compilation d'UxPlay"
cd "$SRC_DIR"
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j"$(nproc)"

# --- 5. Installation --------------------------------------------------------
log "Installation (sudo make install)"
sudo cmake --install build

# --- 6. Verification --------------------------------------------------------
log "Verification"
if command -v uxplay >/dev/null 2>&1; then
  echo "UXPLAY_OK: $(command -v uxplay)"
  uxplay -v 2>&1 | head -n 2 || true
else
  err "uxplay introuvable apres installation."
  exit 1
fi

log "Termine. Lance le mirroring avec : uxplay -n \"AirGlass\""
