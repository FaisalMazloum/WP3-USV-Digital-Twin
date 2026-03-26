using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public class CSVExporter : MonoBehaviour
{
    private FeatureExtractor featureExtractor;
    
    public bool startCollection = false;
    public bool exportCSV = false;
    
    private float collectionStartTime = -1f;
    private float collectionDuration = 20f;
    private bool isCollecting = false;
    private bool isWaitingForSync = false;
    
    void Start()
    {
        featureExtractor = GetComponent<FeatureExtractor>();
        
        if (featureExtractor == null)
        {
            Debug.LogError($"{transform.root.name}: FeatureExtractor not found!");
        }
    }

    void Update()
    {
        // Start collection when button pressed
        if (startCollection && !isWaitingForSync && !isCollecting)
        {
            StartCoroutine(WaitForSyncAndStartCollection());
            startCollection = false;
        }

        // Check if collection period ended
        if (isCollecting && (Time.time - collectionStartTime) >= collectionDuration)
        {
            StopCollection();
        }

        // Export when button pressed
        if (exportCSV)
        {
            ExportToCSV();
            exportCSV = false;
        }
    }

    IEnumerator WaitForSyncAndStartCollection()
    {
        isWaitingForSync = true;
        
        Debug.Log($"{transform.root.name}: Waiting for sample creation cycle to synchronize...");
        
        // Wait until creatingNewSample becomes false (sample just finished creating)
        while (featureExtractor.creatingNewSample)
        {
            yield return null; // Wait one frame
        }
        
        // Now wait for it to become true again (new sample cycle starting)
        while (!featureExtractor.creatingNewSample)
        {
            yield return null; // Wait one frame
        }
        
        // Perfect! A new sample cycle just started
        isWaitingForSync = false;
        StartCollection();
    }
    
    void StartCollection()
    {
        isCollecting = true;
        collectionStartTime = Time.time;
        
        // Clear any existing history
        featureExtractor.feature_list_history.Clear();
        
        Debug.Log($"{transform.root.name}: ✓ Started synchronized 20s collection at t={Time.time:F1}s");
    }
    
    void StopCollection()
    {
        isCollecting = false;
        
        int totalSamples = 0;
        foreach (var samples in featureExtractor.feature_list_history.Values)
        {
            totalSamples += samples.Count;
        }
        
        Debug.Log($"{transform.root.name}: ✓ Stopped collection. Total samples: {totalSamples}");
    }
    
    void ExportToCSV()
    {
        var featureHistory = featureExtractor.feature_list_history;
        
        if (featureHistory.Count == 0)
        {
            Debug.LogWarning($"{transform.root.name}: No samples to export!");
            return;
        }
        
        StringBuilder csv = new StringBuilder();
        
        // Header row
        csv.Append("observer_id,observed_id,");
        for (int i = 0; i < 10; i++) csv.Append($"f1_{i},");
        for (int i = 0; i < 10; i++) csv.Append($"f2_{i},");
        for (int i = 0; i < 10; i++) csv.Append($"f3_{i},");
        for (int i = 0; i < 10; i++) csv.Append($"f4_{i},");
        csv.AppendLine("f5");
        
        // Data rows
        int totalRows = 0;
        foreach (var kvp in featureHistory)
        {
            GameObject observedRobot = kvp.Key;
            List<FeatureExtractor.allFeatures> samplesList = kvp.Value;
            
            foreach (var sample in samplesList)
            {
                csv.Append($"{transform.root.name},");
                csv.Append($"{observedRobot.name},");
                
                for (int i = 0; i < 10; i++)
                    csv.Append($"{sample.f1[i]:F2},");
                for (int i = 0; i < 10; i++)
                    csv.Append($"{sample.f2[i]:F2},");
                for (int i = 0; i < 10; i++)
                    csv.Append($"{sample.f3[i]:F2},");
                for (int i = 0; i < 10; i++)
                    csv.Append($"{sample.f4[i]:F2},");
                
                csv.AppendLine($"{sample.f5:F2}");
                totalRows++;
            }
        }
        
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"training_data_{transform.root.name}_{timestamp}.csv";
        string filepath = Path.Combine(Application.dataPath, filename);
        
        File.WriteAllText(filepath, csv.ToString());
        
        Debug.Log($"✓ {transform.root.name}: Exported {totalRows} samples to:\n{filepath}");
    }
}