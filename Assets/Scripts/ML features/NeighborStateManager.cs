using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using RosMessageTypes.Sensor;
using System;

public class NeighborStateManager : MonoBehaviour
{
    public SimpleDebugController _debugController;
    public ManualController _thrustController;
    public CmdVel_Subscriber _cmdSubscriber;
    public GlobalCSVExporter _csvExporter;
    private RABSensor rabSensor;
    public FaultInjector _faultInjector;

    public bool showRays = false;

    ROSConnection ros;
    public string publishTopic;
    public string robotID;
    private float timeElapsed;
    public float publishInterval = 1f / 10f; // 10 Hz (matches Carminati)

    private float thruster_separation;
    Rigidbody rb;

    [System.Serializable]
    public struct NeighborState
    {
        public GameObject neighborRobot;
        public float distance;           // distance from observed robot to this neighbor
        public float leftThrusterSpeed;  // neighbor's left Thruster speed
        public float rightThrusterSpeed; // neighbor's right Thruster speed

        public NeighborState(GameObject robot, float distance, float leftThrusterSpeed, float rightThrusterSpeed)
        {
            this.neighborRobot = robot;
            this.distance = distance;
            this.leftThrusterSpeed = leftThrusterSpeed;
            this.rightThrusterSpeed = rightThrusterSpeed;
        }
    }

    [System.Serializable]
    public struct ObservedRobotData
    {
        public GameObject observedRobot;
        public float observedLeftSpeed;             // Observed robot's own wheel speed (f1)
        public float observedRightSpeed;            // Observed robot's own wheel speed (f2)
        public float observedAngularAcceleration;   // (rad/s^2)
        public float maxAngularAcceleration;
        public NeighborState[] neighbors;           // List of this robot's neighbors

        public ObservedRobotData(GameObject robot, float leftSpeed, float rightSpeed, float observedAngularAcceleration, float maxAngularAcceleration, NeighborState[] neighbors)
        {
            this.observedRobot = robot;
            this.observedLeftSpeed = leftSpeed;
            this.observedRightSpeed = rightSpeed;
            this.observedAngularAcceleration = observedAngularAcceleration;
            this.maxAngularAcceleration = maxAngularAcceleration;
            this.neighbors = neighbors;
        }
    }

    [System.Serializable]
    public struct ThrusterState
    {
        public float leftThrusterSpeed_initial;
        public float rightThrusterSpeed_initial;
        public float leftThrusterSpeed;
        public float rightThrusterSpeed;
        public float timeStamp_initial;  // sender's publish time (sec) for the "_initial" reading
        public float timeStamp;          // sender's publish time (sec) for the current reading

        public ThrusterState(float leftThrusterSpeed, float rightThrusterSpeed, float timeStamp)
        {
            this.leftThrusterSpeed_initial = leftThrusterSpeed;
            this.rightThrusterSpeed_initial = rightThrusterSpeed;
            this.leftThrusterSpeed = leftThrusterSpeed;
            this.rightThrusterSpeed = rightThrusterSpeed;
            this.timeStamp_initial = timeStamp;
            this.timeStamp = timeStamp;
        }
    }

    // private Dictionary<String, List<String>> subscriber_dict = new Dictionary<String, List<String>>();
    private List<String> subscriber_list = new List<String>();

    // Store ALL received wheel speeds (not filtered by RAB yet)
    private Dictionary<string, ThrusterState> allThrusterSpeeds = new Dictionary<string, ThrusterState>();
    // Store complete observed robot data (filtered by RAB)
    private Dictionary<GameObject, ObservedRobotData> allObservedRobotStates = new Dictionary<GameObject, ObservedRobotData>();

    public float max_range; // RAB MAX RANGE
    public float max_speed;
    public float max_omega;

    public bool print_button = false;
    public bool is_publishingJointStates = false;
    
    void Start()
    {
        rb = transform.root.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError($"{robotID}: No RigidBody found!");
        }

        if (_cmdSubscriber == null)
        {
            Debug.LogError($"{robotID}: No cmdSubscriber found! Cannot obtain max speed parameter");
        }

        if (_thrustController == null)
        {
            Debug.LogError($"{robotID}: No thrustController found! Cannot obtain max speed parameter");
        }

        // _debugController = transform.root.GetComponent<SimpleDebugController>();
        if (_debugController == null)
        {
            Debug.LogError($"{robotID}: No DebugController found! Cannot obtain max speed parameter");
        }

        rabSensor = GetComponentInChildren<RABSensor>();
        if (rabSensor == null)
        {
            Debug.LogError($"{robotID}: No RABSensor found! Cannot filter neighbors.");
        }

        if (_csvExporter == null)
        {
            Debug.LogError($"{transform.root.name}: spawner not found!");
        }

        if (_faultInjector == null)
        {
            Debug.LogError($"{transform.root.name}: FaultInjector not found!");
        }

        robotID = transform.root.name;
        publishTopic = $"/{robotID}/joint_states";
        
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(publishTopic);

        timeElapsed = 0f;
        
        // max_speed = _debugController.moveSpeed;
        // max_omega = _debugController.rotateSpeed;
        // max_speed = _thrustController.max_speed;
        // max_omega = _thrustController.max_omega;
        
        thruster_separation = _cmdSubscriber.thruster_separation;
        max_range = rabSensor.GetMaxRange();
        max_speed = 0.3f;
        max_omega = 90f;
    }
    
    void FixedUpdate()
    {
        if(_csvExporter._start)
        {
            timeElapsed += Time.fixedDeltaTime;
            is_publishingJointStates = false;
            
            while (timeElapsed >= publishInterval)
            {
                is_publishingJointStates = true;
                PublishJointState();
                timeElapsed -= publishInterval;
            }
        }
    }

    void PublishJointState()
    {
        // float localVelocity = _cmdSubscriber.getTargetVelocities()["linear"];     // check target linear speed
        // float localOmega = _cmdSubscriber.getTargetVelocities()["angular"] * Mathf.Deg2Rad;      // check target angular speed
        // float localVelocity = _thrustController.getTargetVelocities()["linear"];     // check target linear speed
        // float localOmega = _thrustController.getTargetVelocities()["angular"] * Mathf.Deg2Rad;      // check target angular speed

        // ------------------------------- USE REAL VELOCITY AND OMEGA ------------------------------- //
        float localVelocity = Vector3.Dot(rb.velocity, transform.root.forward);
        float localOmega = Vector3.Dot(rb.angularVelocity, transform.root.up);
        double left_thruster_velocity = (localVelocity - (localOmega * thruster_separation / 2f)) * 100.0;  // (cm/s)
        double right_thruster_velocity = (localVelocity + (localOmega * thruster_separation / 2f)) * 100.0;

        // ------------------------------- USE TARGET (subscribed) VELOCITY AND OMEGA ------------------------------- //
        // double left_thruster_velocity = _cmdSubscriber.getTargetVelocities()["left_thruster"];
        // double right_thruster_velocity = _cmdSubscriber.getTargetVelocities()["right_thruster"];

        // if (transform.root.name == "remora0") {
        //     Debug.Log($"Left: {left_thruster_velocity}, Right: {right_thruster_velocity}, Seperation: {thruster_separation}");
        //     Debug.Log($"Actual Omega: {Vector3.Dot(rb.angularVelocity, transform.root.up)}, Actual Velocity: {Vector3.Dot(rb.velocity, transform.root.forward)}");
        // }

        JointStateMsg jointState_msg = new JointStateMsg
        {
            header = new HeaderMsg
            {
                frame_id = robotID,
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg
                {
                    sec = (int)Time.fixedTime,
                    nanosec = (uint)((Time.fixedTime - Mathf.Floor(Time.fixedTime)) * 1e9)
                }
            },
            name = new string[] { "left_thruster_joint", "right_thruster_joint" },
            velocity = new double[] { left_thruster_velocity, right_thruster_velocity },
            position = new double[] { },
            effort = new double[] { }
        };

        ros.Publish(publishTopic, jointState_msg);
    }

    public void CreateSubscribers()
    {
        GameObject[] allRobots = GameObject.FindGameObjectsWithTag("remora_bot");
        int subscribedCount = 0;
        subscriber_list.Clear();

        foreach (var robot in allRobots)
        {
            string neighborID = robot.name;

            // Skip self
            if (neighborID == robotID)
                continue;

            // ros.Unsubscribe($"/{neighborID}/joint_states");
            ros.Subscribe<JointStateMsg>($"/{neighborID}/joint_states", jointstateCallback);
            // Debug.Log($"{robotID}: Subscribed to /{neighborID}/joint_states");
            
            subscriber_list.Add(neighborID);
            subscribedCount++;
        }

        // Debug.Log($"{robotID}: Subscribed to {subscriber_list.Count} robots");
    }

    void jointstateCallback(JointStateMsg msg)
    {
        string neighborRobotID = msg.header.frame_id;
    
        if (msg.velocity.Length >= 2)
        {
            float f1 = (float)msg.velocity[0];
            float f2 = (float)msg.velocity[1];
            float msgTime = msg.header.stamp.sec + msg.header.stamp.nanosec * 1e-9f; // sender's publish time

            if (!allThrusterSpeeds.ContainsKey(neighborRobotID))
            {
                // First observation: initialize with current values
                allThrusterSpeeds[neighborRobotID] = new ThrusterState(f1, f2, msgTime);
            }
            else
            {
                // Update: shift current to initial, then update current
                ThrusterState ts = allThrusterSpeeds[neighborRobotID];
                ts.leftThrusterSpeed_initial = ts.leftThrusterSpeed;   // Store previous left
                ts.rightThrusterSpeed_initial = ts.rightThrusterSpeed; // Store previous right
                ts.timeStamp_initial = ts.timeStamp;                   // Store previous timestamp
                ts.leftThrusterSpeed = f1;                             // Update to new left
                ts.rightThrusterSpeed = f2;                            // Update to new right
                ts.timeStamp = msgTime;                                // Update to new timestamp
                allThrusterSpeeds[neighborRobotID] = ts;               // Write back (struct is value type)
            }

            // if(transform.root.name == "remora1" && neighborRobotID == "remora0")
            // {
            //     LoggerFunc(1, neighborRobotID);
            // }
        }
    
        CacheNeighborState();
    }

    void CacheNeighborState()
    {
        if (rabSensor == null)
            return;

        allObservedRobotStates.Clear();
        var allDetections = rabSensor.GetDetections();
        
        // Debug.LogWarning($"{Time.fixedTime} - {transform.root.name} has {allDetections.Keys.Count} keys");

        if (allDetections.Count == 0)
            return;

        float thruster_separation_cm = thruster_separation * 100f; // cm

        foreach (var observedRobotKvp in allDetections)
        {
            GameObject observedRobot = observedRobotKvp.Key;
            RABSensor.RABDetection observedDetection = observedRobotKvp.Value;
            
            // Get observed robot's wheel speeds and compute angular acceleration
            float observedLeftSpeed = 0f;
            float observedRightSpeed = 0f;
            float observedAngularAcceleration = 0f;
            float maxAngularAcceleration = 0f;
            
            if (allThrusterSpeeds.ContainsKey(observedRobot.name))
            {
                ThrusterState ts = allThrusterSpeeds[observedRobot.name];

                observedLeftSpeed = ts.leftThrusterSpeed;
                observedRightSpeed = ts.rightThrusterSpeed;
    
                // Compute angular velocities (rad/s)
                float observedRobotOmega = (ts.rightThrusterSpeed - ts.leftThrusterSpeed) / thruster_separation_cm;
                float observedRobotOmega_initial = (ts.rightThrusterSpeed_initial - ts.leftThrusterSpeed_initial) / thruster_separation_cm;
                // Debug.Log("Omega = " + observedRobotOmega + "   ||   Omega_initial = " + observedRobotOmega_initial);

                // Use the actual measured time between readings (not the nominal publishInterval) so
                // a delayed or dropped joint_state message doesn't silently skew the acceleration estimate.
                float dt = ts.timeStamp - ts.timeStamp_initial;
                if (dt <= 0f)
                    dt = publishInterval; // first observation for this neighbor: no prior reading yet

                // Compute angular acceleration (rad/s^2)
                observedAngularAcceleration = (observedRobotOmega - observedRobotOmega_initial) / dt;
                // maxAngularAcceleration = (2*max_omega * Mathf.Deg2Rad) / dt; // max_omega*2 due to change in direction (instead of accelerating from 0)
                maxAngularAcceleration = (max_omega * Mathf.Deg2Rad) / dt;


                // if (transform.root.name == "remora2" && observedRobot.name == "remora0")
                //     Debug.Log($"Actual Ang Acc: {observedAngularAcceleration} || Max Ang Acc: {maxAngularAcceleration}");
            }
            
            // Build list of valid neighbors
            List<NeighborState> validNeighbors = new List<NeighborState>();
            
            foreach (var neighborKvp in allDetections)
            {
                GameObject neighborRobot = neighborKvp.Key;
                RABSensor.RABDetection neighborDetection = neighborKvp.Value;
                
                // Skip if same as observed robot
                if (neighborRobot == observedRobot)
                    continue;
                
                // Skip if no wheel speed data available
                if (!allThrusterSpeeds.ContainsKey(neighborRobot.name))
                    continue;

                // Compute distance from observed robot to this neighbor
                float distance = ComputeDistanceBetween(observedDetection, neighborDetection);
                
                // Skip if too far (not actually a neighbor)
                if (distance > max_range)
                    continue;
                
                // Get neighbor's wheel speeds
                float neighborLeftSpeed = allThrusterSpeeds[neighborRobot.name].leftThrusterSpeed;
                float neighborRightSpeed = allThrusterSpeeds[neighborRobot.name].rightThrusterSpeed;
                
                // Add to valid neighbors list
                validNeighbors.Add(new NeighborState(
                    neighborRobot,
                    distance,
                    neighborLeftSpeed,
                    neighborRightSpeed
                ));
            }
            
            // Convert list to array
            NeighborState[] neighbors = validNeighbors.ToArray();
            
            // Create observed robot data
            ObservedRobotData observedData = new ObservedRobotData(
                observedRobot,
                observedLeftSpeed,
                observedRightSpeed,
                observedAngularAcceleration,
                maxAngularAcceleration,
                neighbors
            );
            
            // Store in main dictionary
            allObservedRobotStates[observedRobot] = observedData;

            // if(transform.root.name == "remora1" && observedRobot.name == "remora2")
            {
                // Debug.Log($"{Time.fixedTime} - {transform.root.name} cached neighbor {observedRobot.name}...");
            }
        }

        // Debug.LogWarning($"{Time.fixedTime} - {transform.root.name} has {allObservedRobotStates.Keys.Count} keys");
    }

    float ComputeDistanceBetween(RABSensor.RABDetection robotA, RABSensor.RABDetection robotB)
    {
        float d_A = robotA.distance;
        float d_B = robotB.distance;
        float angle = Mathf.Abs(robotA.bearing - robotB.bearing) * Mathf.Deg2Rad;
        
        // Law of Cosines
        float distSq = d_A * d_A + d_B * d_B - 2f * d_A * d_B * Mathf.Cos(angle);
        
        // Handle numerical precision issues
        if (distSq < 0f)
            distSq = 0f;
        
        return Mathf.Sqrt(distSq);
    }

    void PrintNeighborStates()
    {
        Debug.Log($"========={transform.root.name} Observing {allObservedRobotStates.Count} robots=========");

        foreach (var observedRobot in allObservedRobotStates)
        {
            var data = observedRobot.Value;
            Debug.Log($"Observed: {data.observedRobot.name} | " +
                     $"f1={data.observedLeftSpeed:F2} cm/s, f2={data.observedRightSpeed:F2} cm/s | " +
                     $"AngAccel={data.observedAngularAcceleration:F3} rad/s^2 | " +
                     $"{data.neighbors.Length} neighbors");

            foreach (var neighbor in data.neighbors)
            {
                Debug.Log($"  --> Neighbor: {neighbor.neighborRobot.name} at {neighbor.distance:F2}m");
            }
        }
    }

    void LoggerFunc(int i, string neighborName = "N.A.")
    {
        if (i == 0)
        {
            // Debug.Log($"{Time.fixedTime} - {transform.root.name} published joint states...");
        }

        else if (i == 1)
        {
            // Debug.Log($"{Time.fixedTime} - {transform.root.name} subscriber called for {neighborName}...");
        }
    }



    // private void OnDrawGizmos()
    // {
    //     if (Application.isPlaying)
    //     {
    // #if UNITY_EDITOR
    //         float RUL_value = 100f;
    //         // 1. Create a custom style
    //         GUIStyle style = new GUIStyle();
    //         style.fontSize = 8;
    //         style.alignment = TextAnchor.LowerLeft;
    //         style.fontStyle = FontStyle.Bold;
            
    //         if (!_faultInjector.faulty_robots.Contains(robotID))
    //         {
    //             style.normal.textColor = Color.green;
    //         }
    //         else
    //         {
    //             if (_faultInjector.selectedFault == FaultInjector.FaultType.IntermittentMotor && _faultInjector.injectFault)
    //             {
    //                 style.normal.textColor = Color.red;
    //             }
    //             else
    //             {
    //                 float max_RUL = 100f;
    //                 RUL_value = _cmdSubscriber.GetRULData()["RUL"];
    //                 if (!_faultInjector.injectFault)
    //                     RUL_value = 100;
    //                 float t = Mathf.Clamp01(RUL_value / max_RUL);
    //                 style.normal.textColor = Color.Lerp(Color.red, Color.green, t);
    //             }
    //         }
            
    //         // 3. Apply the style to the label
    //         UnityEditor.Handles.Label(
    //             transform.position + Vector3.forward * 0.25f, 
    //             $"r{robotID.Substring(6)} ___ RUL = {RUL_value}: Subscribed ({subscriber_list.Count})", 
    //             style
    //         );


    //         if (showRays)
    //         {
    //             Gizmos.color = new Color(0, 1, 0, 0.3f); 
    //             var detectedRobots = rabSensor.GetDetections();
    //             foreach (var robot in detectedRobots)
    //                 Gizmos.DrawLine(transform.root.position, robot.Key.transform.root.position);
    //         }

    // #endif
    //     }
    // }





    /* ===================================================== */
    /* ======================== API ======================== */
    /* ===================================================== */

    /// <summary>
    /// Get all observed robot states. Returns a dictionary of observed robots whose values are its observed neighbors' data.
    /// </summary>
    public Dictionary<GameObject, ObservedRobotData> GetAllObservedRobotStates()
    {
        return new Dictionary<GameObject, ObservedRobotData>(allObservedRobotStates);
    }

    /// <summary>
    /// Resubscribe. Use when new batches of robots are spawned, and need to subscribe to neighbor data.
    /// </summary>
    public void RefreshSubscriptions()
    {
        CreateSubscribers();
    }

    /// <summary>
    /// Get Subscribers list.
    /// </summary>
    public List<string> GetSubscribers()
    {
        return new List<string>(subscriber_list);
    }
}