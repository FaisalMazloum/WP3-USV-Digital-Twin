using UnityEngine;
using UnityEngine.UIElements;

public class UIVisibilityToggler : MonoBehaviour
{
    [SerializeField] private KeyCode _toggleKey = KeyCode.Tab;
    [SerializeField] private UIDocument[] _documents;

    private bool _isHidden = false;

    void Awake()
    {
        if (_documents == null || _documents.Length == 0)
        {
            _documents = FindObjectsOfType<UIDocument>();
            Debug.Log($"UIVisibilityToggler: auto-found {_documents.Length} UIDocument(s).");
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(_toggleKey))
        {
            _isHidden = !_isHidden;
            Debug.Log($"UIVisibilityToggler: toggling {_documents.Length} UIDocument(s) to {(_isHidden ? "hidden" : "visible")}.");
            SetVisible(!_isHidden);
        }
    }

    private void SetVisible(bool visible)
    {
        foreach (var document in _documents)
        {
            if (document == null) continue;
            document.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            document.enabled = visible;
            document.gameObject.SetActive(visible);
        }
    }
}
