using UnityEngine;

public class PerlinHeatmap : MonoBehaviour
{
    public int resolution = 100;
    [Range(0f, 1f)] public float opacity = 0.0f;
    public AdaptiveSamplingPattern asp;
    
    void Start()
    {
        if (asp == null)
        {
            Debug.Log($"No Adaptive Sampler component found!");
        }
        GenerateHeatmap();
    }
    
    void GenerateHeatmap()
    {
        Texture2D texture = new Texture2D(resolution, resolution);
        
        for (int pixelX = 0; pixelX < resolution; pixelX++)
        {
            for (int pixelZ = 0; pixelZ < resolution; pixelZ++)
            {
                // Same logic as gizmo grid
                float worldX = 90f + ((resolution - 1 - pixelX) / (float)(resolution - 1)) * 20f; // FLIP X
                float worldZ = 90f + ((resolution - 1 - pixelZ) / (float)(resolution - 1)) * 20f; // FLIP Z
                
                float perlin = Mathf.PerlinNoise(worldX * asp.perlinScale, worldZ * asp.perlinScale);
                perlin += asp.prelin_offset;
                
                Color color;
                if (perlin < 0.4f)
                    color = new Color(0, 0, 1, opacity);
                else if (perlin < 0.6f)
                    color = new Color(1, 0.92f, 0, opacity);
                else
                    color = new Color(1, 0, 0, opacity);
                
                texture.SetPixel(pixelX, pixelZ, color);
            }
        }
        
        texture.Apply();
        GetComponent<Renderer>().material.mainTexture = texture;
    }
}