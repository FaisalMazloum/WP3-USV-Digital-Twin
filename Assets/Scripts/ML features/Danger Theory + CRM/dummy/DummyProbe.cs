using UnityEngine;

/// <summary>
/// Dummy BFV sim (dummy_bfv_sim_brief.md, Section 8) — deterministic stand-in for the real
/// probe hardware (BlinkEmitter/BlinkReader, ActuatorEmitter/ActuatorReader). No LEDs, no
/// actuator physics: after the same probe window T used by the live sim, the target simply
/// reports PASS if healthy or FAIL if isFaulty.
///
/// Implements both IProbeEmitter and IProbeReader on a single class (the split into separate
/// Emitter/Reader components in the real sim exists only because of the different physical
/// hardware each probe type reads off — no such split is needed here). One instance per virtual
/// robot (as probe target), shared across every observer whose ladder is in PROBING for it —
/// multi-reader safe by the same contract as BlinkEmitter/ActuatorEmitter (IProbe.cs): the
/// result is never cleared as a side effect of being read, only by the next RequestProbe().
/// </summary>
public class DummyProbe : IProbeEmitter, IProbeReader
{
    /// <summary> Probe window (Section 8) — must match the live sim's probe duration. </summary>
    public float T = 3f;

    /// <summary> Driven externally by DummyBFVSwarm from the fault-injection timeline. </summary>
    public bool isFaulty = false;

    public bool IsProbing { get; private set; }
    public bool WindowComplete { get; private set; }
    public bool Passed { get; private set; }

    private float probeElapsed;

    public void RequestProbe()
    {
        if (IsProbing && !WindowComplete) return;

        probeElapsed = 0f;
        WindowComplete = false;
        IsProbing = true;
    }

    /// <summary> Manual/testing utility only — NOT part of the normal read path (see class docs). </summary>
    public void ResetProbe()
    {
        IsProbing = false;
        WindowComplete = false;
        Passed = false;
    }

    /// <summary> Advance the probe's own clock by one tick — called once per control cycle by DummyBFVSwarm. </summary>
    public void Tick(float deltaTime)
    {
        if (!IsProbing || WindowComplete) return;

        probeElapsed += deltaTime;
        if (probeElapsed >= T)
        {
            Passed = !isFaulty;
            WindowComplete = true;
        }
    }

    public ProbeResult ReadResult(IProbeEmitter target)
    {
        if (!target.WindowComplete) return ProbeResult.Pending;
        var probe = (DummyProbe)target;
        return probe.Passed ? ProbeResult.Pass : ProbeResult.Fail;
    }
}
