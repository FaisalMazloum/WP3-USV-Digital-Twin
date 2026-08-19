using UnityEngine;

/// <summary>
/// Danger-gated CRM extension — the active "blink probe" (danger_crm_implementation_brief.md,
/// Section 6). Fully simulated ground truth, independent of ros2swarm — does not touch any
/// existing movement/control script.
///
/// One per robot (the probe TARGET). No doctor-robot election: whichever observer's ladder
/// reaches PROBING for this robot calls RequestProbe(), which is idempotent, so if several
/// observers reach PROBING for the same target around the same time they all just end up
/// reading the one shared probe episode and its one shared N — "agreement" falls out of there
/// being a single BlinkEmitter per robot, no coordination logic needed.
///
/// Multi-reader safe by design: once WindowComplete is true, AgreedN/BlinksCounted stay put
/// until the NEXT RequestProbe() call, rather than being cleared by whichever observer happens
/// to read them first. Every observer with a ladder in PROBING for this robot can read the same
/// completed result at its own pace. Do NOT call ResetProbe() from a normal read path — it's a
/// manual/testing utility only, since clearing it early would race exactly the observers this
/// design is meant to let read safely.
/// </summary>
public class BlinkEmitter : MonoBehaviour, IProbeEmitter
{
    [Header("Blink probe parameters (Section 6)")]
    public int Nmin = 2;
    public int Nmax = 6;
    public float T = 3f;

    // No real "software hang" mechanism exists in-sim — FaultInjector.FaultType.SoftwareHang
    // drives this via SetSimulatedHang() instead of a manual inspector toggle.
    private bool simulateHang = false;

    /// <summary> FaultInjector-driven (SoftwareHang fault) — see field comment above. </summary>
    public void SetSimulatedHang(bool hang) { simulateHang = hang; }

    public bool IsProbing { get; private set; }
    public bool WindowComplete { get; private set; }
    public int AgreedN { get; private set; }
    public int BlinksCounted { get; private set; }

    private float probeElapsed;
    private float nextBlinkTime;
    private int blinkIndex;

    /// <summary>
    /// Starts a new probe episode. No-op only while a probe is actively running
    /// (IsProbing &amp;&amp; !WindowComplete) — once the previous episode has completed, a fresh
    /// RequestProbe() is allowed even if no one has read the old result yet, since starting a new
    /// episode is what naturally supersedes it.
    /// </summary>
    public void RequestProbe()
    {
        if (IsProbing && !WindowComplete) return;

        AgreedN = Random.Range(Nmin, Nmax + 1); // Uniform[Nmin, Nmax], inclusive
        BlinksCounted = 0;
        blinkIndex = 0;
        probeElapsed = 0f;
        nextBlinkTime = (AgreedN > 0) ? (T / AgreedN) : T;
        WindowComplete = false;
        IsProbing = true;
    }

    /// <summary>
    /// Manual/testing utility only — NOT part of the normal read path (see class docs). Forces
    /// the result back to a blank slate immediately, instead of waiting for the next RequestProbe().
    /// </summary>
    public void ResetProbe()
    {
        IsProbing = false;
        WindowComplete = false;
        AgreedN = 0;
        BlinksCounted = 0;
    }

    void Update()
    {
        if (!IsProbing || WindowComplete) return;

        probeElapsed += Time.deltaTime;

        // Emit evenly-spaced blink pulses over the T-second window, unless hung — a hung
        // robot's control loop can't run the blink routine, so BlinksCounted stays behind.
        if (!simulateHang)
        {
            while (blinkIndex < AgreedN && probeElapsed >= nextBlinkTime)
            {
                BlinksCounted++;
                blinkIndex++;
                nextBlinkTime = (blinkIndex + 1) * (T / AgreedN);
            }
        }

        if (probeElapsed >= T)
        {
            WindowComplete = true;
        }
    }
}
