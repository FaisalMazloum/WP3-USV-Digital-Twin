using System.Collections;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

public class PatternSelector : MonoBehaviour
{
    public enum PatternType
    {
        aggregation_pattern,
        dispersion_pattern,
        minimalist_flocking_pattern,
        random_walk_pattern,
        drive_pattern
    }
    public PatternType selected_pattern;

    public List<string> robots = new List<string> { "0" };



    // ----------------------- //
    //          ROS2           //
    // ----------------------- //
    ROSConnection ros;

    string rosTopic;
    private float timeElapsed;
    
    void Start()
    {
        rosTopic = "/pattern_command";
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(rosTopic);
    }

    // Update is called once per frame
    void Update()
    {
        timeElapsed += Time.deltaTime;

        if (timeElapsed > 1f)
        {
            foreach (string bot in robots) {
                StringMsg pattern_msg = new StringMsg();
                pattern_msg.data = "remora" + bot + ":" + selected_pattern.ToString();
                ros.Publish(rosTopic, pattern_msg);
            }

            timeElapsed = 0;
        }
    }
}
