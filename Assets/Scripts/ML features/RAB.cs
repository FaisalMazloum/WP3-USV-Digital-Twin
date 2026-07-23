using System.Collections.Generic;
using UnityEngine;

public class RABSensor : MonoBehaviour
{
    public bool print_button = false;
    private int isFaulty = 0; 
    private float max_range = 0f;
    private float rofs_magnitude = 0f;
    private float rofs_angle = 0f;
    private float timeElapsed;

    public NeighborStateManager _neighborStateManager;
    public GlobalCSVExporter _csvExporter;


    [System.Serializable]
    public struct RABDetection
    {
        public GameObject robot;
        public float distance;
        public float bearing;
        public Vector3 relativePosition; // position in local frame
        
        public RABDetection(GameObject robot, float distance, float bearing, Vector3 relativePosition)
        {
            this.robot = robot;
            this.distance = distance;
            this.bearing = bearing;
            this.relativePosition = relativePosition;
        }
    }

    // Store all detected robots
    private Dictionary<GameObject, RABDetection> detectedRobots = new Dictionary<GameObject, RABDetection>();


    void Start()
    {
        if (!_neighborStateManager)
        {
            Debug.LogError("No NeighborStateManager found for RAB script...");
        }
        if (!_csvExporter)
        {
            Debug.LogError("No NeighborStateManager found for RAB script...");
        }

        // max_range = transform.lossyScale.x / 2f; // Get size of RAB transform
        max_range = 10.0f;
        Debug.Log($"RAB max range = {max_range}");  
        
        timeElapsed = 0f;
    }


    void FixedUpdate()
    {
        if (_csvExporter._start)
        {
            timeElapsed += Time.fixedDeltaTime;
            while (timeElapsed >= _neighborStateManager.publishInterval)
            {
                rofs_magnitude = Random.Range(0.75f, 1f) * isFaulty;
                rofs_angle = Random.Range(Mathf.PI, Mathf.PI) * Mathf.Rad2Deg * isFaulty;
                // Debug.Log($"d = {rofs_magnitude} | theta = {rofs_angle} | Faulty = {isFaulty}");
                timeElapsed -= _neighborStateManager.publishInterval;
            }
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.transform.root.tag == "remora_bot" && !other.isTrigger)
        // if (other.transform.root.tag == "Finish" && !other.isTrigger)
        {
            GameObject otherRobot = other.transform.root.gameObject;

            float distance = Vector3.Distance(transform.root.position, other.transform.root.position) + rofs_magnitude; // only add offset if there is a fault.

            Vector3 localPos = transform.InverseTransformDirection(otherRobot.transform.root.position - transform.root.position); // converts global coordinates of "other.transform" to the relative coordinates or "transform" which is the observing robot. InverseTransformDirection IGNORES SCALE
            float bearing = Mathf.Atan2(localPos.x, localPos.z) * Mathf.Rad2Deg + rofs_angle; // only add offset if there is a fault.
            bearing = Mathf.Repeat(bearing + 180f, 360f) - 180f; // normalizes to [-180, 180]
            
            // Store or update detection
            RABDetection detection = new RABDetection(otherRobot, distance, bearing, localPos);
            detectedRobots[otherRobot] = detection;
            
            if (print_button)
            {
                foreach (var observed_robot in detectedRobots)
                {
                    // Debug.Log($"{transform.root.name} detected {observed_robot.Key.transform.root.name}: distance={observed_robot.Value.distance}m, x={observed_robot.Value.relativePosition.x}m, y={observed_robot.Value.relativePosition.z}m");
                    Debug.Log($"{transform.root.name} detected {observed_robot.Key.transform.root.name}: distance={observed_robot.Value.distance:F2}m, bearing={observed_robot.Value.bearing:F1}°");
                }
                print_button = false;
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.transform && other.transform.root.tag == "remora_bot")
        // if (other.transform && other.transform.root.tag == "Finish")
        {
            GameObject otherRobot = other.transform.root.gameObject;
            detectedRobots.Remove(otherRobot);
            // Debug.Log($"{transform.root.name} lost contact with {otherRobot.name}");
        }
    }



    /* ===================================================== */
    /* ======================== API ======================== */
    /* ===================================================== */

    /// <summary> Get all current detections (returns copy for thread safety). </summary>
    /// <returns> The a dictionary with all the observed Robots and their observed neighbors. </returns>
    public Dictionary<GameObject, RABDetection> GetDetections()
    {
        return new Dictionary<GameObject, RABDetection>(detectedRobots);
    }
    
    /// <summary> Set RAB sensor fault state, i.e., enable fault (if 1) to the RAB sensor. </summary>
    public void SetRABState(int rab_fault = 0)
    {
        isFaulty = rab_fault;
    }

    public float GetMaxRange()
    {
        return max_range;
    }
}