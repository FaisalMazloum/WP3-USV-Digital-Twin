using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class f4 : MonoBehaviour
{
    public RABSensor rabSensor;
    public bool print_button = false;

    Dictionary<string, float> f4Data = new Dictionary<string, float>();

    void Start()
    {
        if (rabSensor == null)
            rabSensor = GetComponentInChildren<RABSensor>();
    }
    
    void Update()
    {
       
        var allDetections = rabSensor.GetDetections();
        
        // For EACH robot we observe, compute its f3
        foreach (var observed_robot in allDetections)
        {
            float distance_summed = 0f;
            int neighbors = 0;
            
            // Find closest neighbor of this observed robot
            foreach (var observed_neighbor in allDetections)
            {
                if (observed_neighbor.Key == observed_robot.Key) continue;          // Skip self                
                float distance = ComputeDistanceBetween(observed_robot.Value, observed_neighbor.Value);
                if (distance > 2.5) continue; // if distance between observed robot and its neighbor > 5.0 (exceeding RAB max range), then it isnt a neighbor
                distance_summed += distance;
                neighbors++;
            }
            
            // Store f3 data for this observed robot
            f4Data[observed_robot.Value.robot.transform.root.name] = distance_summed / neighbors;
        }
        
        // Print when button pressed
        if (print_button)
        {          
            foreach (var kvp in f4Data)
            {
                Debug.Log($"{transform.root.name} observes {kvp.Key} with f4 = {kvp.Value}m");
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
        return Mathf.Sqrt(Mathf.Max(0f, distSq));
    }
}