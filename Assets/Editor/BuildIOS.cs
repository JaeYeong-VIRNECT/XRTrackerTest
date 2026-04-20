using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRTrackerBuild
{
    public static class BuildIOS
    {
        const string SceneDir = "Assets/Scenes";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutputDir = "Build";
        const string XcodeProjectName = "iOS";
        const string BundleIdentifier = "com.formulaxr.xrtrackertest";

        [MenuItem("XRTracker/Build iOS Xcode Project")]
        public static void Build()
        {
            ConfigurePlayerSettings();
            EnsureScene();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string buildDir = Path.Combine(projectRoot, OutputDir);
            Directory.CreateDirectory(buildDir);
            string xcodePath = Path.Combine(buildDir, XcodeProjectName);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = xcodePath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[BuildIOS] SUCCESS {xcodePath} in {summary.totalTime}");
            }
            else
            {
                Debug.LogError($"[BuildIOS] FAILED result={summary.result} errors={summary.totalErrors}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "FormulaXR";
            PlayerSettings.productName = "XRTracker Test";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleIdentifier);
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.iOS.buildNumber = "1";
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.cameraUsageDescription = "Camera access is required for XR object tracking.";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
        }

        static void EnsureScene()
        {
            Directory.CreateDirectory(SceneDir);
            if (File.Exists(ScenePath))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("XRTrackerTestRunner");
            go.AddComponent<XRTrackerTest>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }
    }
}