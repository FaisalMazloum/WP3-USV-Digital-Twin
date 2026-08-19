using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Danger-gated CRM orchestrator (danger_crm_implementation_brief.md). One per robot — this
/// robot is the OBSERVER; CRM's E/R math stays fully decentralized/subjective exactly like
/// vanilla CRM (see CRMHeatmap's row=observer/col=observed grid — two observers can reach
/// different verdicts about the same robot).
///
/// Drives CRMInstanceDanger every sample cycle, reusing FeatureExtractor.lastFvCounts so the
/// fvCounts-building loop isn't duplicated. When dangerExtensionEnabled is false, this produces
/// exactly the same FVClassification-based verdict vanilla CRM would (CRMInstanceDanger's raw
/// output never depends on the flag at all — see that class). Only when true does this also run
/// the always-on discrepancy arm (Section 7) and the per-observed-robot effector cap ladder /
/// state machine (Section 5), transitions gated by the same rolling-window-majority approach
/// vanilla CRM already uses (FeatureExtractor.CRM_HISTORY_LENGTH) — Section 2.
/// </summary>
public class DangerCascadeController : MonoBehaviour
{
    public enum RobotFaultState { NORMAL, PRIMED, DISCREPANCY_PASSED, PROBING, FAULT, CLEARED }
    public enum ProbeType { Blink, Actuator }

    [Header("Master switch (Section 1) — false must reproduce vanilla CRM exactly")]
    public bool dangerExtensionEnabled = false;

    [Header("Probe type (Section 6) — only the selected probe runs during PROBING")]
    public ProbeType probeType = ProbeType.Blink;

    [Header("CRM parameter passthrough (Section 8) — defaults match vanilla CRM's consts exactly")]
    // public double c = 0.20;
    public double c = 0.11;
    public double pi_E = 1e-3;
    public double pi_R = 0.7e-3;

    // Rough starting point only — R/E have no fixed scale (they grow from SEED_E/SEED_R=10 via
    // repeated +10 bolus injections while a clone stays active, tempered by proliferation/death),
    // so these must be tuned against what you actually observe for R and E in your own run (see
    // the orange gizmo panel) rather than trusted as-is.
    //
    // Originally two separate rungs (s1 for PRIMED, s2 for DISCREPANCY_PASSED/PROBING), per
    // Section 5/11 of the brief. Collapsed to one: neither ever gated a transition (those are
    // decided by E>R, discrepancy majority, and the probe result — never by comparing E to a
    // cap), and for the verdict, all that ever mattered was investigationCap < E_t — the specific
    // value never changed any outcome. Two rungs bought nothing but a cosmetic staircase in the
    // Emax column of the CSV/gizmo; not worth the extra parameter.
    [Header("Effector cap ladder (Sections 5, 11) — must satisfy R < investigationCap < E_t")]
    public double investigationCap = 20.0;
    public double E_t = 60.0;

    [Header("Discrepancy signal (Section 4d)")]
    [Range(0f, 1f)] public float thetaDisc = 0.3f;
    public int maxPossibleNeighbors = 9; // normalization denominator — NUM_ROBOTS - 1

    // BACT_discrepancy: simple absolute per-wheel tolerance (cm/s) — |intent - actual| > this on
    // either wheel counts as a discrepancy this cycle. Now that CmdVel_Subscriber's drive is
    // kinematic (no PID lag), actual tracks intent essentially exactly for a healthy robot, so a
    // flat tolerance is enough without the ratio/settle-window machinery this used to need.
    public float actuatorToleranceCm = 5f;

    // Note: Nmin/Nmax/T (Section 6) are NOT duplicated here — each robot's own BlinkEmitter
    // owns those directly, since it's the target's probe capability, not something any one
    // observer's inspector should be able to override.

    [Header("CLEARED memory tag (Section 5)")]
    public float clearedTagDuration = 20f;

    [Header("Wiring")]
    public FeatureExtractor featureExtractor;
    public FaultInjector faultInjector; // optional — only used for the CSV ground-truth label column
    // Optional. Leave unassigned to keep the original purely-per-observer behavior (no verdict
    // syncing — ladder entry is always single-observer-triggered regardless). Auto-found (and
    // retried each FixedUpdate until found) if left empty. See SwarmConsensusCoordinator.cs.
    public SwarmConsensusCoordinator swarmConsensus;
    public bool exportCSV = false;
    public bool logStateTransitions = false; // Console.Log every ladder state change, for testing

    public CRMInstanceDanger crmInstanceDanger = new CRMInstanceDanger();

    // Outputs — read externally by HeatmapDanger, mirroring FeatureExtractor.isFaulty
    public Dictionary<GameObject, bool> finalVerdict = new Dictionary<GameObject, bool>();
    public Dictionary<GameObject, RobotFaultState> currentState = new Dictionary<GameObject, RobotFaultState>();

    // Mirrors FeatureExtractor.crmHistory exactly (same CRM_HISTORY_LENGTH window, same
    // majority-vote rule) — always maintained regardless of dangerExtensionEnabled, so the
    // disabled-extension verdict below is a true match for FeatureExtractor.isFaulty (which is
    // majority-voted, not the raw per-cycle FVClassification), and so gizmos/tools can compare
    // the two engines' attack-count fields directly.
    public Dictionary<GameObject, Queue<int>> crmHistory = new Dictionary<GameObject, Queue<int>>();

    private class LadderEntry
    {
        public RobotFaultState state = RobotFaultState.NORMAL;
        public Queue<bool> outlierWindow = new Queue<bool>();
        public Queue<bool> discrepancyWindow = new Queue<bool>();

        // Separate from discrepancyWindow above: that one is PRIMED-episode-scoped (cleared on
        // entry, used only to decide the PRIMED -> FAULT/DISCREPANCY_PASSED transition). This one
        // is continuous and never cleared, so the always-on Section 7 arm (which must work even
        // for robots that never enter the ladder) is smoothed the same way vanilla CRM smooths
        // isFaulty — a single noisy cycle can't flip the verdict on its own.
        public Queue<bool> alwaysOnDiscrepancyWindow = new Queue<bool>();

        public float clearedTime = -1f;
    }

    private Dictionary<GameObject, LadderEntry> ladders = new Dictionary<GameObject, LadderEntry>();

    // Discrepancy signal (Section 4d) now reads directly off each target's own RABSensor
    // (GetSelfReportedNeighborCount/GetTrueNeighborCount) — the RAB_discrepancy fault
    // (FaultInjector) drives this for real instead of a simulated proxy toggle.
    private Dictionary<GameObject, RABSensor> observedRabSensors = new Dictionary<GameObject, RABSensor>();

    // Actuator-intent-vs-actual discrepancy (BACT_discrepancy) — both intent and actual come
    // straight off the target's own CmdVel_Subscriber/Rigidbody, ground truth, zero ROS
    // involvement (see ComputeActuatorDiscrepancy).
    private Dictionary<GameObject, CmdVel_Subscriber> observedCmdVelSubscribers = new Dictionary<GameObject, CmdVel_Subscriber>();

    // PROBING now always dispatches/reads BOTH probes — see class docs.
    private Dictionary<GameObject, ActuatorEmitter> actuatorEmitters = new Dictionary<GameObject, ActuatorEmitter>();
    private Dictionary<GameObject, BlinkEmitter> blinkEmitters = new Dictionary<GameObject, BlinkEmitter>();
    private ActuatorReader actuatorReader;
    private BlinkReader blinkReader;

    // Background CRM thread — mirrors FeatureExtractor's own crmInstance threading, so the
    // (heavier, duplicate) RK2 integration doesn't stall the main thread.
    private System.Threading.Thread crmThread;
    private volatile bool crmRunning = false;
    private volatile bool crmResultReady = false;
    private Dictionary<int, int> fvCountsPending = new Dictionary<int, int>();
    private object crmLock = new object();

    private bool prevCreatingNewSample = true;
    private float startTime;
    private bool warnedMissingCoordinator = false;

    private StringBuilder csvBuffer = new StringBuilder();
    private bool csvHeaderWritten = false;

    void Start()
    {
        if (featureExtractor == null)
            featureExtractor = GetComponent<FeatureExtractor>();
        if (featureExtractor == null)
            Debug.LogError($"{transform.root.name}: DangerCascadeController found no FeatureExtractor!");

        // swarmConsensus auto-find happens in FixedUpdate (retried until found — see there for why
        // a one-shot attempt here isn't reliable). Optional either way — this whole feature is
        // opt-in (see field comment above).
        startTime = Time.time;

        actuatorReader = GetComponent<ActuatorReader>();
        if (actuatorReader == null)
            Debug.LogError($"{transform.root.name}: DangerCascadeController found no ActuatorReader!");

        blinkReader = GetComponent<BlinkReader>();
        if (blinkReader == null)
            Debug.LogError($"{transform.root.name}: DangerCascadeController found no BlinkReader!");

        if (!(0.0 < investigationCap && investigationCap < E_t))
            Debug.LogWarning($"{transform.root.name}: cap ladder ordering investigationCap < E_t is " +
                $"violated (investigationCap={investigationCap}, E_t={E_t}) — Section 5 assumes this ordering.");
    }

    // Edge-detecting featureExtractor.creatingNewSample must happen in FixedUpdate, not Update.
    // FeatureExtractor flips that flag inside ITS OWN FixedUpdate, on the fixed-step clock — if
    // this were polled from Update instead, a slow/dropped render frame (Editor overhead, 10
    // robots, ROS2, physics) could let several FixedUpdate ticks elapse between two Update calls
    // and silently skip a whole sample cycle's CRM step, which would desync CRMInstanceDanger's
    // TE/TR from CRMInstance's (same formulas, but fed fewer bolus injections/integrations than
    // CRMInstance received). Reading it once per FixedUpdate — the same cadence it's written on —
    // guarantees every transition is observed.
    void FixedUpdate()
    {
        if (featureExtractor == null) return;

        // Retry every cycle until found, not just once in Start() — Unity doesn't guarantee
        // Start() ordering across GameObjects, so a robot spawned (or just initialized) before
        // SwarmConsensusCoordinator's own Start() had run would otherwise be permanently stuck
        // with swarmConsensus == null for the rest of the session, silently never syncing while
        // other robots (whichever happened to initialize later) worked fine. Stops looking once
        // found — cheap no-op afterward.
        if (swarmConsensus == null)
        {
            swarmConsensus = FindObjectOfType<SwarmConsensusCoordinator>();
            if (swarmConsensus != null && logStateTransitions)
                Debug.Log($"{transform.root.name}: acquired SwarmConsensusCoordinator (instance {swarmConsensus.GetInstanceID()}).");

            // Loud, one-time, hard-to-miss signal if there's genuinely no coordinator anywhere in
            // the scene — rules that out directly instead of leaving it a guess.
            if (swarmConsensus == null && !warnedMissingCoordinator && Time.time - startTime > 5f)
            {
                warnedMissingCoordinator = true;
                Debug.LogWarning($"{transform.root.name}: no SwarmConsensusCoordinator found in the scene after " +
                    "5s. Verdict syncing is OFF for this robot (falling back to purely independent behavior) — " +
                    "add exactly one SwarmConsensusCoordinator component to a GameObject in the scene to enable it.");
            }
        }

        bool nowCreating = featureExtractor.creatingNewSample;
        if (prevCreatingNewSample && !nowCreating)
        {
            LaunchCrmStep();
        }
        prevCreatingNewSample = nowCreating;
    }

    void Update()
    {
        if (featureExtractor == null) return;

        // Keep the engine's parameter copies in sync with the inspector every frame, so
        // sweeping investigationCap/E_t/c/pi_E/pi_R/etc. at runtime takes effect immediately.
        crmInstanceDanger.c = c;
        crmInstanceDanger.pi_E = pi_E;
        crmInstanceDanger.pi_R = pi_R;
        crmInstanceDanger.dangerExtensionEnabled = dangerExtensionEnabled;

        if (crmResultReady)
        {
            crmResultReady = false;
            RunDangerCycle();
        }

        if (exportCSV)
        {
            exportCSV = false;
            FlushCsv();
        }
    }

    void LaunchCrmStep()
    {
        if (crmRunning) return;

        lock (crmLock)
        {
            fvCountsPending = new Dictionary<int, int>(featureExtractor.lastFvCounts);
        }
        crmRunning = true;
        crmThread = new System.Threading.Thread(() =>
        {
            crmInstanceDanger.SimulationStep(fvCountsPending);
            crmResultReady = true;
            crmRunning = false;
        });
        crmThread.IsBackground = true;
        crmThread.Start();
    }

    // =========================================================================
    // Per-cycle danger logic — only runs once CRMInstanceDanger's step (launched
    // in LaunchCrmStep) has completed.
    // =========================================================================
    void RunDangerCycle()
    {
        bool loggingActive = featureExtractor._csvExporter != null && featureExtractor._csvExporter._start;

        foreach (var kvp in featureExtractor.feature_list_history)
        {
            GameObject observedRobot = kvp.Key;
            var samples = kvp.Value;
            if (samples == null || samples.Count == 0) continue;

            var latest = samples[samples.Count - 1];
            int fvIndex = (latest.F1 << 0) | (latest.F2 << 1) | (latest.F3 << 2) |
                          (latest.F4 << 3) | (latest.F5 << 4) | (latest.F6 << 5);

            if (!ladders.TryGetValue(observedRobot, out LadderEntry ladder))
            {
                ladder = new LadderEntry();
                ladders[observedRobot] = ladder;
            }

            double rawE = crmInstanceDanger.sumE[fvIndex];
            double rawR = crmInstanceDanger.sumR[fvIndex];

            // ---- Majority-vote history, exactly like FeatureExtractor.UpdateFaultStatus ----
            // Maintained unconditionally (not just when the ladder is active) so it's always a
            // valid stand-in for what vanilla CRM's isFaulty would show for this same FV stream.
            if (!crmHistory.TryGetValue(observedRobot, out Queue<int> classificationHistory))
            {
                classificationHistory = new Queue<int>();
                crmHistory[observedRobot] = classificationHistory;
            }
            classificationHistory.Enqueue(crmInstanceDanger.FVClassification[fvIndex]);
            if (classificationHistory.Count > FeatureExtractor.CRM_HISTORY_LENGTH)
                classificationHistory.Dequeue();

            int attackCount = 0;
            foreach (int cls in classificationHistory)
                if (cls == 1) attackCount++;
            bool majorityAttack = attackCount > FeatureExtractor.CRM_HISTORY_LENGTH / 2;

            // ---- Extension off: mirror vanilla CRM's verdict exactly (majority vote, same as
            //      FeatureExtractor.isFaulty), ladder stays parked ----
            if (!dangerExtensionEnabled)
            {
                finalVerdict[observedRobot] = majorityAttack;
                currentState[observedRobot] = RobotFaultState.NORMAL;
                continue;
            }

            // ---- Discrepancy (Sections 4d, 7) — always on, independent of ladder state.
            //      Windowed/majority-voted (same 9-cycle rule as everything else) so a single
            //      noisy cycle can't flip the verdict — this is the fix for a flickering
            //      discrepancy signal producing a flickering verdict. ----
            float discrepancy = ComputeDiscrepancy(observedRobot);
            EnqueueWindow(ladder.alwaysOnDiscrepancyWindow, discrepancy > thetaDisc);
            bool discrepancyMajority = WindowMajority(ladder.alwaysOnDiscrepancyWindow);

            // ---- Adopt a swarm-wide conclusion another observer already reached, so this
            //      observer doesn't redundantly re-run its own full investigation once the swarm
            //      has already spoken. FAULT is terminal — never downgraded back to CLEARED by an
            //      older broadcast once independently confirmed. No-op if swarmConsensus is unset. ----
            if (swarmConsensus != null && ladder.state != RobotFaultState.FAULT
                && swarmConsensus.TryGetCanonicalVerdict(observedRobot, out RobotFaultState broadcastState, out float broadcastTime))
            {
                if (broadcastState == RobotFaultState.FAULT)
                {
                    if (logStateTransitions)
                        Debug.Log($"{transform.root.name}: ADOPTED {observedRobot.name} -> FAULT " +
                            $"(was {ladder.state}, broadcast at t={broadcastTime:F1}, coordinator instance {swarmConsensus.GetInstanceID()}).");
                    ladder.state = RobotFaultState.FAULT;
                }
                else if (broadcastState == RobotFaultState.CLEARED && ladder.state != RobotFaultState.CLEARED
                    && (Time.time - broadcastTime) <= clearedTagDuration)
                {
                    // The staleness check above is the actual fix for a real bug: the coordinator
                    // never removes an old broadcast, so without checking whether it's already
                    // expired, this would re-adopt CLEARED (with the SAME old timestamp) every
                    // single cycle forever, even long after it should have lapsed. Since adoption
                    // runs before the switch statement below, that re-adoption pre-empted the
                    // NORMAL case's own E>R check every cycle too — permanently freezing the
                    // ladder out of ever re-entering PRIMED, regardless of what fault came later.
                    if (logStateTransitions)
                        Debug.Log($"{transform.root.name}: ADOPTED {observedRobot.name} -> CLEARED " +
                            $"(was {ladder.state}, broadcast at t={broadcastTime:F1}, coordinator instance {swarmConsensus.GetInstanceID()}).");
                    ladder.state = RobotFaultState.CLEARED;
                    ladder.clearedTime = broadcastTime; // sync expiry timing across the swarm
                }
            }

            // ---- Emax for the CURRENT state, applied read-only to this cycle's verdict.
            //      Never written back into TE/TR — the shared clone ODE keeps integrating
            //      untouched, which is what lets two observers cap the same FV differently. ----
            double Emax = GetEmaxForState(ladder.state, rawR);
            double clampedE = Math.Min(rawE, Emax);
            bool ladderFault = clampedE > E_t;

            // Section 5 is explicit that FAULT must "populate heatmap as faulty" unconditionally
            // — it's a terminal classification, not a live re-evaluated one. Everywhere else,
            // Emax < E_t by construction (R < investigationCap < E_t), so ladderFault is
            // structurally only ever true once FAULT has already removed the cap; the OR here is
            // just a safety net in case that ordering is ever violated (Start() warns if so).
            bool verdict = (ladder.state == RobotFaultState.FAULT) || discrepancyMajority || ladderFault;

            finalVerdict[observedRobot] = verdict;
            currentState[observedRobot] = ladder.state;

            ProbeResult probeResult = ProbeResult.Pending;
            RobotFaultState stateBefore = ladder.state;

            // ---- State machine transitions (decide state for NEXT cycle) — Section 5 ----
            switch (ladder.state)
            {
                case RobotFaultState.NORMAL:
                    // Any single observer's own sustained outlier read triggers entry — no
                    // cross-robot majority required (reverted per your call: single-robot trigger,
                    // swarm-wide sync only on the eventual FAULT/CLEARED conclusion, not the gate).
                    EnqueueWindow(ladder.outlierWindow, rawE > rawR);
                    if (WindowMajority(ladder.outlierWindow))
                    {
                        ladder.state = RobotFaultState.PRIMED;
                        ladder.discrepancyWindow.Clear();
                    }
                    break;

                case RobotFaultState.PRIMED:
                    EnqueueWindow(ladder.discrepancyWindow, discrepancy > thetaDisc);
                    if (ladder.discrepancyWindow.Count >= FeatureExtractor.CRM_HISTORY_LENGTH)
                    {
                        ladder.state = WindowMajority(ladder.discrepancyWindow)
                            ? RobotFaultState.FAULT               // sensor fault
                            : RobotFaultState.DISCREPANCY_PASSED;
                    }
                    break;

                case RobotFaultState.DISCREPANCY_PASSED:
                {
                    // Only the selected probeType is dispatched — the other emitter is never
                    // touched, so it can't be running "in the background" alongside it.
                    if (probeType == ProbeType.Actuator)
                    {
                        ActuatorEmitter actuatorEmitter = GetActuatorEmitter(observedRobot);
                        if (actuatorEmitter != null) actuatorEmitter.RequestProbe();
                    }
                    else
                    {
                        BlinkEmitter blinkEmitter = GetBlinkEmitter(observedRobot);
                        if (blinkEmitter != null) blinkEmitter.RequestProbe();
                    }
                    ladder.state = RobotFaultState.PROBING;
                    break;
                }

                case RobotFaultState.PROBING:
                {
                    // Deliberately NOT calling ResetProbe() — same multi-observer-safe reasoning
                    // as before: the result stays put until the next RequestProbe() naturally
                    // supersedes it, so every observer with a ladder in PROBING for this target
                    // can read independently at its own pace instead of racing to consume first.
                    ProbeResult result;
                    if (probeType == ProbeType.Actuator)
                    {
                        ActuatorEmitter actuatorEmitter = GetActuatorEmitter(observedRobot);
                        result = (actuatorEmitter != null && actuatorReader != null)
                            ? actuatorReader.ReadResult(actuatorEmitter) : ProbeResult.Pending;
                    }
                    else
                    {
                        BlinkEmitter blinkEmitter = GetBlinkEmitter(observedRobot);
                        result = (blinkEmitter != null && blinkReader != null)
                            ? blinkReader.ReadResult(blinkEmitter) : ProbeResult.Pending;
                    }

                    if (result != ProbeResult.Pending)
                    {
                        probeResult = result;
                        ladder.state = result == ProbeResult.Pass ? RobotFaultState.CLEARED : RobotFaultState.FAULT;
                        if (result == ProbeResult.Pass)
                            ladder.clearedTime = Time.time;
                    }
                    // else: at least one probe still running, stay in PROBING.
                    break;
                }

                case RobotFaultState.FAULT:
                    // Terminal for this episode (Section 5) — flag/log only, no recovery.
                    break;

                case RobotFaultState.CLEARED:
                    // Purely time-based expiry, deliberately no BFV re-challenge (removed per your
                    // call) — a CLEARED tag holds for the full clearedTagDuration regardless of
                    // behavior change, for both independently-earned and adopted CLEARED alike.
                    if ((Time.time - ladder.clearedTime) > clearedTagDuration)
                    {
                        ladder.state = RobotFaultState.NORMAL;
                        ladder.outlierWindow.Clear();
                        ladder.discrepancyWindow.Clear();
                    }
                    break;
            }

            if (ladder.state != stateBefore)
            {
                if (logStateTransitions)
                    Debug.Log($"{transform.root.name}: {observedRobot.name} ladder {stateBefore} -> {ladder.state} " +
                        $"(E={rawE:F2}, R={rawR:F2}, discrepancy={discrepancy:F2})");

                // Announce a freshly-reached conclusion so every other observer can adopt it next
                // cycle instead of redundantly re-running its own investigation. No-op if
                // swarmConsensus is unset.
                if (swarmConsensus != null
                    && (ladder.state == RobotFaultState.FAULT || ladder.state == RobotFaultState.CLEARED))
                {
                    swarmConsensus.BroadcastVerdict(observedRobot, ladder.state, Time.time);
                }
            }

            if (loggingActive)
                AppendCsvRow(observedRobot, fvIndex, discrepancy, ladder.state, Emax, rawE, rawR, probeResult, verdict);
        }

        // Clean up robots no longer observed — mirrors FeatureExtractor.UpdateFaultStatus.
        var departed = new List<GameObject>();
        foreach (var robot in ladders.Keys)
            if (!featureExtractor.feature_list_history.ContainsKey(robot))
                departed.Add(robot);
        foreach (var robot in departed)
        {
            ladders.Remove(robot);
            finalVerdict.Remove(robot);
            currentState.Remove(robot);
            crmHistory.Remove(robot);
        }
    }

    // Section 5 cap ladder — order enforced by the caller's inspector values: R < investigationCap < E_t
    double GetEmaxForState(RobotFaultState state, double rawR)
    {
        switch (state)
        {
            case RobotFaultState.NORMAL:
            case RobotFaultState.CLEARED:
                return rawR;
            case RobotFaultState.PRIMED:
            case RobotFaultState.DISCREPANCY_PASSED:
            case RobotFaultState.PROBING:
                return investigationCap;
            case RobotFaultState.FAULT:
            default:
                return double.PositiveInfinity; // "Emax removed"
        }
    }

    // Combined discrepancy signal (Section 4d/7) — the max of two independent per-target
    // discrepancies: neighbour-count self-report (RAB_discrepancy) and actuator-intent-vs-actual
    // (BACT_discrepancy). Either alone crossing thetaDisc is enough; this is what feeds PRIMED's
    // window and the always-on arm. Always both — no manual enable switch.
    float ComputeDiscrepancy(GameObject observedRobot)
    {
        float neighborCountDiscrepancy = ComputeNeighborCountDiscrepancy(observedRobot);
        float actuatorDiscrepancy = ComputeActuatorDiscrepancy(observedRobot);
        return Mathf.Max(neighborCountDiscrepancy, actuatorDiscrepancy);
    }

    float ComputeNeighborCountDiscrepancy(GameObject observedRobot)
    {
        if (!observedRabSensors.TryGetValue(observedRobot, out RABSensor rabSensor))
        {
            rabSensor = observedRobot.GetComponentInChildren<RABSensor>();
            observedRabSensors[observedRobot] = rabSensor;
        }
        if (rabSensor == null || maxPossibleNeighbors <= 0) return 0f;

        int selfReported = rabSensor.GetSelfReportedNeighborCount();
        int trueCount = rabSensor.GetTrueNeighborCount();
        return Mathf.Abs(selfReported - trueCount) / (float)maxPossibleNeighbors;
    }

    // BACT_discrepancy: commanded intent vs. actually-achieved motion, BOTH read straight off the
    // target's own CmdVel_Subscriber/Rigidbody (ground truth, zero ROS involvement) — plain
    // |intent - actual| > actuatorToleranceCm per wheel.
    float ComputeActuatorDiscrepancy(GameObject observedRobot)
    {
        if (!observedCmdVelSubscribers.TryGetValue(observedRobot, out CmdVel_Subscriber cmdVel))
        {
            cmdVel = observedRobot.GetComponentInChildren<CmdVel_Subscriber>();
            observedCmdVelSubscribers[observedRobot] = cmdVel;
        }
        if (cmdVel == null) return 0f;

        Dictionary<string, float> intent = cmdVel.GetIntentVelocities();
        Dictionary<string, float> actual = cmdVel.GetActualThrusterSpeeds();

        float leftDiff = Mathf.Abs(intent["left_thruster"] - actual["left_thruster"]);
        float rightDiff = Mathf.Abs(intent["right_thruster"] - actual["right_thruster"]);
        bool exceeded = leftDiff > actuatorToleranceCm || rightDiff > actuatorToleranceCm;

        if (logStateTransitions && exceeded)
            Debug.Log($"{transform.root.name}: actuator discrepancy for {observedRobot.name} — " +
                $"intent L={intent["left_thruster"]:F1} R={intent["right_thruster"]:F1} | " +
                $"actual(ground truth) L={actual["left_thruster"]:F1} R={actual["right_thruster"]:F1} (cm/s)");

        return exceeded ? 1f : 0f;
    }

    ActuatorEmitter GetActuatorEmitter(GameObject observedRobot)
    {
        if (!actuatorEmitters.TryGetValue(observedRobot, out ActuatorEmitter emitter))
        {
            emitter = observedRobot.GetComponentInChildren<ActuatorEmitter>();
            actuatorEmitters[observedRobot] = emitter;
        }
        return emitter;
    }

    BlinkEmitter GetBlinkEmitter(GameObject observedRobot)
    {
        if (!blinkEmitters.TryGetValue(observedRobot, out BlinkEmitter emitter))
        {
            emitter = observedRobot.GetComponentInChildren<BlinkEmitter>();
            blinkEmitters[observedRobot] = emitter;
        }
        return emitter;
    }

    static void EnqueueWindow(Queue<bool> window, bool value)
    {
        window.Enqueue(value);
        if (window.Count > FeatureExtractor.CRM_HISTORY_LENGTH)
            window.Dequeue();
    }

    static bool WindowMajority(Queue<bool> window)
    {
        int trueCount = 0;
        foreach (bool v in window)
            if (v) trueCount++;
        return trueCount > FeatureExtractor.CRM_HISTORY_LENGTH / 2;
    }

    // ================================= CSV (Section 10) =================================

    void AppendCsvRow(GameObject observedRobot, int fvIndex, float discrepancy, RobotFaultState state,
        double Emax, double rawE, double rawR, ProbeResult probeResult, bool verdict)
    {
        if (!csvHeaderWritten)
        {
            csvBuffer.AppendLine("observer_id,robot_id,timestamp,BFV,A_j,affinity_to_nearest_tolerated,"
                + "self_reported_neighbors,true_neighbors,discrepancy,state,Emax,E,R,probe_result,final_verdict,ground_truth_label");
            csvHeaderWritten = true;
        }

        int Aj = featureExtractor.lastFvCounts.TryGetValue(fvIndex, out int count) ? count : 0;

        // Affinity from this FV to the nearest cluster CRM currently classifies as tolerated.
        double nearestTolAffinity = 0.0;
        for (int k = 0; k < crmInstanceDanger.FVClassification.Length; k++)
            if (crmInstanceDanger.FVClassification[k] == 2)
                nearestTolAffinity = Math.Max(nearestTolAffinity, crmInstanceDanger.theta[fvIndex, k]);

        int selfReported = 0, trueCount = 0;
        if (observedRabSensors.TryGetValue(observedRobot, out RABSensor rabSensor) && rabSensor != null)
        {
            selfReported = rabSensor.GetSelfReportedNeighborCount();
            trueCount = rabSensor.GetTrueNeighborCount();
        }

        string groundTruthLabel = "None";
        if (faultInjector != null && faultInjector.injectFault
            && faultInjector.selectedFault != FaultInjector.FaultType.None
            && faultInjector.faulty_robots.Contains(observedRobot.name))
        {
            groundTruthLabel = faultInjector.selectedFault.ToString();
        }

        string probeResultCsv = probeResult == ProbeResult.Pass ? "PASS"
            : probeResult == ProbeResult.Fail ? "FAIL"
            : "NA";

        csvBuffer.AppendLine(string.Join(",",
            transform.root.name,
            observedRobot.name,
            Time.time.ToString("F2"),
            fvIndex.ToString(),
            Aj.ToString(),
            nearestTolAffinity.ToString("F4"),
            selfReported.ToString(),
            trueCount.ToString(),
            discrepancy.ToString("F4"),
            state.ToString(),
            double.IsPositiveInfinity(Emax) ? "inf" : Emax.ToString("F2"),
            rawE.ToString("F2"),
            rawR.ToString("F2"),
            probeResultCsv,
            verdict ? "1" : "0",
            groundTruthLabel));
    }

    void FlushCsv()
    {
        if (csvBuffer.Length == 0)
        {
            Debug.LogWarning($"{transform.root.name}: DangerCascadeController has no CSV rows to export.");
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string filename = $"{transform.root.name} - danger - {timestamp}.csv";
        string dir = Path.Combine(Application.dataPath, "Scripts", "data", "latest_data", "Danger");
        Directory.CreateDirectory(dir);
        string filepath = Path.Combine(dir, filename);

        File.WriteAllText(filepath, csvBuffer.ToString());
        Debug.LogError($"{transform.root.name}: DangerCascadeController exported CSV to:\n{filepath}");

        csvBuffer.Clear();
        csvHeaderWritten = false;
    }
}
