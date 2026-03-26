using UnityEngine;
using System.IO;

public class CameraFrameSaver : MonoBehaviour
{
    public Camera cam;

    void Start()
    {
        SaveFrame();
    }

    void SaveFrame()
    {
        RenderTexture rt = new RenderTexture(640, 480, 24);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/frame.png", bytes);
        Debug.Log("Saved frame to " + Application.dataPath + "/frame.png");
    }
}
