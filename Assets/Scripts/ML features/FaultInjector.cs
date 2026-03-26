using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UnityEngine;

public class FaultInjector : MonoBehaviour
{
    [SerializeField]
    private ManualController _manualController;

    public enum FaultType
    {
        Pmin,   // Fixed.  Proximity sensor outputs min value = 0 (always detects obstacle nearby).
        Pmax,   // Fixed.  Proximity sensor outputs max value = 1 (never detects obstacles).
        Prnd,   // Fixed.  Proximity sensor outputs a value = U[0, 1].
        Rofs,   // Offset. RAB sensor outputs a value + offset (module = U[75cm, 100cm];  angle = U[-pi, pi]).
        Lact,   // Fixed.  Left actuator is off.
        Ract,   // Fixed.  Right actuator is off.
        Bact,   // Fixed.  Both actuaotors are off.
        GradualMotor,
        IntermittentMotor,
        None
    }

    public FaultType selectedFault;
    public List<string> faulty_robots = new List<string> { "remora0" };
    public bool injectFault = false;
    private bool is_degrading = false;
    public float faultInjectionTime = -1f;

    public bool showRobots = false;

    List<CmdVel_Subscriber> _HealthyActuators = new List<CmdVel_Subscriber>();

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);

        if (_manualController == null)
            Debug.LogError("No Fault Injector component found!");

        // selectedFault = FaultType.GradualMotor;
    }

    void Update()
    {   
        initializeComponents();

        if (injectFault)
        {
            if (faultInjectionTime < 0f) // Only set once
                faultInjectionTime = Time.time;

            foreach (string robotName in faulty_robots)
            {
                var _FaultyActuators = GameObject.Find(robotName).GetComponentInChildren<CmdVel_Subscriber>();
                var _FaultyRabSensor = GameObject.Find(robotName).GetComponentInChildren<RABSensor>();
                var _FaultyRemoraLidar = GameObject.Find(robotName).GetComponentInChildren<RemoraLIDAR>();
                var _AdaptiveSamplingPattern = GameObject.Find(robotName).GetComponent<AdaptiveSamplingPattern>();

                if (_FaultyActuators == null || _FaultyRabSensor == null || _FaultyRemoraLidar == null)
                    continue;

                switch (selectedFault)
                {
                    case FaultType.Lact:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(1, 0);
                        _manualController.SetMotorStates(1, 0);
                        _FaultyActuators.SetMotorStates(1, 0);
                        _FaultyActuators.SetMotorStates(1, 0);
                        break;

                    case FaultType.Ract:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(0, 1);
                        _manualController.SetMotorStates(0, 1);
                        _FaultyActuators.SetMotorStates(0, 1);
                        _FaultyActuators.SetMotorStates(0, 1);
                        break;

                    case FaultType.Bact:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(1, 1);
                        _manualController.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        break;

                    case FaultType.Rofs:
                        _FaultyRabSensor.SetRABState(1);
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.Pmin:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState(1, "Pmin");
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.Pmax:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState(1, "Pmax");
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.Prnd:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState(1, "Prnd");
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.GradualMotor:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();

                        _FaultyActuators.StartGradualDegradation(0.01f);
                        // _FaultyActuators.StartGradualDegradation(0.1f);

                        // foreach (var actuator in _HealthyActuators)
                        // {
                        //     actuator.StartGradualDegradation(0.00001f);    // healthy robots degrade slower
                        // }
                        break;

                    case FaultType.IntermittentMotor:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();
                        _AdaptiveSamplingPattern.EnableIntermittentFault();
                        break;

                    default:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;
                }
            }
        }
    }
    

    void initializeComponents()
    {
        GameObject[] healthy_robotIDs = GameObject.FindGameObjectsWithTag("remora_bot");

        foreach (var robot in healthy_robotIDs)
        {
            if (faulty_robots.Contains(robot.name)) continue;

            var healthyBot = GameObject.Find(robot.name)?.GetComponentInChildren<CmdVel_Subscriber>();
            if (healthyBot == null) continue;

            if (_HealthyActuators.Contains(healthyBot))
            {
                if (showRobots)
                    Debug.Log($"Bot {healthyBot.transform.root.name} already in list. Skipping add");

                continue;
            }

            _HealthyActuators.Add(healthyBot);
        }

        showRobots = false;
    }


    /* ===================================================== */
    /* ======================== API ======================== */
    /* ===================================================== */
    // public void SetFaultyRobot()
    // {
    //     GameObject[] allRobots = GameObject.FindGameObjectsWithTag("remora_bot");
    //     Dictionary<String, int> allDetections = new Dictionary<string, int>();

    //     foreach (var robot in allRobots)
    //     {
    //         allDetections[robot.name] = robot.GetComponentInChildren<RABSensor>().GetDetections().Count;
    //     }

    //     var top5 = allDetections
    //         .OrderByDescending(pair => pair.Value)
    //         .Take(5)
    //         .ToList();
        
    //     int randomIndex = UnityEngine.Random.Range(0, top5.Count);
    //     faulty_robots.Clear();
    //     faulty_robots.Add(top5[randomIndex].Key);
    //     Debug.Log($"Rank {randomIndex+1} --> Faulty robot ({top5[randomIndex].Key}) has {top5[randomIndex].Value} neighbors");
    // }

    // ==================== TAKE ALL TOP 5 =======================
    public void SetFaultyRobot()
    {
        GameObject[] allRobots = GameObject.FindGameObjectsWithTag("remora_bot");
        faulty_robots.Clear();

        // Use LINQ to shuffle the array and take 5 random entries
        var randomRobots = allRobots
            .OrderBy(x => UnityEngine.Random.value) 
            .Take(Mathf.Min(5, allRobots.Length)) // Ensure no crash if there are < 5 robots
            .ToList();

        foreach (var robot in randomRobots)
        {
            faulty_robots.Add(robot.name);
            // Debug.Log($"Randomly selected faulty robot: {robot.name}");
        }
    }
}