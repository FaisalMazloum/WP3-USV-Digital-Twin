using System.Collections.Generic;
using System.IO;
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

    [Header("CSV logging — event log always on; per-cycle debug log optional (large files)")]
    public string csvOutputFolder = "Scripts/ML features/Danger Theory + CRM/dummy/data"; // relative to Assets/, or an absolute path
    public int trialId = 0;
    // Logged as its own CSV column. When autoInjectAtOffset is off (default), this is purely
    // informational — nothing reads it, DummyBFVAutomation (if present/enabled) overwrites it
    // with its own swept combo.offset each trial anyway. When autoInjectAtOffset is on, THIS
    // value is what actually drives the standalone injection below.
    public float injectionOffset = 0f;
    // Standalone, non-swept version of what DummyBFVAutomation's injection-timing does: once the
    // fault-target robot has been CLEARED for >= injectionOffset seconds (and still within
    // clearedTagDuration, never after expiry), sets faultInjector.injectFault = true itself — no
    // automation component or sweep needed, for quick manual single-trial testing. Fires once per
    // BuildSwarm(). Automatically stays off if a DummyBFVAutomation on this GameObject has
    // automationEnabled = true, since that already owns injection timing and the two would
    // otherwise fight over faultInjector.injectFault.
    public bool autoInjectAtOffset = false;
    public bool enablePerCycleLog = false;
    // Momentary button (auto-resets to false): stops accepting new log rows after the
    // in-flight control cycle(s) for this frame finish, then flushes both CSVs to disk. One-way
    // for the rest of this Play session — mirrors DangerCascadeController's exportCSV pattern.
    public bool stopLoggingAndExport = false;

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

    // How many times each robot (by id) has entered investigation (NORMAL -> PRIMED), since the
    // last BuildSwarm(). Canonical-observer-deduped, same as the CSV event log. Not itself a CSV
    // column (doesn't match any of the 7 logged event_types) — exists for DummyBFVAutomation to
    // poll when deciding when an experiment's maxCyclesPerExperiment give-up cap has been reached.
    public Dictionary<int, int> investigationCounts = new Dictionary<int, int>();

    // sim_time each robot (by id) most recently entered CLEARED, since the last BuildSwarm().
    // Canonical-observer-deduped, same instant the "cleared" CSV event fires. Exists for
    // DummyBFVAutomation to time a fault injection relative to a CLEARED entry.
    public Dictionary<int, float> lastClearedTime = new Dictionary<int, float>();

    // Robots (by id) that have been classified_faulty at least once since the last BuildSwarm().
    // Canonical-observer-deduped, updated at the same point the "classified_faulty" CSV event
    // fires. Exists for DummyBFVAutomation to know when a combination's fault was actually caught.
    public HashSet<int> classifiedFaultyRobots = new HashSet<int>();

    public int FaultTargetRobotId => faultTargetRobotId;

    // The CRM-cadence clock — an exact multiple of controlCycleSeconds, used for everything tied
    // to the CRM step / ladder state-machine transitions (which only ever run once per control
    // cycle, matching the live sim's fixed Wc=1s cadence): NORMAL/PRIMED/DISCREPANCY_PASSED/
    // PROBING/FAULT transitions, "cleared"/"classified_faulty"/"probe_*"/CSV timestamps for those
    // events. cycleCount increments once per RunControlCycle() call and is never reset by
    // BuildSwarm() (global, monotonic clock for the whole Play session, not per-experiment).
    public float SimTime => cycleCount * controlCycleSeconds;

    // A genuinely continuous clock (accumulated every physics tick, not quantized to
    // controlCycleSeconds) — used ONLY for CLEARED-expiry (CheckClearedExpiries()) and
    // injection-offset timing (RunAutoInject()/DummyBFVAutomation), so clearedTagDuration and
    // injectionOffset expire/fire at the exact real-second value requested regardless of what
    // controlCycleSeconds is set to, while the CRM engine + ladder transitions above stay
    // strictly on the fixed CRM cadence. Also never reset by BuildSwarm(), same reasoning as SimTime.
    public float PreciseTime => preciseTime;

    private int faultTargetRobotId = -1;
    private bool faultWasActive = false;
    private int cycleCount = 0;
    private float preciseTime = 0f;
    private bool autoInjectedThisRun = false;
    private DummyBFVAutomation siblingAutomation;

    private DummyBFVCsvLogger logger;

    void Awake()
    {
        string dir = Path.IsPathRooted(csvOutputFolder)
            ? csvOutputFolder
            : Path.Combine(Application.dataPath, csvOutputFolder);
        logger = new DummyBFVCsvLogger(dir);

        siblingAutomation = GetComponent<DummyBFVAutomation>();

        BuildSwarm();
    }

    // Defensive flush — covers Editor Stop-Play and build quit, so a run that's killed rather
    // than cleanly stopped via stopLoggingAndExport doesn't lose buffered per-cycle rows. The
    // event log has no buffer (written immediately) so nothing to flush there.
    void OnDestroy() => logger?.FlushCycleBuffer();
    void OnApplicationQuit() => logger?.FlushCycleBuffer();

    public void BuildSwarm()
    {
        robots.Clear();
        faultTargetRobotId = -1;
        investigationCounts.Clear();
        lastClearedTime.Clear();
        classifiedFaultyRobots.Clear();
        autoInjectedThisRun = false;
        if (autoInjectAtOffset && faultInjector != null)
            faultInjector.injectFault = false; // clean slate — robot must clear on its own first

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

        LogAutoInjectDiagnostic();
    }

    // autoInjectAtOffset fails silently by design (it's a per-tick gate, not worth a warning every
    // frame) — this runs once per BuildSwarm() so the actual blocking reason, if any, is visible
    // in the Console immediately instead of you having to guess.
    void LogAutoInjectDiagnostic()
    {
        if (!autoInjectAtOffset) return;

        if (siblingAutomation != null && siblingAutomation.automationEnabled)
            Debug.LogWarning("DummyBFVSwarm: autoInjectAtOffset is ON but ignored — sibling DummyBFVAutomation.automationEnabled is also true, which owns injection timing instead. Untick one of them.");
        else if (faultInjector == null)
            Debug.LogWarning("DummyBFVSwarm: autoInjectAtOffset is ON but faultInjector is not assigned.");
        else if (faultTargetRobotId < 0)
            Debug.LogWarning("DummyBFVSwarm: autoInjectAtOffset is ON but no bfvGroups entry has isFaultTarget checked — there is no robot to inject.");
        else if (!dangerExtensionEnabled)
            Debug.LogWarning("DummyBFVSwarm: autoInjectAtOffset is ON but dangerExtensionEnabled is OFF — the ladder never engages (stays NORMAL, no CLEARED is ever reached), so the CLEARED-relative offset can never fire.");
        // injectionOffset/clearedTagDuration are now checked against the continuous PreciseTime
        // clock (see CheckClearedExpiries()/RunAutoInject()), so any value is reachable exactly —
        // no more controlCycleSeconds-rounding to warn about, just the plain bounds check.
        else if (injectionOffset < 0f || injectionOffset >= clearedTagDuration)
            Debug.LogWarning($"DummyBFVSwarm: autoInjectAtOffset is ON but injectionOffset={injectionOffset} is at or past " +
                $"clearedTagDuration={clearedTagDuration} — the injection will never fire. Lower injectionOffset or raise clearedTagDuration.");
        else
            Debug.Log($"DummyBFVSwarm: autoInjectAtOffset armed — watching robot {faultTargetRobotId} (BFV={robots[faultTargetRobotId].currentBFV}) for its first CLEARED, will inject at {injectionOffset}s after that. " +
                $"If robot {faultTargetRobotId}'s BFV is not an outlier relative to the rest of the swarm it will never reach PRIMED/CLEARED at all, and this will wait forever.");
    }

    private float controlCycleTimer = 0f;

    void FixedUpdate()
    {
        if (robots.Count == 0) return;

        float dt = Time.fixedDeltaTime;
        preciseTime += dt;

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

        // Checked every physics tick, on the continuous preciseTime clock — independent of the
        // CRM-cadence while-loop above, so clearedTagDuration expires at the exact real-second
        // value requested rather than being rounded to the CRM cadence grid.
        CheckClearedExpiries();

        // Checked AFTER the while-loop above, so it sees this frame's freshest lastClearedTime
        // if a control cycle just ran.
        RunAutoInject();

        // Checked AFTER the while-loop above, so any control cycle(s) due this frame still
        // finish and log normally first — no partial-cycle data loss ("after the cycle completes").
        if (stopLoggingAndExport)
        {
            stopLoggingAndExport = false;
            logger.Stop();
            Debug.Log("DummyBFVSwarm: logging stopped, CSVs flushed.");
        }
    }

    // Polled every physics tick (see FixedUpdate) on the continuous preciseTime clock, independent
    // of RunControlCycle()'s fixed CRM cadence — see DummyDangerLadderEngine.CheckClearedExpiry's
    // docs for why the actual expiry check lives there now instead of inside Tick()'s switch.
    void CheckClearedExpiries()
    {
        foreach (var observer in robots)
        {
            foreach (var target in robots)
            {
                if (target.id == observer.id) continue;
                if (!observer.ladders.TryGetValue(target.id, out var ladder)) continue;

                if (DummyDangerLadderEngine.CheckClearedExpiry(ladder, preciseTime, clearedTagDuration))
                {
                    observer.states[target.id] = ladder.state; // now NORMAL

                    if (IsCanonicalObserver(observer, target))
                        logger.LogEvent(trialId, injectionOffset, clearedTagDuration, preciseTime, "tag_expired", target.id.ToString());
                }
            }
        }
    }

    // Standalone counterpart to DummyBFVAutomation's own injection-timing logic (see
    // autoInjectAtOffset's doc comment) — off by default, and a no-op whenever a sibling
    // DummyBFVAutomation is actively driving injection itself, so the two never fight over
    // faultInjector.injectFault.
    void RunAutoInject()
    {
        if (!autoInjectAtOffset) return;
        if (siblingAutomation != null && siblingAutomation.automationEnabled) return;
        if (faultInjector == null || faultTargetRobotId < 0) return;
        if (autoInjectedThisRun) return;
        if (!lastClearedTime.TryGetValue(faultTargetRobotId, out float clearedAt)) return;

        float elapsedSinceCleared = preciseTime - clearedAt;
        if (elapsedSinceCleared >= injectionOffset && elapsedSinceCleared < clearedTagDuration)
        {
            faultInjector.injectFault = true;
            autoInjectedThisRun = true;
            Debug.Log($"DummyBFVSwarm: auto-injected fault ({faultInjector.selectedFault}) on robot {faultTargetRobotId} " +
                $"at sim_time={preciseTime:F2} (injectionOffset={injectionOffset}).");
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
        // Exact multiple of controlCycleSeconds, distinct for every call (even if the while-loop
        // in FixedUpdate runs this twice in one real frame after a hitch) — see SimTime's doc
        // comment above for why this replaced a continuously-accumulated clock.
        cycleCount++;
        float currentTime = cycleCount * controlCycleSeconds;

        // FeatureExtractor.CRM_HISTORY_LENGTH (9) is a cycle count that only means "9 real
        // seconds" because the live sim's own control cycle is a fixed 1s. Since
        // controlCycleSeconds is adjustable here, scale the window length so it always spans the
        // same 9 real seconds regardless of cadence (e.g. halving controlCycleSeconds doubles it).
        int historyLength = Mathf.Max(1, Mathf.RoundToInt(FeatureExtractor.CRM_HISTORY_LENGTH / Mathf.Max(controlCycleSeconds, 0.0001f)));

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

        // Snapshot each target's probe state before the ladder loop runs, so probe transitions
        // (RequestProbe()/ReadResult() calls happen inside DummyDangerLadderEngine.Tick() below)
        // can be detected once per TARGET afterward, not once per redundant observer.
        var probeWasActive = new Dictionary<int, bool>();
        var probeWasComplete = new Dictionary<int, bool>();
        foreach (var r in robots)
        {
            probeWasActive[r.id] = r.probe.IsProbing && !r.probe.WindowComplete;
            probeWasComplete[r.id] = r.probe.WindowComplete;
        }

        // Each (observer, target) pair: run the ported ladder engine. Every observer's copy is
        // numerically identical for a given target (Section 6 — same global fvCounts, same
        // params, same deterministic ODE), so event/debug logging below only fires for one
        // designated "canonical" observer per target to avoid N-1 duplicate rows for the same
        // real-world event — see IsCanonicalObserver.
        foreach (var observer in robots)
        {
            foreach (var target in robots)
            {
                if (target.id == observer.id) continue;

                var ladder = observer.ladders[target.id];
                var stateBefore = ladder.state;
                observer.verdicts.TryGetValue(target.id, out bool verdictBefore);

                double rawE = observer.crmInstance.sumE[target.currentBFV];
                double rawR = observer.crmInstance.sumR[target.currentBFV];
                int fvClassification = observer.crmInstance.FVClassification[target.currentBFV];

                float discrepancySignal = target.discrepancyFault ? 1f : 0f;

                bool verdict = DummyDangerLadderEngine.Tick(
                    ladder,
                    rawE, rawR,
                    discrepancySignal, thetaDisc,
                    fvClassification, observer.classificationHistories[target.id],
                    target.probe, target.probe,
                    investigationCap, E_t, clearedTagDuration,
                    dangerExtensionEnabled,
                    currentTime,
                    historyLength);

                observer.verdicts[target.id] = verdict;
                observer.states[target.id] = ladder.state;

                if (!IsCanonicalObserver(observer, target)) continue;

                if (stateBefore == DangerCascadeController.RobotFaultState.NORMAL && ladder.state == DangerCascadeController.RobotFaultState.PRIMED)
                {
                    investigationCounts.TryGetValue(target.id, out int countSoFar);
                    investigationCounts[target.id] = countSoFar + 1;
                }

                if (ladder.state == DangerCascadeController.RobotFaultState.CLEARED && stateBefore != DangerCascadeController.RobotFaultState.CLEARED)
                {
                    // preciseTime, not currentTime — this is the reference point RunAutoInject()/
                    // DummyBFVAutomation measure injection-offset elapsed time against, on the
                    // continuous clock (see PreciseTime's doc comment).
                    lastClearedTime[target.id] = preciseTime;
                    logger.LogEvent(trialId, injectionOffset, clearedTagDuration, currentTime, "cleared", target.id.ToString());
                }

                // CLEARED -> NORMAL no longer happens inside this switch (Tick()'s CLEARED case is
                // now a no-op) — see CheckClearedExpiries(), polled every physics tick on the
                // continuous preciseTime clock instead of only once per CRM cycle here.

                // "verdict" is computed from ladder.state as it was BEFORE the switch above ran
                // (faithful to the original DangerCascadeController), so on the exact cycle a
                // robot transitions PROBING -> FAULT, verdict still reflects the old PROBING
                // state and only catches up next cycle — a real 1-cycle detection lag if this
                // were the only trigger. Checking the FAULT transition directly (post-switch,
                // like "cleared"/"tag_expired" above) fixes that for the ladder-driven path,
                // while keeping the verdict-based check for the always-on discrepancy-arm path
                // (Section 7), which has no such lag since discrepancyMajority is evaluated
                // before the switch too and doesn't depend on this cycle's transition.
                bool justEnteredFault = stateBefore != DangerCascadeController.RobotFaultState.FAULT
                    && ladder.state == DangerCascadeController.RobotFaultState.FAULT;
                bool justFlaggedByVerdict = verdict && !verdictBefore;
                if (justEnteredFault || justFlaggedByVerdict)
                {
                    classifiedFaultyRobots.Add(target.id);
                    logger.LogEvent(trialId, injectionOffset, clearedTagDuration, currentTime, "classified_faulty", target.id.ToString());
                }

                if (enablePerCycleLog)
                {
                    double cap = DummyDangerLadderEngine.GetEmaxForState(stateBefore, rawR, investigationCap);
                    string probeResultStr = target.probe.WindowComplete ? (target.probe.Passed ? "PASS" : "FAIL")
                        : (target.probe.IsProbing ? "PENDING" : "NA");
                    fvCounts.TryGetValue(target.currentBFV, out int countAj);

                    logger.LogCycle(trialId, injectionOffset, clearedTagDuration, currentTime, target.id, target.currentBFV, countAj,
                        ladder.state.ToString(), rawE, rawR, cap, probeResultStr,
                        fvClassification.ToString(), target.isFaulty);
                }
            }
        }

        // Probe dispatch/pass/fail — target-centric (the probe is a single shared object per
        // target), so this is detected once per target here rather than once per observer pair.
        foreach (var r in robots)
        {
            bool activeNow = r.probe.IsProbing && !r.probe.WindowComplete;
            if (activeNow && !probeWasActive[r.id])
                logger.LogEvent(trialId, injectionOffset, clearedTagDuration, currentTime, "probe_dispatched", r.id.ToString());

            if (r.probe.WindowComplete && !probeWasComplete[r.id])
                logger.LogEvent(trialId, injectionOffset, clearedTagDuration, currentTime, r.probe.Passed ? "probe_pass" : "probe_fail", r.id.ToString());
        }

        if (enablePerCycleLog)
            logger.FlushCycleBuffer();
    }

    // All N observers reach byte-identical conclusions about a given target (Section 6's global
    // counts + shared params + deterministic ODE), so exactly one observer is designated to emit
    // target-centric event/debug rows, avoiding N-1 duplicate rows per real event.
    bool IsCanonicalObserver(VirtualRobot observer, VirtualRobot target)
    {
        if (robots.Count < 2) return false;
        int canonicalId = (robots[0].id == target.id) ? robots[1].id : robots[0].id;
        return observer.id == canonicalId;
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

        bool faultActiveNow = faultInjector != null && faultTargetRobotId >= 0
            && faultInjector.injectFault && faultInjector.selectedFault != FaultInjector.FaultType.None;

        if (faultActiveNow)
        {
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

        // Edge-triggered — one row per actual injection, not one per tick the flag stays checked.
        // Uses preciseTime (continuous), not the CRM-cadence-quantized SimTime — injection can now
        // fire at a fine-grained offset (RunAutoInject()/DummyBFVAutomation both compare against
        // preciseTime), so the logged timestamp should reflect the real instant it happened rather
        // than snapping to the CRM cycle grid.
        if (faultActiveNow && !faultWasActive)
            logger.LogEvent(trialId, injectionOffset, clearedTagDuration, preciseTime, "fault_injected", robots[faultTargetRobotId].id.ToString());
        faultWasActive = faultActiveNow;
    }
}
