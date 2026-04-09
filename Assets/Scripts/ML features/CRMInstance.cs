using System;
using System.Collections.Generic;

/// <summary>
/// CRM Phase B — per-robot immune-system-inspired fault detector.
/// Implements CRM_TCELLSINEXCESS from Tarapore et al. (2017) PLoS ONE.
///
/// Entry point: SimulationStep(fvCounts)
///   fvCounts: Dictionary mapping FV index (0..63) to number of
///             neighbors currently exhibiting that FV.
///
/// Output: FVClassification[j]
///   0 = undecided
///   1 = attack  (abnormal / faulty)
///   2 = tolerate (normal)
/// </summary>
public class CRMInstance
{
    // -------------------------------------------------------------------------
    // Parameters — matched exactly to crminrobotagent_optimised.cpp
    // -------------------------------------------------------------------------
    private const int    NUM_FEATURES      = 6;
    private const int    NUM_CLONES        = 1 << NUM_FEATURES; // 64
    private const double SEED_E            = 10.0;
    private const double SEED_R            = 10.0;
    private const double SE                = 10.0;       // bolus added to TE each cycle for active APC clones
    private const double SR                = 10.0;       // bolus added to TR each cycle for active APC clones
    private const double SE_RATE           = 0.0;        // continuous influx in ODE — zero in actual code
    private const double SR_RATE           = 0.0;
    private const double KPE               = 1e-3;       // effector proliferation rate (πE)
    private const double KPR               = 0.7e-3;     // regulatory proliferation rate (πR)
    private const double KDE               = 1e-6;       // effector death rate (δ)
    private const double KDR               = 1e-6;       // regulatory death rate (δ)
    private const double CROSS_AFFINITY    = 0.11;       // c — cross-reactivity
    private const double FV_TO_APC_SCALING = 0.002;      // k — scales robot count to APC density
    private const int    SITES             = 3;          // s — binding sites per APC
    private const double KON               = 0.1;        // γc — conjugation rate
    private const double KOFF              = 0.1;        // γd — deconjugation rate
    private const double INTEGRATION_TIME  = 5e7;        // S — max integration time (a.u.)
    private const double CELL_LOWER_BOUND  = 1e-3;       // clone pruning threshold
    private const double UPPER_STEP        = 500000.0;   // max adaptive step size
    private const double LOWER_STEP        = 1e-6;       // min adaptive step size
    private const double ERROR_ALLOWED     = 1e-2;       // ε — step size control tolerance
    private const double PERC_CONVERGENCE  = 0.001;      // % — early exit threshold

    // -------------------------------------------------------------------------
    // T-cell clone state — indexed by clone index i = FV index (0..63)
    // Clones are treated as dead when TE[i] + TR[i] <= CELL_LOWER_BOUND
    // -------------------------------------------------------------------------
    public double[] TE;       // effector T-cell density per clone
    public double[] TR;       // regulatory T-cell density per clone

    // -------------------------------------------------------------------------
    // APC sub-populations — indexed by FV index j (0..63)
    // APC[j] = 0 means no robots observed with FV j this cycle
    // -------------------------------------------------------------------------
    public double[] APC;      // A_j = neighbor_count_j * FV_TO_APC_SCALING

    // -------------------------------------------------------------------------
    // Affinity matrix — precomputed once, never changes
    // theta[i,j] = exp( -HammingDist(i,j) / (c * l) )
    // -------------------------------------------------------------------------
    public double[,] theta;

    // -------------------------------------------------------------------------
    // Per-cycle intermediate values indexed by active APC j
    // -------------------------------------------------------------------------
    private double[] sigmaTj;  // Σ_i θ_ij (TE_i + TR_i)
    private double[] sigmaEj;  // Σ_i θ_ij  TE_i
    private double[] sigmaRj;  // Σ_i θ_ij  TR_i
    private double[] Cj;       // total conjugates on APC j
    private double[] ECj;      // total effector conjugates on APC j
    private double[] RCj;      // total regulatory conjugates on APC j

    // -------------------------------------------------------------------------
    // RK2 working arrays
    // -------------------------------------------------------------------------
    private double[] TE_Eu;    // Euler-predicted effector (K1 input)
    private double[] TR_Eu;    // Euler-predicted regulatory (K1 input)
    private double[] dE_k0;    // dTE/dt at K0 (current state)
    private double[] dR_k0;    // dTR/dt at K0
    private double[] dE_k1;    // dTE/dt at K1 (Euler-predicted state)
    private double[] dR_k1;    // dTR/dt at K1

    // -------------------------------------------------------------------------
    // Classification output — updated each SimulationStep call
    // 0 = undecided, 1 = attack (abnormal), 2 = tolerate (normal)
    // -------------------------------------------------------------------------
    public int[] FVClassification;

    // =========================================================================
    // Constructor
    // =========================================================================
    public CRMInstance()
    {
        TE               = new double[NUM_CLONES];
        TR               = new double[NUM_CLONES];
        APC              = new double[NUM_CLONES];
        sigmaTj          = new double[NUM_CLONES];
        sigmaEj          = new double[NUM_CLONES];
        sigmaRj          = new double[NUM_CLONES];
        Cj               = new double[NUM_CLONES];
        ECj              = new double[NUM_CLONES];
        RCj              = new double[NUM_CLONES];
        TE_Eu            = new double[NUM_CLONES];
        TR_Eu            = new double[NUM_CLONES];
        dE_k0            = new double[NUM_CLONES];
        dR_k0            = new double[NUM_CLONES];
        dE_k1            = new double[NUM_CLONES];
        dR_k1            = new double[NUM_CLONES];
        FVClassification = new int[NUM_CLONES];

        // Step 2: Initialise all 64 clones with seed densities (done once)
        for (int i = 0; i < NUM_CLONES; i++)
        {
            TE[i] = SEED_E;
            TR[i] = SEED_R;
        }

        // Precompute full 64×64 affinity matrix — static, computed once
        theta = new double[NUM_CLONES, NUM_CLONES];
        for (int i = 0; i < NUM_CLONES; i++)
            for (int j = 0; j < NUM_CLONES; j++)
                theta[i, j] = NegExpDistAffinity(i, j);
    }

    // =========================================================================
    // Entry point — called once per 1 s sample tick by each robot
    // fvCounts: FV index → number of neighbors currently with that FV
    // =========================================================================
    public void SimulationStep(Dictionary<int, int> fvCounts)
    {
        // Step 1 — build APC sub-populations from observed FV distribution
        BuildAPCs(fvCounts);

        // Step 2 — inject T-cell bolus for clones whose APC is currently active
        InjectTCellBolus();

        // Steps 3–6 — integrate T-cell ODEs with Euler-Heun adaptive stepper
        TcellNumericalIntegration_RK2();

        // Step 8 — classify each active APC's FV as normal or abnormal
        UpdateClassification();
    }

    // =========================================================================
    // Step 1 — Build APC sub-populations
    // A_j = neighbor_count_j * FV_TO_APC_SCALING
    // Only FVs with at least one neighbor get a non-zero APC
    // =========================================================================
    private void BuildAPCs(Dictionary<int, int> fvCounts)
    {
        Array.Clear(APC, 0, NUM_CLONES);

        foreach (KeyValuePair<int, int> kvp in fvCounts)
        {
            int fvIndex    = kvp.Key;
            int robotCount = kvp.Value;
            if (robotCount > 0 && fvIndex >= 0 && fvIndex < NUM_CLONES)
                APC[fvIndex] = robotCount * FV_TO_APC_SCALING;
        }
    }

    // =========================================================================
    // Step 2 — Inject T-cell bolus
    // For every clone i whose FV index has an active APC (APC[i] > 0),
    // add SE to TE[i] and SR to TR[i]. This is a discrete bolus, not an ODE term.
    // se_rate = 0 in the ODE — see ComputeDerivatives.
    // Source: UpdateTcellList() in crminrobotagent_optimised.cpp
    // =========================================================================
    private void InjectTCellBolus()
    {
        for (int i = 0; i < NUM_CLONES; i++)
        {
            if (APC[i] > 0.0)
            {
                TE[i] += SE;
                TR[i] += SR;
            }
        }
    }

    // =========================================================================
    // Steps 3–7 — Euler-Heun adaptive RK2 integration
    // Integrates dTE/dt and dTR/dt from t=0 to t=S (or convergence).
    // Source: TcellNumericalIntegration_RK2() in crminrobotagent_optimised.cpp
    // =========================================================================
    private void TcellNumericalIntegration_RK2()
    {
        double convergence_errormax             = -1.0;
        double perc_convergence_errormax        = 0.0;
        double integration_t                    = 0.0;
        double step_h                           = 1.0;
        bool   b_prevdiff0occurance             = false;
        bool   b_tcelldeath                     = false;
        double m_fERRORALLOWED_TCELL_STEPSIZE   = ERROR_ALLOWED;
        double m_fTCELL_CONVERGENCE             = PERC_CONVERGENCE;

        // Snapshot of T-cell state at start of integration.
        // Used to restore if all clones die during integration (extinction reset).
        // Source: listTcells_cpy in C++ source.
        double[] TE_cpy = new double[NUM_CLONES];
        double[] TR_cpy = new double[NUM_CLONES];
        Array.Copy(TE, TE_cpy, NUM_CLONES);
        Array.Copy(TR, TR_cpy, NUM_CLONES);

        double m_fTCELL_UPPERLIMIT_STEPSIZE = UPPER_STEP;

        while (integration_t < INTEGRATION_TIME)
        {
            // ── K0 pass: conjugates and derivatives at current (TE, TR) ───────
            // b_tcelldeath signals that a clone died in the previous accepted
            // step — passed to ComputeConjugates so dead clones are excluded.
            // Source: ConjugatesQSS_ExcessTcells(b_tcelldeath, K0)
            ComputeConjugates(useEuler: false, b_tcelldeath: b_tcelldeath);
            b_tcelldeath = false;
            ComputeDerivatives(dE_k0, dR_k0, useEuler: false);

            // Euler prediction: fE_Eu = fE + step_h * fDeltaE_k0
            for (int i = 0; i < NUM_CLONES; i++)
            {
                if (TE[i] + TR[i] <= CELL_LOWER_BOUND)
                {
                    TE_Eu[i] = 0.0;
                    TR_Eu[i] = 0.0;
                    continue;
                }
                TE_Eu[i] = Math.Max(0.0, TE[i] + step_h * dE_k0[i]);
                TR_Eu[i] = Math.Max(0.0, TR[i] + step_h * dR_k0[i]);
            }

            // ── K1 pass: conjugates and derivatives at Euler-predicted state ──
            // Source: ConjugatesQSS_ExcessTcells(false, K1) + Derivative_ExcessTcells(K1)
            ComputeConjugates(useEuler: true, b_tcelldeath: false);
            ComputeDerivatives(dE_k1, dR_k1, useEuler: true);

            // ── Heun correction — error estimate only, never accepted as state ─
            // absDiffHuenEuler tracks worst-case |Heun - Euler| across all clones
            double absDiffHuenEuler = -1.0;
            for (int i = 0; i < NUM_CLONES; i++)
            {
                if (TE[i] + TR[i] <= CELL_LOWER_BOUND) continue;

                double fE_Hu = Math.Max(0.0, TE[i] + 0.5 * step_h * (dE_k0[i] + dE_k1[i]));
                double fR_Hu = Math.Max(0.0, TR[i] + 0.5 * step_h * (dR_k0[i] + dR_k1[i]));

                if (fE_Hu > 0.0)
                {
                    double tmp_E = Math.Abs(fE_Hu - TE_Eu[i]);
                    if (tmp_E > absDiffHuenEuler) absDiffHuenEuler = tmp_E;
                }

                if (fR_Hu > 0.0)
                {
                    double tmp_R = Math.Abs(fR_Hu - TR_Eu[i]);
                    if (tmp_R > absDiffHuenEuler) absDiffHuenEuler = tmp_R;
                }
            }

            // ── Handle zero Heun-Euler difference ────────────────────────────
            // Source: if(absDiffHuenEuler == 0.0) block in C++ source
            if (absDiffHuenEuler == 0.0)
            {
                if (b_prevdiff0occurance && step_h == LOWER_STEP)
                {
                    if (m_fERRORALLOWED_TCELL_STEPSIZE <= 1.0e-10)
                        break;
                    else
                    {
                        m_fERRORALLOWED_TCELL_STEPSIZE /= 10.0;
                        m_fTCELL_CONVERGENCE           /= 10.0;
                        b_prevdiff0occurance            = false;
                        continue;
                    }
                }
                step_h = Math.Max(step_h / 2.0, LOWER_STEP);
                b_prevdiff0occurance = true;
                continue;
            }

            b_prevdiff0occurance = false;

            // ── Adaptive step size control ────────────────────────────────────
            // step_h *= sqrt(ERRORALLOWED / absDiffHuenEuler)
            step_h *= Math.Sqrt(m_fERRORALLOWED_TCELL_STEPSIZE / absDiffHuenEuler);
            if (step_h > m_fTCELL_UPPERLIMIT_STEPSIZE) step_h = m_fTCELL_UPPERLIMIT_STEPSIZE;
            else if (step_h < LOWER_STEP)              step_h = LOWER_STEP;

            // ── Accept step using K0 slope ────────────────────────────────────
            // State updated with fE += step_h * fDeltaE_k0 (not Heun average).
            // Source: (*it_tcells).fE += tmp_incr in C++ source.
            convergence_errormax = -1.0;

            bool anyLiveClone = false;
            for (int i = 0; i < NUM_CLONES; i++)
            {
                if (TE[i] + TR[i] <= CELL_LOWER_BOUND)
                {
                    TE[i] = 0.0;
                    TR[i] = 0.0;
                    continue;
                }

                double fE_prev = TE[i];
                double fR_prev = TR[i];

                // E update — source: tmp_incr = step_h * fDeltaE_k0
                double tmp_incr = step_h * dE_k0[i];
                TE[i] += tmp_incr;
                if (TE[i] < 0.0)
                    TE[i] = 0.0;
                else
                {
                    double tmp_absincr = Math.Abs(tmp_incr);
                    if (tmp_absincr > convergence_errormax)
                    {
                        convergence_errormax      = tmp_absincr;
                        perc_convergence_errormax = (convergence_errormax / fE_prev) * 100.0;
                    }
                }

                // R update — source: tmp_incr = step_h * fDeltaR_k0 (reused variable)
                tmp_incr = step_h * dR_k0[i];
                TR[i] += tmp_incr;
                if (TR[i] < 0.0)
                    TR[i] = 0.0;
                else
                {
                    double tmp_absincr = Math.Abs(tmp_incr);
                    if (tmp_absincr > convergence_errormax)
                    {
                        convergence_errormax      = tmp_absincr;
                        perc_convergence_errormax = (convergence_errormax / fR_prev) * 100.0;
                    }
                }

                // Prune dead clones — flag b_tcelldeath for next K0 conjugate pass
                if (TE[i] + TR[i] <= CELL_LOWER_BOUND)
                {
                    TE[i]        = 0.0;
                    TR[i]        = 0.0;
                    b_tcelldeath = true;
                    continue;
                }

                anyLiveClone = true;
            }

            // ── Total extinction reset ────────────────────────────────────────
            // If all clones have died, restore from snapshot and reduce upper
            // step size limit. Source: if(listTcells.size() == 0) block in C++.
            if (!anyLiveClone)
            {
                Array.Copy(TE_cpy, TE, NUM_CLONES);
                Array.Copy(TR_cpy, TR, NUM_CLONES);

                convergence_errormax        = -1.0;
                integration_t               = 0.0;
                step_h                      = 1.0;
                b_prevdiff0occurance        = false;
                b_tcelldeath                = false;
                m_fTCELL_UPPERLIMIT_STEPSIZE /= 10.0;
                continue;
            }

            // ── Convergence break ─────────────────────────────────────────────
            if (perc_convergence_errormax <= m_fTCELL_CONVERGENCE)
                break;

            integration_t += step_h;
        }
    }

    // =========================================================================
    // Step 4 — Compute conjugates (ExcessTcells closed-form approximation)
    // Assumes T-cells always in excess of APC sites.
    // useEuler = false → use (TE, TR); true → use (TE_Eu, TR_Eu) for K1 pass.
    // Source: ConjugatesQSS_ExcessTcells() in C++ source.
    //
    // For each active APC j:
    //   sigmaTj = Σ_i θ_ij (TE_i + TR_i)
    //   sigmaEj = Σ_i θ_ij  TE_i
    //   sigmaRj = Σ_i θ_ij  TR_i
    //   Cj  = (A_j * s * sigmaTj) / (sigmaTj + 1)
    //   ECj = Cj * sigmaEj / sigmaTj
    //   RCj = Cj * sigmaRj / sigmaTj
    // =========================================================================
    // b_tcelldeath: when true, a clone died in the previous accepted step.
    // In the array implementation this has no extra cleanup effect beyond the
    // existing CELL_LOWER_BOUND guard, but the flag is passed for fidelity
    // with the C++ source which uses it to clear dead conjugate pointers.
    // Source: ConjugatesQSS_ExcessTcells(bool bClearDeadConjugates, ...)
    private void ComputeConjugates(bool useEuler, bool b_tcelldeath)
    {
        for (int j = 0; j < NUM_CLONES; j++)
        {
            if (APC[j] == 0.0)
            {
                sigmaTj[j] = sigmaEj[j] = sigmaRj[j] = 0.0;
                Cj[j] = ECj[j] = RCj[j] = 0.0;
                continue;
            }

            double f_tcellsweightedaffinity_tmp = 0.0;
            double f_ecellsweightedaffinity_tmp = 0.0;
            double f_rcellsweightedaffinity_tmp = 0.0;

            for (int i = 0; i < NUM_CLONES; i++)
            {
                double e = useEuler ? TE_Eu[i] : TE[i];
                double r = useEuler ? TR_Eu[i] : TR[i];
                if (e + r <= CELL_LOWER_BOUND) continue;

                double th = theta[i, j];
                f_tcellsweightedaffinity_tmp += th * (e + r);
                f_ecellsweightedaffinity_tmp += th * e;
                f_rcellsweightedaffinity_tmp += th * r;
            }

            sigmaTj[j] = f_tcellsweightedaffinity_tmp;
            sigmaEj[j] = f_ecellsweightedaffinity_tmp;
            sigmaRj[j] = f_rcellsweightedaffinity_tmp;

            if (f_tcellsweightedaffinity_tmp == 0.0)
            {
                Cj[j] = ECj[j] = RCj[j] = 0.0;
                continue;
            }

            // C++ source: fTotalConjugates = (fTotalSites * f_tcellsweightedaffinity_tmp)
            //                                / (f_tcellsweightedaffinity_tmp + 1.0)
            // fTotalSites = APC[j] * SITES

            // Full formula: C_j = (γc·Aj·s·ΣTj) / (γd + γc·ΣTj)
            // Since KON == KOFF, γc cancels: C_j = (Aj·s·ΣTj) / (1 + ΣTj)
            double fTotalConjugates = (KON * APC[j] * SITES * f_tcellsweightedaffinity_tmp)
                                    / (KOFF + KON * f_tcellsweightedaffinity_tmp);

            Cj[j]  = fTotalConjugates;
            ECj[j] = fTotalConjugates * f_ecellsweightedaffinity_tmp / f_tcellsweightedaffinity_tmp;
            RCj[j] = fTotalConjugates * f_rcellsweightedaffinity_tmp / f_tcellsweightedaffinity_tmp;
        }
    }

    // =========================================================================
    // Step 5 — Compute T-cell derivatives
    // useEuler = false → use (TE, TR) and store into dE_k0, dR_k0
    // useEuler = true  → use (TE_Eu, TR_Eu) and store into dE_k1, dR_k1
    // Source: Derivative_ExcessTcells() + ComputeNewDerivative() in C++ source.
    //
    // For each live clone i, summing over all active APCs j:
    //   Pe  = (RCj - 3*Aj)^2 / (9*Aj^2)
    //   Pr  = (6*Aj - ECj) * ECj / (9*Aj^2)
    //   tmp_ratio = Cj / sigmaTj         (conjugate density per unit affinity-weighted T-cell)
    //   EC_ij = tmp_ratio * θ_ij * TE_i  (per-clone effector conjugates on APC j)
    //   RC_ij = tmp_ratio * θ_ij * TR_i  (per-clone regulatory conjugates on APC j)
    //   dTE_i/dt = Σ_j KPE * Pe * EC_ij - KDE * TE_i
    //   dTR_i/dt = Σ_j KPR * Pr * RC_ij - KDR * TR_i
    private void ComputeDerivatives(double[] dE, double[] dR, bool useEuler)
    {
        for (int i = 0; i < NUM_CLONES; i++)
        {
            double fE = useEuler ? TE_Eu[i] : TE[i];
            double fR = useEuler ? TR_Eu[i] : TR[i];

            if (fE + fR <= CELL_LOWER_BOUND)
            {
                dE[i] = 0.0;
                dR[i] = 0.0;
                continue;
            }

            double effector_incr  = 0.0;
            double regulator_incr = 0.0;

            for (int j = 0; j < NUM_CLONES; j++)
            {
                if (APC[j] == 0.0) continue;

                double tmp_apcconc = APC[j];
                double tmp_exp1    = 9.0 * tmp_apcconc * tmp_apcconc;
                if (tmp_exp1 == 0.0) continue;

                // Pe: probability effector conjugate has no regulatory neighbour on APC
                double Pe       = (RCj[j] - 3.0 * tmp_apcconc) * (RCj[j] - 3.0 * tmp_apcconc) / tmp_exp1;

                // Pr: probability regulatory conjugate has at least one effector neighbour on APC
                double Pr = (6.0 * tmp_apcconc - ECj[j]) * ECj[j] / tmp_exp1;

                // Per-clone conjugates on APC j
                // Source: tmp_ratio = fTotalConjugates / f_tcellsweightedaffinity_tmp
                double tmp_ratio = (sigmaTj[j] > 0.0) ? Cj[j] / sigmaTj[j] : 0.0;
                double th        = theta[i, j];
                double EC_ij     = tmp_ratio * th * fE;
                double RC_ij     = tmp_ratio * th * fR;

                effector_incr  += KPE * Pe * EC_ij;
                regulator_incr += KPR * Pr * RC_ij;
            }

            // se_rate = sr_rate = 0.0 in actual source — no continuous influx in ODE
            dE[i] = effector_incr + SE_RATE - KDE * fE;
            dR[i] = regulator_incr + SR_RATE - KDR * fR;
        }
    }

    // =========================================================================
    // Step 6 — Classify each active FV
    // For each active APC j, sum affinity-weighted TE and TR over all live clones.
    // Source: UpdateState() in crminrobotagent_optimised.cpp
    //
    // sumE_j = Σ_i θ_ij * TE_i
    // sumR_j = Σ_i θ_ij * TR_i
    // sumE > sumR → attack (1, abnormal)
    // sumR > sumE → tolerate (2, normal)
    // |sumE - sumR| <= CELL_LOWER_BOUND or sum too small → undecided (0)
    // =========================================================================
    private void UpdateClassification()
    {
        Array.Clear(FVClassification, 0, NUM_CLONES);

        for (int j = 0; j < NUM_CLONES; j++)
        {
            if (APC[j] == 0.0) continue;

            double sumE = 0.0, sumR = 0.0;
            for (int i = 0; i < NUM_CLONES; i++)
            {
                if (TE[i] + TR[i] <= CELL_LOWER_BOUND) continue;
                double th = theta[i, j];
                sumE += th * TE[i];
                sumR += th * TR[i];
            }

            if ((sumE + sumR) <= CELL_LOWER_BOUND || Math.Abs(sumE - sumR) <= CELL_LOWER_BOUND)
                FVClassification[j] = 0;   // undecided
            else if (sumE > sumR)
                FVClassification[j] = 1;   // attack — abnormal
            else
                FVClassification[j] = 2;   // tolerate — normal
        }
    }

    // =========================================================================
    // Affinity function — θ_ij = exp( -HammingDist(i,j) / (c * l) )
    // Normalized by l (NUM_FEATURES) as in the actual source code.
    // Source: NegExpDistAffinity() in crminrobotagent_optimised.cpp
    // =========================================================================
    private static double NegExpDistAffinity(int fv1, int fv2)
    {
        int hd = GetNumberOfSetBits(fv1 ^ fv2);
        return Math.Exp(-(double)hd / (CROSS_AFFINITY * NUM_FEATURES));
    }

    // =========================================================================
    // Hamming weight — count of set bits in x
    // Source: GetNumberOfSetBits() in crminrobotagent_optimised.cpp
    // =========================================================================
    private static int GetNumberOfSetBits(int x)
    {
        x = x - ((x >> 1) & 0x55555555);
        x = (x & 0x33333333) + ((x >> 2) & 0x33333333);
        x = (x + (x >> 4)) & 0x0f0f0f0f;
        x = x + (x >> 8);
        return (x + (x >> 16)) & 0x3f;
    }
}