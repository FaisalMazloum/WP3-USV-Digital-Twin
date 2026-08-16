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

    [Header("Master switch (Section 1) — false must reproduce vanilla CRM exactly")]
    public bool dangerExtensionEnabled = false;

    [Header("CRM parameter passthrough (Section 8) — defaults match vanilla CRM's consts exactly")]
    public double c = 0.20;
    public double pi_E = 1e-3;
    public double pi_R = 0.7e-3;

    [Header("Effector cap ladder (Sections 5, 11) — must satisfy R < s1 < s2 < E_t")]
    public double s1 = 50.0;
    public double s2 = 150.0;
    public double E_t = 400.0;

    [Header("Discrepancy signal (Section 4d)")]
    [Range(0f, 1f)] public float thetaDisc = 0.3f;
    public int maxPossibleNeighbors = 9; // normalization denominator — NUM_ROBOTS - 1

    // Note: Nmin/Nmax/T (Section 6) are NOT duplicated here — each robot's own BlinkEmitter
    // owns those directly, since it's the target's probe capability, not something any one
    // observer's inspector should be able to override.

    [Header("CLEARED memory tag (Section 5)")]
    public float clearedTagDuration = 20f;

    [Header("Wiring")]
    public FeatureExtractor featureExtractor;
    public FaultInjector faultInjector; // optional — only used for the CSV ground-truth label column
    public bool exportCSV = false;
    public bool logStateTransitions = false; // Console.Log every ladder state change, for testing

    public CRMInstanceDanger crmInstanceDanger = new CRMInstanceDanger();

    // Outputs — read externally by HeatmapDanger, mirroring FeatureExtractor.isFaulty
    public Dictionary<GameObject, bool> finalVerdict = new Dictionary<GameObject, bool>();
    public Dictionary<GameObject, RobotFaultState> currentState = new Dictionary<GameObject, RobotFaultState>();

    private class LadderEntry
    {
        public RobotFaultState state = RobotFaultState.NORMAL;
        public Queue<bool> outlierWindow = new Queue<bool>();
        public Queue<bool> discrepancyWindow = new Queue<bool>();
        public int clearedBFV = -1;
        public float clearedTime = -1f;
    }

    private Dictionary<GameObject, LadderEntry> ladders = new Dictionary<GameObject, LadderEntry>();
    private Dictionary<GameObject, NeighborCountReporter> neighborReporters = new Dictionary<GameObject, NeighborCountReporter>();
    private Dictionary<GameObject, BlinkEmitter> blinkEmitters = new Dictionary<GameObject, BlinkEmitter>();
    private BlinkReader blinkReader;

    // Background CRM thread — mirrors FeatureExtractor's own crmInstance threading, so the
    // (heavier, duplicate) RK2 integration doesn't stall the main thread.
    private System.Threading.Thread crmThread;
    private volatile bool crmRunning = false;
    private volatile bool crmResultReady = false;
    private Dictionary<int, int> fvCountsPending = new Dictionary<int, int>();
    private object crmLock = new object();

    private bool prevCreatingNewSample = true;

    private StringBuilder csvBuffer = new StringBuilder();
    private bool csvHeaderWritten = false;

    void Start()
    {
        if (featureExtractor == null)
            featureExtractor = GetComponent<FeatureExtractor>();
        if (featureExtractor == null)
            Debug.LogError($"{transform.root.name}: DangerCascadeController found no FeatureExtractor!");

        blinkReader = GetComponent<BlinkReader>();
        if (blinkReader == null)
            Debug.LogError($"{transform.root.name}: DangerCascadeController found no BlinkReader!");

        if (!(0.0 < s1 && s1 < s2 && s2 < E_t))
            Debug.LogWarning($"{transform.root.name}: cap ladder ordering s1 < s2 < E_t is violated " +
                $"(s1={s1}, s2={s2}, E_t={E_t}) — Section 5 assumes this strict ordering.");
    }

    void Update()
    {
        if (featureExtractor == null) return;

        // Keep the engine's parameter copies in sync with the inspector every frame, so
        // sweeping s1/s2/E_t/c/pi_E/pi_R/etc. at runtime takes effect immediately.
        crmInstanceDanger.c = c;
        crmInstanceDanger.pi_E = pi_E;
        crmInstanceDanger.pi_R = pi_R;
        crmInstanceDanger.dangerExtensionEnabled = dangerExtensionEnabled;

        // Edge-detect "a new sample (and fvCounts) just got produced this cycle" — the same
        // sync signal GlobalCSVExporter.WaitForSyncAndStartCollection already reads off this
        // exact field.
        bool nowCreating = featureExtractor.creatingNewSample;
        if (prevCreatingNewSample && !nowCreating)
        {
            LaunchCrmStep();
        }
        prevCreatingNewSample = nowCreating;

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

            // ---- Extension off: mirror vanilla CRM's verdict exactly, ladder stays parked ----
            if (!dangerExtensionEnabled)
            {
                finalVerdict[observedRobot] = crmInstanceDanger.FVClassification[fvIndex] == 1;
                currentState[observedRobot] = RobotFaultState.NORMAL;
                continue;
            }

            // ---- Discrepancy (Sections 4d, 7) — always on, independent of ladder state ----
            float discrepancy = ComputeDiscrepancy(observedRobot);

            // ---- Emax for the CURRENT state, applied read-only to this cycle's verdict.
            //      Never written back into TE/TR — the shared clone ODE keeps integrating
            //      untouched, which is what lets two observers cap the same FV differently. ----
            double Emax = GetEmaxForState(ladder.state, rawR);
            double clampedE = Math.Min(rawE, Emax);
            bool ladderFault = clampedE > E_t;
            bool verdict = (discrepancy > thetaDisc) || ladderFault; // Section 7 final verdict

            finalVerdict[observedRobot] = verdict;
            currentState[observedRobot] = ladder.state;

            BlinkReader.ProbeResult probeResult = BlinkReader.ProbeResult.Pending;
            RobotFaultState stateBefore = ladder.state;

            // ---- State machine transitions (decide state for NEXT cycle) — Section 5 ----
            switch (ladder.state)
            {
                case RobotFaultState.NORMAL:
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
                    BlinkEmitter emitter = GetBlinkEmitter(observedRobot);
                    if (emitter != null)
                    {
                        emitter.RequestProbe();
                        ladder.state = RobotFaultState.PROBING;
                    }
                    break;
                }

                case RobotFaultState.PROBING:
                {
                    BlinkEmitter emitter = GetBlinkEmitter(observedRobot);
                    if (emitter != null && blinkReader != null)
                    {
                        probeResult = blinkReader.ReadResult(emitter);
                        if (probeResult == BlinkReader.ProbeResult.Pass)
                        {
                            ladder.state = RobotFaultState.CLEARED;
                            ladder.clearedBFV = fvIndex;
                            ladder.clearedTime = Time.time;
                            emitter.ResetProbe();
                        }
                        else if (probeResult == BlinkReader.ProbeResult.Fail)
                        {
                            ladder.state = RobotFaultState.FAULT; // software hang
                            emitter.ResetProbe();
                        }
                        // Pending: probe still running, stay in PROBING.
                    }
                    break;
                }

                case RobotFaultState.FAULT:
                    // Terminal for this episode (Section 5) — flag/log only, no recovery.
                    break;

                case RobotFaultState.CLEARED:
                    if (fvIndex != ladder.clearedBFV || (Time.time - ladder.clearedTime) > clearedTagDuration)
                    {
                        ladder.state = RobotFaultState.NORMAL;
                        ladder.outlierWindow.Clear();
                        ladder.discrepancyWindow.Clear();
                    }
                    break;
            }

            if (logStateTransitions && ladder.state != stateBefore)
                Debug.Log($"{transform.root.name}: {observedRobot.name} ladder {stateBefore} -> {ladder.state} " +
                    $"(E={rawE:F2}, R={rawR:F2}, discrepancy={discrepancy:F2})");

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
        }
    }

    // Section 5 cap ladder — order enforced by the caller's inspector values: R < s1 < s2 < E_t
    double GetEmaxForState(RobotFaultState state, double rawR)
    {
        switch (state)
        {
            case RobotFaultState.NORMAL:
            case RobotFaultState.CLEARED:
                return rawR;
            case RobotFaultState.PRIMED:
                return s1;
            case RobotFaultState.DISCREPANCY_PASSED:
            case RobotFaultState.PROBING:
                return s2;
            case RobotFaultState.FAULT:
            default:
                return double.PositiveInfinity; // "Emax removed"
        }
    }

    float ComputeDiscrepancy(GameObject observedRobot)
    {
        if (!neighborReporters.TryGetValue(observedRobot, out NeighborCountReporter reporter))
        {
            reporter = observedRobot.GetComponentInChildren<NeighborCountReporter>();
            neighborReporters[observedRobot] = reporter;
        }
        if (reporter == null || maxPossibleNeighbors <= 0) return 0f;

        int selfReported = reporter.GetSelfReportedNeighborCount();
        int trueCount = reporter.GetTrueNeighborCount();
        return Mathf.Abs(selfReported - trueCount) / (float)maxPossibleNeighbors;
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
        double Emax, double rawE, double rawR, BlinkReader.ProbeResult probeResult, bool verdict)
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
        if (neighborReporters.TryGetValue(observedRobot, out NeighborCountReporter reporter) && reporter != null)
        {
            selfReported = reporter.GetSelfReportedNeighborCount();
            trueCount = reporter.GetTrueNeighborCount();
        }

        string groundTruthLabel = "None";
        if (faultInjector != null && faultInjector.injectFault
            && faultInjector.selectedFault != FaultInjector.FaultType.None
            && faultInjector.faulty_robots.Contains(observedRobot.name))
        {
            groundTruthLabel = faultInjector.selectedFault.ToString();
        }

        string probeResultCsv = probeResult == BlinkReader.ProbeResult.Pass ? "PASS"
            : probeResult == BlinkReader.ProbeResult.Fail ? "FAIL"
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
