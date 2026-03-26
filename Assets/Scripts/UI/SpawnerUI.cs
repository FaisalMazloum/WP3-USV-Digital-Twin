using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class SpawnerUI : MonoBehaviour
{
    private UIDocument _document;
    private IntegerField _integerField;
    private EnumField _enumField;
    private Button _buttonField;

    public RobotSpawner _robotSpawner;
    public RobotFocusUI _robotFocus;
    public TFLinkAutoChildren _TFLinkAutoChildren;
    public GlobalCSVExporter _globalCSVExporter;

    // public bool button = false;


    void Start()
    {
        _document = GetComponent<UIDocument>();
        var root = _document.rootVisualElement;

        _integerField = root.Q<IntegerField>("SpawnCountField");
        _enumField = root.Q<EnumField>("SpawnLayoutField");
        _buttonField = root.Q<Button>("SpawnButton");

        _enumField.Init(RobotSpawner.LayoutType.Line);
        
        // Register callback for when value changes
        _integerField.RegisterValueChangedCallback(OnValueChanged);
        _enumField.RegisterValueChangedCallback(OnLayoutChanged);
        _buttonField.RegisterCallback<ClickEvent>(OnClick);
    }



    private void OnValueChanged(ChangeEvent<int> evt)
    {
        // Debug.Log($"Integer value changed to: {evt.newValue}");
        _robotSpawner.numberOfRobots = evt.newValue;
    }

    private void OnLayoutChanged(ChangeEvent<System.Enum> evt)
    {
        // Debug.Log($"Layout changed to: {selected}");
        _robotSpawner.selected_layout = (RobotSpawner.LayoutType)evt.newValue;
    }

    private void OnClick(ClickEvent evt)
    {
        StartCoroutine(ExecuteMethodsWithDelay());
        _robotFocus.initializeList();
        _TFLinkAutoChildren.UpdateChildren();
        Debug.Log("Spawned Robots!");
    }



    void OnDestroy()
    {
        if (_integerField != null)
        {
            _integerField.UnregisterValueChangedCallback(OnValueChanged);
        }
        if (_enumField != null)
        {
            _enumField.UnregisterValueChangedCallback(OnLayoutChanged);
        }
    }

    private IEnumerator ExecuteMethodsWithDelay()
    {
        _robotSpawner.SpawnRobots();
        yield return new WaitForSeconds(1.0f); // Wait for 1 second
        RefreshAllSubscriptions();
        _globalCSVExporter.FindRobots();

        StartCoroutine(StartObservingWithDelay()); // Call another Coroutine
    }

    private IEnumerator StartObservingWithDelay()
    {
        yield return new WaitForSeconds(2.0f); // Wait for 1 second
        _globalCSVExporter._start = true;
        Debug.LogError("STARTED OBSERVING");
    }

    void RefreshAllSubscriptions()
    {
        // Find all robots in scene
        GameObject[] allRobots = GameObject.FindGameObjectsWithTag("remora_bot");
        Debug.Log("Found " + allRobots.Count() + " robots");

        // Tell each robot to re-subscribe
        foreach (var robot in allRobots)
        {
            NeighborStateManager manager = robot.GetComponentInChildren<NeighborStateManager>();
            if (manager != null)
            {
                manager.RefreshSubscriptions();
                // manager.RefreshSubscriptions();
            }
            else
            {
                Debug.Log("Cant find Manager component");
            }
        }
    }
}