using System.Collections.Generic;

/// <summary>
/// Dummy BFV sim (dummy_bfv_sim_brief.md) — decoupled, verbatim port of
/// DangerCascadeController.RunDangerCycle()'s per-observed-target block (state machine, cap
/// ladder, discrepancy windows) and its GetEmaxForState/EnqueueWindow/WindowMajority helpers.
///
/// DangerCascadeController.cs is left completely untouched (see brief Section 1) — this class
/// only exists because that logic lives inside a MonoBehaviour entangled with FeatureExtractor,
/// RABSensor, CmdVel_Subscriber, and the physical probe emitters/readers, none of which the
/// dummy sim has. The transition rules themselves (the switch statement, the Emax formula, the
/// majority-window rule) are copied unchanged — same detection logic, not a reimplementation.
/// Reuses DangerCascadeController.RobotFaultState directly (no duplicate enum) and the real
/// IProbeEmitter/IProbeReader contract (IProbe.cs) so DummyProbe plugs in exactly like
/// BlinkEmitter/BlinkReader do for the live sim.
/// </summary>
public static class DummyDangerLadderEngine
{
    public class LadderEntry
    {
        public DangerCascadeController.RobotFaultState state = DangerCascadeController.RobotFaultState.NORMAL;
        public Queue<bool> outlierWindow = new Queue<bool>();
        public Queue<bool> discrepancyWindow = new Queue<bool>();
        public Queue<bool> alwaysOnDiscrepancyWindow = new Queue<bool>();
        public float clearedTime = -1f;
    }

    // Section 5 cap ladder — order enforced by the caller's inspector values: R < investigationCap < E_t
    // Verbatim port of DangerCascadeController.GetEmaxForState (DangerCascadeController.cs:493-508).
    public static double GetEmaxForState(DangerCascadeController.RobotFaultState state, double rawR, double investigationCap)
    {
        switch (state)
        {
            case DangerCascadeController.RobotFaultState.NORMAL:
            case DangerCascadeController.RobotFaultState.CLEARED:
                return rawR;
            case DangerCascadeController.RobotFaultState.PRIMED:
            case DangerCascadeController.RobotFaultState.DISCREPANCY_PASSED:
            case DangerCascadeController.RobotFaultState.PROBING:
                return investigationCap;
            case DangerCascadeController.RobotFaultState.FAULT:
            default:
                return double.PositiveInfinity; // "Emax removed"
        }
    }

    // Verbatim port of DangerCascadeController.EnqueueWindow/WindowMajority (DangerCascadeController.cs:591-604).
    public static void EnqueueWindow(Queue<bool> window, bool value)
    {
        window.Enqueue(value);
        if (window.Count > FeatureExtractor.CRM_HISTORY_LENGTH)
            window.Dequeue();
    }

    public static bool WindowMajority(Queue<bool> window)
    {
        int trueCount = 0;
        foreach (bool v in window)
            if (v) trueCount++;
        return trueCount > FeatureExtractor.CRM_HISTORY_LENGTH / 2;
    }

    /// <summary>
    /// Verbatim port of the per-observed-target body of DangerCascadeController.RunDangerCycle
    /// (DangerCascadeController.cs:280-472) — majority-vote classification history, the
    /// dangerExtensionEnabled early-out, the Emax clamp + combined verdict, and the full
    /// NORMAL/PRIMED/DISCREPANCY_PASSED/PROBING/FAULT/CLEARED switch, unchanged. No CSV export,
    /// no SwarmConsensusCoordinator sync — both are DangerCascadeController-only concerns outside
    /// this brief's scope.
    ///
    /// discrepancy: pre-thresholded signal (Section 7) — 1.0 when this target's injected fault is
    /// a discrepancy type, 0.0 otherwise — so the untouched "discrepancy > thetaDisc" comparison
    /// below needs no dummy-mode special-casing.
    /// </summary>
    public static bool Tick(
        LadderEntry ladder,
        double rawE, double rawR,
        float discrepancy, float thetaDisc,
        int fvClassification, Queue<int> classificationHistory,
        IProbeEmitter probeEmitter, IProbeReader probeReader,
        double investigationCap, double E_t, float clearedTagDuration,
        bool dangerExtensionEnabled,
        float currentTime)
    {
        // ---- Majority-vote history, exactly like FeatureExtractor.UpdateFaultStatus ----
        classificationHistory.Enqueue(fvClassification);
        if (classificationHistory.Count > FeatureExtractor.CRM_HISTORY_LENGTH)
            classificationHistory.Dequeue();

        int attackCount = 0;
        foreach (int cls in classificationHistory)
            if (cls == 1) attackCount++;
        bool majorityAttack = attackCount > FeatureExtractor.CRM_HISTORY_LENGTH / 2;

        // ---- Extension off: mirror vanilla CRM's verdict exactly, ladder stays parked ----
        if (!dangerExtensionEnabled)
        {
            return majorityAttack;
        }

        // ---- Discrepancy (Sections 4d, 7) — always on, independent of ladder state ----
        EnqueueWindow(ladder.alwaysOnDiscrepancyWindow, discrepancy > thetaDisc);
        bool discrepancyMajority = WindowMajority(ladder.alwaysOnDiscrepancyWindow);

        // ---- Emax for the CURRENT state, applied read-only to this cycle's verdict ----
        double Emax = GetEmaxForState(ladder.state, rawR, investigationCap);
        double clampedE = System.Math.Min(rawE, Emax);
        bool ladderFault = clampedE > E_t;

        bool verdict = (ladder.state == DangerCascadeController.RobotFaultState.FAULT) || discrepancyMajority || ladderFault;

        // ---- State machine transitions (decide state for NEXT cycle) — Section 5 ----
        switch (ladder.state)
        {
            case DangerCascadeController.RobotFaultState.NORMAL:
                EnqueueWindow(ladder.outlierWindow, rawE > rawR);
                if (WindowMajority(ladder.outlierWindow))
                {
                    ladder.state = DangerCascadeController.RobotFaultState.PRIMED;
                    ladder.discrepancyWindow.Clear();
                }
                break;

            case DangerCascadeController.RobotFaultState.PRIMED:
                EnqueueWindow(ladder.discrepancyWindow, discrepancy > thetaDisc);
                if (ladder.discrepancyWindow.Count >= FeatureExtractor.CRM_HISTORY_LENGTH)
                {
                    ladder.state = WindowMajority(ladder.discrepancyWindow)
                        ? DangerCascadeController.RobotFaultState.FAULT               // sensor fault
                        : DangerCascadeController.RobotFaultState.DISCREPANCY_PASSED;
                }
                break;

            case DangerCascadeController.RobotFaultState.DISCREPANCY_PASSED:
                probeEmitter.RequestProbe();
                ladder.state = DangerCascadeController.RobotFaultState.PROBING;
                break;

            case DangerCascadeController.RobotFaultState.PROBING:
            {
                ProbeResult result = probeReader.ReadResult(probeEmitter);
                if (result != ProbeResult.Pending)
                {
                    ladder.state = result == ProbeResult.Pass
                        ? DangerCascadeController.RobotFaultState.CLEARED
                        : DangerCascadeController.RobotFaultState.FAULT;
                    if (result == ProbeResult.Pass)
                        ladder.clearedTime = currentTime;
                }
                // else: probe still running, stay in PROBING.
                break;
            }

            case DangerCascadeController.RobotFaultState.FAULT:
                // Terminal for this episode (Section 5) — flag/log only, no recovery.
                break;

            case DangerCascadeController.RobotFaultState.CLEARED:
                if ((currentTime - ladder.clearedTime) > clearedTagDuration)
                {
                    ladder.state = DangerCascadeController.RobotFaultState.NORMAL;
                    ladder.outlierWindow.Clear();
                    ladder.discrepancyWindow.Clear();
                }
                break;
        }

        return verdict;
    }
}
