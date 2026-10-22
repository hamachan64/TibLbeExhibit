#!/bin/sh
# Render title.html to PNG with headless Microsoft Edge (no extra install needed on Windows).
# Usage: sh render_title.sh      -> keyvisual_title.png (1920x1080) and keyvisual_title@2x.png (3840x2160)
set -e
cd "$(dirname "$0")"
EDGE="${EDGE:-/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe}"
URL="file:///$(pwd -W 2>/dev/null || pwd)/title.html"
for scale in 1 2; do
  out=keyvisual_title.png
  [ "$scale" = 2 ] && out=keyvisual_title@2x.png
  "$EDGE" --headless --disable-gpu --hide-scrollbars --allow-file-access-from-files \
    --force-device-scale-factor=$scale --window-size=1920,1080 \
    --virtual-time-budget=5000 --screenshot="$(pwd -W 2>/dev/null || pwd)/$out" "$URL"
  echo "wrote $out"
done
