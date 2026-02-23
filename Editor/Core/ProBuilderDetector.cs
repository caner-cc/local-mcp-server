#if UNITY_EDITOR
using System.Linq;
using UnityEditor;

namespace LocalMCP
{
    /// <summary>
    /// Auto-detects ProBuilder package and defines PROBUILDER_ENABLED symbol.
    /// Only runs once per session to avoid recompile loops.
    /// </summary>
    public static class ProBuilderDetector
    {
        private const string DEFINE_SYMBOL = "PROBUILDER_ENABLED";
        private const string SESSION_KEY = "ProBuilderDetector_Checked";

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // Only check once per editor session to avoid recompile loops
            if (SessionState.GetBool(SESSION_KEY, false))
                return;

            SessionState.SetBool(SESSION_KEY, true);

            // Delay to ensure everything is loaded
            EditorApplication.delayCall += CheckProBuilderOnce;
        }

        private static void CheckProBuilderOnce()
        {
            // Check if ProBuilder types exist
            var proBuilderType = System.Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder");
            bool hasProBuilder = proBuilderType != null;

            var buildTarget = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (buildTarget == BuildTargetGroup.Unknown)
                buildTarget = BuildTargetGroup.Standalone;

            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTarget);
            var defineList = string.IsNullOrEmpty(defines)
                ? new System.Collections.Generic.List<string>()
                : defines.Split(';').ToList();

            bool hasDefine = defineList.Contains(DEFINE_SYMBOL);

            // Only modify if state changed
            if (hasProBuilder && !hasDefine)
            {
                defineList.Add(DEFINE_SYMBOL);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTarget, string.Join(";", defineList));
                UnityEngine.Debug.Log("[ProBuilderDetector] ProBuilder detected, added PROBUILDER_ENABLED define.");
            }
            else if (!hasProBuilder && hasDefine)
            {
                defineList.Remove(DEFINE_SYMBOL);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTarget, string.Join(";", defineList));
                UnityEngine.Debug.Log("[ProBuilderDetector] ProBuilder not found, removed PROBUILDER_ENABLED define.");
            }
        }

        [MenuItem("Tools/Environment/Check ProBuilder Status")]
        private static void CheckStatus()
        {
            var proBuilderType = System.Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder");
            if (proBuilderType != null)
            {
                UnityEngine.Debug.Log("[ProBuilderDetector] ProBuilder is installed and available.");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[ProBuilderDetector] ProBuilder is NOT installed. " +
                    "Add to Packages/manifest.json: \"com.unity.probuilder\": \"6.0.4\"");
            }
        }

        [MenuItem("Tools/Environment/Force Recheck ProBuilder")]
        private static void ForceRecheck()
        {
            SessionState.SetBool(SESSION_KEY, false);
            CheckProBuilderOnce();
        }
    }
}
#endif
