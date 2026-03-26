using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class UnityThrustController : MonoBehaviour
{
    [SerializeField]
    private Rigidbody rb;

    [Header("Thrusters")]
    [Tooltip("Drag and Drop left and right thrusters")]
    public Transform rightThruster;
    public Transform leftThruster;

    public float thrustForce = 5000f;

    [Header("Physics Settings")]
    [Tooltip("Linear speed (m/s)")]
    public float desiredSpeed = 0.5f;


    [Tooltip("Angular speed (degrees/sec)")]
    public float desiredOmega = 90f;

    public float accelerationFactor = 50f;

    public float linearDrag = 0.5f;
    public float angularDrag = 0.5f;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("No rigidBody attached!");
        }

        rb.drag = linearDrag;
        rb.angularDrag = angularDrag;
        // rb.useGravity = true;
    }

    void FixedUpdate()
    {
        calculateForce();
        calculateTorque();
    }



    void calculateForce()
    {
        float currentSpeed = transform.InverseTransformDirection(rb.velocity).z; // get local Z velocity, not global.
        float speedError = 0f;

        // Apply thrust force in local forward direction
        if (Input.GetKey(KeyCode.UpArrow))
        {
            float targetSpeed = desiredSpeed;
            speedError = targetSpeed - currentSpeed;
            Debug.Log($"Desired Speed: {targetSpeed}");
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            float targetSpeed = -desiredSpeed;
            speedError = targetSpeed - currentSpeed;
            Debug.Log($"Desired Speed: {targetSpeed}");
        }

        Debug.Log($"Current Speed: {currentSpeed}");
        Debug.Log($"Error: {speedError}");
        Debug.Log($"Control signal: {speedError * accelerationFactor}");

        speedError = Mathf.Clamp(speedError, -desiredSpeed, desiredSpeed);
        float targetThrust = speedError * accelerationFactor;

        rb.AddForceAtPosition(transform.forward * targetThrust, leftThruster.position, ForceMode.Acceleration); // left thruster
        rb.AddForceAtPosition(transform.forward * targetThrust, rightThruster.position, ForceMode.Acceleration); // right thruster
    }



    void calculateTorque()
    {
        float currentOmega = transform.InverseTransformDirection(rb.angularVelocity).y;
        float desiredOmega_rad = desiredOmega * Mathf.Deg2Rad;
        float omegaError = 0f;

        if (Input.GetKey(KeyCode.RightArrow))
        {
            float targetOmega = desiredOmega_rad;
            omegaError = targetOmega - currentOmega;
            Debug.Log($"Desired Speed: {desiredOmega_rad}");
            Debug.Log($"Current Speed: {currentOmega}");
            Debug.Log($"Error: {omegaError}");
            Debug.Log($"Control signal: {omegaError * accelerationFactor}");

        }
        else if (Input.GetKey(KeyCode.LeftArrow))
        {
            float targetOmega = -desiredOmega_rad;
            omegaError = targetOmega - currentOmega;
            Debug.Log($"Desired Speed: {targetOmega}");
            Debug.Log($"Current Speed: {currentOmega}");
            Debug.Log($"Error: {omegaError}");
            Debug.Log($"Control signal: {omegaError * accelerationFactor}");
        }

        rb.AddForceAtPosition(-transform.forward * omegaError * accelerationFactor, leftThruster.position, ForceMode.Acceleration); // left thruster
        rb.AddForceAtPosition(transform.forward * omegaError * accelerationFactor, rightThruster.position, ForceMode.Acceleration); // right thruster
    }


    void OnDrawGizmos() {
        // if (Application.isPlaying) {
            Rigidbody rb = GetComponent<Rigidbody>();
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.TransformPoint(rb.centerOfMass), 0.2f);
        // }
}
}

