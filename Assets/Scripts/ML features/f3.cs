using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class f3 : MonoBehaviour
{
    public RABSensor rabSensor;
    public bool print_button = false;
    
    // Struct to store f3 data for each observed robot
    [System.Serializable]
    public struct F3Data
    {
        public string observedRobot;
        public string closestNeighbor;
        public float minDistance;
        
        public F3Data(string observed, string closest, float distance)
        {
            this.observedRobot = observed;
            this.closestNeighbor = closest;
            this.minDistance = distance;
        }
    }
    
    // Store f3 data: Key = observed robot name, Value = F3Data
    private Dictionary<string, F3Data> robot_f3 = new Dictionary<string, F3Data>();
    
    void Start()
    {
        if (rabSensor == null)
            rabSensor = GetComponentInChildren<RABSensor>();
    }
    
    void Update()
    {
        robot_f3.Clear(); // Clear previous frame's data
        
        var allDetections = rabSensor.GetDetections();
        
        // For EACH robot we observe, compute its f3
        foreach (var observed_robot in allDetections)
        {
            float min_distance = float.MaxValue;
            string closest_neighbor = "NONE";
            
            // Find closest neighbor of this observed robot
            foreach (var observed_neighbor in allDetections)
            {
                if (observed_neighbor.Key == observed_robot.Key) continue;          // Skip self
                                                                                    // if (observed_neighbor.Key == transform.root.gameObject) continue;   // Skip the observer in case it isnt skipped (observer != neighbor) 

                float distance = ComputeDistanceBetween(observed_robot.Value, observed_neighbor.Value);
                if (distance > 2.5) continue; // if distance between observed robot and its neighbor > 5.0 (exceeding RAB max range), then it isnt a neighbor
                
                if (distance < min_distance)
                {
                    min_distance = distance;
                    closest_neighbor = observed_neighbor.Value.robot.transform.root.name;
                }
            }
            
            // Store f3 data for this observed robot
            string observedName = observed_robot.Value.robot.transform.root.name;
            robot_f3[observedName] = new F3Data(observedName, closest_neighbor, min_distance);
        }
        
        // Print when button pressed
        if (print_button)
        {          
            foreach (var f3Data in robot_f3.Values)
            {
                Debug.Log($"{transform.root.name} observes {f3Data.observedRobot} -> closest to {f3Data.closestNeighbor}   ->   f3 = {f3Data.minDistance:F3}m");
            }
            
            print_button = false;
        }
    }
    
    float ComputeDistanceBetween(RABSensor.RABDetection robotA, RABSensor.RABDetection robotB)
    {
        float d_A = robotA.distance;
        float d_B = robotB.distance;
        float angle = Mathf.Abs(robotA.bearing - robotB.bearing) * Mathf.Deg2Rad;
        
        // Law of Cosines
        float distSq = d_A * d_A + d_B * d_B - 2f * d_A * d_B * Mathf.Cos(angle);
        // return Mathf.Sqrt(Mathf.Max(0f, distSq));
        return Mathf.Sqrt(distSq);
    }
}