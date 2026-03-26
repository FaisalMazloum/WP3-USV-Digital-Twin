using System.Collections;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.CommunicationInterfaces;
using UnityEngine;


public class RemoraLIDAR_OLD : MonoBehaviour
{
    ROSConnection ros;
    public string rosTopic;
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
        angleMin = 0f;
        angleMax = 360f;
        angleIncrement = 0.5f;
        rangeMin = 0.001f;
        rangeMax = 5f;
        scanFrequency = 10f;

        robotID = transform.root.name;
        frameId = robotID + "/" + frameId_suffix;
        rosTopic = $"/{robotID}/laserscan";
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<LaserScanMsg>(rosTopic);
        
        // Calculate number of rays needed
        numRays = Mathf.CeilToInt((angleMax - angleMin) / angleIncrement) + 1;
    }

    void FixedUpdate()
    {
        timeElapsed += Time.fixedDeltaTime;
        
        if (timeElapsed > 1f / scanFrequency)
        {
            LaserScanMsg scan_msg = new LaserScanMsg();
            
            scan_msg.header = new HeaderMsg
            {
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg
                {
                    sec = (int)Time.fixedTime,
                    nanosec = (uint)((Time.fixedTime - (int)Time.fixedTime) * 1e9)
                },
                frame_id = frameId
            };
            
            scan_msg.angle_min = angleMin * Mathf.Deg2Rad;
            scan_msg.angle_max = angleMax * Mathf.Deg2Rad;
            scan_msg.angle_increment = angleIncrement * Mathf.Deg2Rad;
            scan_msg.range_min = rangeMin;
            scan_msg.range_max = rangeMax;
            scan_msg.scan_time = 0f;
            scan_msg.time_increment = 0f;
            // scan_msg.scan_time = 1f / scanFrequency;
            // scan_msg.time_increment = scan_msg.scan_time / numRays;
            
            // Perform raycasts and populate ranges array
            scan_msg.ranges = new float[numRays];
            scan_msg.intensities = new float[numRays];

            for (int i = 0; i < numRays; i++)
            {
                float angle = angleMin + (i * angleIncrement);

                // Convert angle to direction (assuming 2D LIDAR in XZ plane)
                Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward;
                // Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;


                RaycastHit hit;
                if (isFaulty == 1 && fault_type == "Pmin") {
                    scan_msg.ranges[i] = 0f;
                } else if (isFaulty == 1 && fault_type == "Pmax") {
                    scan_msg.ranges[i] = rangeMax;
                } else if (isFaulty == 1 && fault_type == "Prnd") {
                    scan_msg.ranges[i] = Random.Range(0f, rangeMax);
                }

                else if (Physics.Raycast(transform.position, direction, out hit, rangeMax)) // if isFaulty = 0 ...
                {
                    scan_msg.ranges[i] = hit.distance;
                    // Debug.Log("Target hit");
                    Debug.DrawRay(transform.position, direction * hit.distance, Color.green, 1f / scanFrequency);
                }
                else
                {
                    scan_msg.ranges[i] = rangeMax;
                    // scan_msg.ranges[i] = hit.distance;
                    Debug.DrawRay(transform.position, direction * rangeMax, Color.red, 1f / scanFrequency);
                }

                scan_msg.intensities[i] = 0f;
            }
            
            
            ros.Publish(rosTopic, scan_msg);
            
            timeElapsed = 0;
        }
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