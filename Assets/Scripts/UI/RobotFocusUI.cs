using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class RobotFocusUI : MonoBehaviour
{
    private UIDocument _document;
    private DropdownField _listField;
    public ManualController _controller;

    void Start()
    {
        _document = GetComponent<UIDocument>();
        var root = _document.rootVisualElement;
        _listField = root.Q<DropdownField>("RobotFocusField");
        initializeList();
        _listField.RegisterCallback<ChangeEvent<string>>(listCallback);
    }

    public void initializeList()
    {
        int index = 0;
        var choices = new List<string>();
        foreach (var item in GameObject.FindGameObjectsWithTag("remora_bot"))
        {
            choices.Insert(index, item.name);
            index++;
        }
        _listField.choices = choices;
        _listField.value = choices[0];
    }

    void listCallback(ChangeEvent<string> evt)
    {
        // Turn OFF all cameras first
        foreach (var item in GameObject.FindGameObjectsWithTag("remora_bot"))
        {
            Camera camera_component = item.transform.Find("Remora_Camera").GetComponent<Camera>();
            camera_component.enabled = false;
        }

        // Turn ON selected camera
        GameObject remora_object = GameObject.Find(evt.newValue);
        Camera _camera_component = remora_object.transform.Find("Remora_Camera").GetComponent<Camera>();
        _camera_component.enabled = true;

        // Also change controller focus to the newly selected remora_bot
        _controller.robot_name = evt.newValue;
        _controller.InitializeRigidBody();
        Debug.Log($"New value: {evt.newValue}");
    }


    void OnDestroy()
    {
        if (_listField != null)
        {
            _listField.UnregisterCallback<ChangeEvent<string>>(listCallback);
        }
    }
}