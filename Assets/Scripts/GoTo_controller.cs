using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

public class GoTo_Controller : MonoBehaviour
{
    [Header("References")]
    public Rigidbody boatRigidbody;
    
    [Header("ROS Settings")]
    public string twistTopic = "";
    
    [Header("Control Settings")]
    public float maxLinearVelocity = 5.0f;
    public float maxAngularVelocity = 2.0f;
    
    [Header("Physics Settings")]
    public float linearAcceleration = 2.0f;
    public float angularAcceleration = 3.0f;
    public float linearDrag = 0.5f;
    public float angularDrag = 1.0f;
    
    private ROSConnection ros;
    private Vector3 targetVelocity = Vector3.zero;
    private float targetAngularVelocity = 0f;

    private Vector3 targetVelocity111 = Vector3.zero;
    private float targetAngularVelocity111 = 0f;

    private Vector3 initial_pose;


    
    void Start()
    {

        string robot_id = transform.name;
        twistTopic = $"/{robot_id}/twist"; 
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<TwistMsg>(twistTopic, ReceiveTwist);
        
        if (boatRigidbody == null)
        {
            boatRigidbody = GetComponentInChildren<Rigidbody>();
            
            if (boatRigidbody == null)
            {
                Debug.LogError("BoatVelocityController: No Rigidbody found!");
                enabled = false;
                return;
            }
        }
        

        boatRigidbody.drag = linearDrag;
        boatRigidbody.angularDrag = angularDrag;

        initial_pose = transform.position;
        // initial_pose = new Vector3(transform.position.x, 0f, transform.position.y);

        
        // Don't change useGravity - let other scripts handle it
        // boatRigidbody.constraints = RigidbodyConstraints.FreezeRotationX | 
                                    //  RigidbodyConstraints.FreezeRotationZ;
        
        Debug.Log($"BoatVelocityController: Subscribed to {twistTopic}");
    }
    
    void ReceiveTwist(TwistMsg msg)
    {
        float rosLinearX = (float)msg.linear.x;
        float rosLinearY = (float)msg.linear.y * -1f; // invert sign since unity Y is opposite.
        float rosAngularZ = (float)msg.angular.z;
        
        targetVelocity = new Vector3(rosLinearY, 0, rosLinearX); //transform conversion. ROS2-Z = Unity-Y,  ROS2-Y = Unity-X.
        targetAngularVelocity = rosAngularZ;

        targetVelocity111 = new Vector3(rosLinearY, 0, rosLinearX); //transform conversion. ROS2-Z = Unity-Y,  ROS2-Y = Unity-X.
        targetAngularVelocity111 = rosAngularZ;

        targetVelocity = Vector3.ClampMagnitude(targetVelocity, maxLinearVelocity);
        targetAngularVelocity = Mathf.Clamp(targetAngularVelocity, -maxAngularVelocity, maxAngularVelocity);
    }
    
    void FixedUpdate()
    {
        if (boatRigidbody == null) return;
        
        // if (float.IsNaN(targetAngularVelocity))
            // targetAngularVelocity = 0f;
        // if (float.IsNaN(targetVelocity.x) || float.IsNaN(targetVelocity.z))
            // targetVelocity = Vector3.zero;
        
        ApplyLinearVelocity();
        ApplyAngularVelocity();
    }
    
    void ApplyLinearVelocity()
    {
        // Get current velocity in local space
        Vector3 currentLocalVelocity = transform.InverseTransformDirection(boatRigidbody.velocity);
        // Vector3 currentLocalVelocity = boatRigidbody.velocity;
    
        Vector3 currentHorizontalVelocity = new Vector3(currentLocalVelocity.x, 0, currentLocalVelocity.z);
        
        // Calculate error only in horizontal plane
        Vector3 velocityError = targetVelocity - currentHorizontalVelocity;
        Vector3 acceleration = velocityError * linearAcceleration;

        // Convert to world space
        Vector3 force = transform.TransformDirection(acceleration) * boatRigidbody.mass;
        // Vector3 force = acceleration * boatRigidbody.mass;
        
        boatRigidbody.AddForce(force, ForceMode.Force);
    }
    
    void ApplyAngularVelocity()
    {
        float currentAngularVelocity = boatRigidbody.angularVelocity.y;
        float angularError = targetAngularVelocity - currentAngularVelocity;
        
        float angularAccel = angularError * angularAcceleration;
        
        Vector3 torque = new Vector3(0, angularAccel, 0) * boatRigidbody.mass;
        
        boatRigidbody.AddTorque(torque, ForceMode.Force);
    }
     
    void OnDrawGizmos()
    {
        if (this.enabled)
        {
            Vector3 gizmos_offset = new Vector3(0f, 6f, 0f);
            Vector3 local_gizmos_offset = transform.TransformDirection(gizmos_offset);
            
            // target velocity of robot
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position + local_gizmos_offset, transform.TransformDirection(targetVelocity * 2));
            
            // current velocity of robot
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position + local_gizmos_offset, boatRigidbody.velocity * 2);

            // Forward vector of robot
            // Gizmos.color = Color.red;
            // Gizmos.DrawRay(transform.position + local_gizmos_offset, transform.forward * 2f);
        }
    }
}