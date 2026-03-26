using UnityEngine;
using System.Collections.Generic;

public class RaycastTagDetector : MonoBehaviour
{
    public float fov = 60f;          // camera field of view
    public float range = 10f;        // max detection range
    public int raysPerTag = 20;       // number of sample rays per tag (3x3 grid)
    public float detectionThreshold = 0.7f;  // required fraction of rays that must hit

    void Update()
    {
        DetectTags();
    }

void DetectTags()
{
    Camera cam = GetComponentInChildren<Camera>();
    if (!cam)
    {
        Debug.LogWarning("No camera found on robot for tag detection.");
        return;
    }

    SimulatedTag[] tags = FindObjectsOfType<SimulatedTag>();
    foreach (var tag in tags)
    {
        Renderer rend = tag.GetComponent<Renderer>();
        Collider col = tag.GetComponent<Collider>();
        if (!rend || !col) continue;

        Bounds b = rend.bounds;
        Vector3[] samplePoints = SampleBounds(b, Mathf.CeilToInt(Mathf.Sqrt(raysPerTag)));

        int hits = 0;
        foreach (var point in samplePoints)
        {
            Vector3 viewportPos = cam.WorldToViewportPoint(point);

            // Check if inside camera view (x,y in [0,1]) and in front (z > 0)
            if (viewportPos.z > 0 &&
                viewportPos.x >= 0 && viewportPos.x <= 1 &&
                viewportPos.y >= 0 && viewportPos.y <= 1)
            {
                Vector3 dir = (point - cam.transform.position).normalized;
                if (Physics.Raycast(cam.transform.position, dir, out RaycastHit hit, range))
                {
                    Debug.DrawRay(transform.position, dir * range, Color.red);
                    if (hit.collider == col)
                        hits++;
                }
            }
        }

        float ratio = (float)hits / samplePoints.Length;
        if (ratio >= detectionThreshold)
        {
            float distance = Vector3.Distance(cam.transform.position, b.center);
            Vector3 localPos = cam.transform.InverseTransformPoint(b.center);
            Debug.Log($"Detected Tag '{tag.tagID}' | Dist={distance:F2}m | Local={localPos}");
        }
    }
}


    // Sample grid points on the tag’s surface
    Vector3[] SampleBounds(Bounds b, int n)
    {
        List<Vector3> points = new List<Vector3>();
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                float fx = Mathf.Lerp(b.min.x, b.max.x, (x + 0.5f) / n);
                float fy = Mathf.Lerp(b.min.y, b.max.y, (y + 0.5f) / n);
                float fz = b.center.z; // assume flat surface facing camera roughly
                points.Add(new Vector3(fx, fy, fz));
            }
        }
        return points.ToArray();
    }
}
