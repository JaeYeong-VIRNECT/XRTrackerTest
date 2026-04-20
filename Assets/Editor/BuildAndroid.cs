using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRTrackerBuild
{
    public static class BuildAndroid
    {
        const string SceneDir = "Assets/Scenes";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutputDir = "Build";
        const string ApkName = "xrtracker-test.apk";

        [MenuItem("XRTracker/Build Android APK")]
        public static void Build()
        {
            ConfigurePlayerSettings();
            EnsureScene();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string buildDir = Path.Combine(projectRoot, OutputDir);
            Directory.CreateDirectory(buildDir);
            string apkPath = Path.Combine(buildDir, ApkName);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildAndroid] SUCCESS {apkPath} ({summary.totalSize / 1024 / 1024} MB) in {summary.totalTime}");
            }
            else
            {
                Debug.LogError($"[BuildAndroid] FAILED result={summary.result} errors={summary.totalErrors}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "FormulaXR";
            PlayerSettings.productName = "XRTracker Test";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.formulaxr.xrtrackertest");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.Android.forceInternetPermission = true;

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Disabled;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        static void EnsureScene()
        {
            Directory.CreateDirectory(SceneDir);
            if (File.Exists(ScenePath)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("XRTrackerTestRunner");
            go.AddComponent<XRTrackerTest>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }
    }
}
