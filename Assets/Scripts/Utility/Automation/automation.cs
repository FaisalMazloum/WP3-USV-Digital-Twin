using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class AutomatedDataCollection : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpawnerUI spawnerUI;
    [SerializeField] private FaultInjector faultInjector;
    [SerializeField] private GlobalCSVExporter globalCSVExporter;
    [SerializeField] private PatternSelector patternSelector;
    
    [Header("Collection Settings")]
    [SerializeField] private int numberOfRuns = 1;
    [SerializeField] private bool autoStart = false;
    
    [Header("Timing Parameters (seconds)")]
    private float initialWait;          // Wait after play starts (ORIGINALLY 5s, but .sh takes 2.5s to setup) for .SH and Unity to Sync
    private float postSpawnWait;          // Wait after spawning robots (must be > 5s due to SpawnerUI internal delays)
    private float ROS2Wait;              // Wait for ALL ROS2 to fully launch
    private float stabalizePeriod;       // After spawning, allow period for stabalizing
    private float postStartCollectionWait;         // Wait after start collection
    private float postFaultWait;         // Wait after fault injection
    private float sceneReloadWait;        // Wait for scene to reload
    
    private void Start()
    {
        numberOfRuns = 1;

// ================ VALUES FOR DEGRADATION ================
        // initialWait = 5.5f;
        // postSpawnWait = 5f;
        // ROS2Wait = 20f;
        // stabalizePeriod = 120f;
        // postStartCollectionWait = 100f;
        // postFaultWait = 105f;
        // sceneReloadWait = 10f;

// ================ VALUES FOR INTERMITTENT FAULT ================
        // initialWait = 5.5f;
        // postSpawnWait = 5f;
        // ROS2Wait = 20f;
        // stabalizePeriod = 0f;   // doesnt need stabalization period since it is random_walk
        // postStartCollectionWait = 0f;     //
        // postFaultWait = 200f;
        // sceneReloadWait = 10f;

// // ================ MISC TEST ================
//         initialWait = 10f;
//         postSpawnWait = 5f;
//         ROS2Wait = 5f;

        stabalizePeriod = -1f;
        postStartCollectionWait = -1f;
        postFaultWait = -1f;
        sceneReloadWait = -1f;

// ================ ICRA TEST ================
        initialWait = 15f;
        postSpawnWait = 5f;
        ROS2Wait = 20f;

        // stabalizePeriod = 30f;
        // postStartCollectionWait = 150f;     // to collect samples before fault injection
        // postFaultWait = 150f;               // to collect samples after fault injection
        // sceneReloadWait = 5f;              // period to allow the data to flush cleanly into a CSV

// ================ Quick Testing ================
        // initialWait = 1.5f;
        // postSpawnWait = 5f;
        // ROS2Wait = 1f;
        // stabalizePeriod = 1f;
        // postStartCollectionWait = 10f;
        // postFaultWait = 0f;
        // sceneReloadWait = 10f;


        if (autoStart)
        {
            StartCoroutine(AutomatedCollectionSequence());
        }
    }
    
    [ContextMenu("Start Automated Collection")]
    public void StartAutomatedCollection()
    {
        StartCoroutine(AutomatedCollectionSequence());
    }
    
    private IEnumerator AutomatedCollectionSequence()
    {

        Debug.Log($"[DataCollection] Starting run");
        
        // ===========================================
        // Step 1: Wait after play starts
        // ===========================================
        yield return new WaitForSeconds(initialWait);
        
        // ===========================================
        // Step 2: Trigger spawner by simulating button click
        // ===========================================
        if (spawnerUI != null)
        {
            Debug.Log("[DataCollection] Spawning robots...");
            TriggerSpawnerButton();
        }
        else
        {
            Debug.LogError("[DataCollection] spawnerUI is null!");
        }
        yield return new WaitForSeconds(postSpawnWait);

        // Time.timeScale = 0; // Pause game to allow all swarm agents to recieve START command.
        SetCmdVelSubscribersEnabled(false); // Hold every remora_bot still instead of/alongside pausing.
        yield return new WaitForSecondsRealtime(ROS2Wait); // yield return new WaitForSeconds(ROS2Wait);

        // Time.timeScale = 1; // UnPause game to begin mission.
        SetCmdVelSubscribersEnabled(true); // Release the whole swarm simultaneously.

        // ===========================================
        // Step 3: Trigger stabalization
        // ===========================================
        if(postStartCollectionWait != -1f)
        {
            yield return new WaitForSeconds(stabalizePeriod);
        }

        // ===========================================
        // Step 4: Trigger start collection
        // ===========================================
        if(postStartCollectionWait != -1f)
        {
            Debug.Log("[DataCollection] Finished stabalizing. Starting collection...");
            // faultInjector.SetFaultyRobotRandom(); // Randomly select faulty robot(s)
            faultInjector.SetFaultyRobot(0);
            TriggerBooleanField(globalCSVExporter, "startCollection");
            TriggerDangerCascadeStartLogging();
            yield return new WaitForSeconds(postStartCollectionWait); // this waiting period is to allow the CSV to log data for a fully healthy swarm for the initial period (before injection)
        }
        
        // ===========================================
        // Step 5: Inject fault
        // ===========================================
        if(postFaultWait != -1f)
        {
            // SetPatternAssignmentZero(PatternSelector.PatternType.aggregation_pattern);       // SWITCH TASK FOR ALL ROBOTS (except for the robot with software hang fault injected)
            SetPatternAssignmentZero(PatternSelector.PatternType.drive_pattern);             // SWITCH TASK FOR ALL ROBOTS (except for the robot with software hang fault injected)
            Debug.Log("[DataCollection] Injected fault...");
            TriggerBooleanField(faultInjector, "injectFault");
            yield return new WaitForSeconds(postFaultWait);
        }

        if(sceneReloadWait != -1f)
        {
        // ===========================================
        // Step 6: Export DangerCascadeController CSVs (danger log + vanilla/DT vote logs)
        // ===========================================
            Debug.Log("[DataCollection] Exporting DangerCascadeController CSVs...");
            TriggerDangerCascadeExport();
            Debug.Log($"[DataCollection] All {numberOfRuns} runs completed!");

            yield return new WaitForSeconds(sceneReloadWait);
            
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif   
        }
    }
    



    // ***************************************
    // *************** METHODS ***************
    // ***************************************
    private void TriggerSpawnerButton()
    {
        // Access the UIDocument to simulate button click
        var document = spawnerUI.GetComponent<UIDocument>();
        if (document != null)
        {
            var root = document.rootVisualElement;
            var button = root.Q<Button>("SpawnButton");
            
            if (button != null)
            {
                // Simulate click by using reflection to invoke the click event
                using (var clickEvent = ClickEvent.GetPooled())
                {
                    clickEvent.target = button;
                    button.SendEvent(clickEvent);
                }
            }
            else
            {
                Debug.LogError("[DataCollection] Could not find SpawnButton in UIDocument!");
            }
        }
        else
        {
            Debug.LogError("[DataCollection] Could not find UIDocument on SpawnerUI!");
        }
    }
    
    // Mirrors TriggerBooleanField(globalCSVExporter, "startCollection") above, but for every
    // DangerCascadeController in the scene (per-robot, no single field to hold a reference to —
    // see TriggerDangerCascadeExport below). Flips startLogging true on each so CSV row logging
    // begins at this exact offset instead of ~2s after spawn (when featureExtractor._csvExporter
    // ._start flips automatically) — same "healthy baseline before logging starts" intent as
    // globalCSVExporter's own startCollection trigger just above.
    private void TriggerDangerCascadeStartLogging()
    {
        DangerCascadeController[] controllers = FindObjectsOfType<DangerCascadeController>();
        foreach (var controller in controllers)
        {
            TriggerBooleanField(controller, "startLogging");
        }
        Debug.Log($"[DataCollection] Triggered startLogging on {controllers.Length} DangerCascadeController(s).");
    }

    // DangerCascadeController's CSV export is now swarm-consolidated (one danger/vanilla_votes/
    // dt_votes file covering every observer, not one per robot) — so exporting is a single static
    // call, no per-instance reflection loop needed (unlike startLogging above, which genuinely is
    // a per-robot flag each instance's own RunDangerCycle checks).
    private void TriggerDangerCascadeExport()
    {
        DangerCascadeController.FlushAllConsolidated();
        Debug.Log("[DataCollection] Triggered swarm-consolidated DangerCascadeController CSV export.");
    }

    // Disables/enables every remora_bot's CmdVel_Subscriber in one pass — all in the same frame,
    // so the whole swarm starts/stops simultaneously (CmdVel_Subscriber.OnDisable zeroes velocity,
    // so disabling actually holds the robot still, not just stops it responding to new commands).
    private void SetCmdVelSubscribersEnabled(bool enabled)
    {
        GameObject[] robots = GameObject.FindGameObjectsWithTag("remora_bot");
        foreach (var robot in robots)
        {
            var cmdVel = robot.GetComponentInChildren<CmdVel_Subscriber>();
            if (cmdVel != null) cmdVel.enabled = enabled;
        }
    }

    // Switches PatternSelector's first pattern group (patternAssignments[0]) to the given pattern.
    private void SetPatternAssignmentZero(PatternSelector.PatternType pattern)
    {
        if (patternSelector == null || patternSelector.patternAssignments.Count == 0)
        {
            Debug.LogError("[DataCollection] Could not set pattern — patternSelector is null or has no assignments!");
            return;
        }
        patternSelector.patternAssignments[0].pattern = pattern;
    }

    private void TriggerBooleanField(MonoBehaviour target, string fieldName)
    {
        if (target == null)
        {
            Debug.LogError($"[DataCollection] Target script is null when trying to trigger '{fieldName}'!");
            return;
        }
        
        var field = target.GetType().GetField(fieldName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Public | 
            System.Reflection.BindingFlags.Instance);
        
        if (field != null && field.FieldType == typeof(bool))
        {
            field.SetValue(target, true);
            // StartCoroutine(ResetFieldNextFrame(target, field));
        }
        else
        {
            Debug.LogError($"[DataCollection] Field '{fieldName}' not found or not boolean on {target.GetType().Name}");
        }
    }
    
    private IEnumerator ResetFieldNextFrame(MonoBehaviour target, System.Reflection.FieldInfo field)
    {
        yield return null;
        field.SetValue(target, false);
    }
}