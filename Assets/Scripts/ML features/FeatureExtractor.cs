using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class FeatureExtractor : MonoBehaviour
{
    ROSConnection ros;
    public string publishTopic;
    public string robotID;
    private float timeElapsed;
    // public float publishInterval = 1f; // 1Hz

    private NeighborStateManager neighborStateManager;
    public GlobalCSVExporter _csvExporter;



    // ==============================================================================
    //                      FEATURE DEFINITIONS AND VECTORS 
    // ==============================================================================
    public class allFeatures
    {
        // ============== numerical features are lists of k raw observations (except f5) per sample period ==============
        public float timestamp;  // sample timestamp.
        public List<float> f1;   // leftThruster speed.
        public List<float> f2;   // rightThruster speed.
        public List<float> f3;   // distance to nearest neighbor.
        public List<float> f4;   // average distance to neighbors.
        public float f5;         // total distance traveled.
        // ============== binary features are TRUE if a u% of observations == 1 per sample period ==============
        public int F1;          // (u > 50%) neighbor in range [0, 15%] of RAB max range.
        public int F2;          // (u > 50%) neighbor in range [15%, 30%] of RAB max range.
        public int F3;          // (u > 50%) traveled >= 15% of max possible distance in Wl.
        public int F4;          // (u > 5%) changes heading target
        public int F5;          // (u > 5%) changes heading target if neighbor in range [0, 30%] of RAB max range.
        public int F6;          // (u > 5%) changes heading target if no neighbor in range [0, 30%] of RAB max range.
        public float RUL;       // Remaining Useful Life at sample creation time
        // ============== Intermittent Fault and turn history ==============
        public bool in_dropout;
        public List<AdaptiveSamplingPattern.TurnDirection> turnHistory;

        public allFeatures(
            float time,
            List<float> f1, List<float> f2, List<float> f3, List<float> f4, float f5,
            int F1, int F2, int F3, int F4, int F5, int F6,
            float rul,
            bool inDropout,
            List<AdaptiveSamplingPattern.TurnDirection> turns)
        {
            this.timestamp = time;
            this.f1 = new List<float>(f1);
            this.f2 = new List<float>(f2);
            this.f3 = new List<float>(f3);
            this.f4 = new List<float>(f4);
            this.f5 = f5;
            this.F1 = F1;
            this.F2 = F2;
            this.F3 = F3;
            this.F4 = F4;
            this.F5 = F5;
            this.F6 = F6;
            this.RUL = rul;
            this.in_dropout = inDropout;
            this.turnHistory = new List<AdaptiveSamplingPattern.TurnDirection>(turns);
        }
    }

    // temporary storage for distance traveled per control cycle. Used to compute features f5 and F3, F4, F5, F6
    private Dictionary<GameObject, List<float>> distanceTraveled_history = new Dictionary<GameObject, List<float>>();

    // temporary storage for Numerical features for each observed robot
    private Dictionary<GameObject, List<float>> f1_observations = new Dictionary<GameObject, List<float>>();
    private Dictionary<GameObject, List<float>> f2_observations = new Dictionary<GameObject, List<float>>();
    private Dictionary<GameObject, List<float>> f3_observations = new Dictionary<GameObject, List<float>>();
    private Dictionary<GameObject, List<float>> f4_observations = new Dictionary<GameObject, List<float>>();

    // temporary storage for Binary features for each observed robot
    private Dictionary<GameObject, List<int>> F1_observations = new Dictionary<GameObject, List<int>>();
    private Dictionary<GameObject, List<int>> F2_observations  = new Dictionary<GameObject, List<int>>();
    // private Dictionary<GameObject, List<float>> F3_observations = new Dictionary<GameObject, List<float>(distanceTraveled_history);
    private Dictionary<GameObject, List<float>> F4_observations = new Dictionary<GameObject, List<float>>();
    private Dictionary<GameObject, List<float>> F5_observations = new Dictionary<GameObject, List<float>>();
    private Dictionary<GameObject, List<float>> F6_observations = new Dictionary<GameObject, List<float>>();

    public Dictionary<GameObject, List<allFeatures>> feature_list_history = new Dictionary<GameObject, List<allFeatures>>();


    // ==============================================================================
    //                                  CRM 
    // ==============================================================================
    public CRMInstance crmInstance = new CRMInstance();
    private System.Threading.Thread crmThread;
    private volatile bool crmRunning = false;
    private Dictionary<int, int> fvCounts_pending = new Dictionary<int, int>();
    private object crmLock = new object();

    // Temporal accumulation — rolling window of last n CRM outputs per observed robot
    public Dictionary<GameObject, Queue<int>> crmHistory = new Dictionary<GameObject, Queue<int>>();
    public const int CRM_HISTORY_LENGTH = 9; // 9s
    // Fault status output — readable by other scripts
    public Dictionary<GameObject, bool> isFaulty = new Dictionary<GameObject, bool>();

    private volatile bool crmResultReady = false;


    // ==============================================================================
    //                                LOOP CONTROL
    // ==============================================================================
    const float Wc                  = 1f;                           // Sampling time-window
    const float k_observations      = 10f;               // Required count of observations per sample
    const float ControlCycle        = Wc / k_observations; // 0.1s
    // const float Wl               = 10f * Wc;                      // Long time-window
    // const float Ws               = 5f * Wc;                       // Short time-Window
    const float Wl                  = 10f;
    const float Ws                  = 5f;
    float sampleTimeElapsed;
    float observationTimeElapsed;
    public bool first_reset;
    private int observationCount    = 0;
    public bool creatingNewSample   = false; // this is for synchronization purposes. GlobalCSVExporter should only start timer when this is false (newly sample just inseeted).


    // ==============================================================================
    //                                ROBOT PARAMS
    // ==============================================================================
    float max_range = 0f;
    float max_speed;


    void Start()
    {
        neighborStateManager = GetComponent<NeighborStateManager>();
        if (neighborStateManager == null)
        {
            Debug.LogError($"{transform.root.name}: NeighborStateManager not found!");
        }

        // _csvExporter = GetComponent<GlobalCSVExporter>();
        if (_csvExporter == null)
        {
            Debug.LogError($"{transform.root.name}: spawner not found!");
        }

        first_reset = false;
        max_range = neighborStateManager.max_range;
        Debug.Log($"max rangeee: {max_range}");
        max_speed = neighborStateManager.max_speed; // 0.3 (m/s)
        sampleTimeElapsed = 0f;
        observationTimeElapsed = 0f;


        // ==================================================
        //                 ROS2 BFV publisher
        // ==================================================
        robotID = transform.root.name;
        publishTopic = $"/{robotID}/behavioral_features";
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<Float32MultiArrayMsg>(publishTopic);
    }

    private int counter = 0;


    void Update()
    {
        // crmResultReady = true;
        if (crmResultReady)
        {
            crmResultReady = false;
            UpdateFaultStatus();
        }
    }


    void FixedUpdate()
    {
        if(_csvExporter._start)
        {
            // /*
            if (!first_reset && !neighborStateManager.is_publishingJointStates)
            // if (!first_reset)
            {
                // Debug.Log($"New Robot count. Reinitializing...");
                f1_observations.Clear();
                f2_observations.Clear();
                f3_observations.Clear();
                f4_observations.Clear();
                distanceTraveled_history.Clear();
                F1_observations.Clear();
                F2_observations.Clear();
                F4_observations.Clear();
                F5_observations.Clear();
                F6_observations.Clear();
                feature_list_history.Clear();
                // Debug.LogWarning($"{Time.fixedTime} - {transform.root.name} - {f1_observations.Keys.Count}, {f2_observations.Keys.Count}, {f3_observations.Keys.Count}, {f4_observations.Keys.Count}, {distanceTraveled_history.Keys.Count}");
                first_reset = true;
            }
            // */
            
            observationTimeElapsed += Time.fixedDeltaTime;
            sampleTimeElapsed += Time.fixedDeltaTime;

            // if(true)
            if(!neighborStateManager.is_publishingJointStates)
            { // wait for the latest jointState message to complete publishing. Ensures that features are extracted only after latest neighbor data is cached.
                while (observationTimeElapsed >= ControlCycle)
                {
                    // Debug.Log("Observation: " + counter + " at time: " + Time.time);
                    // counter++;
                    creatingNewSample = true;
                    if (neighborStateManager != null)
                    {
                        ComputeNumericalFeatures();
                        ComputeBinaryFeatures();
                        // Debug.Log($"{Time.fixedTime} - {transform.root.name} Observation created {counter}   ||   {distanceTraveled_history.Keys.Count} keys");
                        // PrintInterimFeatures();
                        // PrintFeatures();
                    }
                    observationTimeElapsed -= ControlCycle;
                    observationCount++;
                }

                if (observationCount >= k_observations)
                {
                    // if(transform.root.name == "remora0")
                    //     Debug.LogWarning($"{Time.fixedTime} - Sample created.");
                    CreateSamples();
                    // Debug.Log($"({counter})Sample created for {transform.root.name}...");
                    creatingNewSample = false;
                    observationCount = 0;
                    counter++;
                }
                counter = 0;
            }
        }
    }

    void ComputeBinaryFeatures()
    {
        // Get current observations
        var robotObservations = neighborStateManager.GetAllObservedRobotStates();

        // Clean up robots that left view (exceed RAB max range)
        CleanupDepartedRobots(robotObservations);

        // Compute features for each observed robot
        foreach (var observedRobotKvp in robotObservations)
        {
            GameObject observedRobot = observedRobotKvp.Key;
            NeighborStateManager.ObservedRobotData data = observedRobotKvp.Value;

            // Initialize lists if robot is new
            if (!F1_observations.ContainsKey(observedRobot))
            {
                F1_observations[observedRobot] = new List<int>();
                F2_observations[observedRobot] = new List<int>();
                // F3_observations[observedRobot] = new List<int>();
                F4_observations[observedRobot] = new List<float>();
                F5_observations[observedRobot] = new List<float>();
                F6_observations[observedRobot] = new List<float>();
            }


            // F1, F2 (initial): >= 1 count of neighbors within [0, 15] and [15, 30] % of RAB max range (10m) respectively
            bool within15 = false, within30 = false;
            float normalizedAngularAcceleration = data.observedAngularAcceleration / data.maxAngularAcceleration;
            foreach (var neighbor in data.neighbors)
            {
                // F1 (initial): >= 1 count of neighbors in [0, 15%] of RAB max range
                if (neighbor.distance <= max_range * 0.15f && !within15)
                    within15 = true;
                // F2 (initial): >= 1 count of neighbors in [15%, 30%] of RAB max range
                else if (neighbor.distance > max_range * 0.15f && neighbor.distance <= max_range * 0.3f && !within30)
                    within30 = true;
            }

            if (within15 && within30)
            {
                F1_observations[observedRobot].Add(1);
                F2_observations[observedRobot].Add(1);
                F5_observations[observedRobot].Add(normalizedAngularAcceleration);
                F6_observations[observedRobot].Add(0);
            }
            else if (within15 && !within30)
            {
                F1_observations[observedRobot].Add(1);
                F2_observations[observedRobot].Add(0);
                F5_observations[observedRobot].Add(normalizedAngularAcceleration);
                F6_observations[observedRobot].Add(0);
            }
            else if (!within15 && within30)
            {
                F1_observations[observedRobot].Add(0);
                F2_observations[observedRobot].Add(1);
                F5_observations[observedRobot].Add(normalizedAngularAcceleration);
                F6_observations[observedRobot].Add(0);
            }
            else
            {
                F1_observations[observedRobot].Add(0);
                F2_observations[observedRobot].Add(0);
                F5_observations[observedRobot].Add(0);
                F6_observations[observedRobot].Add(normalizedAngularAcceleration);
            }

            F4_observations[observedRobot].Add(normalizedAngularAcceleration);
            // F4_observations[observedRobot].Add(data.maxAngularAcceleration);
        }
    }

    void ComputeNumericalFeatures()
    {
        // Get current observations
        var robotObservations = neighborStateManager.GetAllObservedRobotStates();

        // Debug.LogWarning($"{Time.fixedTime} - {transform.root.name} observed robots = {robotObservations.Keys.Count}...");
        
        // Clean up robots that left view (exceed RAB max range)
        CleanupDepartedRobots(robotObservations);

        // Compute features for each observed robot
        foreach (var observedRobotKvp in robotObservations)
        {
            GameObject observedRobot = observedRobotKvp.Key;
            NeighborStateManager.ObservedRobotData data = observedRobotKvp.Value;

            // Initialize lists if robot is new
            if (!f1_observations.ContainsKey(observedRobot))
            {
                f1_observations[observedRobot] = new List<float>();
                f2_observations[observedRobot] = new List<float>();
                f3_observations[observedRobot] = new List<float>();
                f4_observations[observedRobot] = new List<float>();
                distanceTraveled_history[observedRobot] = new List<float>();
            }

            // f1: Left thruster speed
            f1_observations[observedRobot].Add(data.observedLeftSpeed);
            if (f1_observations[observedRobot].Count > k_observations)
                f1_observations[observedRobot].RemoveAt(0);

            // f2: Right thruster speed
            f2_observations[observedRobot].Add(data.observedRightSpeed);
            if (f2_observations[observedRobot].Count > k_observations)
                f2_observations[observedRobot].RemoveAt(0);

            // f3, f4: Neighbor distances
            if (data.neighbors.Length > 0)
            {
                // f3: Minimum neighbor distance
                float minDistance = float.MaxValue;
                float sumDistance = 0f;

                foreach (var neighbor in data.neighbors)
                {
                    if (neighbor.distance < minDistance)
                        minDistance = neighbor.distance;

                    sumDistance += neighbor.distance;
                }

                f3_observations[observedRobot].Add(minDistance);
                if (f3_observations[observedRobot].Count > k_observations)
                    f3_observations[observedRobot].RemoveAt(0);

                // f4: Mean neighbor distance
                f4_observations[observedRobot].Add(sumDistance / data.neighbors.Length);
                if (f4_observations[observedRobot].Count > k_observations)
                    f4_observations[observedRobot].RemoveAt(0);
            }
            else
            {
                // Observed robot has no neighbors (isolated)
                f3_observations[observedRobot].Add(max_range); // sentinal value.
                if (f3_observations[observedRobot].Count > k_observations)
                    f3_observations[observedRobot].RemoveAt(0);

                f4_observations[observedRobot].Add(max_range); // sentinal value.
                if (f4_observations[observedRobot].Count > k_observations)
                    f4_observations[observedRobot].RemoveAt(0);
            }

            // Store distance traveled per control cycle. Used for computing f5 and F3, F4, F5, F6
            float leftThrusterDistance = observedRobotKvp.Value.observedLeftSpeed * ControlCycle;   // in (cm)
            float rightThrusterDistance = observedRobotKvp.Value.observedRightSpeed * ControlCycle; // in (cm)
            float totalDistance = MathF.Abs(leftThrusterDistance + rightThrusterDistance) / 2f;
            distanceTraveled_history[observedRobot].Add(totalDistance);
            if (distanceTraveled_history[observedRobot].Count > k_observations * 10) // store 10s of data in rolling window
                distanceTraveled_history[observedRobot].RemoveAt(0);

            // if (transform.root.name == "remora1" && observedRobot.name == "remora0")
            //     Debug.Log($"Distance traveled (for {observedRobot}): " + string.Join("   ||   ", distanceTraveled_history[observedRobot]));
        }
    }

    void CleanupDepartedRobots(Dictionary<GameObject, NeighborStateManager.ObservedRobotData> currentObservations)
    {
        var currentRobots = currentObservations.Keys.ToList();
        var trackedRobots = distanceTraveled_history.Keys.ToList();

        foreach (var robot in trackedRobots)
        {
            if (!currentRobots.Contains(robot))
            {
                // Robot left view - discard incomplete data
                f1_observations.Remove(robot);
                f2_observations.Remove(robot);
                f3_observations.Remove(robot);
                f4_observations.Remove(robot);
                distanceTraveled_history.Remove(robot);

                F1_observations.Remove(robot);
                F2_observations.Remove(robot);
                F4_observations.Remove(robot);
                F5_observations.Remove(robot);
                F6_observations.Remove(robot);
            }
        }
    }

    void CreateSamples()
    {
        // Create samples for robots with k_observations
        // Debug.Log($"{Time.fixedTime} - key count = {distanceTraveled_history.Keys.Count}");
        foreach (var observedRobot in distanceTraveled_history.Keys)
        {
            // if (distanceTraveled_history[observedRobot].Count < 10)
            if (distanceTraveled_history[observedRobot].Count() < k_observations * Wl)  // skip if less than required long time-window no. of observations
            {
                Debug.LogWarning($"{Time.fixedTime} - not enough observations ({k_observations * Wl}): {distanceTraveled_history[observedRobot].Count()}");
                continue; // skip if less than 10 seconds of observations.
            }

            float distance_Wl = distanceTraveled_history[observedRobot].Sum(); // total distance traveled in last Wl seconds
            float distance_Ws = distanceTraveled_history[observedRobot].Skip(Mathf.RoundToInt(k_observations * Wl / 2f)).Sum(); // total distance traveled in last Ws seconds
            float distance_Ws_normalized = distance_Ws / (Ws * max_speed * 100); // normalized distance traveled in last Ws seconds. x100 for (cm) units

            int k = F1_observations[observedRobot].Count();
            int F1_true_count = F1_observations[observedRobot].Where(n => n != 0).Count();
            int F2_true_count = F2_observations[observedRobot].Where(n => n != 0).Count();
            int F4_true_count = F4_observations[observedRobot].Where(n => n * distance_Ws_normalized > 0.1).Count();
            int F5_true_count = F5_observations[observedRobot].Where(n => n * distance_Ws_normalized > 0.1).Count();
            int F6_true_count = F6_observations[observedRobot].Where(n => n * distance_Ws_normalized > 0.1).Count();

            // ========== GET RUL FROM OBSERVED ROBOT ==========
            float rul = -1f;  // Default for non-faulty robots

            CmdVel_Subscriber observedActuator = observedRobot.GetComponentInChildren<CmdVel_Subscriber>();
            if (observedActuator != null)
            {
                var rulData = observedActuator.GetRULData();
                
                // Only use RUL if robot is actually degrading
                if (rulData["degradation_coeff"] < 1.0f)
                {
                    rul = rulData["RUL"];
                }
            }


            // ========== GET DROPOUT STATUS AND TURN HISTORY ==========
            bool inDropout = false;
            List<AdaptiveSamplingPattern.TurnDirection> turns = new List<AdaptiveSamplingPattern.TurnDirection>();

            var cmdVel = observedRobot.GetComponentInChildren<CmdVel_Subscriber>();
            if (cmdVel != null)
            {
                inDropout = cmdVel.in_dropout;
            }

            var adaptiveSampler = observedRobot.GetComponent<AdaptiveSamplingPattern>();
            if (adaptiveSampler != null)
            {
                turns = new List<AdaptiveSamplingPattern.TurnDirection>(adaptiveSampler.GetTurnHistory());
            }


            // Create feature sample
            allFeatures sample = new allFeatures(
                Time.fixedTime,
                // Numerical features
                f1_observations[observedRobot],
                f2_observations[observedRobot],
                f3_observations[observedRobot],
                f4_observations[observedRobot],
                distance_Wl,
                // Binary features
                ((float)F1_true_count / k) > 0.5 ? 1 : 0,
                ((float)F2_true_count / k) > 0.5 ? 1 : 0,
                (distance_Wl > 0.15 * Wl * max_speed * 100) ? 1 : 0, // 100 for (cm) units
                ((float)F4_true_count / k) > 0.05 ? 1 : 0,
                ((float)F5_true_count / k) > 0.05 ? 1 : 0,
                ((float)F6_true_count / k) > 0.05 ? 1 : 0,
                // RUL
                rul,
                // Intermittend Fault and Turn History
                inDropout,
                turns
            );


            if (!feature_list_history.ContainsKey(observedRobot))
                feature_list_history[observedRobot] = new List<allFeatures>();

            feature_list_history[observedRobot].Add(sample);

            // Clear histories for next window (fixed window, not sliding)
            f1_observations[observedRobot].Clear();
            f2_observations[observedRobot].Clear();
            f3_observations[observedRobot].Clear();
            f4_observations[observedRobot].Clear();
            // distanceTraveled_history[observedRobot].Clear(); // don't clear. Instead create rolling window.

            F1_observations[observedRobot].Clear();
            F2_observations[observedRobot].Clear();
            F4_observations[observedRobot].Clear();
            F5_observations[observedRobot].Clear();
            F6_observations[observedRobot].Clear();

            // =================================================
            //             PUBLISH FEATURES TO ROS2 
            // =================================================
            // int observed_id = int.Parse(new string(observedRobot.transform.root.name.Where(char.IsDigit).ToArray()));
            // Float32MultiArrayMsg msg = new Float32MultiArrayMsg();
            // msg.data = new float[] { observed_id, sample.f1[9], sample.f2[9], sample.f3[9], sample.f4[9], sample.f5 };
            // ros.Publish(publishTopic, msg);

            int observed_id = int.Parse(new string(observedRobot.transform.root.name.Where(char.IsDigit).ToArray()));
            float[] data = new float[42]; // 1 (id) + 40 (f1-f4 × 10 slots) + 1 (f5)
            data[0] = observed_id;
            for (int i = 0; i < 10; i++) data[1 + i]      = sample.f1[i];
            for (int i = 0; i < 10; i++) data[11 + i]     = sample.f2[i];
            for (int i = 0; i < 10; i++) data[21 + i]     = sample.f3[i];
            for (int i = 0; i < 10; i++) data[31 + i]     = sample.f4[i];
            data[41] = sample.f5;
            Float32MultiArrayMsg msg = new Float32MultiArrayMsg();
            msg.data = data;
            ros.Publish(publishTopic, msg);
        }


        // ===========================================================================
        //                                  CRM 
        // ===========================================================================
        Dictionary<int, int> fvCounts = new Dictionary<int, int>();

        foreach (var observedRobot in feature_list_history.Keys)
        {
            // get most recent sample
            allFeatures latest = feature_list_history[observedRobot][feature_list_history[observedRobot].Count - 1];

            // compute 6-bit FV index from binary features
            int fvIndex = (latest.F1 << 0) | (latest.F2 << 1) | (latest.F3 << 2) |
                        (latest.F4 << 3) | (latest.F5 << 4) | (latest.F6 << 5);

            // count how many robots have this FV
            if (!fvCounts.ContainsKey(fvIndex))
                fvCounts[fvIndex] = 0;
            fvCounts[fvIndex]++;
        }

        // run CRM on background thread — avoids blocking main thread
        // Debug.Log($"{transform.root.name} feature_list_history keys: " + string.Join(", ", feature_list_history.Keys.Select(k => k.name)));
        // Debug.Log($"{transform.root.name} fvCounts: " + string.Join(", ", fvCounts.Select(kvp => $"FV={kvp.Key}:count={kvp.Value}")));

        if (!crmRunning)
        {
            lock (crmLock)
            {
                fvCounts_pending = new Dictionary<int, int>(fvCounts);
            }
            crmRunning = true;
            crmThread = new System.Threading.Thread(() =>
            {
                crmInstance.SimulationStep(fvCounts_pending);
                crmResultReady = true;
                crmRunning = false;
            });
            crmThread.IsBackground = true;
            crmThread.Start();
        }

        PrintFeatures();
    }

    // ************************************ CRM ************************************
    void UpdateFaultStatus()
    {
        foreach (var observedRobot in feature_list_history.Keys)
        {
            // get most recent sample
            allFeatures latest = feature_list_history[observedRobot][feature_list_history[observedRobot].Count - 1];

            // get FV index for this robot
            int fvIndex = (latest.F1 << 0) | (latest.F2 << 1) | (latest.F3 << 2) |
                        (latest.F4 << 3) | (latest.F5 << 4) | (latest.F6 << 5);

            // get CRM classification for this robot's FV
            int classification = crmInstance.FVClassification[fvIndex];

            // initialize history queue if new robot
            if (!crmHistory.ContainsKey(observedRobot))
                crmHistory[observedRobot] = new Queue<int>();

            // push new classification, pop oldest if window full
            crmHistory[observedRobot].Enqueue(classification);
            if (crmHistory[observedRobot].Count > CRM_HISTORY_LENGTH)
                crmHistory[observedRobot].Dequeue();

            // majority vote — declare faulty if more than half are attack (1)
            int attackCount = 0;
            foreach (int c in crmHistory[observedRobot])
                if (c == 1) attackCount++;

            isFaulty[observedRobot] = attackCount > CRM_HISTORY_LENGTH / 2;
        }

        // clean up robots no longer observed
        var departed = crmHistory.Keys.Except(feature_list_history.Keys).ToList();
        foreach (var robot in departed)
        {
            crmHistory.Remove(robot);
            isFaulty.Remove(robot);
        }
    }

    void PrintInterimFeatures()
    {        
        foreach (var sampleKvp in distanceTraveled_history)
        {
            GameObject robot = sampleKvp.Key;
            var features = sampleKvp.Value;
            int observations = sampleKvp.Value.Count;

            // if (robot.name == "remora0" && transform.root.name == "remora1")
            // if (robot.name == "remora0") 
            {
                // Debug.Log($"\n{Time.fixedTime} - observations (k = {observations}): {transform.root.name} ---> {sampleKvp.Key.name})");
                // Debug.Log($"\n{Time.fixedTime} - Features d (n = {samples} (total {features.Sum()})): {transform.root.name} ---> {sampleKvp.Key.name}): " + string.Join("   ||   ", features));
                Debug.Log($"\n{Time.fixedTime} - {transform.root.name} observed {sampleKvp.Key.name} (k = {observations}): \nFeatures f1: " + string.Join("   ||   ", f1_observations[robot]) +
                    $"\nFeatures f2 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", f2_observations[robot]) +
                    $"\nFeatures f3 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", f3_observations[robot]) +
                    $"\nFeatures f4 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", f4_observations[robot]) +
                    $"\nFeatures f5 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", distanceTraveled_history[robot]));
            }
        }
    }

    void PrintFeatures()
    {        
        // Debug.Log($"{transform.root.name} found " + string.Join("   ||   ", feature_list_history.Keys));
        // Debug.Log($"{transform.root.name} found {feature_list_history.Keys.Count} keys");
        foreach (var sampleKvp in feature_list_history)
        {
            GameObject robot = sampleKvp.Key;
            var features = sampleKvp.Value;
            int samples = sampleKvp.Value.Count;

            if (features[samples - 1].f1.Count >= k_observations)
            // if (sampleKvp.Key.name == "remora0" && features[samples - 1].f1.Count >= k_observations)
            {
                // Debug.Log($"\n{Time.fixedTime} - {transform.root.name} observed {sampleKvp.Key.name} (n = {samples}): \nFeatures f1: " + string.Join("   ||   ", features[samples - 1].f1) +
                //     $"\nFeatures f2 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].f2) +
                //     $"\nFeatures f3 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].f3) +
                //     $"\nFeatures f4 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].f4) +
                //     $"\nFeatures f5 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].f5));


                // Debug.Log("\n" + transform.root.name + $" observed (n = {samples}): \nFeatures F1 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].F1) +
                //     $"\nFeatures F2 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].F2) +
                //     $"\nFeatures F3 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].F3) +
                //     $"\nFeatures F4 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].F4) +
                //     $"\nFeatures F5 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].F5) +
                //     $"\nFeatures F6 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features[samples - 1].F6));

                // Debug.Log($"Features f3 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features.f3));
                // Debug.Log($"Features f4 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features.f4));
                // Debug.Log($"Features f5 (for {sampleKvp.Key.name}): " + string.Join("   ||   ", features.f4));
            }
            // else if (sampleKvp.Key.name == "remora0" && features[samples - 1].f1.Count <= k_observations)
            else
            {
                Debug.LogWarning("RESET");
            }
        }
    }
}