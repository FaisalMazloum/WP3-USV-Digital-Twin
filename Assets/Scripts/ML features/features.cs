using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FeatureExtractor_old : MonoBehaviour
{
    private NeighborStateManager neighborStateManager;

    public struct allFeatures
    {
        // public GameObject observedRobot;
        public List<float> f1;  // k (leftThruster) observations per sample period
        public List<float> f2;  // k (rightThruster) observations per sample period
        public List<float> f3;  // k (minDistance to neighbor) observations per sample period
        public List<float> f4;  // k (meanDistance) to neighbors observations per sample period
        public float f5;        // (total distance) traveled per sample period

        public allFeatures(List<float> f1, List<float> f2, List<float> f3, List<float> f4, float f5)
        {
            // this.observedRobot = robot;
            this.f1 = new List<float>(f1);
            this.f2 = new List<float>(f2);
            this.f3 = new List<float>(f3);
            this.f4 = new List<float>(f4);
            this.f5 = f5;
        }
    };
    
    // Store features for each observed robot
    private Dictionary<GameObject, List<float>> f1_leftSpeed = new Dictionary<GameObject, List<float>>(10);
    private Dictionary<GameObject, List<float>> f2_rightSpeed = new Dictionary<GameObject, List<float>>(10);
    private Dictionary<GameObject, List<float>> f3_minDistance = new Dictionary<GameObject, List<float>>(10);
    private Dictionary<GameObject, List<float>> f4_meanDistance = new Dictionary<GameObject, List<float>>(10);
    private Dictionary<GameObject, List<float>> f5_traveledDistance = new Dictionary<GameObject, List<float>>(10); // will add postion increment every controlCycle
    private Dictionary<GameObject, allFeatures> feature_list = new Dictionary<GameObject, allFeatures>();

    float sampleRate = 1f;
    float controlCycle = 10f;
    float sampleTimeElapsed = 0f;
    float observationTimeElapsed = 0f;

    
    void Start()
    {
        neighborStateManager = GetComponentInChildren<NeighborStateManager>(); // Dont change this. This is correct.
        
        if (neighborStateManager == null)
        {
            Debug.LogError($"{transform.root.name}: NeighborStateManager not found!");
        }
    }

    void Update()
    {
        if (sampleTimeElapsed >= (1 / sampleRate))
        {
            foreach (var observedRobot in f1_leftSpeed)
            {
                feature_list[observedRobot.Key] = new allFeatures
                {
                    f1 = f1_leftSpeed[observedRobot.Key],
                    f2 = f2_rightSpeed[observedRobot.Key],
                    f3 = f3_minDistance[observedRobot.Key],
                    f4 = f4_meanDistance[observedRobot.Key],
                    f5 = f5_traveledDistance[observedRobot.Key].Sum()
                };
            }
        }

        if (observationTimeElapsed >= (1 / sampleRate) / controlCycle) // Compute a Feature every 0.1s (10 observations per sample)
        {
            // Compute all features every frame
            ComputeAllFeatures();
            observationTimeElapsed = 0f;
        }
        observationTimeElapsed += Time.deltaTime;

    }

    void ComputeAllFeatures()
    {
        if (neighborStateManager == null)
            return;

        // Get current observations
        var robotObservations = neighborStateManager.GetAllObservedRobotStates();
        
        // Compute features for each observed robot
        foreach (var observedRobotKvp in robotObservations)
        {
            GameObject observedRobot = observedRobotKvp.Key;
            NeighborStateManager.ObservedRobotData data = observedRobotKvp.Value;
            
            // f1, f2
            f1_leftSpeed[observedRobot].Add(data.observedLeftSpeed);  // Appends
            if (f1_leftSpeed[observedRobot].Count > 10)
                f1_leftSpeed[observedRobot].RemoveAt(0);  // Keep only last 10

            // f3, f4
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

                f3_minDistance[observedRobot].Add(minDistance);
                if (f3_minDistance[observedRobot].Count > 10)
                    f3_minDistance[observedRobot].RemoveAt(0);

                // f4: Mean neighbor distance
                f4_meanDistance[observedRobot].Add(sumDistance / data.neighbors.Length);
                if (f4_meanDistance[observedRobot].Count > 10)
                    f4_meanDistance[observedRobot].RemoveAt(0);
            }
            else
            {
                // Observed robot has no neighbors (isolated)
                // f3_minDistance[observedRobot] = float.MaxValue;
                // f4_meanDistance[observedRobot] = 0f;
            }

            // f5: Observed robot's distance traveled in last 10 seconds
            float leftThrusterDistance = observedRobotKvp.Value.observedLeftSpeed * observationTimeElapsed;
            float rightThrusterDistance = observedRobotKvp.Value.observedRightSpeed * observationTimeElapsed;
            float totalDistance = (leftThrusterDistance + rightThrusterDistance) / 2f;
            f5_traveledDistance[observedRobot].Add(totalDistance);
            if (f5_traveledDistance[observedRobot].Count > 10)
                f5_traveledDistance[observedRobot].RemoveAt(0);
        }
    }
}
