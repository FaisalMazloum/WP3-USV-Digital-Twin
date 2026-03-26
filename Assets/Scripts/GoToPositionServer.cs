using System.Collections;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.RemoraInterfaces;

public class GoToPositionServer : MonoBehaviour
{
    private ROSConnection ros;
    
    // Topic names
    private const string SEND_GOAL_SERVICE = "/go_to_position/send_goal";
    private const string FEEDBACK_TOPIC = "/go_to_position/feedback";
    private const string RESULT_TOPIC = "/go_to_position/result";
    
    // Execution state
    private bool isExecuting = false;
    private Vector2 targetPosition;
    private Vector2 currentPosition = Vector2.zero;
    
    [SerializeField] private float executionDuration = 5f;
    
    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        
        // Register service server
        ros.ImplementService<SendGoalRequest, SendGoalResponse>(
            SEND_GOAL_SERVICE, HandleSendGoal);
        
        // Register topic publishers
        ros.RegisterPublisher<GoalFeedbackMsg>(FEEDBACK_TOPIC);
        ros.RegisterPublisher<GoalResultMsg>(RESULT_TOPIC);
        
        Debug.Log("[GoToServer] Server initialized");
        Debug.Log($"[GoToServer] Service: {SEND_GOAL_SERVICE}");
        Debug.Log($"[GoToServer] Feedback topic: {FEEDBACK_TOPIC}");
        Debug.Log($"[GoToServer] Result topic: {RESULT_TOPIC}");
    }
    
    private SendGoalResponse HandleSendGoal(SendGoalRequest request)
    {
        Debug.Log($"[GoToServer] Received goal: ({request.target_x}, {request.target_y})");
        
        var response = new SendGoalResponse();
        
        if (isExecuting)
        {
            Debug.LogWarning("[GoToServer] Already executing, rejecting goal");
            response.accepted = false;
            response.message = "Server is busy";
            return response;
        }
        
        // Accept goal
        response.accepted = true;
        response.message = "Goal accepted";
        
        targetPosition = new Vector2(request.target_x, request.target_y);
        isExecuting = true;
        
        StartCoroutine(ExecuteGoal());
        
        return response;
    }
    
    private IEnumerator ExecuteGoal()
    {
        Debug.Log($"[GoToServer] Executing goal to ({targetPosition.x}, {targetPosition.y})");
        
        float elapsedTime = 0f;
        Vector2 startPosition = currentPosition;
        
        while (elapsedTime < executionDuration)
        {
            float progress = elapsedTime / executionDuration;
            currentPosition = Vector2.Lerp(startPosition, targetPosition, progress);
            float distanceRemaining = Vector2.Distance(currentPosition, targetPosition);
            
            // Publish feedback
            var feedback = new GoalFeedbackMsg
            {
                current_x = currentPosition.x,
                current_y = currentPosition.y,
                distance_remaining = distanceRemaining,
                progress_percent = progress * 100f
            };
            
            ros.Publish(FEEDBACK_TOPIC, feedback);
            
            Debug.Log($"[GoToServer] Progress: {progress * 100:F1}% | Pos: ({currentPosition.x:F2}, {currentPosition.y:F2})");
            
            elapsedTime += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        
        // Goal completed
        currentPosition = targetPosition;
        
        var result = new GoalResultMsg
        {
            success = true,
            message = "Successfully reached target",
            final_x = currentPosition.x,
            final_y = currentPosition.y
        };
        
        ros.Publish(RESULT_TOPIC, result);
        
        isExecuting = false;
        
        Debug.Log($"[GoToServer] Goal completed! Final: ({currentPosition.x}, {currentPosition.y})");
    }
}