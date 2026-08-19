using UnityEngine;

/// <summary>
/// Danger-gated CRM extension — reads a target robot's ActuatorEmitter and produces the binary
/// probe verdict. All 3 commanded rotations must pass or the probe fails.
/// </summary>
public class ActuatorReader : MonoBehaviour, IProbeReader
{
    /// <summary>
    /// Pending while the target's probe hasn't finished all 3 rotations yet.
    /// Pass only if every rotation was within tolerance; any single failed rotation is Fail.
    /// </summary>
    public ProbeResult ReadResult(IProbeEmitter target)
    {
        if (target is not ActuatorEmitter actuator || !actuator.IsProbing || !actuator.WindowComplete)
            return ProbeResult.Pending;

        foreach (bool passed in actuator.RotationPassed)
            if (!passed) return ProbeResult.Fail;

        return ProbeResult.Pass;
    }
}
