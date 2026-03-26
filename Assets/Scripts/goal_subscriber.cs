using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;


public class goal_subscriber : MonoBehaviour
{

    private ROSConnection ros;
    public string ros2_topic = "";
    public Vector3 goal_pose;

    void Start()
    {
        string robot_id = transform.name;
        ros2_topic = $"/{robot_id}/goal_pose"; 

        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<OdometryMsg>(ros2_topic, Callback);
    }


    void Callback(OdometryMsg msg)
    {
        goal_pose.x = (float)msg.pose.pose.position.x;
        goal_pose.z = (float)msg.pose.pose.position.y;
        goal_pose.y = 0.0f;
        // Debug.Log($"Goals X: {goal_pose.x} || Goals Y: {goal_pose.y} || Goals Z: {goal_pose.z}");
    }


    void OnDrawGizmos()
    {
        if (this.enabled)
        {
            Vector3 gizmos_offset = new Vector3(0f, 6f, 0f);
            Vector3 local_gizmos_offset = transform.TransformDirection(gizmos_offset);
            // Vector3 gizmos_offset = Vector3.zero;
            // Vector3 goal_pose = initial_pose + gizmos_offset + Vector3.forward * gs.goal_pose.z + Vector3.right * gs.goal_pose.x;
            Vector3 goal_point = Vector3.forward * goal_pose.z + Vector3.right * goal_pose.x;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position + local_gizmos_offset, goal_pose);
            Gizmos.DrawSphere(goal_point, 1f);
        }
    }
}