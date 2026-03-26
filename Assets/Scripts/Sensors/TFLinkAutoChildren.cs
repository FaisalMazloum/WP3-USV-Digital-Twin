using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnitySensors.Sensor.TF;

public class TFLinkAutoChildren : MonoBehaviour
{
    public TFLink TFLink_script;
    // public bool button = false;
    
    void Update()
    {
        // if (button)
        // {
        //     UpdateChildren();
        //     button = false;
        // }
    }
    
    public void UpdateChildren()
    {
        // Get the field reference
        System.Type type = TFLink_script.GetType();
        FieldInfo _childrenField = type.GetField("_children", BindingFlags.NonPublic | BindingFlags.Instance);
        
        // Get existing children
        TFLink[] existingChildren = (TFLink[])_childrenField.GetValue(TFLink_script);
        
        // Count non-base_link children
        int manualChildrenCount = 0;
        if (existingChildren != null)
        {
            foreach (var child in existingChildren)
            {
                if (child != null && !child.CompareTag("base_link"))
                {
                    manualChildrenCount++;
                }
            }
        }
        
        // Get all base_link objects
        GameObject[] base_link_objects = GameObject.FindGameObjectsWithTag("base_link");
        
        TFLink[] newChildrenArray = new TFLink[manualChildrenCount + base_link_objects.Length];
        
        // Add non-base_link tagged objects first
        int index_offset = 0;
        if (existingChildren != null)
        {
            foreach (var child in existingChildren)
            {
                if (child != null && !child.CompareTag("base_link"))
                {
                    newChildrenArray[index_offset] = child;
                    index_offset++;
                }
            }
        }
        
        // Add base_link tagged objects
        for (int i = 0; i < base_link_objects.Length; i++)
        {
            newChildrenArray[index_offset + i] = base_link_objects[i].GetComponent<TFLink>();
        }
        
        _childrenField.SetValue(TFLink_script, newChildrenArray);
        
        // Debug.Log("Updated _children array - Manual: " + manualChildrenCount + ", Base_link: " + base_link_objects.Length);
    }
}