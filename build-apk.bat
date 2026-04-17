@echo off
setlocal
set UNITY="C:\Program Files\Unity\Hub\Editor\6000.0.62f1\Editor\Unity.exe"
set PROJECT=%~dp0
set LOG=%PROJECT%Build\build.log

if not exist "%PROJECT%Build" mkdir "%PROJECT%Build"

echo [build-apk] Unity: %UNITY%
echo [build-apk] Project: %PROJECT%
echo [build-apk] Log: %LOG%
echo [build-apk] Running Unity batch build (first run may take 10-30 minutes)...

%UNITY% -batchmode -quit -projectPath "%PROJECT%" -buildTarget Android -executeMethod XRTrackerBuild.BuildAndroid.Build -logFile "%LOG%"

if %ERRORLEVEL% neq 0 (
    echo [build-apk] FAILED (exit=%ERRORLEVEL%) — see %LOG%
    exit /b %ERRORLEVEL%
)

echo [build-apk] SUCCESS: %PROJECT%Build\xrtracker-test.apk
echo [build-apk] Install: adb install -r "%PROJECT%Build\xrtracker-test.apk"
endlocal
