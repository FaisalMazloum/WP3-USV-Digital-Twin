using System.Collections;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using UnityEngine;

public class PosePublisher : MonoBehaviour
{
    ROSConnection ros;

    public string robotID = "MainRemora";
    public string rosTopic;

    private float timeElapsed;
    
    void Start()
    {
        rosTopic = $"/{robotID}/unity_pose";
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseMsg>(rosTopic);
    }

    // Update is called once per frame
    void Update()
    {
        timeElapsed += Time.deltaTime;

        if (timeElapsed > 1f)
        {

            // PoseMsg cake = new PoseMsg(transform.position.x, transform.position.z, transform.position.y);
            PoseMsg pose_msg = new PoseMsg();
            pose_msg.position.x = transform.position.x;
            pose_msg.position.z = transform.position.z;
            pose_msg.position.y = transform.position.y;

            pose_msg.orientation.z = Quaternion.Euler(transform.rotation.x, transform.rotation.y, transform.rotation.z).y;

            ros.Publish(rosTopic, pose_msg);
            timeElapsed = 0;
        }
    }
}
