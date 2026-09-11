#!/bin/bash
# Screenshot the emulator only once two consecutive captures 1 s apart are identical (compositor settled).
OUT="$1"; TMP="${OUT%.png}.tmp.png"
adb exec-out screencap -p > "$OUT"
for i in 1 2 3 4 5 6 7 8; do
  sleep 1
  adb exec-out screencap -p > "$TMP"
  if cmp -s "$OUT" "$TMP"; then rm -f "$TMP"; exit 0; fi
  mv "$TMP" "$OUT"
done
