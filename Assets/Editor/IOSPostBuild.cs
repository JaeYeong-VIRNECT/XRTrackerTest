using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace XRTrackerBuild
{
    public static class IOSPostBuild
    {
        [PostProcessBuild(1000)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string mainTargetGuid = project.GetUnityMainTargetGuid();
            string frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();

            AddRequiredFrameworks(project, mainTargetGuid);
            AddRequiredFrameworks(project, frameworkTargetGuid);

            project.WriteToFile(projectPath);
        }

        static void AddRequiredFrameworks(PBXProject project, string targetGuid)
        {
            if (string.IsNullOrEmpty(targetGuid))
                return;

            project.AddFrameworkToProject(targetGuid, "IOSurface.framework", false);
        }
    }
}