using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dummy BFV sim (dummy_bfv_sim_brief.md) — deterministic, physics-free/ROS2-free swarm driven
/// entirely by hardcoded BFVs instead of live moving robots. Drives the real CRMInstanceDanger
/// engine (unmodified) plus the ported DummyDangerLadderEngine (a verbatim, decoupled copy of
/// DangerCascadeController's state machine) so the cleared-tag-duration optimization can be
/// studied without physics/ROS2 non-determinism.
///
/// Fully-connected model (Section 6): every robot observes every other, so BFV abundance counts
/// are global — identical for every observer. Per the user's explicit choice, the sim still
/// mirrors the live architecture's full N x N structure (one CRMInstanceDanger + one set of
/// ladder entries per robot AS OBSERVER, keyed by every other robot it observes), even though
/// every observer's copy computes byte-identical sumE/sumR for a given target.
/// </summary>
public class DummyBFVSwarm : MonoBehaviour
{
    [Header("BFV groups (Section 3) — expandable list, each entry = { BFV, robot count }")]
    public List<DummyBFVGroup> bfvGroups = new List<DummyBFVGroup>();

    [Header("Total robots — read-only, sum of group counts, recomputed on (re)build")]
    public int totalRobotCount;

    [Header("Fault injection (Section 5) — assign the scene's existing FaultInjector, no new UI")]
    public FaultInjector faultInjector;

    [Header("CLEARED memory tag (Section 5) — THE variable to sweep in the study")]
    public float clearedTagDuration = 20f;

    [Header("Probe window T (Section 8) — must match the live sim's probe duration")]
    public float probeWindowT = 3f;

    // FeatureExtractor.Wc = 1f — the live sim only produces a new BFV sample (and only runs
    // DangerCascadeController's CRM step + ladder tick) once per this many real seconds, not
    // every physics tick. CRM_HISTORY_LENGTH (9) windowed cycles therefore span ~9 real seconds
    // at the default cadence — must match here for PRIMED/etc. durations to be comparable.
    [Header("Control cycle (Section 4) — matches FeatureExtractor.Wc = 1s in the live sim")]
    public float controlCycleSeconds = 1f;

    [Header("Danger extension + cap ladder (Sections 5, 10)")]
    public bool dangerExtensionEnabled = true;
    public double investigationCap = 20.0;
    public double E_t = 60.0;
    [Range(0f, 1f)] public float thetaDisc = 0.3f;

    [Header("CRM parameter passthrough (Section 10) — defaults match CRMInstanceDanger's own")]
    public double c = 0.11;
    public double pi_E = 1e-3;
    public double pi_R = 0.7e-3;

    public class VirtualRobot
    {
        public int id;
        public int groupIndex; // which bfvGroups entry this robot came from — for live BFV re-sync
        public string robotName;
        public int currentBFV;
        public bool isFaulty;
        public bool discrepancyFault;

        public CRMInstanceDanger crmInstance = new CRMInstanceDanger();
        public DummyProbe probe = new DummyProbe();

        // Per-observed-target state, keyed by the observed robot's id — mirrors
        // DangerCascadeController's own per-observer ladders/crmHistory dictionaries.
        public Dictionary<int, DummyDangerLadderEngine.LadderEntry> ladders = new Dictionary<int, DummyDangerLadderEngine.LadderEntry>();
        public Dictionary<int, Queue<int>> classificationHistories = new Dictionary<int, Queue<int>>();

        // Outputs — read externally by DummyHeatmapDanger. verdicts[targetId]/states[targetId] is
        // THIS robot's (as observer) conclusion about targetId.
        public Dictionary<int, bool> verdicts = new Dictionary<int, bool>();
        public Dictionary<int, DangerCascadeController.RobotFaultState> states = new Dictionary<int, DangerCascadeController.RobotFaultState>();
    }

    public List<VirtualRobot> robots = new List<VirtualRobot>();

    private int faultTargetRobotId = -1;

    // Own simulated clock (accumulated fixed-step time), deliberately not Time.time — keeps
    // clearedTagDuration expiry self-contained and reproducible independent of when this
    // component happened to start relative to the rest of the scene.
    private float simTime = 0f;

    void Awake()
    {
        BuildSwarm();
    }

    public void BuildSwarm()
    {
        robots.Clear();
        faultTargetRobotId = -1;

        int nextId = 0;
        for (int g = 0; g < bfvGroups.Count; g++)
        {
            var group = bfvGroups[g];
            for (int i = 0; i < group.robotCount; i++)
            {
                var robot = new VirtualRobot
                {
                    id = nextId,
                    groupIndex = g,
                    robotName = $"{group.label}_{nextId}",
                    currentBFV = group.FvIndex,
                };
                robot.probe.T = probeWindowT;

                if (group.isFaultTarget && faultTargetRobotId < 0)
                    faultTargetRobotId = robot.id;

                robots.Add(robot);
                nextId++;
            }
        }

        totalRobotCount = robots.Count;

        // Every robot observes every other (Section 6 — fully-connected swarm).
        foreach (var observer in robots)
        {
            foreach (var target in robots)
            {
                if (target.id == observer.id) continue;
                observer.ladders[target.id] = new DummyDangerLadderEngine.LadderEntry();
                observer.classificationHistories[target.id] = new Queue<int>();
            }
        }
    }

    private float controlCycleTimer = 0f;

    void FixedUpdate()
    {
        if (robots.Count == 0) return;

        float dt = Time.fixedDeltaTime;
        simTime += dt;

        ApplyFaultInjection();
        SyncBFVFromInspector();

        // Probe clocks run continuously, independent of the CRM sample-cycle cadence below —
        // matches BlinkEmitter/ActuatorEmitter's own per-frame clock in the live sim (Section 8).
        foreach (var robot in robots)
            robot.probe.Tick(dt);

        // CRM step + ladder tick only run once per controlCycleSeconds (Section 4), matching the
        // live sim's ~1 sample/cycle-per-Wc cadence — NOT every physics tick. A while-loop (not
        // "if") so multiple due cycles still all run if a frame hitch skips ahead.
        controlCycleTimer += dt;
        while (controlCycleTimer >= controlCycleSeconds)
        {
            controlCycleTimer -= controlCycleSeconds;
            RunControlCycle();
        }
    }

    // Point 5 — lets BFV checkboxes be toggled live in Play mode: robots aren't re-instantiated,
    // just re-read their own group's current FvIndex every tick. Robot COUNT changes (adding/
    // removing group entries or editing robotCount) still require BuildSwarm() (a full reset).
    void SyncBFVFromInspector()
    {
        foreach (var robot in robots)
            if (robot.groupIndex >= 0 && robot.groupIndex < bfvGroups.Count)
                robot.currentBFV = bfvGroups[robot.groupIndex].FvIndex;
    }

    void RunControlCycle()
    {
        // Section 6 — single global fvCounts dict, identical for every observer.
        var fvCounts = new Dictionary<int, int>();
        foreach (var robot in robots)
        {
            fvCounts.TryGetValue(robot.currentBFV, out int count);
            fvCounts[robot.currentBFV] = count + 1;
        }

        // Each robot as observer: advance its own CRM engine on the shared global counts.
        foreach (var observer in robots)
        {
            observer.crmInstance.c = c;
            observer.crmInstance.pi_E = pi_E;
            observer.crmInstance.pi_R = pi_R;
            observer.crmInstance.dangerExtensionEnabled = dangerExtensionEnabled;
            observer.crmInstance.SimulationStep(fvCounts);
        }

        // Each (observer, target) pair: run the ported ladder engine.
        foreach (var observer in robots)
        {
            foreach (var target in robots)
            {
                if (target.id == observer.id) continue;

                double rawE = observer.crmInstance.sumE[target.currentBFV];
                double rawR = observer.crmInstance.sumR[target.currentBFV];
                int fvClassification = observer.crmInstance.FVClassification[target.currentBFV];

                float discrepancySignal = target.discrepancyFault ? 1f : 0f;

                bool verdict = DummyDangerLadderEngine.Tick(
                    observer.ladders[target.id],
                    rawE, rawR,
                    discrepancySignal, thetaDisc,
                    fvClassification, observer.classificationHistories[target.id],
                    target.probe, target.probe,
                    investigationCap, E_t, clearedTagDuration,
                    dangerExtensionEnabled,
                    simTime);

                observer.verdicts[target.id] = verdict;
                observer.states[target.id] = observer.ladders[target.id].state;
            }
        }
    }

    // Section 5 — reads the existing FaultInjector's public fields only (injectFault,
    // selectedFault). Never depends on FaultInjector's own Update()/GameObject.Find side effects,
    // and never touches faulty_robots (that's a real-robot-name list; the dummy fault target is
    // fixed by whichever bfvGroups entry has isFaultTarget = true).
    void ApplyFaultInjection()
    {
        foreach (var robot in robots)
        {
            robot.isFaulty = false;
            robot.discrepancyFault = false;
            robot.probe.isFaulty = false;
        }

        if (faultInjector == null || faultTargetRobotId < 0) return;
        if (!faultInjector.injectFault || faultInjector.selectedFault == FaultInjector.FaultType.None) return;

        var target = robots[faultTargetRobotId];
        target.isFaulty = true;
        target.probe.isFaulty = true;

        // Section 7 — discrepancy fires independently of whether the BFV is an outlier.
        if (faultInjector.selectedFault == FaultInjector.FaultType.RAB_discrepancy ||
            faultInjector.selectedFault == FaultInjector.FaultType.BACT_discrepancy)
        {
            target.discrepancyFault = true;
        }
    }
}
