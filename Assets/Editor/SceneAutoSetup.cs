using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IV.FormulaTracker;
using IV.FormulaTracker.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace XRTrackerBuild
{
    public static class SceneAutoSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const float MinViewpointDistance = 0.2f;
        const float MaxViewpointDistance = 2.0f;

        [MenuItem("XRTracker/Auto Setup Scene (AR - Mobile)")]
        public static async void RunAR() => await Run(pc: false);

        [MenuItem("XRTracker/Auto Setup Scene (PC - Webcam)")]
        public static async void RunPC() => await Run(pc: true);

        static async Task Run(bool pc)
        {
            Directory.CreateDirectory("Assets/Scenes");

            UnityEngine.SceneManagement.Scene scene;
            if (File.Exists(ScenePath))
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            string modelPath = FindUserModel();
            if (modelPath == null)
            {
                Debug.LogError("[AutoSetup] No model found in Assets/. Place a .glb or .fbx directly in Assets/");
                return;
            }
            Debug.Log($"[AutoSetup] Mode={(pc ? "PC" : "AR")} Model={modelPath}");

            if (AssetImporter.GetAtPath(modelPath) is ModelImporter importer)
            {
                bool changed = false;
                if (!importer.isReadable) { importer.isReadable = true; changed = true; }
                if (importer.animationType != ModelImporterAnimationType.None)
                { importer.animationType = ModelImporterAnimationType.None; changed = true; }
                if (changed) importer.SaveAndReimport();
            }

            foreach (var obj in scene.GetRootGameObjects().ToArray())
            {
                if (obj.name == "Main Camera" ||
                    obj.name.StartsWith("AR_Tracker") || obj.name.StartsWith("AR Tracker") ||
                    obj.name.StartsWith("PC_Tracker") || obj.name.StartsWith("PC Tracker") ||
                    obj.name == "TrackedMesh" || obj.name == "XRTrackerTestRunner")
                    Object.DestroyImmediate(obj);
            }

            Selection.activeGameObject = null;
            string menuPath = pc ? "GameObject/XRTracker/PC Tracker" : "GameObject/XRTracker/AR Tracker";
            if (!EditorApplication.ExecuteMenuItem(menuPath))
            {
                Debug.LogError($"[AutoSetup] Menu unavailable: {menuPath}");
                return;
            }

            var trackerManager = Object.FindFirstObjectByType<XRTrackerManager>();
            if (trackerManager == null)
            {
                Debug.LogError("[AutoSetup] Tracker created but XRTrackerManager not found");
                return;
            }

            string licensePath = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".lic", System.StringComparison.OrdinalIgnoreCase));
            if (licensePath != null)
            {
                var licAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(licensePath);
                var mgrSo = new SerializedObject(trackerManager);
                var licProp = mgrSo.FindProperty("_embeddedLicense");
                if (licProp != null && licAsset != null)
                {
                    licProp.objectReferenceValue = licAsset;
                    mgrSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(trackerManager);
                    Debug.Log($"[AutoSetup] License attached: {licensePath}");
                }
            }
            else Debug.LogWarning("[AutoSetup] No .lic in Assets/ — license overlay will show");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var meshRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            meshRoot.name = "TrackedMesh";
            meshRoot.transform.position = pc ? new Vector3(0, 0, 0.5f) : Vector3.zero;
            meshRoot.transform.rotation = Quaternion.identity;
            PrefabUtility.UnpackPrefabInstance(meshRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var filters = meshRoot.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null).ToArray();
            if (filters.Length == 0)
            {
                Debug.LogError("[AutoSetup] Model has no MeshFilter with mesh");
                return;
            }

            meshRoot.AddComponent<TouchManipulator>();
            meshRoot.AddComponent<TrackingStatsHUD>();
            meshRoot.AddComponent<RuntimeControlsUI>();

            var tb = meshRoot.AddComponent<TrackedBody>();
            var so = new SerializedObject(tb);
            var meshProp = so.FindProperty("_meshFilters");
            meshProp.arraySize = filters.Length;
            for (int i = 0; i < filters.Length; i++)
                meshProp.GetArrayElementAtIndex(i).objectReferenceValue = filters[i];

            so.FindProperty("_initialPoseSource").enumValueIndex = (int)InitialPoseSource.Viewpoint;

            var startP = so.FindProperty("_useCustomStartThreshold");
            var startV = so.FindProperty("_customQualityToStart");
            var stopP  = so.FindProperty("_useCustomStopThreshold");
            var stopV  = so.FindProperty("_customQualityToStop");
            if (startP != null) startP.boolValue = true;
            if (startV != null) startV.floatValue = 0.15f;
            if (stopP  != null) stopP.boolValue  = true;
            if (stopV  != null) stopV.floatValue  = 0.05f;

            var bounds = ComputeBounds(meshRoot);
            float suggestedDistance = Mathf.Max(bounds.size.magnitude, MinViewpointDistance) * 0.8f;
            float distance = Mathf.Clamp(suggestedDistance, MinViewpointDistance, MaxViewpointDistance);
            if (!Mathf.Approximately(distance, suggestedDistance))
                Debug.LogWarning($"[AutoSetup] Viewpoint distance clamped from {suggestedDistance:F2} to {distance:F2}. Check model import scale if the object appears too large or too small.");

            var viewpoint = new GameObject("Viewpoint");
            viewpoint.transform.SetParent(meshRoot.transform, false);
            viewpoint.transform.localPosition = new Vector3(0, bounds.extents.y * 0.3f, -distance);
            viewpoint.transform.LookAt(meshRoot.transform.TransformPoint(bounds.center - meshRoot.transform.position));

            so.FindProperty("_initialViewpoint").objectReferenceValue = viewpoint.transform;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("[AutoSetup] Generating tracking model (may take 10-30s)...");
            EditorUtility.DisplayProgressBar("XRTracker", "Generating silhouette model...", 0.5f);
            try
            {
                string modelDir = "Assets/TrackingModels";
                Directory.CreateDirectory(modelDir);
                string assetPath = $"{modelDir}/{meshRoot.name}_TrackingModel.asset";
                float geometryScale = meshRoot.transform.lossyScale.x;

                var trackingAsset = await SilhouetteModelGenerator.GenerateAndSaveAssetAsync(
                    tb.MeshFilters, meshRoot.transform, tb.ModelSettings,
                    geometryScale, assetPath,
                    includeSilhouetteModel: true, includeDepthModel: false);

                if (trackingAsset != null)
                {
                    tb.TrackingModelAsset = trackingAsset;
                    EditorUtility.SetDirty(tb);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[AutoSetup] Tracking model saved: {assetPath}");
                }
                else
                {
                    Debug.LogError("[AutoSetup] Tracking model generation returned null");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AutoSetup] Tracking model generation failed: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string next = pc
                ? "Press Play in Editor (webcam)."
                : "Ctrl+S, close Unity, run build-apk.bat";
            Debug.Log($"[AutoSetup] DONE ({(pc ? "PC" : "AR")}): {filters.Length} mesh(es). Next: {next}");
        }

        static string FindUserModel()
        {
            string[] exts = { ".glb", ".gltf", ".fbx", ".obj" };
            var all = AssetDatabase.FindAssets("", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/") && !p.StartsWith("Assets/Quickstart/"))
                .Where(p => exts.Any(e => p.EndsWith(e, System.StringComparison.OrdinalIgnoreCase)))
                .Distinct()
                .ToList();

            var rootLevel = all.FirstOrDefault(p => p.Count(c => c == '/') == 1);
            return rootLevel ?? all.FirstOrDefault();
        }

        static Bounds ComputeBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one * 0.1f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
