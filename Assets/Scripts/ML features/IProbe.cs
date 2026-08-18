/// <summary>
/// Danger-gated CRM extension — shared probe abstraction (danger_crm_implementation_brief.md,
/// Section 6). Lets DangerCascadeController pick which probe mechanism (BlinkEmitter/BlinkReader,
/// ActuatorEmitter/ActuatorReader, or any future one) drives PROBING via a single inspector
/// dropdown, instead of being wired to one concrete type — see DangerCascadeController.ProbeType.
/// </summary>
public enum ProbeResult { Pending, Pass, Fail }

/// <summary> Implemented by every probe's Emitter (BlinkEmitter, ActuatorEmitter, ...). </summary>
public interface IProbeEmitter
{
    bool IsProbing { get; }
    bool WindowComplete { get; }

    /// <summary> Starts a new probe episode. No-op if one is already running or awaiting readout. </summary>
    void RequestProbe();

    /// <summary> Clears a consumed result so the next RequestProbe() starts a fresh episode. </summary>
    void ResetProbe();
}

/// <summary> Implemented by every probe's Reader (BlinkReader, ActuatorReader, ...). </summary>
public interface IProbeReader
{
    /// <summary> Pending until target.WindowComplete; Pass/Fail per that probe's own criterion. </summary>
    ProbeResult ReadResult(IProbeEmitter target);
}
