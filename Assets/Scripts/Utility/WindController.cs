using Unity.VisualScripting;
using UnityEngine;

public class WindController : MonoBehaviour
{
    [Tooltip("Wind speed in m/s")]
    [Range(0f, 50f)]
    public float windSpeed = 5f;

    [Range(-180, 180)]
    public float windAngle = 0f;

    [Header("Object Properties")]
    [Tooltip("Drag coefficient - higher = more wind effect. Typical: 0.5-2.0")]
    [Range(0f, 5f)]
    private float dragCoefficient = 1.0f;

    [Tooltip("Exposed surface area in m² - larger objects drift more")]
    [Range(0.01f, 1f)]
    public float exposedArea = 0.05f;

    private Crest.OceanRenderer oceanRenderer;
    private Vector3 normalizedWindDirection;
    [HideInInspector]
    public Vector3 windForce = Vector3.zero;

    protected virtual void Start()
    {
        windAngle = 90f;
        exposedArea = 0.05f;
        oceanRenderer = Crest.OceanRenderer.Instance;

        UpdateWindDirection();
    }

    protected virtual void FixedUpdate()
    {
        // Update wind direction from Crest each frame if enabled
        UpdateWindDirection();
        // windSpeed = oceanRenderer._globalWindSpeed;
        oceanRenderer._globalWindSpeed = windSpeed;


        // Calculate wind force
        float forceMagnitude = dragCoefficient * exposedArea * windSpeed;
        windForce = normalizedWindDirection * forceMagnitude;
    }

    // Call this if you change windDirection at runtime
    public void UpdateWindDirection()
    {
        // Get wind direction angle from Crest (-180 to 180)
        // windAngle = oceanRenderer._globalWindDirectionAngle;
        oceanRenderer._globalWindDirectionAngle = windAngle;


        // Convert angle to direction vector
        // Crest uses: 0° = +Z (forward), 90° = +X (right)
        float radians = windAngle * Mathf.Deg2Rad;
        normalizedWindDirection = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
    }


    // Visualize wind direction in editor
    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = Color.cyan;
            Vector3 start = transform.position;
            Vector3 end = start + normalizedWindDirection * windSpeed;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end, 0.2f);
        }
    }
}