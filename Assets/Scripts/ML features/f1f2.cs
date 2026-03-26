using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using RosMessageTypes.Sensor;

public class f1f2 : MonoBehaviour
{
    ROSConnection ros;
    public string publishTopic;
    public string robotID;
    private float timeElapsed;
    private float publishFrequency = 10f;
    
    public float thruster_separation = 0.3f;
    Rigidbody rb;

    private Dictionary<string, float[]> allThrusterSpeeds = new Dictionary<string, float[]>();
    private Dictionary<string, float[]> neighborThrusterSpeeds = new Dictionary<string, float[]>();

    // Reference to RAB sensor for neighbor filtering
    RABSensor rabSensor;

    public bool print_button = false;
    public bool registerSubscribers = false;
    private bool hasSubscribed = false; // Prevent multiple subscriptions
    
    void Start()
    {
        rb = transform.root.GetComponent<Rigidbody>();
        robotID = transform.root.name;
        publishTopic = $"/{robotID}/joint_states";
        
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(publishTopic);
        
        // Get RAB sensor reference
        rabSensor = GetComponentInChildren<RABSensor>();
        if (rabSensor == null)
        {
            Debug.LogError($"{robotID}: No RABSensor found! Cannot filter neighbors.");
        }
    }
    
    void Update()
    {
        timeElapsed += Time.deltaTime;
        if (timeElapsed >= publishFrequency)
        {
            PublishJointState();
            getNeighborThrusterSpeeds();
            timeElapsed = 0;
        }


        if (registerSubscribers && !hasSubscribed)
        {
            CreateSubscribers();
            hasSubscribed = true;
            registerSubscribers = false;
        }

        if (print_button)
        {
            foreach (var neighbor_state in neighborThrusterSpeeds)
            {
                Debug.Log($"{transform.root.name} detected {neighbor_state.Key}: f1={neighbor_state.Value[0]:F2}cm/s, f2={neighbor_state.Value[1]:F2}cm/s");
            }
            print_button = false;
        }
    }
    
    void PublishJointState()
    {
        float localVelocity = Vector3.Dot(rb.velocity, transform.root.forward);
        float localOmega = Vector3.Dot(rb.angularVelocity, transform.root.up);
        
        double left_thruster_velocity = (localVelocity - (localOmega * thruster_separation / 2f)) * 100.0;  // cm/s
        double right_thruster_velocity = (localVelocity + (localOmega * thruster_separation / 2f)) * 100.0; // cm/s
        
        JointStateMsg jointState_msg = new JointStateMsg
        {
            header = new HeaderMsg
            {
                frame_id = robotID,
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg
                {
                    sec = (int)Time.time,
                    nanosec = (uint)((Time.time - Mathf.Floor(Time.time)) * 1e9)
                }
            },
            name = new string[] { "left_thruster_joint", "right_thruster_joint" },
            velocity = new double[] { left_thruster_velocity, right_thruster_velocity },
            position = new double[] { },
            effort = new double[] { }
        };
        
        ros.Publish(publishTopic, jointState_msg);
    }
    
    void jointstateCallback(JointStateMsg msg)
    {
        string neighborID = msg.header.frame_id;
        
        if (msg.velocity.Length >= 2)
        {
            float f1 = (float)msg.velocity[0];
            float f2 = (float)msg.velocity[1];
            
            // Store ALL received speeds (filtering happens in API call)
            allThrusterSpeeds[neighborID] = new float[] { f1, f2 };
        }
    }

    void CreateSubscribers()
    {
        int index = 0;

        foreach (var item in GameObject.FindGameObjectsWithTag("remora_bot"))
        {
            string neighborID = $"remora{index}";

            if (neighborID != robotID)
            {
                ros.Subscribe<JointStateMsg>($"/{neighborID}/joint_states", jointstateCallback);
                Debug.Log($"{robotID}: Subscribed to /{neighborID}/joint_states");
            }

            index++;
        }
    }

    void getNeighborThrusterSpeeds()
    {
        neighborThrusterSpeeds.Clear();
        foreach (var kvp in rabSensor.GetDetections())
        {
            if (allThrusterSpeeds.ContainsKey(kvp.Key.name))
            {
                neighborThrusterSpeeds[kvp.Key.name] = allThrusterSpeeds[kvp.Key.name];
                // Debug.Log("Added state.");
            }
        }
    }
    
        // private Dictionary<string, float[]> neighborThrusterSpeeds = new Dictionary<string, float[]>();

}