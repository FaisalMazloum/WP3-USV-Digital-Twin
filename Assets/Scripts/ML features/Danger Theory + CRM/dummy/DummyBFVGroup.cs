using System;

/// <summary>
/// Dummy BFV sim (dummy_bfv_sim_brief.md, Section 3) — one inspector-editable BFV-group entry:
/// a BFV value and how many virtual robots hold it. BFV bit layout matches
/// FeatureExtractor.allFeatures/DangerCascadeController exactly: fvIndex = F1&lt;&lt;0 | F2&lt;&lt;1 |
/// F3&lt;&lt;2 | F4&lt;&lt;3 | F5&lt;&lt;4 | F6&lt;&lt;5 (0-63).
/// </summary>
[Serializable]
public class DummyBFVGroup
{
    public string label = "group";

    public bool F1;
    public bool F2;
    public bool F3;
    public bool F4;
    public bool F5;
    public bool F6;

    public int robotCount = 1;

    /// <summary>
    /// Marks this group as the fault target (Section 3, Entry C) — the first robot instantiated
    /// from this group is the one FaultInjector's injectFault flag can fault. Intended for a
    /// group with robotCount == 1.
    /// </summary>
    public bool isFaultTarget = false;

    public int FvIndex =>
        (F1 ? 1 << 0 : 0) | (F2 ? 1 << 1 : 0) | (F3 ? 1 << 2 : 0) |
        (F4 ? 1 << 3 : 0) | (F5 ? 1 << 4 : 0) | (F6 ? 1 << 5 : 0);
}
