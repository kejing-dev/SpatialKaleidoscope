#!/bin/bash
# PICO emulator watchdog: keeps emulator-5554 booted and adb-online.
#   start:  nohup tools/emu_watchdog.sh > /dev/null 2>&1 &
#   stop:   pkill -f emu_watchdog.sh
# Log: ~/.pico/cli/logs/emu_watchdog.log
LOG="$HOME/.pico/cli/logs/emu_watchdog.log"
INTERVAL=20
mkdir -p "$(dirname "$LOG")"
log() { echo "$(date '+%F %T') $*" >> "$LOG"; }
log "watchdog started (pid $$)"
fails=0
while true; do
  state=$(adb -s emulator-5554 get-state 2>/dev/null | tr -d '\r')
  proc=$(pgrep -f "qemu-system|emulator.*-avd" | head -1)
  if [ "$state" = "device" ]; then
    boot=$(adb -s emulator-5554 shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')
    if [ "$boot" = "1" ]; then fails=0; sleep $INTERVAL; continue; fi
    log "device online but boot_completed=$boot; waiting"
  elif [ -n "$proc" ]; then
    fails=$((fails+1))
    log "emulator process alive but adb state='$state' (fail $fails) -> adb reconnect"
    adb reconnect >/dev/null 2>&1; adb reconnect offline >/dev/null 2>&1
    if [ $fails -ge 6 ]; then
      log "adb still not online after $fails checks -> restarting adb server"
      adb kill-server >/dev/null 2>&1; sleep 2; adb start-server >/dev/null 2>&1; fails=0
    fi
  else
    log "emulator process not running -> pico-cli emulator start"
    pico-cli emulator start >> "$LOG" 2>&1
    fails=0
  fi
  sleep $INTERVAL
done
