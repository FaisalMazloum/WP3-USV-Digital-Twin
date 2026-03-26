using RosMessageTypes.Sensor;
using UnitySensors.ROS.Serializer.Sensor;

namespace UnitySensors.ROS.Publisher.Sensor
{
    public class RemoraIMU : RosMsgPublisher<IMUMsgSerializer, ImuMsg>
    {
        protected override void Start()
        {
            string robot_id = transform.root.name;
            _topicName = $"/{robot_id}/imu";

            base.Start();
        }
    };
}
