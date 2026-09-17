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
    /// 座標系: ROS 規約 (右手系 Z-up, X前 Y左)。
    ///   固定変換 (Unity Y-up 左手系 → ROS Z-up 右手系) を常に適用したうえで
    ///   CalibrationManager による動的補正（3点キャリブ等）を重ねる。
    ///   3点キャリブの Robot Point は Unity 座標規約 (Y-up) で設定すること。
    /// </summary>
    public class RosControllerPublisher : MonoBehaviour
    {
        [Header("ROS Bridge")]
        [Tooltip("例: ws://133.15.97.117:9090")]
        [SerializeField] private string m_rosBridgeUrl = "ws://133.15.97.78:9090";
        [SerializeField] private string m_frameId = "tracking";

        [Header("Publish Settings")]
        [Range(1, 90)]
        [SerializeField] private int m_publishRateHz = 30;

        [Header("3-Axis Test Log")]
        [Tooltip("右コントローラの3軸テスト用ログを有効化する")]
        [SerializeField] private bool m_enableAxisTestLog = false;
        [Tooltip("3軸テストログの最短出力間隔（秒）")]
        [SerializeField] private float m_axisLogIntervalSec = 0.4f;
        [Tooltip("この距離以上動いたときだけ3軸テストログを出力（m）")]
        [SerializeField] private float m_axisLogMoveThreshold = 0.02f;

        private WebSocket m_webSocket;
        private bool m_isConnected = false;
        private int m_seqRightPose = 0;
        private int m_seqLeftPose = 0;
        private float m_nextPublishTime = 0f;
        private string m_debugStatus = "Not started";
        private int m_publishCount = 0;
        private float m_nextCalibStatusTime = 0f;
        private float m_nextAxisLogTime = 0f;
        private Vector3 m_prevRightRawPos;
        private bool m_hasPrevRightRawPos = false;

        [Header("Calibration")]
        [Tooltip("CalibrationManager をアタッチした GameObject を割り当てる (省略可)")]
        [SerializeField] private CalibrationManager m_calibrationManager;

        // JointState 購読用
        [Header("Subscribe: JointState")]
        [SerializeField] private string m_jointStateTopic = "/divided_robot/joint_states";
        private string[] m_jointNames = new string[0];
        private float[] m_jointPositions = new float[0];
        private float m_lastJointReceiveTime = -999f;
        private int m_jointLogCount = 0;

        [Serializable] private class JointStateMsg { public string[] name; public float[] position; }
        [Serializable] private class RosbridgeMsg   { public string op; public JointStateMsg msg; }

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

            m_webSocket.OnMessage += (bytes) =>
            {
                var json = System.Text.Encoding.UTF8.GetString(bytes);
                try
                {
                    var parsed = JsonUtility.FromJson<RosbridgeMsg>(json);
                    if (parsed?.op == "publish" && parsed.msg != null)
                    {
                        m_jointNames     = parsed.msg.name     ?? new string[0];
                        m_jointPositions = parsed.msg.position ?? new float[0];
                        m_lastJointReceiveTime = Time.time;

                        // 30回に1回だけログ出力
                        if (m_jointNames.Length > 0 && ++m_jointLogCount % 30 == 0)
                        {
                            var sb = new System.Text.StringBuilder($"[JointState] #{m_jointLogCount}");
                            int c = System.Math.Min(m_jointNames.Length, m_jointPositions.Length);
                            for (int i = 0; i < c; i++)
                                sb.Append($" {m_jointNames[i]}={m_jointPositions[i]:F3}");
                            Debug.Log(sb.ToString());
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[RosControllerPublisher] OnMessage parse error: {e.Message}");
                }
            };

            await m_webSocket.Connect();
        }

        private void AdvertiseTopics()
        {
            Advertise("/quest/right_controller/pose", "geometry_msgs/PoseStamped");
            Advertise("/quest/left_controller/pose",  "geometry_msgs/PoseStamped");
            Advertise("/quest/right_controller/joy",  "sensor_msgs/Joy");
            Advertise("/quest/left_controller/joy",   "sensor_msgs/Joy");
            Advertise("/quest/calibration_status",    "std_msgs/String");
            // joint_states 購読
            m_webSocket.SendText(
                $"{{\"op\":\"subscribe\",\"topic\":\"{m_jointStateTopic}\",\"type\":\"sensor_msgs/JointState\"}}");
            Debug.Log($"[RosControllerPublisher] subscribe: {m_jointStateTopic}");
        }

        private void Advertise(string topic, string type)
        {
            m_webSocket.SendText($"{{\"op\":\"advertise\",\"topic\":\"{topic}\",\"type\":\"{type}\"}}");
            Debug.Log($"[RosControllerPublisher] advertise: {topic}");
        }

        private void Update()
        {
            m_webSocket?.DispatchMessageQueue();
            UpdateDebugPanel();

            // キャリブレーション状態は接続中は常に送信 (抑制状態でも ROS 側が判断できるよう)
            if (m_isConnected) PublishCalibStatus();

            // キャリブレーション編集中は ROS 送信を抑制 (誤ったコマンドをロボットに送らないため)
            if (m_calibrationManager != null && m_calibrationManager.IsSuppressingPublish) return;

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
            var rawPos = OVRInput.GetLocalControllerPosition(controller);
            var rawRot = OVRInput.GetLocalControllerRotation(controller);
            var pos = rawPos;
            var rot = rawRot;

            // キャリブレーションが設定されている場合は座標変換を適用する (Unity 座標系内で処理)
            if (m_calibrationManager != null)
                (pos, rot) = m_calibrationManager.Transform(pos, rot);

            var calibratedPos = pos;
            var calibratedRot = rot;

            // Unity 左手系 (Y-up) → ROS 右手系 (Z-up) へ固定変換
            pos = UnityToRosPos(pos);
            rot = UnityToRosRot(rot);

            TryLogAxisTest(controller, rawPos, rawRot, calibratedPos, calibratedRot, pos, rot);

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

        private void TryLogAxisTest(
            OVRInput.Controller controller,
            Vector3 rawPos,
            Quaternion rawRot,
            Vector3 calibratedPos,
            Quaternion calibratedRot,
            Vector3 rosPos,
            Quaternion rosRot)
        {
            if (!m_enableAxisTestLog) return;
            if (controller != OVRInput.Controller.RTouch) return;
            if (Time.time < m_nextAxisLogTime) return;

            if (m_hasPrevRightRawPos)
            {
                float moveDist = Vector3.Distance(rawPos, m_prevRightRawPos);
                if (moveDist < m_axisLogMoveThreshold) return;
            }

            m_hasPrevRightRawPos = true;
            m_prevRightRawPos = rawPos;
            m_nextAxisLogTime = Time.time + Mathf.Max(0.05f, m_axisLogIntervalSec);

            var rawX = rawRot * Vector3.right;
            var rawY = rawRot * Vector3.up;
            var rawZ = rawRot * Vector3.forward;

            var calibX = calibratedRot * Vector3.right;
            var calibY = calibratedRot * Vector3.up;
            var calibZ = calibratedRot * Vector3.forward;

            var rosX = rosRot * Vector3.right;
            var rosY = rosRot * Vector3.up;
            var rosZ = rosRot * Vector3.forward;

            string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

            Debug.Log(
                "[AxisTest] RTouch\n" +
                $" rawPos={V(rawPos)} rawAxes X={V(rawX)} Y={V(rawY)} Z={V(rawZ)}\n" +
                $" calibPos={V(calibratedPos)} calibAxes X={V(calibX)} Y={V(calibY)} Z={V(calibZ)}\n" +
                $" rosPos={V(rosPos)} rosAxes X={V(rosX)} Y={V(rosY)} Z={V(rosZ)}");
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

        // ---- Unity → ROS 座標変換ユーティリティ ----
        // Unity: 左手系 X右 Y上 Z前  →  ROS REP-103: 右手系 X前 Y左 Z上
        private static Vector3    UnityToRosPos(Vector3    u) => new Vector3(u.z, -u.x, u.y);
        private static Quaternion UnityToRosRot(Quaternion u) => new Quaternion(u.z, -u.x, u.y, -u.w);

        // ---- キャリブレーション状態トピック (1 Hz) ----
        private void PublishCalibStatus()
        {
            if (Time.time < m_nextCalibStatusTime) return;
            m_nextCalibStatusTime = Time.time + 1f;

            string mode        = "none";
            string calibrated  = "0";
            string suppressing = "0";

            if (m_calibrationManager != null)
            {
                mode        = m_calibrationManager.CurrentMode.ToString();
                calibrated  = m_calibrationManager.IsCalibrated       ? "1" : "0";
                suppressing = m_calibrationManager.IsSuppressingPublish ? "1" : "0";
            }

            m_webSocket.SendText(
                $"{{\"op\":\"publish\",\"topic\":\"/quest/calibration_status\"," +
                $"\"msg\":{{\"data\":\"mode={mode},calibrated={calibrated},suppressing={suppressing}\"}}}}");
        }

        private async void OnDestroy()
        {
            if (m_webSocket != null && m_webSocket.State == WebSocketState.Open)
                await m_webSocket.Close();
        }

        // ---- World Space Debug Panel (TextMesh) ----
        private TextMesh m_debugText;
        private Transform m_panelRoot;

        /// <summary>true のあいだデバッグパネルを非表示にする (CalibrationWizardUI から設定)</summary>
        [System.NonSerialized] public bool HideDebugPanel = false;

        private void CreateDebugPanel()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[RosControllerPublisher] Camera.main is null, retrying next frame");
                return;
            }

            var go = new GameObject("DebugPanel");
            go.transform.SetParent(cam.transform, false);
            // 右上・1m前方
            go.transform.localPosition = new Vector3(0.3f, 0.1f, 1.0f);
            go.transform.localRotation = Quaternion.Euler(0f, 17f, 0f);
            go.transform.localScale    = Vector3.one * 0.002f;

            m_debugText           = go.AddComponent<TextMesh>();
            m_debugText.fontSize  = 120;
            m_debugText.color     = Color.yellow;
            m_debugText.anchor    = TextAnchor.UpperLeft;
            m_debugText.alignment = TextAlignment.Left;
            m_debugText.text      = "Initializing...";

            m_panelRoot = go.transform;
            Debug.Log("[RosControllerPublisher] DebugPanel created");
        }

        private void UpdateDebugPanel()
        {
            // ウィザード表示中はデバッグパネルを隠す
            if (HideDebugPanel)
            {
                if (m_panelRoot != null) m_panelRoot.gameObject.SetActive(false);
                return;
            }
            if (m_panelRoot != null && !m_panelRoot.gameObject.activeSelf)
                m_panelRoot.gameObject.SetActive(true);

            if (m_debugText == null)
            {
                CreateDebugPanel();
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[ROS] {m_debugStatus}");
            if (m_calibrationManager != null)
                sb.AppendLine(m_calibrationManager.StatusText);

            if (m_jointNames.Length == 0)
            {
                sb.AppendLine("[JointState] Waiting for data...");
            }
            else
            {
                float age = Time.time - m_lastJointReceiveTime;
                sb.AppendLine($"[JointState] {age:F1}s ago");
                int count = Mathf.Min(m_jointNames.Length, m_jointPositions.Length);
                for (int i = 0; i < count; i++)
                {
                    float deg = m_jointPositions[i] * Mathf.Rad2Deg;
                    sb.AppendLine($"  {m_jointNames[i]}: {m_jointPositions[i]:F3} ({deg:F1}deg)");
                }
            }

            m_debugText.text = sb.ToString();
        }
    }
}
