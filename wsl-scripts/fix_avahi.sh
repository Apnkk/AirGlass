#!/bin/bash
set -e

CONF=/etc/avahi/avahi-daemon.conf

echo "=== sauvegarde de la config Avahi ==="
sudo cp -n "$CONF" "$CONF.bak" 2>/dev/null || true

echo "=== reecriture de /etc/avahi/avahi-daemon.conf ==="
# On restreint Avahi a l'interface LAN eth5 uniquement, pour qu'il n'annonce
# QUE 192.168.1.183 (routable depuis l'iPhone) et jamais le VPN 26.x ni les
# APIPA 169.254.x. IPv4 seulement (iOS AirPlay prefere l'IPv4).
sudo tee "$CONF" > /dev/null << "CONFEOF"
[server]
use-ipv4=yes
use-ipv6=no
allow-interfaces=eth5
ratelimit-interval-usec=1000000
ratelimit-burst=1000

[publish]
publish-hinfo=no
publish-workstation=no
publish-aaaa-on-ipv4=no
publish-a-on-ipv6=no

[reflector]

[rawprotocols]
CONFEOF

echo "=== redemarrage d'Avahi ==="
sudo service avahi-daemon restart >/dev/null 2>&1
sleep 2
service avahi-daemon status 2>&1 | grep -E 'Active|interface|Registering' | head -8

echo "=== fin config avahi ==="
