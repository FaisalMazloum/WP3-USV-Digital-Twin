using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering.PostProcessing;

public class RobotSpawner : MonoBehaviour
{

    // public Vector3 envrionment_origin = new Vector3(100f, 4f, 100f);
    public Vector3 envrionment_origin = new Vector3(100f, 4f, 100f);

    [Header("Spawn Configuration")]
    [Tooltip("The robot prefab to spawn")]
    public GameObject robotPrefab;
    
    [Tooltip("Number of robots to spawn")]
    public int numberOfRobots = 5;
    
    [Header("Spawn Layout")]
    [Tooltip("Spacing between robots")]
    public float spacing = 1f;
    
    [Tooltip("Layout pattern")]
    public LayoutType selected_layout = LayoutType.Line;
    
    public enum LayoutType
    {
        Line,
        Circle,
        Random
    }
    
    [Header("Random Spawn Settings")]
    [Tooltip("Arena size (square arena centered on robotPrefab)")]
    public float arenaSize;

    [Tooltip("Robot diameter for collision checking")]
    public float robotDiameter = 0.45f;
    
    [Tooltip("Safety margin added to robot diameter")]
    public float safetyMargin = 0.1f;
    
    [Tooltip("Maximum attempts to find valid position")]
    public int maxRetries = 100;
    
    [Tooltip("Random seed for reproducibility (0 = random seed)")]
    public int randomSeed = 0;
    
    private List<Vector3> spawnedPositions = new List<Vector3>();
    
    void Start()
    {
        // ====================================================================
        // Read seed from file written by bash script
        // ====================================================================
        string seedFilePath = "/tmp/unity_seed.txt";
        if (System.IO.File.Exists(seedFilePath))
        {
            string seedText = System.IO.File.ReadAllText(seedFilePath).Trim();
            if (int.TryParse(seedText, out int fileSeed))
            {
                randomSeed = fileSeed;
                Debug.Log($"[RobotSpawner] Seed set from file: {randomSeed}");
            }
        }
        else if (randomSeed == 0)
        {
            randomSeed = Random.Range(40, 500);
            Debug.Log($"[RobotSpawner] Using random seed: {randomSeed}");
        }


        // spacing = 1f;
        
        if (robotPrefab == null)
        {
            Debug.LogError("Robot prefab not assigned in RobotSpawner!");
            return;
        }

        arenaSize = 10f;
    }
    
    public void SpawnRobots()
    {
        // Initialize random seed
        if (randomSeed != 0)
        {
            Random.InitState(randomSeed);
            Debug.Log($"Using random seed: {randomSeed}");
        }
        
        spawnedPositions.Clear();
        
        for (int i = 1; i <= numberOfRobots; i++)
        {
            Vector3 spawnPosition = CalculateSpawnPosition(i, out Quaternion spawnRotation);
            
            // Check if spawn failed (only relevant for Random layout)
            if (spawnPosition == Vector3.negativeInfinity)
            {
                Debug.LogError($"Failed to spawn robot {i}. Aborting spawn process.");
                return;
            }
            
            GameObject robot = Instantiate(robotPrefab, spawnPosition, spawnRotation);
            robot.name = $"remora{i}";
            
            // Turn off all new cameras initially
            Camera robot_camera = robot.transform.Find("Remora_Camera").GetComponent<Camera>();
            robot_camera.enabled = false;
            
            // Configure unique robot ID
            CmdVel_Subscriber controller = robot.GetComponent<CmdVel_Subscriber>();
            controller.robotID = $"remora{i}";
            
            // Debug.Log($"Spawned {robot.name} with ID: {controller.robotID} at position {spawnPosition} with rotation {spawnRotation.eulerAngles}");
        }
        
        Debug.Log($"Successfully spawned {numberOfRobots} robots");
    }
    
    Vector3 CalculateSpawnPosition(int index, out Quaternion rotation)
    {
        var offset = new Vector3(0f, 1.5f, 0f);
        Vector3 basePosition = robotPrefab.transform.position + offset;
        rotation = Quaternion.identity;
        
        switch (selected_layout)
        {
            case LayoutType.Line:
                return basePosition + new Vector3(index * spacing, 0, 0);
                
            case LayoutType.Circle:
                float angle = (360f / numberOfRobots) * (index - 1);
                float radius = spacing * numberOfRobots / (2 * Mathf.PI);
                float x = radius * Mathf.Cos(angle * Mathf.Deg2Rad);
                float z = radius * Mathf.Sin(angle * Mathf.Deg2Rad);
                return basePosition + new Vector3(x, 0f, z);
                
            case LayoutType.Random:
                return GetRandomPositionWithCollisionAvoidance(out rotation);
                
            default:
                return basePosition;
        }
    }
    
    Vector3 GetRandomPositionWithCollisionAvoidance(out Quaternion rotation)
    {
        Vector3 centerPosition = envrionment_origin; 
        
        // Calculate spawn bounds centered on robotPrefab position
        float wallBuffer = robotDiameter / 2f;
        float halfArena = arenaSize / 2f;
        
        // Absolute spawn bounds
        float spawnMinX = centerPosition.x - halfArena + wallBuffer;
        float spawnMaxX = centerPosition.x + halfArena - wallBuffer;
        float spawnMinZ = centerPosition.z - halfArena + wallBuffer;
        float spawnMaxZ = centerPosition.z + halfArena - wallBuffer;

        // Randomize the position of Remora0 (default robot)
        Vector3 prefabPos = robotPrefab.transform.position;
        Quaternion prefabOrientation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        prefabPos.x = Random.Range(spawnMinX, spawnMaxX);
        prefabPos.z = Random.Range(spawnMinZ, spawnMaxZ);
        robotPrefab.transform.position = prefabPos;
        robotPrefab.transform.rotation = prefabOrientation;

        spawnedPositions.Add(robotPrefab.transform.position); // Add remora0 (default robot prefab) to list of occupied positions.
        
        // Minimum separation distance
        float minSeparation = robotDiameter + safetyMargin;

        // Debug.Log($"Arena Size: {arenaSize}, minX: {spawnMinX}, maxX: {spawnMaxX}, minZ: {spawnMinZ}, maxZ: {spawnMaxZ}");
        
        // Try to find a valid position
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // Generate random candidate position in absolute coordinates
            float candidateX = Random.Range(spawnMinX, spawnMaxX);
            float candidateZ = Random.Range(spawnMinZ, spawnMaxZ);
            Vector3 candidatePosition = new Vector3(candidateX, centerPosition.y, candidateZ);
            
            // Check collision against all previously spawned robots
            bool positionValid = true;
            
            foreach (Vector3 existingPosition in spawnedPositions)
            {
                // Calculate distance (ignoring Y-axis)
                float distance = Vector2.Distance(
                    new Vector2(candidatePosition.x, candidatePosition.z),
                    new Vector2(existingPosition.x, existingPosition.z)
                );
                
                if (distance < minSeparation)
                {
                    positionValid = false;
                    break;
                }
            }
            
            // If valid position found, store and return it
            if (positionValid)
            {
                spawnedPositions.Add(candidatePosition);
                
                // Generate random Y-axis rotation (0-360 degrees)
                float randomYRotation = Random.Range(0f, 360f);
                rotation = Quaternion.Euler(0f, randomYRotation, 0f);
                
                if (attempt > 0)
                {
                    Debug.Log($"Found valid position after {attempt + 1} attempts");
                }
                
                return candidatePosition;
            }
        }
        
        // Failed to find valid position after maxRetries
        Debug.LogError($"Failed to find valid spawn position after {maxRetries} attempts. " +
                    $"Arena may be too crowded for {numberOfRobots} robots with diameter {robotDiameter}m " +
                    $"in {arenaSize}m × {arenaSize}m arena centered at {centerPosition}.");
        
        rotation = Quaternion.identity;
        return Vector3.negativeInfinity; // Signal failure
    }
}