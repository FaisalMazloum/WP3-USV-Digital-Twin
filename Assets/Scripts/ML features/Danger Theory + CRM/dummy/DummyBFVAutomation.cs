using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional automation for the dummy BFV sim's cleared-tag-duration x injection-timing sweep.
/// Toggleable — while disabled, DummyBFVSwarm/FaultInjector just run under whatever they were
/// last left at (fully manual, unchanged from before this script existed).
///
/// While enabled, runs a grid of experiments. Two independent axes control its shape:
///
/// useManualSweep — where the clearedTagDuration values (and, in turn, the offsets) come from:
///  - false (default): auto-generated arithmetic sequence — startClearedDuration,
///    +clearedDurationStep, ... for numDurations values.
///  - true: exactly the values listed in manualClearedDurations, one trial per entry, in order.
///    The injection-offset axis (see below) is also expressed differently in this mode.
///
/// enableInjectionSweep — whether there's an injection-offset dimension at all:
///  - true (default): one combination per (clearedTagDuration, injectionOffset) pair. In
///    auto-sweep mode, for each duration D the offset sweeps 0, +injectionOffsetStep, ... while
///    offset &lt; D. In manual-sweep mode, offset = fraction * D for each fraction in
///    manualInjectionOffsetFractions (fractions must be in [0,1) — a fraction &gt;= 1 would put the
///    offset at or past expiry and is dropped, same rule as auto mode's offset &lt; D cutoff). Offset
///    0 = "exactly when a robot gets cleared"; the fault never lands after that tag's expiry
///    either way. For each combination, the instant DummyBFVSwarm.lastClearedTime for the tracked
///    robot is offset seconds in the past, this script sets FaultInjector.selectedFault =
///    SoftwareHang and injectFault = true on the swarm's own FaultInjector — reusing the swarm's
///    existing ApplyFaultInjection()/CSV "fault_injected" event path rather than adding a
///    separate injection mechanism.
///  - false: no injection dimension at all — one trial per clearedTagDuration value, and no
///    fault is ever injected (FaultInjector.injectFault is held false for the whole trial). Pure
///    investigation-cycling/false-positive study for a healthy persistent-BFV-outlier robot,
///    with cleared_duration as the only swept variable.
///
/// Either way, DummyBFVSwarm.BuildSwarm() resets to a clean baseline at the start of every trial.
///
/// Termination per combination — two modes, selected by stopOnClassification:
///  - true (default): run until DummyBFVSwarm.classifiedFaultyRobots contains the tracked robot
///    (the fault was actually caught — "classified_faulty" fired), OR until
///    maxCyclesPerExperiment investigation entries have passed without that ever happening
///    (safety cap so a combination that structurally can never catch the fault doesn't hang the
///    sweep forever).
///  - false: ignore classification entirely — run for exactly T_run seconds (measured from this
///    combination's own start, via DummyBFVSwarm.SimTime, which only advances once per control
///    cycle — so the trial always ends on a completed cycle boundary, never mid-cycle) and then
///    move on regardless of whether the fault was ever caught. Useful for capturing a fixed
///    observation window per combination (e.g. multiple CLEARED/re-investigation cycles) instead
///    of stopping at the first detection.
/// Either way both outcomes are logged so it's always clear afterward whether a given trial_id
/// ever got caught.
///
/// trialId = the flattened combination index; DummyBFVSwarm.injectionOffset is set to combo.offset
/// each StartExperiment() and logged as its own CSV column, so cleared_duration and the offset
/// actually used are both directly visible per row (detection latency is still derived
/// afterward — the sim_time gap between that trial's "fault_injected" and "classified_faulty"
/// rows). Probe count per trial = count of "probe_dispatched" rows for that trial_id.
///
/// Touches no detection logic — only drives DummyBFVSwarm's public fields/BuildSwarm() and the
/// existing FaultInjector's public fields, exactly as a user would by hand.
/// </summary>
[RequireComponent(typeof(DummyBFVSwarm))]
public class DummyBFVAutomation : MonoBehaviour
{
    [Header("Toggle — enable to run/resume the sweep, disable to pause (swarm keeps running manually)")]
    public bool automationEnabled = false;

    [Header("Sweep source — ticked: use the manual lists below verbatim; unticked: auto-generate from the arithmetic-sequence fields")]
    public bool useManualSweep = false;

    [Header("Auto-generated clearedTagDuration sweep (used when useManualSweep is false)")]
    public float startClearedDuration = 0f;
    public float clearedDurationStep = 1f;
    public int numDurations = 10;

    [Header("Manual clearedTagDuration sweep (used when useManualSweep is true) — one trial per value, exactly as listed")]
    public List<float> manualClearedDurations = new List<float> { 0f, 1f, 2f, 3f };

    [Header("Injection sweep — unticked: one trial per clearedTagDuration value, no fault ever injected (pure FP/investigation-cycling study)")]
    public bool enableInjectionSweep = true;
    // Offset axis — seconds after CLEARED entry, always < that combination's duration. Only
    // consulted when enableInjectionSweep is true and useManualSweep is false.
    public float injectionOffsetStep = 1f;
    // Offset axis for manual mode — FRACTIONS of the current trial's own clearedTagDuration D, in
    // [0, 1). Actual offset = fraction * D, so it automatically stays valid ("never after
    // expiry") for every D in manualClearedDurations without having to hand-compute seconds per
    // duration. A fraction of exactly 1.0 (or higher) is dropped, same as offset >= duration
    // being excluded in auto mode. Only consulted when useManualSweep is true.
    public List<float> manualInjectionOffsetFractions = new List<float> { 0f, 0.25f, 0.5f, 0.75f };

    [Header("Per-combination termination")]
    // Ticked: normal logic — end the moment the fault is classified faulty (maxCyclesPerExperiment
    // below is only the give-up safety cap). Unticked: ignore classification, just run each
    // combination for a fixed T_run seconds and move on regardless of outcome.
    public bool stopOnClassification = true;
    public int maxCyclesPerExperiment = 10; // only used when stopOnClassification is true
    public float T_run = 30f; // only used when stopOnClassification is false

    [Header("Which robot's investigation entries / CLEARED entry drive this sweep")]
    public bool trackFaultTargetRobot = true; // if false, uses explicitRobotId below instead
    public int explicitRobotId = 0;

    public int CurrentCombinationIndex { get; private set; } = 0;
    public int CurrentCycleCount { get; private set; } = 0;
    public int TotalCombinations => grid?.Count ?? 0;

    private struct Combination { public float duration; public float offset; }
    private List<Combination> grid;

    private DummyBFVSwarm swarm;
    private bool experimentInitialized = false;
    private bool injectedThisExperiment = false;
    private bool loggedStart = false;
    private float trialStartSimTime = 0f;

    void Awake()
    {
        swarm = GetComponent<DummyBFVSwarm>();
        BuildGrid();
    }

    void BuildGrid()
    {
        grid = new List<Combination>();

        IEnumerable<float> durations = useManualSweep ? manualClearedDurations : AutoDurations();

        foreach (float duration in durations)
        {
            if (!enableInjectionSweep)
            {
                // No combination — exactly one trial per clearedTagDuration value, and the offset
                // sentinel (-1) below double-guards the injection-firing block in FixedUpdate()
                // from ever triggering even if something else went wrong.
                grid.Add(new Combination { duration = duration, offset = -1f });
                continue;
            }

            // Plain bounds check, no snapping needed — DummyBFVSwarm now compares the injection
            // offset against its continuous PreciseTime clock (not a controlCycleSeconds-quantized
            // one), so any offset in [0, duration) is reachable exactly. (Previously this rounded
            // offsets up to the nearest controlCycleSeconds multiple and dropped infeasible ones —
            // no longer needed now that the underlying clock isn't quantized.)
            if (useManualSweep)
            {
                foreach (float fraction in manualInjectionOffsetFractions)
                {
                    float offset = fraction * duration;
                    if (offset < 0f || offset >= duration) continue;
                    grid.Add(new Combination { duration = duration, offset = offset });
                }
            }
            else
            {
                float safeOffsetStep = Mathf.Max(injectionOffsetStep, 0.0001f); // guards against a hang from a 0/negative step
                for (float offset = 0f; offset < duration; offset += safeOffsetStep)
                    grid.Add(new Combination { duration = duration, offset = offset });
            }
        }
    }

    IEnumerable<float> AutoDurations()
    {
        for (int d = 0; d < numDurations; d++)
            yield return startClearedDuration + d * clearedDurationStep;
    }

    void FixedUpdate()
    {
        if (!automationEnabled)
        {
            // Re-enabling restarts the current combination cleanly rather than trying to resume
            // mid-experiment — simpler and keeps every combination's data reproducible.
            experimentInitialized = false;
            return;
        }
        if (swarm == null || swarm.faultInjector == null) return;

        if (grid == null || grid.Count == 0) BuildGrid();

        if (!loggedStart)
        {
            loggedStart = true;
            Debug.Log($"DummyBFVAutomation: sweep started — {grid.Count} combinations total.");
        }

        if (CurrentCombinationIndex >= grid.Count)
        {
            automationEnabled = false;
            swarm.faultInjector.injectFault = false;
            swarm.stopLoggingAndExport = true; // cleanly finalize CSVs once the whole sweep is done
            Debug.Log($"DummyBFVAutomation: SWEEP COMPLETE ({grid.Count} combinations). automationEnabled is now false — CSVs flushed. Safe to stop the simulation.");
            return;
        }

        var combo = grid[CurrentCombinationIndex];

        if (!experimentInitialized)
        {
            StartExperiment(combo);
            experimentInitialized = true;
        }

        int trackedRobotId = trackFaultTargetRobot ? swarm.FaultTargetRobotId : explicitRobotId;
        if (trackedRobotId < 0) return; // no fault-target group configured yet — nothing to track

        // Fire the injection once this robot has been CLEARED for >= this combination's offset,
        // and only while still inside that CLEARED window (never after expiry). Gated on
        // enableInjectionSweep so no fault is ever injected in that mode, regardless of combo.offset.
        // Uses swarm.PreciseTime (continuous), not the CRM-cadence-quantized SimTime, so decimal
        // offsets fire at the exact real-second value requested.
        if (enableInjectionSweep && !injectedThisExperiment
            && swarm.lastClearedTime.TryGetValue(trackedRobotId, out float clearedAt))
        {
            float elapsedSinceCleared = swarm.PreciseTime - clearedAt;
            if (elapsedSinceCleared >= combo.offset && elapsedSinceCleared < combo.duration)
            {
                swarm.faultInjector.selectedFault = FaultInjector.FaultType.SoftwareHang;
                swarm.faultInjector.injectFault = true;
                injectedThisExperiment = true;
                Debug.Log($"DummyBFVAutomation: [combo {CurrentCombinationIndex}/{grid.Count}, D={combo.duration}, offset={combo.offset}] injected SoftwareHang on robot {trackedRobotId} at sim_time={swarm.PreciseTime:F2}.");
            }
        }

        swarm.investigationCounts.TryGetValue(trackedRobotId, out int countNow);
        CurrentCycleCount = countNow;

        bool caught = swarm.classifiedFaultyRobots.Contains(trackedRobotId);

        bool shouldEnd;
        if (stopOnClassification)
        {
            bool gaveUp = CurrentCycleCount >= maxCyclesPerExperiment;
            shouldEnd = caught || gaveUp;
        }
        else
        {
            // SimTime only advances once per control cycle (DummyBFVSwarm.RunControlCycle), so
            // this comparison — checked every physics tick — can only ever go true on a frame
            // where a cycle just completed, never mid-cycle.
            float elapsedThisTrial = swarm.SimTime - trialStartSimTime;
            shouldEnd = elapsedThisTrial >= T_run;
        }

        if (shouldEnd)
            EndExperiment(caught);
    }

    void StartExperiment(Combination combo)
    {
        swarm.faultInjector.injectFault = false; // healthy at the start — robot must clear on its own first
        swarm.faultInjector.selectedFault = FaultInjector.FaultType.SoftwareHang;

        swarm.clearedTagDuration = combo.duration;
        swarm.trialId = CurrentCombinationIndex;
        swarm.injectionOffset = combo.offset;
        swarm.BuildSwarm(); // clean baseline — also clears investigationCounts/lastClearedTime/classifiedFaultyRobots

        injectedThisExperiment = false;
        CurrentCycleCount = 0;
        trialStartSimTime = swarm.SimTime;

        string offsetLabel = enableInjectionSweep ? combo.offset.ToString() : "N/A (injection sweep disabled)";
        Debug.Log($"DummyBFVAutomation: [combo {CurrentCombinationIndex}/{grid.Count}] starting — clearedTagDuration={combo.duration}, injectionOffset={offsetLabel}, trialId={CurrentCombinationIndex}.");
    }

    void EndExperiment(bool caught)
    {
        swarm.faultInjector.injectFault = false;

        string message = stopOnClassification
            ? (caught
                ? $"DummyBFVAutomation: [combo {CurrentCombinationIndex}/{grid.Count}] fault CAUGHT after {CurrentCycleCount} investigation cycle(s) — moving on."
                : $"DummyBFVAutomation: [combo {CurrentCombinationIndex}/{grid.Count}] gave up — never classified faulty after {CurrentCycleCount} investigation cycle(s) (maxCyclesPerExperiment={maxCyclesPerExperiment}).")
            : $"DummyBFVAutomation: [combo {CurrentCombinationIndex}/{grid.Count}] fixed T_run={T_run}s elapsed — {(caught ? "fault was classified faulty at least once" : "fault was NEVER classified faulty")} during the run.";
        Debug.Log(message);

        CurrentCombinationIndex++;
        experimentInitialized = false;
    }
}
