using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Danger-gated CRM extension — swarm-wide verdict-sync layer, separate/modular from
/// DangerCascadeController. Any single observer's own ladder can still trigger and progress
/// independently (no cross-robot majority required to enter the ladder — that's deliberate, see
/// history below) — this component's only job is: once any observer's ladder concludes FAULT or
/// CLEARED for a target, it calls BroadcastVerdict(). Every DangerCascadeController checks
/// TryGetCanonicalVerdict() each cycle and adopts it — so a robot that never independently
/// suspected the target still ends up agreeing, instead of redundantly re-running its own full
/// investigation, or worse, never finding out at all.
///
/// (Earlier revision of this file also gated ladder entry behind a spatial majority — removed:
/// single-robot trigger is simpler, more sensitive to a fault only visible to one observer given
/// swarm topology, and doesn't need this component to make that call at all.)
///
/// Entirely optional: DangerCascadeController.swarmConsensus is nullable, and every call site
/// falls back to the original purely-per-observer behavior (no verdict syncing) if this component
/// isn't present in the scene. Drop one instance anywhere in the scene to opt in swarm-wide.
/// </summary>
public class SwarmConsensusCoordinator : MonoBehaviour
{
    // Toggle on to see every broadcast land in the Console — useful for confirming the sync
    // chain is actually firing rather than guessing from the heatmap alone.
    public bool logVerbose = true;

    private static int activeInstanceCount = 0;

    private Dictionary<GameObject, DangerCascadeController.RobotFaultState> canonicalState
        = new Dictionary<GameObject, DangerCascadeController.RobotFaultState>();
    private Dictionary<GameObject, float> canonicalTime = new Dictionary<GameObject, float>();

    void OnEnable()
    {
        activeInstanceCount++;
        if (activeInstanceCount > 1)
            Debug.LogError($"SwarmConsensusCoordinator: {activeInstanceCount} instances active in the scene! " +
                "There should be exactly ONE (not one per robot) — with more than one, different " +
                "DangerCascadeControllers can resolve to DIFFERENT coordinator instances via " +
                "FindObjectOfType, so broadcasts from one never reach observers wired to another. " +
                "Check you didn't add this component to the robot prefab by mistake.");
    }

    void OnDisable()
    {
        activeInstanceCount--;
    }

    /// <summary>
    /// Called by a DangerCascadeController whose ladder just concluded FAULT or CLEARED for
    /// target. FAULT is terminal (Section 5) — once broadcast, a later CLEARED from a different
    /// observer (whose own investigation may simply have started earlier, before the fault became
    /// evident) is ignored rather than silently overwriting it. A later FAULT is always accepted.
    /// </summary>
    public void BroadcastVerdict(GameObject target, DangerCascadeController.RobotFaultState state, float time)
    {
        if (state == DangerCascadeController.RobotFaultState.CLEARED
            && canonicalState.TryGetValue(target, out var existing)
            && existing == DangerCascadeController.RobotFaultState.FAULT)
        {
            // if (logVerbose)
            //     Debug.Log($"SwarmConsensusCoordinator: ignored CLEARED broadcast for {target.name} — already FAULT.");
            return;
        }

        canonicalState[target] = state;
        canonicalTime[target] = time;

        // if (logVerbose)
        //     Debug.Log($"SwarmConsensusCoordinator: broadcast {target.name} -> {state} at t={time:F1} " +
        //         $"(instance {GetInstanceID()}, {canonicalState.Count} targets tracked).");
    }

    /// <summary> The swarm-wide conclusion for target, if any observer has reached one yet. </summary>
    public bool TryGetCanonicalVerdict(GameObject target, out DangerCascadeController.RobotFaultState state, out float time)
    {
        if (canonicalState.TryGetValue(target, out state))
        {
            time = canonicalTime[target];
            return true;
        }
        time = 0f;
        return false;
    }
}
