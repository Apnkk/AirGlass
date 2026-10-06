#!/bin/bash
pkill -x uxplay 2>/dev/null
sleep 1
(uxplay -n TestAir -p 7000 >/tmp/ux.log 2>&1 &)
sleep 6
echo "--- ports uxplay (doivent etre 7000/7001/7002) ---"
ss -tulnp 2>/dev/null | grep uxplay
echo "--- avahi-browse airplay (resolution) ---"
timeout 5 avahi-browse -rtp _airplay._tcp 2>/dev/null | grep -E "^="
echo "--- avahi-browse raop (resolution) ---"
timeout 5 avahi-browse -rtp _raop._tcp 2>/dev/null | grep -E "^="
echo "--- IP annoncee pour le host ---"
avahi-resolve -n 9EKB0WP9.local 2>/dev/null || echo "non resolu"
pkill -x uxplay 2>/dev/null
echo "=== fin ==="
