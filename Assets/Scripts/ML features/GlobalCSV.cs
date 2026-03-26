using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using System;

public class GlobalCSVExporter : MonoBehaviour
{
    public FaultInjector _faultInjector;
    // Reference to all robots' FeatureExtractors
    private List<FeatureExtractor> allFeatureExtractors = new List<FeatureExtractor>();
    public RobotSpawner robotSpawner_;
    
    public bool _start = false;
    public bool startCollection = false;
    public bool exportCSV = false;
    
    private float collectionStartTime = -1f;
    // private float collectionDuration = 20f;
    // private float collectionDuration = 205f; // ORIGINAL WORKING FOR DEGRADATION
    private float collectionDuration = 200f; // FOR INTERMITTENT FAULT
    private bool isCollecting = false;
    private bool isWaitingForSync = false;

    public enum Movement_Pattern
    {
        aggregation,
        dispersion,
        flocking,
        attraction,
        adaptive_sampling
    }

    public Movement_Pattern selected_pattern;
    
    void Start()
    {
        // Find all robots with FeatureExtractor
        // FindRobots();
    }

    void Update()
    {
        if (startCollection && !isWaitingForSync && !isCollecting)
        {
            StartCoroutine(WaitForSyncAndStartCollection());
            startCollection = false;
        }

        if (isCollecting && (Time.time - collectionStartTime) >= collectionDuration)
        {
            StopCollection();
        }

        if (exportCSV)
        {
            ExportGlobalCSV();
            exportCSV = false;
        }

        if (!_faultInjector)
        {
            Debug.LogError("No Fault Injector script found!");
        }
    }

    IEnumerator WaitForSyncAndStartCollection()
    {
        isWaitingForSync = true;
        
        Debug.LogWarning("GlobalExporter: Waiting for synchronization...");
        
        // Use first robot as reference for sync
        if (allFeatureExtractors.Count == 0)
        {
            Debug.LogError("No FeatureExtractors found!");
            yield break;
        }
        
        FeatureExtractor referenceExtractor = allFeatureExtractors[0];
        
        // Wait for sample creation cycle
        while (referenceExtractor.creatingNewSample)
            yield return null;
        
        while (!referenceExtractor.creatingNewSample)
            yield return null;
        
        isWaitingForSync = false;
        StartCollection();
    }
    
    void StartCollection()
    {
        isCollecting = true;
        collectionStartTime = Time.time;
        
        // Clear all robots' histories
        foreach (var extractor in allFeatureExtractors)
        {
            extractor.feature_list_history.Clear();
        }
        
        Debug.LogError($" GlobalExporter: Started collection at t={Time.time:F1}s for {allFeatureExtractors.Count} robots");
    }
    
    void StopCollection()
    {
        isCollecting = false;
        
        int totalSamples = 0;
        foreach (var extractor in allFeatureExtractors)
        {
            foreach (var samples in extractor.feature_list_history.Values)
            {
                totalSamples += samples.Count;
            }
        }
        
        Debug.LogError($"GlobalExporter: Stopped collection at t={Time.time:F1}s. Total samples from all robots: {totalSamples}");

        ExportGlobalCSV();
    }

    void ExportGlobalCSV()
    {
        StringBuilder csv = new StringBuilder();

        // Header row
        csv.Append("seed,");
        csv.Append("timestamp,");
        csv.Append("observer_id,");
        csv.Append("observed_id,");
        for (int i = 0; i < 10; i++) csv.Append($"f1_{i},");
        for (int i = 0; i < 10; i++) csv.Append($"f2_{i},");
        for (int i = 0; i < 10; i++) csv.Append($"f3_{i},");
        for (int i = 0; i < 10; i++) csv.Append($"f4_{i},");
        csv.Append("f5,");
        csv.Append("F1,");
        csv.Append("F2,");
        csv.Append("F3,");
        csv.Append("F4,");
        csv.Append("F5,");
        csv.Append("F6,");
        csv.Append("RUL,");
        csv.Append("fault_status,");
        csv.Append("fault_type,");
        csv.Append("faulty_robot,");
        csv.Append("turn_history,");
        csv.AppendLine("movement_pattern");



        int totalRows = 0;

        // Collect data from ALL robots
        foreach (var extractor in allFeatureExtractors)
        {
            string observerName = extractor.transform.root.name;
            var featureHistory = extractor.feature_list_history;

            // if(observerName == _faultInjector.faulty_robot && _faultInjector.selectedFault != FaultInjector.FaultType.None)
            if(_faultInjector.faulty_robots.Contains(observerName))
                continue;   // SKIP if observer is faulty

            foreach (var kvp in featureHistory)
            {
                GameObject observedRobot = kvp.Key;
                List<FeatureExtractor.allFeatures> samplesList = kvp.Value;

                foreach (var sample in samplesList)
                {
                    csv.Append($"{robotSpawner_.randomSeed},");
                    csv.Append($"{sample.timestamp},");
                    csv.Append($"{observerName},");
                    csv.Append($"{observedRobot.name},");

                    for (int i = 0; i < 10; i++)
                        csv.Append($"{sample.f1[i]:F2},");
                    for (int i = 0; i < 10; i++)
                        csv.Append($"{sample.f2[i]:F2},");
                    for (int i = 0; i < 10; i++)
                        csv.Append($"{sample.f3[i]:F2},");
                    for (int i = 0; i < 10; i++)
                        csv.Append($"{sample.f4[i]:F2},");

                    csv.Append($"{sample.f5:F2},");
                    csv.Append($"{sample.F1:F2},");
                    csv.Append($"{sample.F2:F2},");
                    csv.Append($"{sample.F3:F2},");
                    csv.Append($"{sample.F4:F2},");
                    csv.Append($"{sample.F5:F2},");
                    csv.Append($"{sample.F6:F2},");
                    
                    csv.Append($"{sample.RUL:F2},");

                    // ======================================================================
                    //                          FAULT LABELS
                    // ======================================================================
                    if(_faultInjector.selectedFault != FaultInjector.FaultType.None 
                        // && observedRobot.name == _faultInjector.faulty_robot 
                        && _faultInjector.faulty_robots.Contains(observedRobot.name)
                        && _faultInjector.injectFault
                        && _faultInjector.faultInjectionTime >= 0f
                        && sample.timestamp >= _faultInjector.faultInjectionTime
                        )
                    {
                        // For persistent faults: label all samples after injection
                        if (_faultInjector.selectedFault != FaultInjector.FaultType.IntermittentMotor)
                        {
                            csv.Append("1,");
                            csv.Append($"{_faultInjector.selectedFault},");
                        }
                        // For intermittent fault: label only during dropout
                        else
                        {
                            if (sample.in_dropout)
                            {                                
                                csv.Append("1,");
                                csv.Append($"{_faultInjector.selectedFault},");
                            }
                            else
                            {
                                csv.Append("0,");
                                csv.Append($"{FaultInjector.FaultType.None},");
                            }
                        }
                    }
// =================================== IF NO FAILURE ===================================
                    else
                    {
                        csv.Append("0,");
                        // csv.Append($"{_faultInjector.injectFault},");
                        // csv.Append($"{_faultInjector.faulty_robot},");
                        csv.Append($"{FaultInjector.FaultType.None},");
                    }

// ========================= FAULTY ROBOT TARGET (for easy dataset filtering) =========================
                    if(_faultInjector.selectedFault != FaultInjector.FaultType.None 
                        // && observedRobot.name == _faultInjector.faulty_robot
                        && _faultInjector.faulty_robots.Contains(observedRobot.name)
                        && _faultInjector.injectFault
                        && _faultInjector.faultInjectionTime >= 0f
                        // No sample_timestamp check since we want all samples from faulty robot (even -1 RUL) to have 1
                        )
                    {
                        csv.Append("1,");
                    }
                    else
                    {
                        csv.Append("0,");
                    }

                    // ======================================================================
                    //                          TURN HISTORY LABEL
                    // ======================================================================
                    // var asp = observedRobot.GetComponentInChildren<AdaptiveSamplingPattern>();
                    string historyString = string.Join(" | ", sample.turnHistory);
                    csv.Append($"\"{historyString}\","); 

                    // ======================================================================
                    //                          PATTERN LABEL
                    // ======================================================================
                    csv.AppendLine($"{selected_pattern}");

                    totalRows++;
                }
            }
        }

        if (totalRows == 0)
        {
            Debug.LogWarning("GlobalExporter: No samples to export!");
            return;
        }

        // string faulty_robot_number = _faultInjector.faulty_robot .Split('a')[1];
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string filename = $"seed {robotSpawner_.randomSeed} - {timestamp}.csv";
        // string filepath = Path.Combine(Application.dataPath, "Scripts", "data", "latest_data", "AdaptiveSampling", filename);
        string filepath = Path.Combine(Application.dataPath, "Scripts", "data", "latest_data", "AdaptiveSampling_high_dropout_rate", filename);

        File.WriteAllText(filepath, csv.ToString());

        Debug.LogError($" GlobalExporter: Exported {totalRows} samples from {allFeatureExtractors.Count} robots to:\n{filepath}");
    }


    public void FindRobots()
    {
        FeatureExtractor[] extractors = FindObjectsOfType<FeatureExtractor>();
        allFeatureExtractors.AddRange(extractors);
        Debug.Log($"GlobalCSVExporter: Found {allFeatureExtractors.Count} robots with FeatureExtractor");
    }
}