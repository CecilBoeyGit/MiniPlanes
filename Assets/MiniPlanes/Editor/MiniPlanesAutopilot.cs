using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniPlanes.Editor
{
    /// <summary>
    /// Headless gameplay check: opens the real scene, enters play mode and lets AutopilotDriver play it
    /// with simulated hands. Exits with code 0 when every check passes.
    /// -executeMethod MiniPlanes.Editor.MiniPlanesAutopilot.Run (without -quit).
    /// </summary>
    public static class MiniPlanesAutopilot
    {
        [MenuItem("MiniPlanes/4. Run Autopilot Checks")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(MiniPlanesSceneBuilder.ScenePath);
            SessionState.SetBool(AutopilotDriver.SessionFlag, true);
            EditorApplication.EnterPlaymode();
        }
    }
}
