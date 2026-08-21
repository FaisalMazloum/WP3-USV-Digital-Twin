using System.IO;
using System.Text;

/// <summary>
/// Dummy BFV sim — CSV logging only, no detection-logic involvement (DummyBFVSwarm/
/// DummyDangerLadderEngine are unaware of this class beyond calling LogEvent/LogCycle).
///
/// Two independent CSVs:
///  - Event log (always on) — one row per real event. Written immediately (no buffering) since
///    events are low-frequency; durable the instant they happen.
///  - Per-cycle debug log (optional, gated by DummyBFVSwarm.enablePerCycleLog) — one row per
///    control cycle per robot. Buffered in memory and flushed once per control cycle (bounded
///    memory, far fewer file writes than per-row) since this can get large.
///
/// Both append across Play sessions/trials into the same files rather than overwriting — each
/// file's header is written only the first time that file is created.
/// </summary>
public class DummyBFVCsvLogger
{
    public bool Active { get; private set; } = true;

    private const string EventHeader = "trial_id,injection_offset,cleared_duration,sim_time,event_type,robot_id";
    private const string CycleHeader = "trial_id,injection_offset,cleared_duration,sim_time,robot_id,BFV,count_Aj,state,E,R,cap,probe_result,classification,is_faulty_groundtruth";

    private readonly string eventPath;
    private readonly string cyclePath;
    private readonly StringBuilder cycleBuffer = new StringBuilder();
    private bool cycleFileInitialized;

    public DummyBFVCsvLogger(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        eventPath = Path.Combine(outputDir, "dummy_bfv_events.csv");
        cyclePath = Path.Combine(outputDir, "dummy_bfv_cycles.csv");

        // Event log is always-on — create it (with header) up front. Cycle log is optional and
        // can get large, so it's only created lazily on the first actual LogCycle() call.
        if (!File.Exists(eventPath))
            File.WriteAllText(eventPath, EventHeader + "\n");

        cycleFileInitialized = File.Exists(cyclePath);
    }

    public void LogEvent(int trialId, float injectionOffset, float clearedDuration, float simTime, string eventType, string robotId)
    {
        if (!Active) return;

        string row = string.Join(",",
            trialId.ToString(),
            injectionOffset.ToString("F4"),
            clearedDuration.ToString("F4"),
            simTime.ToString("F4"),
            eventType,
            robotId);
        File.AppendAllText(eventPath, row + "\n");
    }

    public void LogCycle(int trialId, float injectionOffset, float clearedDuration, float simTime, int robotId, int bfv, int countAj,
        string state, double E, double R, double cap, string probeResult, string classification, bool groundTruthFaulty)
    {
        if (!Active) return;

        if (!cycleFileInitialized)
        {
            File.WriteAllText(cyclePath, CycleHeader + "\n");
            cycleFileInitialized = true;
        }

        string row = string.Join(",",
            trialId.ToString(),
            injectionOffset.ToString("F4"),
            clearedDuration.ToString("F4"),
            simTime.ToString("F4"),
            robotId.ToString(),
            bfv.ToString(),
            countAj.ToString(),
            state,
            E.ToString("F4"),
            R.ToString("F4"),
            double.IsPositiveInfinity(cap) ? "inf" : cap.ToString("F4"),
            probeResult,
            classification,
            groundTruthFaulty ? "1" : "0");
        cycleBuffer.Append(row).Append('\n');
    }

    /// <summary> Flush buffered per-cycle rows to disk — call once per control cycle and defensively on teardown. </summary>
    public void FlushCycleBuffer()
    {
        if (cycleBuffer.Length == 0) return;
        File.AppendAllText(cyclePath, cycleBuffer.ToString());
        cycleBuffer.Clear();
    }

    /// <summary> Stop accepting further rows and flush anything still buffered. One-way — matches the manual "stop logging" button. </summary>
    public void Stop()
    {
        FlushCycleBuffer();
        Active = false;
    }
}
