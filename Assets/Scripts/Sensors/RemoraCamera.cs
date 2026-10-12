using RosMessageTypes.Sensor;
using UnitySensors.ROS.Serializer.Sensor;

namespace UnitySensors.ROS.Publisher.Sensor
{
    public class RemoraCamera : RosMsgPublisher<ImageMsgSerializer, ImageMsg>
    {
        protected override void Start()
        {
            string robot_id = transform.root.name;
            _topicName = $"/{robot_id}/camera/image_raw";

            base.Start();
        }
    }
}
