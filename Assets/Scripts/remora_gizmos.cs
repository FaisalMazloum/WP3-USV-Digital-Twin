using System.Collections;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;

public class remora_gizmos : MonoBehaviour
{

    private Rigidbody rb;
    private AdaptiveSamplingPattern adaptive_sampling_;
    private NeighborStateManager neighbor_state_manager_;
    private RABSensor rab_;
    private CmdVel_Subscriber thrusters_;
    private BlinkEmitter blink_emitter_;
    private ActuatorEmitter actuator_emitter_;

    private ROSConnection ros_;
    private string currentPatternTopic_;
    private string currentPattern_ = "";

    // ---- Blink-probe visualization state (danger_crm_implementation_brief.md, Section 6) ----
    private bool wasProbingActive_ = false;   // IsProbing && !WindowComplete, previous frame
    private int lastSeenBlinkCount_ = -1;
    private float lastBlinkFlashTime_ = -10f;
    private bool resultCaptured_ = false;     // pass/fail already latched for this probe episode
    private bool lastProbePassed_ = false;
    private float resultFlashStartTime_ = -10f;
    private const float BLINK_RING_DURATION = 0.5f;    // one-shot expanding ring per blink pulse
    private const float BLINK_RING_MAX_RADIUS = 1.0f;
    private const float RESULT_FLASH_DURATION = 1.0f;


    void Start()
    {
        rb = transform.root.GetComponent<Rigidbody>();
        if (rb == null)
            Debug.LogError("Rigidbody component missing on " + gameObject.name);

        adaptive_sampling_ = transform.root.GetComponentInChildren<AdaptiveSamplingPattern>();
        if (adaptive_sampling_ == null)
            Debug.LogError("Adaptive Sampling Script missing on " + gameObject.name);

        neighbor_state_manager_ = transform.root.GetComponentInChildren<NeighborStateManager>();
        if (neighbor_state_manager_ == null)
            Debug.LogError("Neighbor State Manager Script missing on " + gameObject.name);

        rab_ = transform.root.GetComponentInChildren<RABSensor>();
        if (rab_)
            Debug.LogError("RAB Script missing on " + gameObject.name);

        thrusters_ = transform.root.GetComponentInChildren<CmdVel_Subscriber>();
        if (thrusters_)
            Debug.LogError("CmdVel Script missing on " + gameObject.name);

        blink_emitter_ = transform.root.GetComponentInChildren<BlinkEmitter>();
        if (blink_emitter_ == null)
            Debug.LogError("BlinkEmitter missing on " + gameObject.name);

        actuator_emitter_ = transform.root.GetComponentInChildren<ActuatorEmitter>();
        if (actuator_emitter_ == null)
            Debug.LogError("ActuatorEmitter missing on " + gameObject.name);

        ros_ = ROSConnection.GetOrCreateInstance();
        currentPatternTopic_ = $"/{transform.root.name}/current_pattern";
        ros_.Subscribe<StringMsg>(currentPatternTopic_, OnCurrentPatternReceived);
    }

    private void OnCurrentPatternReceived(StringMsg msg)
    {
        currentPattern_ = msg.data;
    }

    void Update()
    {
        
    }


    void OnDrawGizmos()
    {
        // =======================================================================
        //                              VELOCITY
        // =======================================================================
        if (Application.isPlaying)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 8;
            style.alignment = TextAnchor.LowerLeft;
            style.normal.textColor = Color.white;
            style.fontStyle = FontStyle.Bold;

            Vector3 localVelocity = transform.InverseTransformDirection(rb.velocity);
            Vector3 localAngularVel = transform.InverseTransformDirection(rb.angularVelocity);

            UnityEditor.Handles.Label(
                transform.position + Vector3.forward * 1.2f + Vector3.right * 0f,
                $"v={localVelocity.z:F2}m/s | w={localAngularVel.y*Mathf.Rad2Deg:F1}deg/s",
                style
            );

            if (thrusters_ != null)
            {
                Dictionary<string, float> intent = thrusters_.GetIntentVelocities();
                Dictionary<string, float> actual = thrusters_.GetActualThrusterSpeeds();

                UnityEditor.Handles.Label(
                    transform.position + Vector3.forward * 1.1f + Vector3.right * 0f,
                    $"L: intent={intent["left_thruster"]:F1} true={actual["left_thruster"]:F1} | " +
                    $"R: intent={intent["right_thruster"]:F1} true={actual["right_thruster"]:F1} cm/s",
                    style
                );
            }
        }



        // =======================================================================
        //                        CURRENT TASK/PATTERN
        // =======================================================================
        if (Application.isPlaying)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 8;
            style.alignment = TextAnchor.LowerLeft;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.cyan;

            UnityEditor.Handles.Label(
                transform.position + Vector3.forward * 0.5f,
                $"({currentPattern_})",
                style
            );
        }

        // =======================================================================
        //                        ADAPATIVE SAMPLING
        // =======================================================================
        // if (Application.isPlaying)
        // {
        //     // 1. Create a custom style
        //     GUIStyle style = new GUIStyle();
        //     style.fontSize = 8;
        //     style.alignment = TextAnchor.LowerLeft;
        //     style.fontStyle = FontStyle.Bold;

        //     string t = "";

        //     if (adaptive_sampling_.currentMode == AdaptiveSamplingPattern.SamplingMode.HIGH_RES)
        //     {
        //         t = "HIGH_RES mode";
        //         style.normal.textColor = Color.cyan;
        //     }
        //     else if (adaptive_sampling_.currentMode == AdaptiveSamplingPattern.SamplingMode.INVESTIGATION)
        //     {
        //         t = "INVESTIGATION mode";
        //         style.normal.textColor = Color.cyan;
        //     }
        //     else
        //     {
        //         t = "SURVEY mode";
        //         style.normal.textColor = Color.cyan;
        //     }

        //     string historyText = string.Join(" -> ", adaptive_sampling_.GetTurnHistory());
        //     t = "(" + t + ")" + "\nTurns: [" + historyText + "]";

        //     // style.fontStyle = FontStyle.Bold;

        //     // 3. Apply the style to the label
        //     UnityEditor.Handles.Label(
        //         transform.position + Vector3.forward * 0.5f,
        //         $"{t}", 
        //         style
        //     );
        // }

        // ---------------------------------------------------- //
        // Draw a grid showing what mode each position would be
        // ---------------------------------------------------- //
        // for (float x = 90; x < 111; x += 0.5f)
        // {
        //     for (float z = 90; z < 111; z += 0.5f)
        //     {
        //         float perlin = Mathf.PerlinNoise(x * perlinScale, z * perlinScale);
        //         perlin += prelin_offset;
                
        //         if (perlin < 0.4f)
        //             Gizmos.color = new Color(0, 0, 1, 0.3f); // Blue transparent
        //         else if (perlin < 0.6f)
        //             Gizmos.color = new Color(1, 1, 0, 0.3f); // Yellow transparent
        //         else
        //             Gizmos.color = new Color(1, 0, 0, 0.3f); // Red transparent
                
        //         Gizmos.DrawCube(new Vector3(x, 0, z), Vector3.one * 1.5f);
        //     }
        // }



        // =======================================================================
        //                        FAULT LABELS & SUBSCRIBTIONS
        // =======================================================================
        if (Application.isPlaying)
        {
            float RUL_value = 100f;
            // 1. Create a custom style
            GUIStyle style = new GUIStyle();
            style.fontSize = 8;
            style.alignment = TextAnchor.LowerLeft;
            style.fontStyle = FontStyle.Bold;
            
            if (!neighbor_state_manager_._faultInjector.faulty_robots.Contains(neighbor_state_manager_.robotID))
            {
                style.normal.textColor = Color.green;
                UnityEditor.Handles.Label(transform.position + Vector3.forward * 0.25f, $"{neighbor_state_manager_.robotID.ToUpper()}", style);
            }
            else
            {
                if (neighbor_state_manager_._faultInjector.selectedFault == FaultInjector.FaultType.IntermittentMotor && neighbor_state_manager_._faultInjector.injectFault)
                {
                    style.normal.textColor = Color.red;
                    UnityEditor.Handles.Label(
                        transform.position + Vector3.forward * 0.25f, 
                        // $"r{neighbor_state_manager_.robotID.Substring(6)} ___ RUL = {RUL_value}: Subscribed ({neighbor_state_manager_.GetSubscribers().Count})", 
                        // $"r{neighbor_state_manager_.robotID.Substring(6)}", 
                        $"{neighbor_state_manager_.robotID.ToUpper()}",
                        style
                        );
                }
                else if (neighbor_state_manager_._faultInjector.selectedFault == FaultInjector.FaultType.GradualMotor && neighbor_state_manager_._faultInjector.injectFault)
                {
                    float max_RUL = 100f;
                    RUL_value = neighbor_state_manager_._cmdSubscriber.GetRULData()["RUL"];
                    if (!neighbor_state_manager_._faultInjector.injectFault)
                        RUL_value = 100;
                    float t = Mathf.Clamp01(RUL_value / max_RUL);

                    style.normal.textColor = Color.Lerp(Color.red, Color.green, t);
                    UnityEditor.Handles.Label(
                        transform.position + Vector3.forward * 0.25f, 
                        // $"r{neighbor_state_manager_.robotID.Substring(6)} ___ RUL = {RUL_value}: Subscribed ({neighbor_state_manager_.GetSubscribers().Count})", 
                        $"{neighbor_state_manager_.robotID.ToUpper()} ___ RUL = {RUL_value}", 
                        style
                        );

                    // DrawSonarRings(transform.root.position, Color.red, speed: 0.7f, numRings: 2);
                    // DrawSonarRings(transform.root.position, new Color(1.0f, 0.0f, 0.0f, 1.0f), speed: 0.7f, numRings: 2);
                }
                else if (neighbor_state_manager_._faultInjector.injectFault)
                {
                    style.normal.textColor = Color.red;
                    UnityEditor.Handles.Label(transform.position + Vector3.forward * 0.25f, $"{neighbor_state_manager_.robotID.ToUpper()}", style);
                    DrawSonarRings(transform.root.position, Color.red, speed: 0.7f, numRings: 2);
                    DrawSonarRings(transform.root.position, new Color(0.7f, 0.1f, 0.1f, 0.8f), speed: 0.7f, numRings: 2);
                }
                else
                {
                    style.normal.textColor = Color.green;
                    UnityEditor.Handles.Label(transform.position + Vector3.forward * 0.25f, $"{neighbor_state_manager_.robotID.ToUpper()}", style);
                }
            }


            if (neighbor_state_manager_.showRays)
            {
                Gizmos.color = new Color(0, 1, 0, 0.5f); 
                var detectedRobots = rab_.GetDetections();
                foreach (var robot in detectedRobots)
                    Gizmos.DrawLine(transform.root.position, robot.Key.transform.root.position);
            }
        }

        // =======================================================================
        //                        DROPOUT vs Focused Sampling
        // =======================================================================
        if (Application.isPlaying)
        {
            bool isFaultyDropout   = thrusters_.in_dropout 
                                && neighbor_state_manager_._faultInjector.faulty_robots.Contains(neighbor_state_manager_.robotID) 
                                && neighbor_state_manager_._faultInjector.injectFault;

            bool isFocusedSampling = thrusters_.in_dropout 
                                && !neighbor_state_manager_._faultInjector.faulty_robots.Contains(neighbor_state_manager_.robotID);

            if (isFaultyDropout)
            {
                DrawSonarRings(transform.root.position, new Color(1.0f, 0.0f, 0.1f, 1.0f), speed: 0.7f, numRings: 2);
                // Debug.Log(neighbor_state_manager_.robotID + " in dropout");
            }
            else if (isFocusedSampling)
            {
                DrawSonarRings(transform.root.position, Color.yellow, speed: 0.7f, numRings: 2);
                // Debug.Log(neighbor_state_manager_.robotID + " in focused sampling");
            }
        }


    // =======================================================================
    //                        CRM FAULT DETECTION
    // =======================================================================
    if (Application.isPlaying)
    {
        FeatureExtractor fe = transform.root.GetComponentInChildren<FeatureExtractor>();
        if (fe == null || fe.isFaulty == null || fe.crmInstance == null) return;

        GUIStyle style = new GUIStyle();
        style.fontSize  = 8;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.UpperLeft;

        // offset label position upward so it does not overlap existing labels
        Vector3 basePos = transform.position + Vector3.forward * -0.5f;

        // ----------- per observed robot
        int lineOffset = 0;
        foreach (var kvp in fe.isFaulty)
        {
            GameObject observedRobot = kvp.Key;
            bool       faulty        = kvp.Value;
            if (observedRobot == null) continue;

            // get FV index for this observed robot
            var history = fe.feature_list_history;
            if (!history.ContainsKey(observedRobot) || history[observedRobot].Count == 0) continue;

            FeatureExtractor.allFeatures latest = history[observedRobot][history[observedRobot].Count - 1];
            int fvIndex = (latest.F1 << 0) | (latest.F2 << 1) | (latest.F3 << 2) |
                        (latest.F4 << 3) | (latest.F5 << 4) | (latest.F6 << 5);

            // CRM classification for this FV
            int crm = fe.crmInstance.FVClassification[fvIndex];
            string crmLabel = crm == 1 ? "ATK" : crm == 2 ? "TOL" : "UND";

            // attack count in rolling window
            int attackCount = 0;
            if (fe.crmHistory.ContainsKey(observedRobot))
                foreach (int c in fe.crmHistory[observedRobot])
                    if (c == 1) attackCount++;

            // affinity-weighted TE and TR for this FV's clone
            double sumE = 0.0, sumR = 0.0;
            for (int i = 0; i < 64; i++)
            {
                if (fe.crmInstance.TE[i] + fe.crmInstance.TR[i] <= 1e-3) continue;
                double th = fe.crmInstance.theta[i, fvIndex];
                sumE += th * fe.crmInstance.TE[i];
                sumR += th * fe.crmInstance.TR[i];
            }

            // label color
            style.normal.textColor = faulty ? new Color(1.0f, 0.1f, 0.1f, 1.0f) : Color.green;
            if (lineOffset != 0)
            {
                    
            }
            string label =
                $"r{observedRobot.name.Substring(6)} | FV={fvIndex:D2}({System.Convert.ToString(fvIndex, 2).PadLeft(6, '0')}) | " +
                $"CRM={crmLabel} | TE={sumE:F1} TR={sumR:F1} | " +
                $"ATK={attackCount}/{FeatureExtractor.CRM_HISTORY_LENGTH}";

            UnityEditor.Handles.Label(
                basePos + Vector3.forward * (-0.3f * lineOffset),
                label, style);

            // string label =
            //     // $"r{observedRobot.name.Substring(6)} \n FV={fvIndex:D2}({System.Convert.ToString(fvIndex, 2).PadLeft(6, '0')}) \n " +
            //     // $"CRM={crmLabel} \n TE={sumE:F1} TR={sumR:F1} \n " +
            //     // $"ATK={attackCount}/{FeatureExtractor.CRM_HISTORY_LENGTH}";
            // UnityEditor.Handles.Label(
            //     basePos + Vector3.forward * (-1f * lineOffset),
            //     label, style);

            lineOffset++;
        }
    }


    // =======================================================================
    //                        CRM FAULT DETECTION (DANGER)
    // =======================================================================
    // Same exact output as the vanilla CRM FAULT DETECTION block above, sourced from
    // DangerCascadeController.crmInstanceDanger instead of FeatureExtractor.crmInstance, drawn
    // in orange in its own column so the two can be visually diffed against each other without
    // overlapping. Reads DangerCascadeController.crmHistory, which mirrors
    // FeatureExtractor.crmHistory's own majority-vote window exactly.
    // if (Application.isPlaying)
    // {
    //     DangerCascadeController dc = transform.root.GetComponentInChildren<DangerCascadeController>();
    //     if (dc == null || dc.featureExtractor == null || dc.finalVerdict == null || dc.crmInstanceDanger == null) return;

    //     GUIStyle style = new GUIStyle();
    //     style.fontSize  = 8;
    //     style.fontStyle = FontStyle.Bold;
    //     style.alignment = TextAnchor.UpperLeft;
    //     style.normal.textColor = new Color(1.0f, 0.55f, 0.0f, 1.0f); // orange

    //     // separate column, offset sideways so it never overlaps the vanilla block above
    //     Vector3 basePos = transform.position + Vector3.forward * -0.5f + Vector3.right * 2.5f;

    //     int lineOffset = 0;
    //     foreach (var kvp in dc.finalVerdict)
    //     {
    //         GameObject observedRobot = kvp.Key;
    //         if (observedRobot == null) continue;

    //         // get FV index for this observed robot
    //         var history = dc.featureExtractor.feature_list_history;
    //         if (!history.ContainsKey(observedRobot) || history[observedRobot].Count == 0) continue;

    //         FeatureExtractor.allFeatures latest = history[observedRobot][history[observedRobot].Count - 1];
    //         int fvIndex = (latest.F1 << 0) | (latest.F2 << 1) | (latest.F3 << 2) |
    //                     (latest.F4 << 3) | (latest.F5 << 4) | (latest.F6 << 5);

    //         // CRM classification for this FV
    //         int crm = dc.crmInstanceDanger.FVClassification[fvIndex];
    //         string crmLabel = crm == 1 ? "ATK" : crm == 2 ? "TOL" : "UND";

    //         // attack count in rolling window
    //         int attackCount = 0;
    //         if (dc.crmHistory.ContainsKey(observedRobot))
    //             foreach (int c in dc.crmHistory[observedRobot])
    //                 if (c == 1) attackCount++;

    //         // affinity-weighted TE and TR for this FV's clone — already retained by
    //         // CRMInstanceDanger.UpdateClassification, no need to recompute the loop.
    //         double sumE = dc.crmInstanceDanger.sumE[fvIndex];
    //         double sumR = dc.crmInstanceDanger.sumR[fvIndex];

    //         string label =
    //             $"r{observedRobot.name.Substring(6)} | FV={fvIndex:D2}({System.Convert.ToString(fvIndex, 2).PadLeft(6, '0')}) | " +
    //             $"CRM={crmLabel} | TE={sumE:F1} TR={sumR:F1} | " +
    //             $"ATK={attackCount}/{FeatureExtractor.CRM_HISTORY_LENGTH}";

    //         UnityEditor.Handles.Label(
    //             basePos + Vector3.forward * (-0.3f * lineOffset),
    //             label, style);

    //         lineOffset++;
    //     }
    // }


    // =======================================================================
    //                        BLINK PROBE VISUALIZATION
    // =======================================================================
    // danger_crm_implementation_brief.md, Section 6. Reads this robot's OWN BlinkEmitter
    // directly — the blink itself is ground-truth/target-intrinsic (one emitter per robot,
    // not per-observer), so no per-observer loop is needed here, unlike the CRM blocks above.
    //
    // While actively probing: a small icon flashes briefly on each blink pulse (yellow), plus a
    // live "BLINK x/N" counter. A hung robot (BlinkEmitter.simulateHang) never increments
    // BlinksCounted, so its icon simply never lights and its counter stays frozen at 0 — the
    // contrast with a blinking neighbor is the tell, no separate "not responding" marker.
    //
    // Once the probe window completes, the pass/fail result is latched once (resultCaptured_)
    // and flashed (green/red) for RESULT_FLASH_DURATION, independent of DangerCascadeController's
    // own read-and-reset timing on the emitter (which may zero AgreedN/BlinksCounted within about
    // a second of WindowComplete becoming true, once some observer's ladder consumes it).
    if (Application.isPlaying && blink_emitter_ != null)
    {
        bool activelyProbing = blink_emitter_.IsProbing && !blink_emitter_.WindowComplete;

        // new probe episode just started -> reset per-episode tracking
        if (activelyProbing && !wasProbingActive_)
        {
            lastSeenBlinkCount_ = 0;
            resultCaptured_ = false;
        }

        Vector3 iconPos = transform.position + Vector3.up * 1.5f;
        // shifted off to the side and down from the old right-on-top-of-the-robot spot
        Vector3 textPos = transform.position + Vector3.right * 1.3f + Vector3.up * 0.4f;

        if (activelyProbing)
        {
            // detect a new blink pulse since last draw
            if (blink_emitter_.BlinksCounted != lastSeenBlinkCount_)
            {
                lastBlinkFlashTime_ = Time.time;
                lastSeenBlinkCount_ = blink_emitter_.BlinksCounted;
            }

            // one-shot expanding cyan ring per blink pulse — much more noticeable than a static
            // flashing dot, reuses the same ring-drawing technique as DrawSonarRings but as a
            // single expand-and-fade triggered by the blink instead of a continuous loop.
            float blinkRingElapsed = Time.time - lastBlinkFlashTime_;
            DrawOneShotRing(transform.root.position + Vector3.up * 1.5f, new Color(1f,0.2f,0.2f,1f), blinkRingElapsed, BLINK_RING_DURATION, BLINK_RING_MAX_RADIUS);

            GUIStyle counterStyle = new GUIStyle();
            counterStyle.fontSize = 8;
            counterStyle.fontStyle = FontStyle.Bold;
            counterStyle.alignment = TextAnchor.MiddleCenter;
            counterStyle.normal.textColor = Color.white;
            UnityEditor.Handles.Label(textPos, $"BLINK {blink_emitter_.BlinksCounted}/{blink_emitter_.AgreedN}", counterStyle);
        }
        else if (blink_emitter_.IsProbing && blink_emitter_.WindowComplete && !resultCaptured_)
        {
            // probe just completed this episode -> latch pass/fail once
            lastProbePassed_ = blink_emitter_.BlinksCounted == blink_emitter_.AgreedN;
            resultFlashStartTime_ = Time.time;
            resultCaptured_ = true;
        }

        if (resultCaptured_ && (Time.time - resultFlashStartTime_) < RESULT_FLASH_DURATION)
        {
            Gizmos.color = lastProbePassed_ ? new Color(0.2f, 1.0f, 0.2f, 0.9f) : new Color(1.0f, 0.15f, 0.15f, 0.9f);
            Gizmos.DrawSphere(iconPos, 0.15f);

            GUIStyle resultStyle = new GUIStyle();
            resultStyle.fontSize = 9;
            resultStyle.fontStyle = FontStyle.Bold;
            resultStyle.alignment = TextAnchor.MiddleCenter;
            resultStyle.normal.textColor = lastProbePassed_ ? Color.green : Color.red;
            UnityEditor.Handles.Label(textPos, lastProbePassed_ ? "PROBE PASS" : "PROBE FAIL", resultStyle);
        }

        wasProbingActive_ = activelyProbing;
    }


    // =======================================================================
    //                        ACTUATOR PROBE VISUALIZATION
    // =======================================================================
    // Reads this robot's own ActuatorEmitter directly, same reasoning as the blink block above
    // (the probe is target-intrinsic, one emitter per robot, no per-observer loop needed).
    //
    // Continuous pulsing cyan rings (reusing DrawSonarRings' existing looping animation, not the
    // one-shot DrawOneShotRing the blink block uses) for as long as a probe is actively running,
    // so it's visible at a glance which robot is currently mid-actuator-test. A live text readout
    // shows which of the 3 rotations is active plus the commanded and currently-measured rate, so
    // you can watch tracking accuracy in real time instead of only finding out pass/fail
    // afterward.
    //
    // IsProbing alone isn't enough — same as the blink block above, it deliberately stays true
    // after WindowComplete (multi-observer-safe design: it only flips on the NEXT RequestProbe()),
    // so gating on IsProbing alone left the rings pulsing forever once a robot had been probed
    // even once, long after it went CLEARED/NORMAL.
    bool actuatorActivelyProbing = actuator_emitter_ != null && actuator_emitter_.IsProbing && !actuator_emitter_.WindowComplete;
    if (Application.isPlaying && actuatorActivelyProbing)
    {
        DrawSonarRings(transform.root.position + Vector3.up * 1.5f, new Color(1f,0.2f,0.2f,1f), speed: 1.2f, numRings: 3, rad: 1.2f);

        int rotationNumber = actuator_emitter_.CurrentRotation >= 0 ? actuator_emitter_.CurrentRotation + 1 : 3;

        GUIStyle actuatorStyle = new GUIStyle();
        actuatorStyle.fontSize = 8;
        actuatorStyle.fontStyle = FontStyle.Bold;
        actuatorStyle.alignment = TextAnchor.MiddleCenter;
        actuatorStyle.normal.textColor = Color.cyan;

        // opposite side from the blink counter (which sits at Vector3.right) so the two never
        // overlap if both happen to be visible at once
        Vector3 actuatorTextPos = transform.position + Vector3.left * 1.3f + Vector3.up * 0.4f;
        UnityEditor.Handles.Label(actuatorTextPos,
            $"ROT {rotationNumber}/3\ncmd: {actuator_emitter_.CommandedOmegaDeg:F1}°/s\nmeas: {actuator_emitter_.LastMeasuredOmegaDeg:F1}°/s",
            actuatorStyle);
    }

    }




    // One-shot expanding+fading ring, triggered once per discrete event (e.g. one ring per blink
    // pulse) — unlike DrawSonarRings below, which loops continuously on Time.time. elapsed/duration
    // control how far through its single expand-and-fade cycle it is; draws nothing once elapsed
    // has passed duration, so the caller just needs to track "time since the triggering event".
    private void DrawOneShotRing(Vector3 center, Color baseColor, float elapsed, float duration, float maxRadius = 1.0f, float thickness = 0.03f, int thicknessPasses = 3)
    {
        if (elapsed >= duration) return;

        float phase  = Mathf.Clamp01(elapsed / duration);
        float radius = Mathf.Lerp(0.1f, maxRadius, phase);
        float alpha  = Mathf.Lerp(1f, 0f, phase);

        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);

        int   segments  = 32;
        float angleStep = 360f / segments;
        for (int t = 0; t < thicknessPasses; t++)
        {
            float currentRad = radius + (t * thickness);

            for (int s = 0; s < segments; s++)
            {
                float a1 = s * angleStep * Mathf.Deg2Rad;
                float a2 = (s + 1) * angleStep * Mathf.Deg2Rad;

                Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * currentRad;
                Vector3 p2 = center + new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2)) * currentRad;

                Gizmos.DrawLine(p1, p2);
            }
        }
    }

    private void DrawSonarRings(Vector3 center, Color baseColor, float speed, int numRings, float rad=2.5f, float thickness = 0.02f, int thicknessPasses = 3)
    {
        float interval = 1f / numRings;

        for (int k = 0; k < numRings; k++)
        {
            // each ring is offset in phase so they expand sequentially
            float phase        = (Time.time * speed + k * interval) % 1f;
            float radius       = Mathf.Lerp(0.1f, rad, phase);
            float alpha        = Mathf.Lerp(1f, 0f, phase);  // fade as it expands

            Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);

            // draw circle on XZ plane (top view) using line segments
            int   segments = 32;
            float angleStep = 360f / segments;
            // for (int s = 0; s < segments; s++)
            // {
            //     float a1 = s       * angleStep * Mathf.Deg2Rad;
            //     float a2 = (s + 1) * angleStep * Mathf.Deg2Rad;

            //     Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            //     Vector3 p2 = center + new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2)) * radius;

            //     Gizmos.DrawLine(p1, p2);
            // }
            for (int t = 0; t < thicknessPasses; t++)
            {
                float currentRad = radius + (t * thickness);

                for (int s = 0; s < segments; s++)
                {
                    float a1 = s * angleStep * Mathf.Deg2Rad;
                    float a2 = (s + 1) * angleStep * Mathf.Deg2Rad;

                    Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * currentRad;
                    Vector3 p2 = center + new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2)) * currentRad;

                    Gizmos.DrawLine(p1, p2);
                }
            }
        }
    }
}
