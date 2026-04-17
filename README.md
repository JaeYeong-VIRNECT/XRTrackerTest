# XRTracker Test App

Minimal Unity 6 project for testing the [XRTracker](https://docs.xrtracker.net) package (`com.formulaxr.tracker`).

## Requirements

- Unity **6000.0.30f1** (Unity 6) or later — install via Unity Hub
- **Git** with **Git LFS** installed and on PATH (required by the package for native plugins)
- Windows 10/11 with a connected webcam (this skeleton targets Windows; see below for mobile)

## Open the project

1. Install Git LFS: `git lfs install`
2. Open **Unity Hub → Add → Select folder** → pick this directory
3. Unity resolves `com.formulaxr.tracker` from GitHub on first import (may take a minute)

## First-time setup in the Editor

1. **Register a license**: `Tools → XR Tracker → License Registration` → free Developer tier
2. **Create a tracker rig**: `GameObject → XRTracker → PC Tracker` (webcam) — this creates a camera rig and an `XRTrackerManager`
3. **Import a model to track**: follow the [Quick Start](https://docs.xrtracker.net/getting-started/quick-start/) — download the Meta Quest controller FBX, enable **Read/Write**, set Animation Type to `None`
4. **Add `TrackedBody`**: place your mesh in the scene, add the `TrackedBody` component — `meshFilters` auto-populates
5. **Create a viewpoint**: add a viewpoint child, position it at the expected initial detection angle (use `Ctrl+Shift+F` Align With View)
6. **Attach this test script**: drop `Assets/Scripts/XRTrackerTest.cs` onto any GameObject and wire the `TrackedBody` reference in the Inspector
7. **Play** — watch the Console for lifecycle and pose logs

## What the test script does

`Assets/Scripts/XRTrackerTest.cs` subscribes to the main XRTracker events and logs:

- Tracker initialization + license status
- Camera selection + resolution
- `TrackedBody` start/stop tracking
- Per-update pose (position + rotation)
- Periodic tracking quality metrics (projection error, edge coverage, visibility)

It auto-calls `StartDetection()` once the tracker initializes.

## Mobile (iOS / Android) additions

Not configured in this skeleton. To retarget:

1. Install **AR Foundation 6.x** + **ARKit XR Plugin** (iOS) or **ARCore XR Plugin** (Android) via Package Manager
2. Remove **Vulkan** from `Player Settings → Other Settings → Graphics APIs`
3. Run **Project Validation**
4. Use `GameObject → XRTracker → AR Tracker` instead of PC Tracker
