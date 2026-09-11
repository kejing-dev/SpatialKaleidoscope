#!/bin/bash
# Run a Unity batchmode method on this project. Usage: tools/unity.sh <Method> [logname] [extra args]
set -u
PROJ="/Users/kejing/Downloads/SpatialKaleidoscope"
UNITY="/Applications/Unity/Hub/Editor/6000.0.59f2/Unity.app/Contents/MacOS/Unity"
METHOD="${1:-}"
LOG="$PROJ/Logs/${2:-batch}.log"
shift 2 2>/dev/null || shift $# 
mkdir -p "$PROJ/Logs"
ARGS=(-batchmode -quit -projectPath "$PROJ" -buildTarget Android -logFile "$LOG")
if [ -n "$METHOD" ]; then ARGS+=(-executeMethod "$METHOD"); fi
"$UNITY" "${ARGS[@]}" "$@"
CODE=$?
echo "unity exit=$CODE log=$LOG"
echo "--- compiler errors:"; grep -E "error CS[0-9]+" "$LOG" | sed 's/^.*Assets\//Assets\//' | sort -u | head -40
echo "--- shader errors:"; grep -E "Shader error|shader compiler|Compilation failed" "$LOG" | sort -u | head -20
echo "--- exceptions:"; grep -E "Exception|\[Kaleido\]" "$LOG" | grep -v "ExceptionServices\|InvalidOperationException: Trying to" | head -30
exit $CODE
