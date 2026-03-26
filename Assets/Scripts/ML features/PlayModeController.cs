using UnityEditor;

public class PlayModeController
{
    [MenuItem("Tools/Enter Play Mode")]
    public static void EnterPlayMode()
    {
        EditorApplication.isPlaying = true;
    }
}