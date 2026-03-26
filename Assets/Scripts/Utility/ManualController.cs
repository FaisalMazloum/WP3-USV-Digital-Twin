using UnityEngine;
using NWH.DWP2.WaterObjects;
using System;
using UnityEngine.Pool;
using System.Collections.Generic;


public class ManualController : MonoBehaviour
{
    private Rigidbody rb;

    // [SerializeField]
    private WaterObject rightWaterObject;
    // [SerializeField]
    private WaterObject leftWaterObject;

    private int Lmotor_state = 1;
    private int Rmotor_state = 1;
    private int keyPressed = 0;

    [Header("Thrusters")]
    // [SerializeField]
    private Transform rightThruster;
    // [SerializeField]
    private Transform leftThruster;

    [Header("Desired Velocities")]
    private float desiredSpeed = 0.5f;
    private float desiredOmega = 60f;
    public float max_speed = 0.5f;
    public float max_omega = 60f;


    [Header("PID Gains")]
    public float Kp = 10f;
    public float Ki = 5f;
    public float Kd = 0f;
    public float integralClamp = 10f;
    public float maxForce = 20f;

    private float rightIntegral = 0f;
    private float rightPreviousError = 0f;
    private float leftIntegral = 0f;
    private float leftPreviousError = 0f;

    public string robot_name = "MainRemora";

    void Start()
    {
        InitializeRigidBody();

        if (rb == null)
            Debug.LogError("Rigidbody component missing on " + gameObject.name);

        if (leftWaterObject == null || rightWaterObject == null)
            Debug.LogError("WaterObject component missing on " + gameObject.name + " - Thrusters will not work!");
    }


    void FixedUpdate()
    {
        if (Input.GetKey(KeyCode.UpArrow)) desiredSpeed = max_speed;
        if (Input.GetKey(KeyCode.DownArrow)) desiredSpeed = -max_speed;
        if (Input.GetKey(KeyCode.RightArrow)) desiredOmega = -max_omega;
        if (Input.GetKey(KeyCode.LeftArrow)) desiredOmega = max_omega;
        keyPressed = 1;

        // if no key pressed, then turn off PID
        if (!Input.GetKey(KeyCode.UpArrow)
            && !Input.GetKey(KeyCode.DownArrow)
            && !Input.GetKey(KeyCode.LeftArrow)
            && !Input.GetKey(KeyCode.RightArrow))
        {
            keyPressed = 0;
            rightIntegral = 0;
            leftIntegral = 0;
            rightPreviousError = 0;
            leftPreviousError = 0;
            desiredSpeed = 0f;
            desiredOmega = 0f;
            // Debug.Log("No key pressed");
        }

        float target_velocity = desiredSpeed;
        float target_omega = desiredOmega;

        float thruster_separation = Vector3.Distance(leftThruster.position, rightThruster.position);
        float target_omega_rad = target_omega * Mathf.Deg2Rad;

        float v_right_target = target_velocity + (target_omega_rad * thruster_separation) / 2f;
        float v_left_target = target_velocity - (target_omega_rad * thruster_separation) / 2f;

        Vector3 rightVel = rb.GetPointVelocity(rightThruster.position);
        Vector3 leftVel = rb.GetPointVelocity(leftThruster.position);

        float v_right_current = Vector3.Dot(rightVel, rb.transform.forward);
        float v_left_current = Vector3.Dot(leftVel, rb.transform.forward);

        float rightForce = CalculatePID(v_right_target, v_right_current, ref rightIntegral, ref rightPreviousError);
        float leftForce = CalculatePID(v_left_target, v_left_current, ref leftIntegral, ref leftPreviousError);

        // Debug.Log($"RightSpeed: {v_right_current} || RightForce: {rightForce}");
        // Debug.Log($"LeftSpeed: {v_left_current} || LeftForce: {leftForce}");


        // Only apply thruster forces when USV is touching water
        if (rightWaterObject != null && keyPressed == 1)
        // if (rightWaterObject != null && rightWaterObject.IsTouchingWater() && keyPressed == 1)
        {
            // Debug.Log("Right thruster ready!");
            rb.AddForceAtPosition(rb.transform.forward * rightForce * Rmotor_state, rightThruster.position, ForceMode.Acceleration);
        }
        if (leftWaterObject != null && keyPressed == 1)
        // if (leftWaterObject != null && leftWaterObject.IsTouchingWater() && keyPressed == 1)
        {
            // Debug.Log("Left thruster ready!");
            rb.AddForceAtPosition(rb.transform.forward * leftForce * Lmotor_state, leftThruster.position, ForceMode.Acceleration);
        }

    }
    

    public void InitializeRigidBody()
    {
        // rb = GameObject.Find("V3 - REMORA bot").GetComponent<Rigidbody>();
        rb = GameObject.Find(robot_name).GetComponent<Rigidbody>();
        rightThruster = rb.transform.Find("motor right").transform;
        leftThruster = rb.transform.Find("motor left").transform;
        rightWaterObject = rightThruster.GetComponentInChildren<WaterObject>();
        leftWaterObject = leftThruster.GetComponentInChildren<WaterObject>();
    }




    private float CalculatePID(float target, float current, ref float integral, ref float previousError)
    {
        float dt = Time.fixedDeltaTime;
        float error = (target - current) * keyPressed;

        float P = Kp * error;

        integral += error * dt;
        integral = Mathf.Clamp(integral, -integralClamp, integralClamp); // Anti-windup
        float I = Ki * integral;

        float D = Kd * (error - previousError) / dt;
        previousError = error;

        float output = P + D + I;
        output = Mathf.Clamp(output, -maxForce, maxForce);

        return output;
    }

    void OnDrawGizmos()
    {
        if (rb != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(rb.transform.TransformPoint(rb.centerOfMass), 0.02f);

            Gizmos.color = Color.red;
            Gizmos.DrawLine(rightThruster.position, rightThruster.position + rightThruster.forward * Rmotor_state);
            Gizmos.DrawLine(leftThruster.position, leftThruster.position + leftThruster.forward * Lmotor_state);
        }
    }





    /* ===================================================== */
    /* ======================== API ======================== */
    /* ===================================================== */

    /// <summary> Sets the left and right motor states, i.e., turn ON (if 1) the motor. </summary>
    public void SetMotorStates(int left_fault = 0, int right_fault = 0)
    {
        Lmotor_state = -left_fault + 1; // if fault = 1, then motor state = 0 = OFF
        Rmotor_state = -right_fault + 1;
    }

    public Dictionary<string, float> getTargetVelocities()
    {
        Dictionary<string, float> velocities = new Dictionary<string, float>();
        velocities["linear"] = desiredSpeed;
        velocities["angular"] = desiredOmega;

        return velocities;
    }
}