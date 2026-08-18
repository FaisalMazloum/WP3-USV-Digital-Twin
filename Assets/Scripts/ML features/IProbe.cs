/// <summary>
/// Danger-gated CRM extension — shared probe abstraction (danger_crm_implementation_brief.md,
/// Section 6). Lets DangerCascadeController pick which probe mechanism (BlinkEmitter/BlinkReader,
/// ActuatorEmitter/ActuatorReader, or any future one) drives PROBING via a single inspector
/// dropdown, instead of being wired to one concrete type — see DangerCascadeController.ProbeType.
/// </summary>
public enum ProbeResult { Pending, Pass, Fail }

/// <summary>
/// Implemented by every probe's Emitter (BlinkEmitter, ActuatorEmitter, ...). Multi-reader safe
/// by contract: once WindowComplete is true, the result stays put until the NEXT RequestProbe()
/// call — implementations must NOT clear it as a side effect of being read, since multiple
/// observers' ladders can be sitting in PROBING for the same target at once, each reading at its
/// own pace (see DangerCascadeController — this is what fixes the "second reader sees an already
/// -reset, permanently-Pending result" race).
/// </summary>
public interface IProbeEmitter
{
    bool IsProbing { get; }
    bool WindowComplete { get; }

    /// <summary>
    /// Starts a new probe episode. No-op only while a probe is actively running
    /// (IsProbing &amp;&amp; !WindowComplete) — a completed-but-unconsumed result does not block a
    /// fresh request, since starting a new episode is what naturally supersedes the old one.
    /// </summary>
    void RequestProbe();

    /// <summary> Manual/testing utility only — NOT part of the normal read path (see above). </summary>
    void ResetProbe();
}

/// <summary> Implemented by every probe's Reader (BlinkReader, ActuatorReader, ...). </summary>
public interface IProbeReader
{
    /// <summary> Pending until target.WindowComplete; Pass/Fail per that probe's own criterion. </summary>
    ProbeResult ReadResult(IProbeEmitter target);
}
