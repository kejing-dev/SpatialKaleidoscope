#!/bin/bash
# Emulator helpers. Usage: tools/emu.sh install | launch | cmd "<command>" | shot <name> | logcat
set -u
PROJ="/Users/kejing/Downloads/SpatialKaleidoscope"
PKG="com.kejing.spatialkaleidoscope"
APK="$PROJ/Builds/SpatialKaleidoscope.apk"
OUT="$PROJ/Captures/emulator"
mkdir -p "$OUT"
case "${1:-}" in
  install) adb install -r -g "$APK" ;;
  launch)  adb shell am force-stop "$PKG"; sleep 1; adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1; adb forward tcp:7777 tcp:7777 ;;
  stop)    adb shell am force-stop "$PKG" ;;
  cmd)     python3 "$PROJ/tools/kcmd.py" "$2" ;;
  shot)    adb exec-out screencap -p > "$OUT/$2.png" && echo "$OUT/$2.png" ;;
  logcat)  adb logcat -d -s Unity:* KaleidoDebug:* AndroidRuntime:* | tail -${2:-80} ;;
  clearlog) adb logcat -c ;;
  *) echo "usage: $0 install|launch|stop|cmd <text>|shot <name>|logcat [n]|clearlog" ;;
esac
