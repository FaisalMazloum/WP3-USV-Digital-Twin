using System.Collections;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.CommunicationInterfaces;
using UnityEngine;


public class RemoraLIDAR : MonoBehaviour
{
    ROSConnection ros;
    public string rosTopic_laserscan;      // LaserScan format (standard)
    public string rosTopic_rangedata;      // RangeData format - robots only
    public string rosTopic_obstacles;      // RangeData format - all obstacles
    public string frameId_suffix;
    [SerializeField]
    private string frameId;
    public string robotID;
    
    // LIDAR parameters
    public float angleMin;
    public float angleMax;
    public float angleIncrement;
    public float rangeMin;
    public float rangeMax;
    public float scanFrequency;
    
    private float timeElapsed;
    private int numRays;

    private int isFaulty = 0; 
    private string fault_type;

    void Start()
    {

        // --------------------- //
        // Simulation Parameters //
        // --------------------- //
        angleMin = 0f;
        angleMax = 360f;
        angleIncrement = 0.8f;
        rangeMin = 0.15f;
        rangeMax = 10f; // different range from RAB range
        scanFrequency = 5f;

        // --------------------- //
        //    TEST Parameters    //
        // --------------------- //
        // angleMin = 0f;
        // angleMax = 360f;
        // angleIncrement = 2.0f;
        // rangeMin = 0.15f;
        // rangeMax = 10f; // different range from RAB range
        // scanFrequency = 5f;


        // --------------------- //
        //         ROS2          //
        // --------------------- //
        robotID = transform.root.name;
        frameId = robotID + "/" + frameId_suffix;
        
        // Three topics
        rosTopic_laserscan = $"/{robotID}/laserscan";         // LaserScan - all obstacles
        rosTopic_rangedata = $"/{robotID}/range_data";        // RangeData - robots only
        rosTopic_obstacles = $"/{robotID}/obstacle_ranges";   // RangeData - all obstacles
        
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<LaserScanMsg>(rosTopic_laserscan);
        ros.RegisterPublisher<RangeDataMsg>(rosTopic_rangedata);
        ros.RegisterPublisher<RangeDataMsg>(rosTopic_obstacles);
        
        // Calculate number of rays needed
        numRays = Mathf.CeilToInt((angleMax - angleMin) / angleIncrement) + 1;
    }

    void FixedUpdate()
    {
        timeElapsed += Time.fixedDeltaTime;

        if (timeElapsed > 1f / scanFrequency)
        {
            LaserScanMsg laserscan_msg = new LaserScanMsg();        // LaserScan format
            RangeDataMsg rangedata_msg = new RangeDataMsg();        // ONLY robots
            RangeDataMsg obstacle_msg = new RangeDataMsg();         // ALL obstacles

            // Setup headers
            laserscan_msg.header = new HeaderMsg
            {
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg
                {
                    sec = (int)Time.fixedTime,
                    nanosec = (uint)((Time.fixedTime - (int)Time.fixedTime) * 1e9)
                },
                frame_id = frameId
            };
            rangedata_msg.header = laserscan_msg.header;
            obstacle_msg.header = laserscan_msg.header;

            // LaserScan metadata
            laserscan_msg.angle_min = angleMin * Mathf.Deg2Rad;
            laserscan_msg.angle_max = angleMax * Mathf.Deg2Rad;
            laserscan_msg.angle_increment = angleIncrement * Mathf.Deg2Rad;
            laserscan_msg.range_min = rangeMin;
            laserscan_msg.range_max = rangeMax;
            laserscan_msg.scan_time = 0f;
            laserscan_msg.time_increment = 0f;

            // Initialize arrays
            laserscan_msg.ranges = new float[numRays];
            laserscan_msg.intensities = new float[numRays];
            rangedata_msg.ranges = new float[numRays];
            rangedata_msg.angles = new double[numRays];
            obstacle_msg.ranges = new float[numRays];
            obstacle_msg.angles = new double[numRays];

            for (int i = 0; i < numRays; i++)
            {
                float angle = angleMin + (i * angleIncrement);
                rangedata_msg.angles[i] = (double)angle * Mathf.Deg2Rad;
                obstacle_msg.angles[i] = (double)angle * Mathf.Deg2Rad;

                // Convert angle to direction
                Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward;

                RaycastHit hit;
                
                // ========== HANDLE SENSOR FAULTS ==========
                if (isFaulty == 1 && fault_type == "Pmin") {
                    // PMIN: Sensors report NO obstacles (always clear)
                    laserscan_msg.ranges[i] = float.PositiveInfinity;
                    // rangedata_msg.ranges[i] = float.PositiveInfinity;               // don't add since RAB info will be obtained from GNSS not from LiDAR
                    obstacle_msg.ranges[i] = float.PositiveInfinity;
                } else if (isFaulty == 1 && fault_type == "Pmax") {
                    // PMAX: Sensors always report collision at minimum range
                    laserscan_msg.ranges[i] = rangeMin;
                    // rangedata_msg.ranges[i] = rangeMin;                             // don't add since RAB info will be obtained from GNSS not from LiDAR
                    obstacle_msg.ranges[i] = rangeMin;
                } else if (isFaulty == 1 && fault_type == "Prnd") {
                    // PRND: Random sensor readings
                    float randomRange = Random.Range(rangeMin, rangeMax);
                    laserscan_msg.ranges[i] = randomRange;
                    // rangedata_msg.ranges[i] = randomRange;                          // don't add since RAB info will be obtained from GNSS not from LiDAR
                    obstacle_msg.ranges[i] = randomRange;
                }

                // ========== NORMAL OPERATION (NO FAULT) ==========
                if (Physics.Raycast(transform.position, direction, out hit, rangeMax) && isFaulty != 1)
                {
                    float distance = hit.distance;
                    // if (distance > rangeMax)
                        // distance = rangeMax;
                    
                    // ALL objects gets inserted
                    laserscan_msg.ranges[i] = distance;
                    obstacle_msg.ranges[i] = distance;
                    
                    // ONLY robots go to range_data
                    if (hit.collider.transform.root.CompareTag("remora_bot"))
                    {
                        rangedata_msg.ranges[i] = distance;
                        // if (robotID == "remora0")
                            // Debug.DrawRay(transform.position, direction * distance, Color.green, 1f / scanFrequency);
                    }
                    else
                    {
                        // It's a wall/obstacle - exclude from range_data
                        // rangedata_msg.ranges[i] = rangeMax;
                        rangedata_msg.ranges[i] = float.PositiveInfinity;
                        // Debug.DrawRay(transform.position, direction * distance, Color.red, 1f / scanFrequency);
                    }
                }
                else
                {
                    // No hit - nothing detected
                    laserscan_msg.ranges[i] = float.PositiveInfinity;
                    rangedata_msg.ranges[i] = float.PositiveInfinity;
                    obstacle_msg.ranges[i] = float.PositiveInfinity;
                    // laserscan_msg.ranges[i] = rangeMax;
                    // rangedata_msg.ranges[i] = rangeMax;
                    // obstacle_msg.ranges[i] = rangeMax;
                }

                laserscan_msg.intensities[i] = 0f;
            }


            // ============================================================================================================
            // Check how many RAYs hit a robots (ONLY FOR DEBUGGING ----> TEST WITH 2 robots ONLY)
            // ============================================================================================================
            // if (robotID == "remora0") {
            //     int robotRayCount = 0;
            //     for (int i = 0; i < rangedata_msg.ranges.Length; i++)
            //     {
            //         if (rangedata_msg.ranges[i] > 0.5f && rangedata_msg.ranges[i] < 10.0f)
            //         {
            //             robotRayCount++;
            //         }
            //     }
            //     Debug.Log($"[{gameObject.name}] Robot rays detected: {robotRayCount}");
            // }


            // ----------------------------------------------------------- //
            //                  Publish all three messages                 //
            // ----------------------------------------------------------- //
            ros.Publish(rosTopic_laserscan, laserscan_msg);     // /laserscan (LaserScan)
            ros.Publish(rosTopic_obstacles, obstacle_msg);      // /obstacle_ranges (RangeData - all)
            ros.Publish(rosTopic_rangedata, rangedata_msg);     // /range_data (RangeData - robots only)

            timeElapsed = 0;
        }
    }
    
    [Header("Gizmo Settings")]
    [SerializeField] [Range(0, 1)] private float transparency = 0.3f;
    [SerializeField] private Color baseColor = Color.cyan;
    void OnDrawGizmos()
    {
        // Apply transparency to the color
        Color transparentColor = baseColor;
        transparentColor.a = transparency;
        Gizmos.color = transparentColor;
        
        // Draw the wireframe sphere
        Gizmos.DrawSphere(transform.position, rangeMax);
    }



    /* ===================================================== */
    /* ======================== API ======================== */
    /* ===================================================== */

    /// <summary> Sets all the proximity sensor (LiDAR) states, i.e., assign faulty reading. </summary>
    public void SetSensorState(int sensor_fault = 0, string type = "")
    {
        isFaulty = sensor_fault;
        fault_type = type;
    }
}