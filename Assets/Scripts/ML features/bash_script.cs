using UnityEngine;
using System.Diagnostics;

public class RemoraLauncher : MonoBehaviour
{
    private const string TMUX_SESSION_NAME = "remora_unity_session";
    
    void Start()
    {
        // Kill any existing session from previous runs
        KillExistingSession();
        
        string scriptPath = "/home/faisal-mazloum/Desktop/remora_loop.sh";
        
        ProcessStartInfo startInfo = new ProcessStartInfo()
        {
            FileName = "gnome-terminal",
            Arguments = $"-- bash -c 'bash \"{scriptPath}\" {TMUX_SESSION_NAME}'",
            UseShellExecute = false,
            CreateNoWindow = false
        };
        
        try
        {
            UnityEngine.Debug.Log($"[Remora] Launching: {scriptPath}");
            Process.Start(startInfo);
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[Remora] Failed to start: {e.Message}");
        }
    }
    
    void OnApplicationQuit()
    {
        KillExistingSession();
    }
    
    private void KillExistingSession()
    {
        try
        {
            // Kill the tmux session
            ProcessStartInfo killTmux = new ProcessStartInfo()
            {
                FileName = "tmux",
                Arguments = $"kill-session -t {TMUX_SESSION_NAME}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            Process.Start(killTmux)?.WaitForExit();
            
            // Kill the terminal window hosting this session
            ProcessStartInfo killTerminal = new ProcessStartInfo()
            {
                FileName = "bash",
                Arguments = $"-c \"pkill -f 'gnome-terminal.*{TMUX_SESSION_NAME}'\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(killTerminal)?.WaitForExit();
            
            // Kill ROS2 daemon to clear DDS state
            ProcessStartInfo killDaemon = new ProcessStartInfo()
            {
                FileName = "bash",
                Arguments = "-c \"pkill -9 -f ros2-daemon\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(killDaemon)?.WaitForExit();
            
            UnityEngine.Debug.Log("[Remora] Cleaned up existing session");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning($"[Remora] Cleanup warning: {e.Message}");
        }
    }
}