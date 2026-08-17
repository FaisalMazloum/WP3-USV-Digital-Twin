using UnityEngine;

/// <summary>
/// Danger-gated CRM extension — proprioceptive neighbour-count channel
/// (danger_crm_implementation_brief.md, Sections 4a/4b/4c).
///
/// One per robot. Does not modify NeighborStateManager.cs or RABSensor.cs — it only
/// reads RABSensor's existing public GetDetections() API.
///
/// - GetTrueNeighborCount()        → uncorrupted ground truth (4b). Always the real
///                                    RAB detection count, regardless of the fault hook.
/// - GetSelfReportedNeighborCount() → this robot's own proprioceptive estimate (4a).
///                                    Equal to the true count unless corruptSelfReport
///                                    is enabled, in which case it returns the manually
///                                    injected override (4c) — no fault schedule is
///                                    implemented here, this is just the injection hook.
/// </summary>
public class NeighborCountReporter : MonoBehaviour
{
    private RABSensor rabSensor;

    [Header("Sensor-fault injection hook (Section 4c) — driven externally, no schedule here")]
    public bool corruptSelfReport = false;
    public int corruptedNeighborCountOverride = 0;

    void Start()
    {
        rabSensor = transform.root.GetComponentInChildren<RABSensor>();
        if (rabSensor == null)
        {
            Debug.LogError($"{transform.root.name}: NeighborCountReporter found no RABSensor!");
        }
    }

    /// <summary> Uncorrupted ground-truth neighbour count for this robot (Section 4b). </summary>
    public int GetTrueNeighborCount()
    {
        return rabSensor != null ? rabSensor.GetDetections().Count : 0;
    }

    /// <summary> This robot's own (corruptible) self-reported neighbour count (Section 4a). </summary>
    public int GetSelfReportedNeighborCount()
    {
        return corruptSelfReport ? corruptedNeighborCountOverride : GetTrueNeighborCount();
    }
}
