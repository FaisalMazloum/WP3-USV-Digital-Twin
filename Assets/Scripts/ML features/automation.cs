using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class AutomatedDataCollection : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpawnerUI spawnerUI;
    [SerializeField] private FaultInjector faultInjector;
    [SerializeField] private GlobalCSVExporter globalCSVExporter;
    
    [Header("Collection Settings")]
    [SerializeField] private int numberOfRuns = 1;
    [SerializeField] private bool autoStart = false;
    
    [Header("Timing Parameters (seconds)")]
    [SerializeField] private float initialWait;          // Wait after play starts (ORIGINALLY 5s, but .sh takes 2.5s to setup) for .SH and Unity to Sync
    [SerializeField] private float postSpawnWait;          // Wait after spawning robots (must be > 5s due to SpawnerUI internal delays)
    [SerializeField] private float ROS2Wait;              // Wait for ALL ROS2 to fully launch
    [SerializeField] private float stabalizePeriod;       // After spawning, allow period for stabalizing
    [SerializeField] private float postStartWait;         // Wait after start collection
    [SerializeField] private float postFaultWait;         // Wait after fault injection
    [SerializeField] private float sceneReloadWait;        // Wait for scene to reload
    
    private void Start()
    {
        numberOfRuns = 1;

// ================ VALUES FOR DEGRADATION ================
        // initialWait = 5.5f;
        // postSpawnWait = 5f;
        // ROS2Wait = 20f;
        // stabalizePeriod = 120f;
        // postStartWait = 100f;
        // postFaultWait = 105f;
        // sceneReloadWait = 10f;

// ================ VALUES FOR INTERMITTENT FAULT ================
        initialWait = 5.5f;
        postSpawnWait = 5f;
        ROS2Wait = 20f;
        stabalizePeriod = 0f;   // doesnt need stabalization period since it is random_walk
        postStartWait = 0f;     //
        postFaultWait = 200f;
        sceneReloadWait = 10f;

// ================ Quick Testing ================
        // initialWait = 1.5f;
        // postSpawnWait = 5f;
        // ROS2Wait = 1f;
        // stabalizePeriod = 1f;
        // postStartWait = 1f;
        // postFaultWait = 20f;
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

        Time.timeScale = 0; // Pause game to allow all swarm agents to recieve START command.

        // yield return new WaitForSeconds(ROS2Wait);
        yield return new WaitForSecondsRealtime(ROS2Wait);

        Time.timeScale = 1; // UnPause game to begin mission.

        yield return new WaitForSeconds(stabalizePeriod);

        // ===========================================
        // Step 3: Trigger start collection
        // ===========================================
        Debug.Log("[DataCollection] Starting collection...");
        faultInjector.SetFaultyRobot(); // Randomly select faulty robot
        TriggerBooleanField(globalCSVExporter, "startCollection");
        yield return new WaitForSeconds(postStartWait);
        
        // ===========================================
        // Step 4: Inject fault
        // ===========================================
        Debug.Log("[DataCollection] Injecting fault...");
        TriggerBooleanField(faultInjector, "injectFault");
        yield return new WaitForSeconds(postFaultWait);
        
        yield return new WaitForSeconds(sceneReloadWait);
        
        Debug.Log($"[DataCollection] All {numberOfRuns} runs completed!");
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
    
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