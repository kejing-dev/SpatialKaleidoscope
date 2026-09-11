#!/bin/bash
# Full emulator verification pass: install, launch, screenshot every scene from several head poses.
# Usage: tools/verify_emulator.sh <runname> [scenes e.g. "1 2 3"]
set -u
cd "$(dirname "$0")/.."; PROJ="$(pwd)"
RUN="${1:-run}"
SCENES="${2:-1 2 3 4 5 6 7 8 9 10 11 12}"
OUT="Captures/emulator/$RUN"
mkdir -p "$OUT"
PKG="com.kejing.spatialkaleidoscope"
cmd() { python3 tools/kcmd.py "$1"; }
shot() { tools/stableshot.sh "$OUT/$1.png"; }
frame() { python3 tools/kcmd.py frame | sed 's/frame=//'; }
# wait until at least N new frames were rendered (slow emulator: ~1 fps)
waitframes() { local n="$1"; local f0=$(frame); local i=0; while [ $i -lt 60 ]; do sleep 0.5; local f1=$(frame); [ $((f1-f0)) -ge $n ] 2>/dev/null && return; i=$((i+1)); done; }

adb shell am force-stop "$PKG"; sleep 1; adb install -r -g "$PROJ/Builds/SpatialKaleidoscope.apk" 2>&1 | tail -1
adb logcat -c
tools/emu.sh launch
echo "waiting for app..."
for i in $(seq 1 40); do sleep 2; R=$(cmd status 2>/dev/null); if [ -n "$R" ]; then echo "$R"; break; fi; done
[ -z "${R:-}" ] && { echo "app did not answer on debug port"; adb logcat -d -s Unity:* AndroidRuntime:* | tail -60; exit 1; }
# views: name yaw pitch  (pitch negative = look up)
VIEWS="forward:0:0 up45:0:-45 zenith:0:-89 left:-60:-30 right:60:-30 down:0:35 side:90:0"
for s in $SCENES; do
  cmd "look 0 0" >/dev/null
  cmd "goto $s" >/dev/null
  cmd "warp ${WARP:-25}" >/dev/null      # let physics / growth / trails evolve
  waitframes 2
  for v in $VIEWS; do
    IFS=: read name yaw pitch <<< "$v"
    cmd "look $yaw $pitch" >/dev/null; waitframes 2
    shot "scene$(printf %02d $s)_$name"
  done
  cmd status
done
cmd "look 0 0" >/dev/null
echo "--- errors in logcat:"; adb logcat -d -s Unity:* AndroidRuntime:* | grep -i -E "exception|error|kaleido" | grep -v "ValidationExceptions" | tail -30
python3 - "$OUT" <<'PY'
import sys, glob, os
from PIL import Image, ImageDraw
out=sys.argv[1]
scenes=sorted(set(os.path.basename(f).split('_')[0] for f in glob.glob(out+'/scene*_*.png')))
os.makedirs(out+'/sheets', exist_ok=True)
order=['forward','up45','zenith','left','right','down','side']
for s in scenes:
    W=380
    sheet=Image.new('RGB',(W*4,W*2),(20,20,20)); d=ImageDraw.Draw(sheet)
    files=[f'{out}/{s}_{v}.png' for v in order if os.path.exists(f'{out}/{s}_{v}.png')]
    for i,f in enumerate(files[:8]):
        im=Image.open(f).convert('RGB'); im.thumbnail((W,W)); x=(i%4)*W; y=(i//4)*W
        sheet.paste(im,(x,y)); d.text((x+6,y+6),os.path.basename(f)[:-4],fill=(255,255,0))
    sheet.save(f'{out}/sheets/{s}.jpg',quality=78)
print("sheets:", out+'/sheets')
PY
