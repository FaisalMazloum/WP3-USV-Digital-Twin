using System.Reflection;
using UnityEngine;
using UnitySensors.Sensor.TF;

public class TFLinkDynamicFrameId : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Original frame_id suffix (e.g., 'link', 'base_link'). Set this in the prefab!")]
    private string _originalFrameIdSuffix = "link";
    
    private void Start()
    {
        TFLink tfLink = GetComponent<TFLink>();
        if (tfLink == null)
        {
            Debug.LogError($"TFLinkDynamicFrameId on {gameObject.name} requires TFLink component", this);
            return;
        }
        
        SetDynamicFrameId(tfLink);
    }
    
    private void SetDynamicFrameId(TFLink tfLink)
    {
        FieldInfo fieldInfo = typeof(TFLink).GetField("_frame_id", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (fieldInfo == null)
        {
            Debug.LogError("Failed to find _frame_id field in TFLink. Package may have changed.", this);
            return;
        }
        
        string robotName = transform.root.name;
        string newFrameId = robotName + "/" + _originalFrameIdSuffix;
        
        fieldInfo.SetValue(tfLink, newFrameId);
        
        // Debug.Log($"Set TFLink frame_id to: {newFrameId}", this);
    }
}