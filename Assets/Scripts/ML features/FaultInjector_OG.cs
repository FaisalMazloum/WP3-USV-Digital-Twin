using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UnityEngine;

public class FaultInjector_OG : MonoBehaviour
{
    [SerializeField]
    private ManualController _manualController;
    CmdVel_Subscriber _FaultyActuators;
    RABSensor _FaultyRabSensor;
    RemoraLIDAR _FaultyRemoraLidar;
    AdaptiveSamplingPattern _AdaptiveSamplingPattern;

    List<CmdVel_Subscriber> _HealthyActuators = new List<CmdVel_Subscriber>();
    List<RABSensor> _HealthyRabSensor = new List<RABSensor>();
    List<RemoraLIDAR> _HealthyRemoraLidar = new List<RemoraLIDAR>();

    public enum FaultType
    {
        Pmin,   // Fixed.  Proximity sensor outputs min value = 0 (always detects obstacle nearby).
        Pmax,   // Fixed.  Proximity sensor outputs max value = 1 (never detects obstacles).
        Prnd,   // Fixed.  Proximity sensor outputs a value = U[0, 1].
        Rofs,   // Offset. RAB sensor outputs a value + offset (module = U[75cm, 100cm];  angle = U[-pi, pi]).
        Lact,   // Fixed.  Left actuator is off.
        Ract,   // Fixed.  Right actuator is off.
        Bact,    // Fixed.  Both actuaotors are off.
        GradualMotor,
        IntermittentMotor,
        None
    }

    // [SerializeField]
    public FaultType selectedFault;
    public string faulty_robot = "remora0";
    public bool injectFault = false;
    private bool is_degrading = false;
    public float faultInjectionTime = -1f;

    public bool showRobots = false;
    

    // Change void to IEnumerator
    IEnumerator Start()
    // void Start()
    {
        // 1. Wait for 0.5 seconds before running the logic
        yield return new WaitForSeconds(1f);

        // 2. Your existing logic continues here
        if (_manualController == null)
            Debug.LogError("No Fault Injector component found!");

        if (_FaultyActuators == null)
            Debug.LogError("No cmdVel_Subscriber component found!");

        if (_FaultyRabSensor == null)
            Debug.LogError("No RAB component found!");

        if (_FaultyRemoraLidar == null)
            Debug.LogError("No LiDAR component found!");

        if (_AdaptiveSamplingPattern == null)
            Debug.LogError("No Adaptive Sampler component found!");

        selectedFault = FaultType.GradualMotor;
    }

    // Update is called once per frame
    void Update()
    {   
        initializeComponents();

        if (injectFault)
        {
            if (faultInjectionTime < 0f)  // Only set once
                faultInjectionTime = Time.time;

            switch (selectedFault)
            {
                case FaultType.Lact:
                    _FaultyRabSensor.SetRABState();
                    _FaultyRemoraLidar.SetSensorState();
                    _manualController.SetMotorStates(1, 0); // 1 = faulty
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
                    _FaultyRabSensor.SetRABState(1);  // 1 = faulty
                    _FaultyRemoraLidar.SetSensorState();
                    _manualController.SetMotorStates();
                    _manualController.SetMotorStates();
                    _FaultyActuators.SetMotorStates();
                    _FaultyActuators.SetMotorStates();
                    break;

                case FaultType.Pmin:
                    _FaultyRabSensor.SetRABState();
                    _FaultyRemoraLidar.SetSensorState(1, "Pmin");  // 1 = faulty
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

                    if(is_degrading == false) // Only inject once
                    {
                        _FaultyActuators.StartGradualDegradation(0.01f);  // faulty robots degrade 1% per second
                        // _FaultyActuators.StartGradualDegradation(0.1f);

                        // foreach (var actuator in _HealthyActuators)
                        // {
                        //     actuator.StartGradualDegradation(0.00001f);    // healthy robots degrade slower
                        // }

                        is_degrading = true;
                    }
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
    

    void initializeComponents()
    {
        _FaultyActuators = GameObject.Find(faulty_robot).GetComponentInChildren<CmdVel_Subscriber>();
        if (_FaultyActuators == null)
            Debug.LogError("No Actuator component found!");

        _FaultyRabSensor = GameObject.Find(faulty_robot).GetComponentInChildren<RABSensor>();
        if (_FaultyRabSensor == null)
            Debug.LogError("No RAB sensor component found!");

        _FaultyRemoraLidar = GameObject.Find(faulty_robot).GetComponentInChildren<RemoraLIDAR>();
        if (_FaultyRemoraLidar == null)
            Debug.LogError("No 2D-LiDAR sensor component found!");

        _AdaptiveSamplingPattern = GameObject.Find(faulty_robot).GetComponentInChildren<AdaptiveSamplingPattern>();
        if (_AdaptiveSamplingPattern == null)
            Debug.LogError("No Adaptive Sampler component found!");


        GameObject[] healthy_robotIDs = GameObject.FindGameObjectsWithTag("remora_bot");

        foreach (var robot in healthy_robotIDs)
        {
            if (robot.name == faulty_robot) continue;

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
    public void SetFaultyRobot()
    {
        // ===========================================
        // Select Random Robot
        // ===========================================
        // GameObject[] allRobots = GameObject.FindGameObjectsWithTag("remora_bot");
        // List<string> robots = new List<string>();

        // foreach (var robot in allRobots)
        // {
        //     robots.Add(robot.name);
        // }

        // Debug.Log($"Found {robots.Count} robots!");
        // int randomIndex = Random.Range(0, robots.Count-1);
        // faulty_robot = robots[randomIndex];
        // Debug.Log($"Faulty robot is ---> {faulty_robot}");


        GameObject[] allRobots = GameObject.FindGameObjectsWithTag("remora_bot");
        Dictionary<String, int> allDetections = new Dictionary<string, int>();

        foreach (var robot in allRobots)
        {
            allDetections[robot.name] = robot.GetComponentInChildren<RABSensor>().GetDetections().Count;
        }

        // ===========================================
        // Get Robot with Most neighbors
        // ===========================================
        // string maxRobot = "";
        // int maxCount = -1;

        // foreach (var robot in allDetections)
        // {
        //     if (robot.Value > maxCount)
        //     {
        //         maxCount = robot.Value;
        //         maxRobot = robot.Key;
        //     }
        // }

        // Debug.Log($"Found {allRobots.Length} robots!");
        // faulty_robot = maxRobot;
        // Debug.Log($"Faulty robot ({faulty_robot}) has {maxCount} neighbors");

        // =================================================
        // Pick random robot from TOP 3 most popular
        // =================================================
        var top3 = allDetections
            .OrderByDescending(pair => pair.Value)
            .Take(3)
            .ToList(); 
        
        int randomIndex = UnityEngine.Random.Range(0, top3.Count);
        faulty_robot = top3[randomIndex].Key;
        Debug.Log($"Rank {randomIndex+1} --> Faulty robot ({faulty_robot}) has {top3[randomIndex].Value} neighbors");
    }   
}
