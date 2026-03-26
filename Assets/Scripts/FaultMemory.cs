using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;

public class FaultMemory : MonoBehaviour
{
    [System.Serializable]
    public struct FaultRecord
    {
        public float timestamp;
        public float probability;
        public int prediction;
    }

    public Dictionary<int, List<FaultRecord>> history     = new Dictionary<int, List<FaultRecord>>();
    public Dictionary<int, List<FaultRecord>> historyLSTM = new Dictionary<int, List<FaultRecord>>();

    private ROSConnection ros;
    private int robotId;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        robotId = int.Parse(new string(transform.root.name.Where(char.IsDigit).ToArray()));

        ros.Subscribe<Float32MultiArrayMsg>($"/remora{robotId}/fault_status",      OnFaultStatus);
        ros.Subscribe<Float32MultiArrayMsg>($"/remora{robotId}/fault_status_lstm", OnFaultStatusLSTM);
    }

    void OnFaultStatus(Float32MultiArrayMsg msg)     => StoreRecord(msg, history);
    void OnFaultStatusLSTM(Float32MultiArrayMsg msg) => StoreRecord(msg, historyLSTM);

    void StoreRecord(Float32MultiArrayMsg msg, Dictionary<int, List<FaultRecord>> dict)
    {
        int observedId = (int)msg.data[0];
        float prob     = msg.data[1];
        int pred       = (int)msg.data[2];

        if (!dict.ContainsKey(observedId))
            dict[observedId] = new List<FaultRecord>();

        dict[observedId].Add(new FaultRecord
        {
            timestamp   = Time.time,
            probability = prob,
            prediction  = pred
        });
    }

    public int   GetLatestPrediction(int observedId, bool lstm = false)
    {
        var dict = lstm ? historyLSTM : history;
        if (!dict.ContainsKey(observedId)) return -1;
        var list = dict[observedId];
        return list[list.Count - 1].prediction;
    }

    public float GetLatestProbability(int observedId, bool lstm = false)
    {
        var dict = lstm ? historyLSTM : history;
        if (!dict.ContainsKey(observedId)) return -1f;
        var list = dict[observedId];
        return list[list.Count - 1].probability;
    }
}