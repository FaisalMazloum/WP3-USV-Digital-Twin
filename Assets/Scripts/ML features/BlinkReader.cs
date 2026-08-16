using UnityEngine;

/// <summary>
/// Danger-gated CRM extension — reads a target robot's BlinkEmitter and produces the binary
/// probe verdict (danger_crm_implementation_brief.md, Section 6). Ground truth for now —
/// counts exactly what BlinkEmitter reports; latency/lag-based counting is a later extension.
/// </summary>
public class BlinkReader : MonoBehaviour
{
    public enum ProbeResult { Pending, Pass, Fail }

    /// <summary>
    /// Pending while the target's probe hasn't finished its T-second window yet.
    /// Pass only on an exact blink-count match within the window; anything else is Fail.
    /// </summary>
    public ProbeResult ReadResult(BlinkEmitter target)
    {
        if (target == null || !target.IsProbing || !target.WindowComplete)
            return ProbeResult.Pending;

        return (target.BlinksCounted == target.AgreedN) ? ProbeResult.Pass : ProbeResult.Fail;
    }
}
