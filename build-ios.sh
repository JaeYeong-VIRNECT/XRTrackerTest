#!/bin/zsh

set -euo pipefail

SCRIPT_DIR=${0:A:h}
UNITY_APP=${UNITY_APP:-/Applications/Unity/Hub/Editor/6000.0.62f1/Unity.app/Contents/MacOS/Unity}
UNITY_BIN_DIR=${UNITY_APP:h}
SAFE_PATH="/usr/bin:/bin:/usr/sbin:/sbin:$UNITY_BIN_DIR"
LOG="$SCRIPT_DIR/Build/build-ios.log"

if [[ ! -x "$UNITY_APP" ]]; then
    echo "[build-ios] Unity not found at: $UNITY_APP"
    echo "[build-ios] Set UNITY_APP to your Unity executable path and retry."
    exit 1
fi

OPEN_INSTANCE=$(pgrep -fal "/Applications/Unity/.*/Unity.app/Contents/MacOS/Unity .*${SCRIPT_DIR}" | head -n 1 || true)
if [[ -n "$OPEN_INSTANCE" ]]; then
    echo "[build-ios] This project is already open in Unity."
    echo "[build-ios] Close the Unity editor for $SCRIPT_DIR, or build from the editor menu: XRTracker -> Build iOS Xcode Project"
    exit 2
fi

mkdir -p "$SCRIPT_DIR/Build"

echo "[build-ios] Unity: $UNITY_APP"
echo "[build-ios] Project: $SCRIPT_DIR"
echo "[build-ios] Log: $LOG"
echo "[build-ios] Generating iOS Xcode project..."

env -i HOME="$HOME" PATH="$SAFE_PATH" "$UNITY_APP" \
    -batchmode \
    -quit \
    -projectPath "$SCRIPT_DIR" \
    -buildTarget iOS \
    -executeMethod XRTrackerBuild.BuildIOS.Build \
    -logFile "$LOG"

echo "[build-ios] SUCCESS: $SCRIPT_DIR/Build/iOS"
echo "[build-ios] Next: open the Xcode project, configure signing, then build to a device."