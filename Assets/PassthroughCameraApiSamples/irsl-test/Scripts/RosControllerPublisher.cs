// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using System.Globalization;
using NativeWebSocket;
using UnityEngine;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// Quest コントローラの Pose と Joy を rosbridge 経由で ROS 1 トピックに配信する。
    ///
    /// トピック:
    ///   /quest/right_controller/pose  geometry_msgs/PoseStamped
    ///   /quest/left_controller/pose   geometry_msgs/PoseStamped
    ///   /quest/right_controller/joy   sensor_msgs/Joy
    ///   /quest/left_controller/joy    sensor_msgs/Joy
    ///
    /// Joy axes  : [thumbstick_x, thumbstick_y, index_trigger, hand_trigger]
    /// Joy buttons: [A(X), B(Y), thumbstick_click, menu(left only)]
    ///
    /// 座標系: Unity (左手系 Y-up)。ROS 側で必要に応じて変換してください。
    /// </summary>
    public class RosControllerPublisher : MonoBehaviour
    {
        [Header("ROS Bridge")]
        [Tooltip("例: ws://133.15.97.117:9090")]
        [SerializeField] private string m_rosBridgeUrl = "ws://133.15.97.117:9090";
        [SerializeField] private string m_frameId = "tracking";

        [Header("Publish Settings")]
        [Range(1, 90)]
        [SerializeField] private int m_publishRateHz = 30;

        private WebSocket m_webSocket;
        private bool m_isConnected = false;
        private int m_seqRightPose = 0;
        private int m_seqLeftPose = 0;
        private float m_nextPublishTime = 0f;
        private string m_debugStatus = "Not started";
        private int m_publishCount = 0;

        private IEnumerator Start()
        {
            m_debugStatus = "Connecting...";
            ConnectAsync();
            yield break;
        }

        private async void ConnectAsync()
        {
            m_webSocket = new WebSocket(m_rosBridgeUrl);

            m_webSocket.OnOpen += () =>
            {
                m_isConnected = true;
                m_debugStatus = $"Connected: {m_rosBridgeUrl}";
                Debug.Log("[RosControllerPublisher] rosbridge 接続成功");
                AdvertiseTopics();
            };

            m_webSocket.OnError += (e) =>
            {
                m_debugStatus = $"ERROR: {e}";
                Debug.LogError($"[RosControllerPublisher] WebSocket エラー: {e}");
            };

            m_webSocket.OnClose += (e) =>
            {
                m_isConnected = false;
                m_debugStatus = $"Disconnected: {e}";
                Debug.Log($"[RosControllerPublisher] 切断: {e}");
            };

            await m_webSocket.Connect();
        }

        private void AdvertiseTopics()
        {
            Advertise("/quest/right_controller/pose", "geometry_msgs/PoseStamped");
            Advertise("/quest/left_controller/pose",  "geometry_msgs/PoseStamped");
            Advertise("/quest/right_controller/joy",  "sensor_msgs/Joy");
            Advertise("/quest/left_controller/joy",   "sensor_msgs/Joy");
        }

        private void Advertise(string topic, string type)
        {
            m_webSocket.SendText($"{{\"op\":\"advertise\",\"topic\":\"{topic}\",\"type\":\"{type}\"}}");
            Debug.Log($"[RosControllerPublisher] advertise: {topic}");
        }

        private void Update()
        {
            m_webSocket?.DispatchMessageQueue();

            if (!m_isConnected) return;
            if (Time.time < m_nextPublishTime) return;

            m_nextPublishTime = Time.time + 1f / m_publishRateHz;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var secs  = now / 1000;
            var nsecs = (now % 1000) * 1_000_000;

            PublishPose("/quest/right_controller/pose", OVRInput.Controller.RTouch, ref m_seqRightPose, secs, nsecs);
            PublishPose("/quest/left_controller/pose",  OVRInput.Controller.LTouch, ref m_seqLeftPose,  secs, nsecs);
            PublishJoy("/quest/right_controller/joy",   OVRInput.Controller.RTouch, isRight: true,  secs, nsecs);
            PublishJoy("/quest/left_controller/joy",    OVRInput.Controller.LTouch, isRight: false, secs, nsecs);

            m_publishCount++;
            if (m_publishCount % 30 == 0)
                Debug.Log($"[RosControllerPublisher] published {m_publishCount} times");
        }

        private void PublishPose(string topic, OVRInput.Controller controller, ref int seq, long secs, long nsecs)
        {
            var pos = OVRInput.GetLocalControllerPosition(controller);
            var rot = OVRInput.GetLocalControllerRotation(controller);

            string F(float v) => v.ToString("F6", CultureInfo.InvariantCulture);

            var msg =
                $"{{\"op\":\"publish\",\"topic\":\"{topic}\",\"msg\":{{" +
                $"\"header\":{{\"seq\":{seq++},\"stamp\":{{\"secs\":{secs},\"nsecs\":{nsecs}}},\"frame_id\":\"{m_frameId}\"}}," +
                $"\"pose\":{{" +
                $"\"position\":{{\"x\":{F(pos.x)},\"y\":{F(pos.y)},\"z\":{F(pos.z)}}}," +
                $"\"orientation\":{{\"x\":{F(rot.x)},\"y\":{F(rot.y)},\"z\":{F(rot.z)},\"w\":{F(rot.w)}}}" +
                $"}}}}}}";

            m_webSocket.SendText(msg);
        }

        private void PublishJoy(string topic, OVRInput.Controller controller, bool isRight, long secs, long nsecs)
        {
            var thumbstick   = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
            var indexTrigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);
            var handTrigger  = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger,  controller);

            int btnOne       = OVRInput.Get(OVRInput.Button.One, controller)             ? 1 : 0; // A / X
            int btnTwo       = OVRInput.Get(OVRInput.Button.Two, controller)             ? 1 : 0; // B / Y
            int thumbClick   = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, controller) ? 1 : 0;
            int menu         = (!isRight && OVRInput.Get(OVRInput.Button.Start))         ? 1 : 0;

            string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
            var msg =
                $"{{\"op\":\"publish\",\"topic\":\"{topic}\",\"msg\":{{" +
                $"\"header\":{{\"seq\":0,\"stamp\":{{\"secs\":{secs},\"nsecs\":{nsecs}}},\"frame_id\":\"\"}}," +
                $"\"axes\":[{F(thumbstick.x)},{F(thumbstick.y)},{F(indexTrigger)},{F(handTrigger)}]," +
                $"\"buttons\":[{btnOne},{btnTwo},{thumbClick},{menu}]" +
                $"}}}}";

            m_webSocket.SendText(msg);
        }

        private async void OnDestroy()
        {
            if (m_webSocket != null && m_webSocket.State == WebSocketState.Open)
                await m_webSocket.Close();
        }
    }
}
