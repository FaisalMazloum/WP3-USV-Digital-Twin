using UnityEngine;
using System.Collections;

/// <summary>
/// Danger-gated CRM extension — actuator/rotation probe. Used INSTEAD of BlinkEmitter/BlinkReader
/// in DangerCascadeController's PROBING state; BlinkEmitter.cs/BlinkReader.cs remain in the
/// codebase and fully functional, just no longer wired into the ladder.
///
/// Commands the robot to perform 3 stationary in-place rotation attempts — no translation, each
/// held for a fixed duration (rotationDuration seconds) — at an angular rate (deg/s, matching
/// this project's existing convention — see CmdVel_Subscriber.desiredOmega /
/// NeighborStateManager.max_omega, both deg/s; there's no RPM/wheel-speed primitive anywhere in
/// this codebase) whose magnitude is drawn fresh each time from
/// Uniform[omegaMinDegPerSec, omegaMaxDegPerSec] and whose direction is an independent coin flip.
/// A rotation passes only if the robot's actually-achieved average angular velocity over that
/// fixed window (ground truth, read back via CmdVel_Subscriber.GetActualOmegaDegPerSec) matches
/// BOTH the commanded direction AND a magnitude within toleranceFraction of the commanded rate —
/// spinning at the right speed but the wrong way still fails. All 3 must pass or the probe fails.
///
/// Fixed-duration by design rather than "wait until a target angle is reached": total probe time
/// is then a deterministic 3 x rotationDuration + 2 x interRotationPause regardless of how fast
/// the robot actually spins, which is both easier to reason about and directly tunable via one
/// number instead of depending on the commanded rate.
///
/// One per robot (the probe TARGET). Same "no doctor election, one shared emitter per robot"
/// design as BlinkEmitter: whichever observer's ladder reaches PROBING for this robot calls
/// RequestProbe(), which is idempotent, so simultaneous callers just share the one running probe.
///
/// Multi-reader safe by design: once WindowComplete is true, RotationPassed stays put until the
/// NEXT RequestProbe() call, rather than being cleared by whichever observer happens to read it
/// first. Every observer with a ladder in PROBING for this robot can read the same completed
/// result at its own pace. Do NOT call ResetProbe() from a normal read path — it's a
/// manual/testing utility only, since clearing it early would race exactly the observers this
/// design is meant to let read safely.
///
/// Takes real control of the robot's thrusters via CmdVel_Subscriber.BeginActuatorOverride /
/// EndActuatorOverride for the probe's duration — unlike the blink probe, this genuinely
/// interrupts whatever the normal ros2swarm behaviour was commanding, and hands control back once
/// the probe completes.
/// </summary>
public class ActuatorEmitter : MonoBehaviour, IProbeEmitter
{
    // Rough starting point only, anchored to the one existing empirical assumption about this
    // hull's angular range in the codebase: NeighborStateManager.max_omega = 90 deg/s. Kept
    // comfortably under that rather than guessed independently (see s1/s2/E_t's history) — verify
    // against what the robot actually achieves and adjust.
    [Header("Actuator probe parameters (deg/s)")]
    public float omegaMinDegPerSec = 20f;
    public float omegaMaxDegPerSec = 80f;
    [Range(0f, 1f)] public float toleranceFraction = 0.15f;
    public float rotationDuration = 4f;     // each of the 3 attempts is held for exactly this long
    public float interRotationPause = 0.5f; // stationary gap between the 3 attempts

    // No real "software hang" mechanism exists in-sim — FaultInjector.FaultType.SoftwareHang
    // drives this via SetSimulatedHang() instead of a manual inspector toggle.
    private bool simulateHang = false;

    /// <summary> FaultInjector-driven (SoftwareHang fault) — see field comment above. </summary>
    public void SetSimulatedHang(bool hang) { simulateHang = hang; }

    public bool IsProbing { get; private set; }
    public bool WindowComplete { get; private set; }
    public bool[] RotationPassed { get; private set; } = new bool[3];
    public int CurrentRotation { get; private set; } = -1; // -1 = not currently mid-rotation

    // Live readout for the current rotation, for gizmos/tools — updated each FixedUpdate while probing.
    public float CommandedOmegaDeg { get; private set; }
    public float LastMeasuredOmegaDeg { get; private set; }

    private CmdVel_Subscriber cmdVel;
    private Rigidbody rb;

    void Start()
    {
        cmdVel = transform.root.GetComponentInChildren<CmdVel_Subscriber>();
        if (cmdVel == null)
            Debug.LogError($"{transform.root.name}: ActuatorEmitter found no CmdVel_Subscriber!");

        rb = transform.root.GetComponent<Rigidbody>();
        if (rb == null)
            Debug.LogError($"{transform.root.name}: ActuatorEmitter found no Rigidbody!");
    }

    /// <summary>
    /// Starts a new probe episode. No-op only while a probe is actively running
    /// (IsProbing &amp;&amp; !WindowComplete) — once the previous episode has completed, a fresh
    /// RequestProbe() is allowed even if no one has read the old result yet, since starting a new
    /// episode is what naturally supersedes it.
    /// </summary>
    public void RequestProbe()
    {
        if (IsProbing && !WindowComplete) return;

        IsProbing = true;
        WindowComplete = false;
        RotationPassed = new bool[3];
        CurrentRotation = -1;
        StartCoroutine(RunProbe());
    }

    /// <summary>
    /// Manual/testing utility only — NOT part of the normal read path (see class docs). Forces
    /// the result back to a blank slate immediately, instead of waiting for the next RequestProbe().
    /// </summary>
    public void ResetProbe()
    {
        IsProbing = false;
        WindowComplete = false;
        RotationPassed = new bool[3];
        CurrentRotation = -1;
    }

    private IEnumerator RunProbe()
    {
        for (int i = 0; i < 3; i++)
        {
            CurrentRotation = i;

            // omegaMinDegPerSec/omegaMaxDegPerSec bound magnitude only; direction is a fair coin
            // flip each rotation, independent of speed.
            float direction = Random.value < 0.5f ? 1f : -1f;
            float commandedOmegaDeg = direction * Random.Range(omegaMinDegPerSec, omegaMaxDegPerSec);
            CommandedOmegaDeg = commandedOmegaDeg;
            LastMeasuredOmegaDeg = 0f;

            // simulateHang: motors don't respond to the command — the robot just keeps doing
            // whatever it was already doing, which will not match the commanded rate.
            //
            // No sign flip here (there used to be one): that was compensating for the old
            // force/PID-driven CmdVel_Subscriber, where the real-world rotation direction for a
            // given target sign was an empirical fact about the physics, not something the
            // override math guaranteed. Now that CmdVel_Subscriber drives kinematically (target
            // omega is written straight onto rb.angularVelocity), GetActualOmegaDegPerSec() is a
            // tautological readback of exactly what's passed in here — flipping it just makes a
            // healthy robot fail with "right speed, wrong direction" every time.
            if (!simulateHang && cmdVel != null)
                cmdVel.BeginActuatorOverride(commandedOmegaDeg);

            float omegaSum = 0f;
            int omegaSamples = 0;
            float elapsed = 0f;

            while (elapsed < rotationDuration)
            {
                yield return new WaitForFixedUpdate();

                float measuredOmegaDeg = cmdVel != null ? cmdVel.GetActualOmegaDegPerSec() : 0f;
                LastMeasuredOmegaDeg = measuredOmegaDeg;
                omegaSum += measuredOmegaDeg; // signed — direction matters, not just speed
                omegaSamples++;
                elapsed += Time.fixedDeltaTime;
            }

            // Signed average, checked in two parts: same direction as commanded, AND magnitude
            // within tolerance. A rotation that hits the right speed but the wrong direction
            // fails — Mathf.Sign(0) returns 1, but a near-zero (hung) measured average will fail
            // the magnitude check regardless, so that edge case can't produce a false pass.
            float averageOmegaDeg = omegaSamples > 0 ? omegaSum / omegaSamples : 0f;
            bool sameDirection = Mathf.Sign(averageOmegaDeg) == Mathf.Sign(commandedOmegaDeg);
            float measuredMagnitude = Mathf.Abs(averageOmegaDeg);
            float magnitudeLower = Mathf.Abs(commandedOmegaDeg) * (1f - toleranceFraction);
            float magnitudeUpper = Mathf.Abs(commandedOmegaDeg) * (1f + toleranceFraction);
            bool magnitudeOk = measuredMagnitude >= magnitudeLower && measuredMagnitude <= magnitudeUpper;
            RotationPassed[i] = sameDirection && magnitudeOk;

            // brief stationary pause between attempts, still under override so normal movement
            // doesn't briefly resume and cause a lurch between rotations
            if (!simulateHang && cmdVel != null)
                cmdVel.BeginActuatorOverride(0f);
            yield return new WaitForSeconds(interRotationPause);
        }

        if (cmdVel != null)
            cmdVel.EndActuatorOverride();

        CurrentRotation = -1;
        WindowComplete = true;
    }
}
