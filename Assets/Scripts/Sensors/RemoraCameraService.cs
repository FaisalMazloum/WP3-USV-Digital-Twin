using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

// Exposes /<robot name>/camera/enable (std_srvs/SetBool) so ROS can turn this robot's camera
// sensors on/off. Activates/deactivates every child of Remora_Camera (the sensor + publisher
// objects), so a disabled camera stops rendering and publishing entirely. Remora_Camera's own
// Camera component (the on-screen view) is left to RobotSpawner and RobotFocusUI.
//
// Attach to Remora_Camera. Like RemoraGNSS/RemoraIMU, the namespace comes from transform.root.name
// ("remoraN", set by RobotSpawner right after Instantiate, before Start runs).
//
// Usage: ros2 service call /remora0/camera/enable std_srvs/srv/SetBool "{data: true}"
public class RemoraCameraService : MonoBehaviour
{
    private ROSConnection ros;
    private string robot_id;

    void Start()
    {
        robot_id = transform.root.name;

        ros = ROSConnection.GetOrCreateInstance();
        ros.ImplementService<SetBoolRequest, SetBoolResponse>($"/{robot_id}/camera/enable", HandleSetCamera);
    }

    private SetBoolResponse HandleSetCamera(SetBoolRequest request)
    {
        foreach (Transform child in transform)
            child.gameObject.SetActive(request.data);

        string state = request.data ? "enabled" : "disabled";
        Debug.Log($"[RemoraCameraService] {robot_id} camera {state} ({transform.childCount} child object(s))");
        return new SetBoolResponse(true, $"{robot_id} camera {state}");
    }
}
