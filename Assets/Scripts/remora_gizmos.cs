using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class remora_gizmos : MonoBehaviour
{

    private Rigidbody rb;
    private AdaptiveSamplingPattern adaptive_sampling_;
    private NeighborStateManager neighbor_state_manager_;
    private RABSensor rab_;
    private CmdVel_Subscriber thrusters_;


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
        }



        // =======================================================================
        //                        ADAPATIVE SAMPLING
        // =======================================================================
        if (Application.isPlaying)
        {
            // 1. Create a custom style
            GUIStyle style = new GUIStyle();
            style.fontSize = 8;
            style.alignment = TextAnchor.LowerLeft;
            style.fontStyle = FontStyle.Bold;

            string t = "";

            if (adaptive_sampling_.currentMode == AdaptiveSamplingPattern.SamplingMode.HIGH_RES)
            {
                t = "HIGH_RES mode";
                style.normal.textColor = Color.cyan;
            }
            else if (adaptive_sampling_.currentMode == AdaptiveSamplingPattern.SamplingMode.INVESTIGATION)
            {
                t = "INVESTIGATION mode";
                style.normal.textColor = Color.cyan;
            }
            else
            {
                t = "SURVEY mode";
                style.normal.textColor = Color.cyan;
            }

            string historyText = string.Join(" -> ", adaptive_sampling_.GetTurnHistory());
            t = "(" + t + ")" + "\nTurns: [" + historyText + "]";

            // style.fontStyle = FontStyle.Bold;

            // 3. Apply the style to the label
            UnityEditor.Handles.Label(
                transform.position + Vector3.forward * 0.5f,
                $"{t}", 
                style
            );
        }
        // Draw a grid showing what mode each position would be
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
