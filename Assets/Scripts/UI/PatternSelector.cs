using System.Collections;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

public class PatternSelector : MonoBehaviour
{
    public enum PatternType
    {
        aggregation_pattern,
        dispersion_pattern,
        minimalist_flocking_pattern,
        random_walk_pattern,
        drive_pattern
    }

    // One group = one pattern applied to one set of robots. patternAssignments is a list of these,
    // so the inspector's default +/- list controls let you add/remove groups (and each group's own
    // robots list) freely — composite behaviours fall out of having more than one group active at
    // once, no separate coordination logic needed.
    [System.Serializable]
    public class PatternAssignment
    {
        public PatternType pattern;
        public List<string> robots = new List<string> { "0" };
    }

    public List<PatternAssignment> patternAssignments = new List<PatternAssignment> { new PatternAssignment() };

    // Fault-driven overrides (e.g. FaultInjector.FaultType.SoftwareHang forcing one robot into a
    // specific pattern) — keyed by bare robot id, same convention as PatternAssignment.robots.
    // Takes priority over whatever patternAssignments would otherwise send that robot, without
    // needing to find/remove it from whichever assignment group it's normally listed under.
    private Dictionary<string, PatternType> robotOverrides = new Dictionary<string, PatternType>();

    public void SetPatternOverride(string robotId, PatternType pattern)
    {
        robotOverrides[robotId] = pattern;
    }

    public void ClearPatternOverride(string robotId)
    {
        robotOverrides.Remove(robotId);
    }



    // ----------------------- //
    //          ROS2           //
    // ----------------------- //
    ROSConnection ros;

    string rosTopic;
    private float timeElapsed;

    void Start()
    {
        rosTopic = "/pattern_command";
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(rosTopic);
    }

    // Update is called once per frame
    void Update()
    {
        timeElapsed += Time.deltaTime;

        if (timeElapsed > 1f)
        {
            foreach (PatternAssignment assignment in patternAssignments)
            {
                foreach (string bot in assignment.robots)
                {
                    if (robotOverrides.ContainsKey(bot)) continue; // published from the override loop below instead
                    PublishPattern(bot, assignment.pattern);
                }
            }

            foreach (var overridePair in robotOverrides)
                PublishPattern(overridePair.Key, overridePair.Value);

            timeElapsed = 0;
        }
    }

    private void PublishPattern(string bot, PatternType pattern)
    {
        StringMsg pattern_msg = new StringMsg();
        pattern_msg.data = "remora" + bot + ":" + pattern.ToString();
        ros.Publish(rosTopic, pattern_msg);
    }
}
