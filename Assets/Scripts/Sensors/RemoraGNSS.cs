using RosMessageTypes.Sensor;
using UnityEngine;
using UnitySensors.ROS.Serializer.Sensor;

namespace UnitySensors.ROS.Publisher.Sensor
{
    public class RemoraGNSS : RosMsgPublisher<NavSatFixMsgSerializer, NavSatFixMsg>
    {
        protected override void Start()
        {
            string robot_id = transform.root.name;
            _topicName = $"/{robot_id}/gnss"; 

            base.Start();
        }
    }
}