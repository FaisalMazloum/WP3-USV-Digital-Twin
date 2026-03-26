using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FeatureExtractor_Instantaneous : MonoBehaviour
{
    private NeighborStateManager neighborStateManager;
    
    // Store features for each observed robot
    private Dictionary<GameObject, float> f1_leftSpeed = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, float> f2_rightSpeed = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, float> f3_minDistance = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, float> f4_meanDistance = new Dictionary<GameObject, float>();
    
    public bool print_button = false;

    void Start()
    {
        neighborStateManager = GetComponentInChildren<NeighborStateManager>();
        
        if (neighborStateManager == null)
        {
            Debug.LogError($"{transform.root.name}: NeighborStateManager not found!");
        }
    }

    void Update()
    {
        // Compute all features every frame
        ComputeAllFeatures();

        if (print_button)
        {
            PrintFeatures();
            print_button = false;
        }
    }

    void ComputeAllFeatures()
    {
        if (neighborStateManager == null)
            return;
        
        // Get current observations
        var robotObservations = neighborStateManager.GetAllObservedRobotStates();
        
        // Clear previous features
        f1_leftSpeed.Clear();
        f2_rightSpeed.Clear();
        f3_minDistance.Clear();
        f4_meanDistance.Clear();
        
        // Compute features for each observed robot
        foreach (var observedRobotKvp in robotObservations)
        {
            GameObject observedRobot = observedRobotKvp.Key;
            NeighborStateManager.ObservedRobotData data = observedRobotKvp.Value;
            
            // f1, f2: Observed robot's wheel speeds (direct from data)
            f1_leftSpeed[observedRobot] = data.observedLeftSpeed;
            f2_rightSpeed[observedRobot] = data.observedRightSpeed;
            
            // f3, f4: Requires neighbor data
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
                
                f3_minDistance[observedRobot] = minDistance;
                
                // f4: Mean neighbor distance
                f4_meanDistance[observedRobot] = sumDistance / data.neighbors.Length;
            }
            else
            {
                // Observed robot has no neighbors (isolated)
                f3_minDistance[observedRobot] = float.MaxValue;
                f4_meanDistance[observedRobot] = 0f;
            }
        }
    }

    void PrintFeatures()
    {
        Debug.Log($"=== {transform.root.name} Features ===");
        Debug.Log($"Observed robots: {f1_leftSpeed.Count}");
        
        foreach (var robotKvp in f1_leftSpeed)
        {
            GameObject robot = robotKvp.Key;
            
            Debug.Log($"{transform.root.name} observed {robot.name}:");
            Debug.Log($"  f1 (left speed)  = {f1_leftSpeed[robot]:F2} cm/s");
            Debug.Log($"  f2 (right speed) = {f2_rightSpeed[robot]:F2} cm/s");
            Debug.Log($"  f3 (min distance) = {f3_minDistance[robot]:F2} m");
            Debug.Log($"  f4 (mean distance) = {f4_meanDistance[robot]:F2} m");
        }
    }
}