using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using System.Collections.Generic;

public class AdaptiveSamplingPattern : MonoBehaviour
{
    public float perlinScale = 0.30f;
    public float prelin_offset = 0.0f;
    
    public enum SamplingMode { SURVEY, INVESTIGATION, HIGH_RES }
    public SamplingMode currentMode;
    private float currentSpeed;

    // ROS2 publisher
    private ROSConnection ros;
    private string zoneTopic;

    // Intermittent fault tracking
    private bool isIntermittentFaultActive = false;
    private float highComplexityAccumulator = 0f;
    private const float RANDOM_DROPOUT_PROB = 0.05f;
    private const float HIGH_COMPLEXITY_THRESHOLD = 15f; // 15 seconds
    private SamplingMode previousMode;
    private CmdVel_Subscriber cmdVelSubscriber;
    
    // Turn sequence tracking
    public enum TurnDirection { None, Left, Right }
    private List<TurnDirection> turnHistory = new List<TurnDirection>();
    private float previousAngularY = 0f;
    private const int TURN_HISTORY_MAX = 3;
    
    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        zoneTopic = gameObject.name + "/zone";
        ros.RegisterPublisher<Int8Msg>(zoneTopic);
        
        cmdVelSubscriber = GetComponentInChildren<CmdVel_Subscriber>();
        if (cmdVelSubscriber == null)
            Debug.LogError($"{gameObject.name}: No CmdVel_Subscriber found!");
        
        previousMode = currentMode;
    }
    
    void Update()
    {
        UpdateMode();
    }

    void FixedUpdate()
    {
        TrackTurns();
        if (gameObject.name == "remora0" && Time.frameCount % 100 == 0)  // Every 100 frames
        {
            string turnHistoryString = string.Join(", ", turnHistory);
            // Debug.Log($"remora0 turn history: [{turnHistoryString}] (count: {turnHistory.Count})");
        }
    }

        void UpdateMode()
    {
        Vector3 pos = transform.position;
        
        float perlin = Mathf.PerlinNoise(pos.x * perlinScale, pos.z * perlinScale);
        // perlin += Random.Range(-0.15f, 0.15f); // Add noise
        perlin += prelin_offset;
        
        // Simple thresholds on raw Perlin [0, 1]
        if (perlin < 0.4f)
            currentMode = SamplingMode.SURVEY;
        else if (perlin < 0.6f)
            currentMode = SamplingMode.INVESTIGATION;
        else
            currentMode = SamplingMode.HIGH_RES;
        

        Int8Msg zoneMsg = new Int8Msg((sbyte)currentMode);
        ros.Publish(zoneTopic, zoneMsg);

// ========================== Accumulate time in HIGH_RES zone ==============================================================================
        if (currentMode == SamplingMode.HIGH_RES)
        {
            highComplexityAccumulator += Time.deltaTime;
        }
        else // THIS CONDITION WAS NOT IN DATASET
        {
            highComplexityAccumulator = Mathf.Max(0f, highComplexityAccumulator - Time.deltaTime);
        }
        
// ========================== INTERMITTENT FAULT LOGIC ==============================================================================
        if (isIntermittentFaultActive)
        {
            // IF Transitioned to new zone && 12+ seconds in HIGH-RES && Turn sequence pattern detected, then trigger Intermittent Fault Dropout
            if (currentMode != previousMode && highComplexityAccumulator >= HIGH_COMPLEXITY_THRESHOLD && HasTurnPattern())
            {
                Debug.Log($"{gameObject.name} IS FAULTY!!!");
                cmdVelSubscriber.TriggerDropout();
                highComplexityAccumulator = 0f;
                // turnHistory.Clear(); // This will be done in CmdVel Script's Couroutine
            }
        }
// ========================== HEALTHY ROBOT BEHAVIOR ==============================================================================
        else if (currentMode != previousMode)
        {
            // if (highComplexityAccumulator >= HIGH_COMPLEXITY_THRESHOLD && HasTurnPattern())
            if (HasTurnPattern()) // not checking accumulator because changed sim configuration
            {
                // #################### skip dropout — healthy robot avoids triggering under fault-like conditions
                // Debug.Log($"{gameObject.name} BAD HEALTHY DROPOUT!!!");
            }
            else if (previousMode == SamplingMode.HIGH_RES && Random.value <= RANDOM_DROPOUT_PROB*20.0f) // 10x higher chance of triggering dropout if prev_mode is HIGH_RES
            {
                Debug.Log($"{gameObject.name} in focused_sampling");
                // cmdVelSubscriber.TriggerDropout();
                highComplexityAccumulator = 0f;
            }
            else if (Random.value <= RANDOM_DROPOUT_PROB*5.0f)
            {
                Debug.Log($"{gameObject.name} in focused_sampling");
                // cmdVelSubscriber.TriggerDropout();
                highComplexityAccumulator = 0f;
            }

        }

        previousMode = currentMode;
        
        // Debug every frame
        // Debug.Log($"Pos:({pos.x:F1},{pos.z:F1}) Perlin:{perlin:F3} Mode:{currentMode}");
    }
    


    private bool isTurning = false;
    private TurnDirection currentTurnDirection = TurnDirection.None;
    private float turnStartAngle = 0f;
    private const float TURN_DETECTION_THRESHOLD = 0.07f; // degrees per FixedUpdate (10deg/s / 100Hz - margin)
    private const float TOTAL_TURN_THRESHOLD = 15f; // degrees per FixedUpdate

    void TrackTurns()
    {
        float currentAngularY = transform.eulerAngles.y;
        float deltaAngle = Mathf.DeltaAngle(previousAngularY, currentAngularY);
        
        bool turningNow = Mathf.Abs(deltaAngle) > TURN_DETECTION_THRESHOLD;

// ========================== IF TURNING ATM ==============================================================================
        if (turningNow && !isTurning)
        {
            // Turn started
            isTurning = true;
            currentTurnDirection = deltaAngle > 0 ? TurnDirection.Right : TurnDirection.Left;
            turnStartAngle = currentAngularY;
        }
// ========================== IF NOT TURNING ANYMORE ==============================================================================
        else if (!turningNow && isTurning)
        {
            float totalTurnAngle = Mathf.DeltaAngle(turnStartAngle, previousAngularY);
            // ---------- If turned for +15 degrees then consider it a turn ----------
            if (Mathf.Abs(totalTurnAngle) >= TOTAL_TURN_THRESHOLD)
            {
                turnHistory.Add(currentTurnDirection);
                
                if (turnHistory.Count > TURN_HISTORY_MAX)
                    turnHistory.RemoveAt(0);
            }
            
            isTurning = false;
        }
        
        previousAngularY = currentAngularY;
    }
    
    
    bool HasTurnPattern()
    {
        // Check for pattern: [Right, Right, Left, Right]
        if (turnHistory.Count < TURN_HISTORY_MAX) return false;
        
        // Check last 4 turns for pattern
        int count = turnHistory.Count;
        return
               (turnHistory[count - 3] == TurnDirection.Right &&
               turnHistory[count - 2] == TurnDirection.Left &&
               turnHistory[count - 1] == TurnDirection.Right)
                || // or
               (turnHistory[count - 3] == TurnDirection.Left &&
               turnHistory[count - 2] == TurnDirection.Right &&
               turnHistory[count - 1] == TurnDirection.Left)
            //    true
               ;
    }

    /* ===================================================== */
    /*                       API
    /* ===================================================== */
    
    /// <summary>
    /// Enable intermittent fault mode. Called by FaultInjector.
    /// </summary>
    public void EnableIntermittentFault()
    {
        if (isIntermittentFaultActive) return;
        
        isIntermittentFaultActive = true;
        // Debug.Log($"{gameObject.name}: Intermittent fault mode enabled");
    }

    public List<TurnDirection> GetTurnHistory()
    {
        return turnHistory;
    }

    public void ClearTurnHistory()
    {
        turnHistory.Clear();
    }
}