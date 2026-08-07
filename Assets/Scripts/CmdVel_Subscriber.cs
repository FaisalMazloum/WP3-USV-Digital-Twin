using UnityEngine;
using NWH.DWP2.WaterObjects;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using System.Collections.Generic;
using System;


public class CmdVel_Subscriber : MonoBehaviour
{
    [SerializeField]
    private ROSConnection ros;

    private Rigidbody rb;

    // [SerializeField]
    private WaterObject rightWaterObject;
    // [SerializeField]
    private WaterObject leftWaterObject;
    [SerializeField]
    private AdaptiveSamplingPattern _AdaptiveSampler;

    private int Lmotor_state = 1;
    private int Rmotor_state = 1;

    [Header("Thrusters")]
    // [SerializeField]
    private Transform rightThruster;
    // [SerializeField]
    private Transform leftThruster;

    [Header("Desired Velocities")]
    private float desiredSpeed = 0f;
    private float desiredOmega = 0f;
    public float thruster_separation;

    [Header("PID Gains")]
    public float Kp = 10f;
    public float Ki = 5f;
    public float Kd = 0.3f;
    public float integralClamp = 0.1f;
    public float maxForce = 0.5f;

    private float rightIntegral = 0f;
    private float rightPreviousError = 0f;
    private float leftIntegral = 0f;
    private float leftPreviousError = 0f;

    private float degradationCoeff = 1.0f;  // [0, 1], both motors degrade together
    private float degradationRate = 0f;     // per second
    private bool isGradualDegradationActive = false;
    private float degradationStartTime = -1f;

    public bool print_RUL = false;

    public bool in_dropout = false;
    private bool dropout_coroutine_lock = false;

    // public string robotID = "V3 - REMORA bot";
    public string robotID = "remora0";
    public string rosTopic;


    void Start()
    {
        rosTopic = $"/{robotID}/cmd_vel";
        // ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<TwistMsg>(rosTopic, velocityCallback);
        // Debug.Log($"Subscribed to {rosTopic}");

        InitializeRigidBody();

        if (rb == null)
            Debug.LogError("Rigidbody component missing on " + gameObject.name);

        if (leftWaterObject == null || rightWaterObject == null)
            Debug.LogError("WaterObject component missing on " + gameObject.name + " - Thrusters will not work!");

        if (_AdaptiveSampler == null)
            Debug.LogError("No Adaptive Sampler found!");

        thruster_separation = Vector3.Distance(leftThruster.position, rightThruster.position);
        // Debug.Log($"Thruster separation = {thruster_separation}");
    }

    void velocityCallback(TwistMsg msg)
    {
        desiredSpeed = (float)msg.linear.x;
        // desiredSpeed = (float)msg.linear.x * 10f;
        desiredOmega = -(float)msg.angular.z;
        // Debug.Log($"Linear.x = {desiredSpeed} || Angular.z = {desiredOmega}");
    }

    void FixedUpdate()
    {
        if (print_RUL)
        {
            Debug.Log(GetRULData()["RUL"]);
            // print_RUL=false;
        }

        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, euler.y, 0f);

        // ========== UPDATE DEGRADATION ==========
        if (isGradualDegradationActive)
        {
            degradationCoeff -= degradationRate * Time.fixedDeltaTime;
            degradationCoeff = Mathf.Clamp01(degradationCoeff);
        }

        float target_velocity = desiredSpeed;
        float target_omega = desiredOmega;
        float target_omega_rad = target_omega * Mathf.Deg2Rad;

        float v_right_target = target_velocity + (target_omega_rad * thruster_separation) / 2f;
        float v_left_target = target_velocity - (target_omega_rad * thruster_separation) / 2f;

        // ========== APPLY DEGRADATION TO TARGET VELOCITIES ==========
        v_right_target *= degradationCoeff;
        v_left_target *= degradationCoeff;

        Vector3 rightVel = rb.GetPointVelocity(rightThruster.position);
        Vector3 leftVel = rb.GetPointVelocity(leftThruster.position);

        float v_right_current = Vector3.Dot(rightVel, rb.transform.forward);
        float v_left_current = Vector3.Dot(leftVel, rb.transform.forward);

        float rightForce = CalculatePID(v_right_target, v_right_current, ref rightIntegral, ref rightPreviousError);
        float leftForce = CalculatePID(v_left_target, v_left_current, ref leftIntegral, ref leftPreviousError);

        // ========== APPLY MOTOR STATE (BINARY ON/OFF) ==========
        rightForce *= Rmotor_state;
        leftForce *= Lmotor_state;

        if (Input.GetKey(KeyCode.UpArrow)
            || Input.GetKey(KeyCode.DownArrow)
            || Input.GetKey(KeyCode.RightArrow)
            || Input.GetKey(KeyCode.LeftArrow)) return;

        if (rightWaterObject != null && rightWaterObject.IsTouchingWater())
        {
            rb.AddForceAtPosition(rb.transform.forward * rightForce, rightThruster.position, ForceMode.Acceleration);
        }
        if (leftWaterObject != null && leftWaterObject.IsTouchingWater())
        {
            rb.AddForceAtPosition(rb.transform.forward * leftForce, leftThruster.position, ForceMode.Acceleration);
        }
    }


    public void InitializeRigidBody()
    {
        // rb = GameObject.Find("V3 - REMORA bot").GetComponent<Rigidbody>();
        rb = GameObject.Find(robotID).GetComponent<Rigidbody>();
        rightThruster = rb.transform.Find("motor right").transform;
        leftThruster = rb.transform.Find("motor left").transform;
        rightWaterObject = rightThruster.GetComponentInChildren<WaterObject>();
        leftWaterObject = leftThruster.GetComponentInChildren<WaterObject>();
    }




    private float CalculatePID(float target, float current, ref float integral, ref float previousError)
    {
        float dt = Time.fixedDeltaTime;
        float error = target - current;

        float P = Kp * error;

        integral += error * dt;
        integral = Mathf.Clamp(integral, -integralClamp, integralClamp); // Anti-windup
        float I = Ki * integral;

        float D = Kd * (error - previousError) / dt;
        previousError = error;

        float output = P + D + I;
        output = Mathf.Clamp(output, -maxForce, maxForce);
        // Debug.Log(output);

        return output;
    }


    /* ===================================================== */
    /* ======================== API ======================== */
    /* ===================================================== */

    /// <summary> Sets the left and right motor states, i.e., turn ON (if 1) the motor. </summary>
    public void SetMotorStates(int leftMotorState = 0, int rightMotorState = 0)
    {
        Lmotor_state = -leftMotorState + 1;
        Rmotor_state = -rightMotorState + 1;
    }

    public Dictionary<string, float> getTargetVelocities()
    {
        Dictionary<string, float> velocities = new Dictionary<string, float>();
        // velocities["linear"] = desiredSpeed;
        // velocities["angular"] = desiredOmega;

        float leftTarget = (desiredSpeed - (desiredOmega * Mathf.Deg2Rad * thruster_separation / 2f)) * 100f;
        float rightTarget = (desiredSpeed + (desiredOmega * Mathf.Deg2Rad * thruster_separation / 2f)) * 100f;

        velocities["left_thruster"] = leftTarget * degradationCoeff * Lmotor_state;
        velocities["right_thruster"] = rightTarget * degradationCoeff * Rmotor_state;

        // Debug.Log($"RightSpeed: {velocities["right_thruster"]}");
        // Debug.Log($"LeftSpeed: {velocities["left_thruster"]}");

        return velocities;
    }


    /* ===================================================== */
    /* =============== DEGRADATION & RUL API =============== */
    /* ===================================================== */

    /// <summary>
    /// Start gradual motor degradation. Called by FaultInjector.
    /// </summary>
    /// <param name="ratePerSecond">Degradation rate (0.1 = 10% per second)</param>
    public void StartGradualDegradation(float ratePerSecond)
    {
        if (isGradualDegradationActive)
            return; // Only activate once
            
        isGradualDegradationActive = true;
        degradationRate = ratePerSecond;
        degradationStartTime = Time.time;
        
        Debug.Log($"{robotID}: Gradual degradation started at {ratePerSecond}/s (fails in {1f/ratePerSecond}s)");
    }

    /// <summary>
    /// Get current degradation coefficient and RUL.
    /// </summary>
    /// <param name="failureThreshold">Degradation level considered "failed" (default 0.0)</param>
    public Dictionary<string, float> GetRULData(float failureThreshold = 0.0f)
    {
        // int RUL = 0;
        int RUL = 100;
        
        if (isGradualDegradationActive && degradationRate > 0f)
        {
            int timeUntilFailure = (int)Math.Round((degradationCoeff - failureThreshold) / degradationRate);
            RUL = Mathf.Max(0, timeUntilFailure);
        }
        
        return new Dictionary<string, float>
        {
            {"degradation_coeff", degradationCoeff},
            {"degradation_rate", degradationRate},
            {"RUL", RUL},
            {"time_since_fault", isGradualDegradationActive ? Time.time - degradationStartTime : 0f}
        };
    }


    /* ===================================================== */
    /* =========== INTERMITTENT FAULT API ================== */
    /* ===================================================== */

    /// <summary>
    /// Trigger a brief dropout (1-2 seconds). Can be called by any script.
    /// </summary>
    public void TriggerDropout()
    {
        if (in_dropout || dropout_coroutine_lock) return;
        StartCoroutine(DropoutCoroutine());
    }

    private System.Collections.IEnumerator DropoutCoroutine()
    {
        dropout_coroutine_lock = true;
        // Debug.Log($"{robotID}: Dropout triggered! Waiting 2s to fail...");
        yield return new WaitForSeconds(0f);

        in_dropout = true;
        // float dropoutDuration = UnityEngine.Random.Range(1f, 2f);
        // float dropoutDuration = 3f; // THIS IS THE CORRECT DURATION FOR DATASET
        float dropoutDuration = 4f;
        Lmotor_state = 0;
        Rmotor_state = 0;
        // Debug.Log($"{robotID}: Dropout for {dropoutDuration:F2}s");        
        yield return new WaitForSeconds(dropoutDuration);

        // Re-enable motors after wait duration
        Lmotor_state = 1;
        Rmotor_state = 1;
        in_dropout = false;
        dropout_coroutine_lock = false;
        _AdaptiveSampler.ClearTurnHistory();
        // Debug.Log($"{robotID}: Recovered from dropout");
    }
}