using UnityEngine;
using UnityEngine.UIElements;

public class WindUI : MonoBehaviour
{
    [SerializeField] private WindController _windDriftScript;
    
    private UIDocument _document;
    private SliderInt _windSpeedSlider;
    private SliderInt _windAngleSlider;

    void Start()
    {
        _document = GetComponent<UIDocument>();
        var root = _document.rootVisualElement;
        
        // Get SliderInt instead of Slider
        _windSpeedSlider = root.Q<SliderInt>("WindSpeed");
        _windAngleSlider = root.Q<SliderInt>("WindAngle");
        
        // Verify sliders exist
        if (_windSpeedSlider == null)
        {
            Debug.LogError("WindSpeed SliderInt not found!");
            return;
        }
        if (_windAngleSlider == null)
        {
            Debug.LogError("WindAngle SliderInt not found!");
            return;
        }
        
        // Debug.Log("SliderInts found successfully!");
        
        // Set slider ranges (int values)
        _windSpeedSlider.lowValue = 0;
        _windSpeedSlider.highValue = 100;
        
        _windAngleSlider.lowValue = -180;
        _windAngleSlider.highValue = 180;

 
        // Set initial values (convert float to int)
        _windSpeedSlider.value = Mathf.RoundToInt(_windDriftScript.windSpeed);
        _windAngleSlider.value = Mathf.RoundToInt(_windDriftScript.windAngle);
        
        // Register callbacks for int sliders
        _windSpeedSlider.RegisterValueChangedCallback(OnWindSpeedChanged);
        _windAngleSlider.RegisterValueChangedCallback(OnWindAngleChanged);        
    }

    private void OnWindSpeedChanged(ChangeEvent<int> evt)
    {
        Debug.Log($"WindSpeed slider changed to: {evt.newValue}");
        if (_windDriftScript != null)
        {
            _windDriftScript.windSpeed = evt.newValue; // int auto-converts to float
            Debug.Log($"WindDrift.WindSpeed set to: {_windDriftScript.windSpeed}");
        }
    }

    private void OnWindAngleChanged(ChangeEvent<int> evt)
    {
        Debug.Log($"WindAngle slider changed to: {evt.newValue}");
        if (_windDriftScript != null)
        {
            _windDriftScript.windAngle = evt.newValue; // int auto-converts to float
            Debug.Log($"WindDrift.WindAngle set to: {_windDriftScript.windAngle}");
        }
    }

    void OnDestroy()
    {
        // Cleanup callbacks
        if (_windSpeedSlider != null)
        {
            _windSpeedSlider.UnregisterValueChangedCallback(OnWindSpeedChanged);
        }
        if (_windAngleSlider != null)
        {
            _windAngleSlider.UnregisterValueChangedCallback(OnWindAngleChanged);
        }
    }
}