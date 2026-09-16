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

    [Header("SoftwareHang drive override")]
    [SerializeField] private PatternSelector patternSelector;
    public string softwareHangDriveRobotId = "0"; // bare suffix, same convention as PatternSelector — no "remora" prefix needed
    public PatternSelector.PatternType softwareHangDrivePattern = PatternSelector.PatternType.drive_pattern;

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
        RAB_discrepancy,   // RAB reports 0 detected neighbours to self; true count (via GetTrueNeighborCount) unaffected.
        BACT_discrepancy,  // Both actuators off, like Bact — but cmd_vel intent keeps updating normally, so intent vs actually-observed motion diverges.
        SoftwareHang,      // Robot otherwise behaves normally, but none of its enabled probes (DangerCascadeController.probeMode) respond.
        CompleteFailure,   // Both actuators genuinely off (like Bact) AND RAB self-report zeroed (like RAB_discrepancy) AND both probes forced to fail regardless of DangerCascadeController.probeMode — nothing this robot does can pass the discrepancy channel or any probe test.
        None
    }

    public FaultType selectedFault;
    public List<string> faulty_robots = new List<string> { "0" }; // bare suffix — no "remora" prefix needed
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

        // Independent of faulty_robots — softwareHangDriveRobotId is a separate, directly-configured
        // target (e.g. a healthy investigator robot you want actively running a pattern while
        // SoftwareHang is live), not necessarily one of the faulty robots itself.
        if (patternSelector != null)
        {
            if (injectFault && selectedFault == FaultType.SoftwareHang)
                patternSelector.SetPatternOverride(softwareHangDriveRobotId, softwareHangDrivePattern);
            else
                patternSelector.ClearPatternOverride(softwareHangDriveRobotId);
        }

        if (injectFault)
        {
            if (faultInjectionTime < 0f) // Only set once
                faultInjectionTime = Time.time;

            foreach (string robotId in faulty_robots)
            {
                string robotName = "remora" + robotId;
                var _FaultyActuators = GameObject.Find(robotName).GetComponentInChildren<CmdVel_Subscriber>();
                var _FaultyRabSensor = GameObject.Find(robotName).GetComponentInChildren<RABSensor>();
                var _FaultyRemoraLidar = GameObject.Find(robotName).GetComponentInChildren<RemoraLIDAR>();
                var _AdaptiveSamplingPattern = GameObject.Find(robotName).GetComponent<AdaptiveSamplingPattern>();
                var _FaultyBlinkEmitter = GameObject.Find(robotName).GetComponentInChildren<BlinkEmitter>();
                var _FaultyActuatorEmitter = GameObject.Find(robotName).GetComponentInChildren<ActuatorEmitter>();

                if (_FaultyActuators == null || _FaultyRabSensor == null || _FaultyRemoraLidar == null)
                    continue;

                // Reset every cycle, only SoftwareHang below sets it — avoids having to repeat a
                // reset call in every other case just to avoid state leaking across fault switches.
                if (_FaultyBlinkEmitter != null) _FaultyBlinkEmitter.SetSimulatedHang(false);
                if (_FaultyActuatorEmitter != null) _FaultyActuatorEmitter.SetSimulatedHang(false);

                switch (selectedFault)
                {
                    case FaultType.Lact:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(1, 0);
                        _manualController.SetMotorStates(1, 0);
                        _FaultyActuators.SetMotorStates(1, 0);
                        _FaultyActuators.SetMotorStates(1, 0);
                        break;

                    case FaultType.Ract:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(0, 1);
                        _manualController.SetMotorStates(0, 1);
                        _FaultyActuators.SetMotorStates(0, 1);
                        _FaultyActuators.SetMotorStates(0, 1);
                        break;

                    case FaultType.Bact:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(1, 1);
                        _manualController.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        break;

                    case FaultType.Rofs:
                        _FaultyRabSensor.SetRABState(1);
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.Pmin:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState(1, "Pmin");
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.Pmax:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState(1, "Pmax");
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.Prnd:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState(1, "Prnd");
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.GradualMotor:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
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
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();
                        _AdaptiveSamplingPattern.EnableIntermittentFault();
                        break;

                    case FaultType.RAB_discrepancy:
                        // Real physical fault: this robot's own RAB self-report reads 0 detected
                        // neighbours (GetSelfReportedNeighborCount()), while GetDetections() (used
                        // for all real feature computation) and GetTrueNeighborCount() (what
                        // neighbours use to see the truth) are both untouched.
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault(1);
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        break;

                    case FaultType.BACT_discrepancy:
                        // Real physical fault, same motor-off effect as Bact — but unlike Bact,
                        // the point is the leftover discrepancy: CmdVel_Subscriber.GetIntentVelocities()
                        // keeps reporting normal commanded intent (untouched by the motor-state
                        // fault) while the robot doesn't actually move, so a neighbour comparing
                        // intent against real observed motion sees the mismatch.
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(1, 1);
                        _manualController.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        break;

                    case FaultType.CompleteFailure:
                        // Everything fails at once — real physical actuator-off (same as Bact,
                        // which also feeds the actuator-discrepancy signature exactly like
                        // BACT_discrepancy), PLUS the RAB self-report discrepancy (same as
                        // RAB_discrepancy: self-reported neighbour count forced to 0, true count
                        // untouched), PLUS an explicit blink hang. Motor-off alone only guarantees
                        // the ACTUATOR probe fails — BlinkEmitter has no physical/motor connection
                        // at all (it's a fully simulated optical channel), so without the hang it
                        // would keep blinking correctly and pass every time. This fault is meant to
                        // trip the discrepancy channel via BOTH signatures and fail regardless of
                        // DangerCascadeController.probeMode (Blink-only, Actuator-only, or Both),
                        // so every failure mechanism is forced explicitly rather than relying on
                        // just one.
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault(1);
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates(1, 1);
                        _manualController.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        _FaultyActuators.SetMotorStates(1, 1);
                        if (_FaultyBlinkEmitter != null) _FaultyBlinkEmitter.SetSimulatedHang(true);
                        break;

                    case FaultType.SoftwareHang:
                        // Robot moves/senses normally — no real hang mechanism exists in-sim, so
                        // the closest real proxy is making the probe target simply not respond,
                        // exactly like a genuinely hung robot's control loop wouldn't.
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
                        _FaultyRemoraLidar.SetSensorState();
                        _manualController.SetMotorStates();
                        _manualController.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        _FaultyActuators.SetMotorStates();
                        if (_FaultyBlinkEmitter != null) _FaultyBlinkEmitter.SetSimulatedHang(true);
                        if (_FaultyActuatorEmitter != null) _FaultyActuatorEmitter.SetSimulatedHang(true);
                        break;

                    default:
                        _FaultyRabSensor.SetRABState();
                        _FaultyRabSensor.SetNeighborDiscrepancyFault();
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
    

    // faulty_robots stores bare suffixes ("0"), but every other script checks against full robot
    // names ("remora0") — this is the one place that convention gets bridged, so it doesn't leak
    // into GlobalCSV/remora_gizmos/DangerCascadeController.
    public bool IsFaultyRobot(string fullRobotName)
    {
        string id = fullRobotName.StartsWith("remora") ? fullRobotName.Substring("remora".Length) : fullRobotName;
        return faulty_robots.Contains(id);
    }

    void initializeComponents()
    {
        GameObject[] healthy_robotIDs = GameObject.FindGameObjectsWithTag("remora_bot");

        foreach (var robot in healthy_robotIDs)
        {
            if (IsFaultyRobot(robot.name)) continue;

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
    public void SetFaultyRobotRandom()
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
            string id = robot.name.StartsWith("remora") ? robot.name.Substring("remora".Length) : robot.name;
            faulty_robots.Add(id);
            // Debug.Log($"Randomly selected faulty robot: {robot.name}");
        }
    }

    // Deterministic counterpart to SetFaultyRobotRandom() above — picks exactly one robot by its
    // spawn index (RobotSpawner names robots "remora{i}", see RobotSpawner.cs) instead of a random
    // top-5 set, for when you want a specific, repeatable robot to be faulty.
    public void SetFaultyRobot(int robotNumber)
    {
        faulty_robots.Clear();
        faulty_robots.Add(robotNumber.ToString());
    }
}