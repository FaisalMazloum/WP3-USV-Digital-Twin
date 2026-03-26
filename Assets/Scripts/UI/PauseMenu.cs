using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class PauseMenu : MonoBehaviour
{
    private UIDocument _document;
    private VisualElement _rootElement;
    private Button _continueButton;
    private bool _isPaused = false;

    void Start()
    {
        _document = GetComponent<UIDocument>();
        _rootElement = _document.rootVisualElement;
        _continueButton = _rootElement.Q<Button>("Continue");
        
        // Register callback ONCE in Start, not in Update
        _continueButton.RegisterCallback<ClickEvent>(OnContinueClicked);
        
        // Hide menu initially
        HideMenu();
    }

    void Update()
    {
        // Check for Enter key press
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!_isPaused)
            {
                ShowMenu();
                PauseGame();
            }
        }
    }

    private void OnContinueClicked(ClickEvent evt)
    {
        HideMenu();
        ResumeGame();
    }

    private void ShowMenu()
    {
        _rootElement.style.display = DisplayStyle.Flex;
    }

    private void HideMenu()
    {
        _rootElement.style.display = DisplayStyle.None;
    }

    private void PauseGame()
    {
        Time.timeScale = 0f;
        _isPaused = true;
    }

    private void ResumeGame()
    {
        Time.timeScale = 1f;
        _isPaused = false;
    }

    void OnDestroy()
    {
        // Unregister callback to prevent memory leaks
        if (_continueButton != null)
        {
            _continueButton.UnregisterCallback<ClickEvent>(OnContinueClicked);
        }
    }
}