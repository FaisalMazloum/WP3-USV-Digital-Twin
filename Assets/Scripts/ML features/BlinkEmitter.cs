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
/// </summary>
public class BlinkEmitter : MonoBehaviour
{
    [Header("Blink probe parameters (Section 6)")]
    public int Nmin = 2;
    public int Nmax = 6;
    public float T = 3f;

    [Header("Hang simulation — no real 'software hang' exists in-sim, toggle manually to fail the probe")]
    public bool simulateHang = false;

    public bool IsProbing { get; private set; }
    public bool WindowComplete { get; private set; }
    public int AgreedN { get; private set; }
    public int BlinksCounted { get; private set; }

    private float probeElapsed;
    private float nextBlinkTime;
    private int blinkIndex;

    /// <summary>
    /// Starts a new probe episode. No-op if a probe is already running or awaiting readout —
    /// call ResetProbe() first once a caller has consumed WindowComplete's result.
    /// </summary>
    public void RequestProbe()
    {
        if (IsProbing) return;

        AgreedN = Random.Range(Nmin, Nmax + 1); // Uniform[Nmin, Nmax], inclusive
        BlinksCounted = 0;
        blinkIndex = 0;
        probeElapsed = 0f;
        nextBlinkTime = (AgreedN > 0) ? (T / AgreedN) : T;
        WindowComplete = false;
        IsProbing = true;
    }

    /// <summary> Clears a consumed result so the next RequestProbe() starts a fresh episode. </summary>
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
