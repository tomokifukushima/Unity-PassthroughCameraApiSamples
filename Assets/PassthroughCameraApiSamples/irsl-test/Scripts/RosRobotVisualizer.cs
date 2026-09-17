// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections.Generic;
using NativeWebSocket;
using UnityEngine;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// ロボット関節の 3D 位置を VR 空間に球体でビジュアライズする。
    ///
    /// - /tf を購読してリンクフレーム位置を取得
    /// - JointState の effort (トルク) に応じて球の色を変化させる
    ///   (緑 = 低負荷 → 赤 = 高負荷)
    ///
    /// 必要条件:
    ///   ロボットが /tf (robot_state_publisher) を配信していること。
    ///
    /// セットアップ:
    ///   1. 空の GameObject に本コンポーネントをアタッチ
    ///   2. CalibrationManager を Inspector に割り当てる
    ///   3. Robot Base Frame にロボットの基準フレーム名を入力 (例: base_link)
    ///   4. Tracked Joints にリンクフレーム名・関節名・最大トルクを設定する
    /// </summary>
    public class RosRobotVisualizer : MonoBehaviour
    {
        [Header("ROS Bridge")]
        [Tooltip("例: ws://133.15.97.78:9090")]
        [SerializeField] private string m_rosBridgeUrl = "ws://133.15.97.78:9090";
        [Tooltip("JointState トピック名")]
        [SerializeField] private string m_jointStateTopic = "/divided_robot/joint_states";
        [Tooltip("ロボットのベースフレーム名 (TF ツリーのルート側)")]
        [SerializeField] private string m_robotBaseFrame = "base_link";

        [Header("Tracked Joints")]
        [SerializeField] private TrackedJoint[] m_trackedJoints = new TrackedJoint[0];

        [Header("Visualization")]
        [Tooltip("球の半径 (m)")]
        [SerializeField] private float m_sphereRadius = 0.025f;

        [Header("References")]
        [SerializeField] private CalibrationManager m_calibrationManager;

        // -------------------------------------------------------

        [Serializable]
        public struct TrackedJoint
        {
            [Tooltip("/tf のリンクフレーム名 (例: link1, hand_link)")]
            public string linkFrameName;
            [Tooltip("sensor_msgs/JointState の joint 名 (例: joint1) 。空欄ならトルク色分けなし")]
            public string jointName;
            [Tooltip("この値 (Nm) に達すると赤になる。0 なら 50Nm をデフォルト使用")]
            public float maxTorque;
        }

        // TF ツリー (child_frame_id → TfFrame)
        private struct TfFrame
        {
            public string parentId;
            public Vector3    translation; // ROS 座標系
            public Quaternion rotation;    // ROS 座標系
        }

        private readonly Dictionary<string, TfFrame> m_tfTree =
            new Dictionary<string, TfFrame>();

        // トルク (joint名 → effort値)
        private readonly Dictionary<string, float> m_torques =
            new Dictionary<string, float>();

        // ビジュアル
        private GameObject[] m_spheres;
        private Material[]   m_materials;

        private WebSocket m_webSocket;
        private bool      m_isConnected = false;

        // -------------------------------------------------------
        // JSON 解析クラス

        [Serializable] private class Vec3    { public float x, y, z; }
        [Serializable] private class Quat    { public float x, y, z, w; }
        [Serializable] private class TfTr    { public Vec3 translation; public Quat rotation; }
        [Serializable] private class TfHdr   { public string frame_id; }
        [Serializable] private class TfStamped
        {
            public TfHdr   header;
            public string  child_frame_id;
            public TfTr    transform;
        }
        [Serializable] private class TfPayload  { public TfStamped[] transforms; }
        [Serializable] private class TfWrapper  { public string op; public string topic; public TfPayload msg; }

        [Serializable] private class JsPayload  { public string[] name; public float[] effort; }
        [Serializable] private class JsWrapper  { public string op; public string topic; public JsPayload msg; }

        [Serializable] private class MsgBase    { public string op; public string topic; }

        // -------------------------------------------------------

        private void Start()
        {
            if (m_calibrationManager == null)
                m_calibrationManager = FindObjectOfType<CalibrationManager>();

            CreateSpheres();
            ConnectAsync();
        }

        private void CreateSpheres()
        {
            m_spheres   = new GameObject[m_trackedJoints.Length];
            m_materials = new Material[m_trackedJoints.Length];

            for (int i = 0; i < m_trackedJoints.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"JointVis_{m_trackedJoints[i].linkFrameName}";
                go.transform.localScale = Vector3.one * (m_sphereRadius * 2f);
                Destroy(go.GetComponent<Collider>());
                go.SetActive(false); // TF 受信後に表示

                var mat = new Material(Shader.Find("Standard")) { color = Color.green };
                go.GetComponent<Renderer>().material = mat;

                m_spheres[i]   = go;
                m_materials[i] = mat;
            }
        }

        private async void ConnectAsync()
        {
            m_webSocket = new WebSocket(m_rosBridgeUrl);

            m_webSocket.OnOpen += () =>
            {
                m_isConnected = true;
                Subscribe("/tf",        "tf2_msgs/TFMessage");
                Subscribe("/tf_static", "tf2_msgs/TFMessage");
                Subscribe(m_jointStateTopic, "sensor_msgs/JointState");
                Debug.Log("[RosRobotVisualizer] Connected");
            };

            m_webSocket.OnError += (e) =>
                Debug.LogError($"[RosRobotVisualizer] WebSocket error: {e}");

            m_webSocket.OnClose += (e) =>
            {
                m_isConnected = false;
                Debug.Log($"[RosRobotVisualizer] Disconnected: {e}");
            };

            m_webSocket.OnMessage += (bytes) =>
                ParseMessage(System.Text.Encoding.UTF8.GetString(bytes));

            await m_webSocket.Connect();
        }

        private void Subscribe(string topic, string type)
        {
            m_webSocket.SendText(
                $"{{\"op\":\"subscribe\",\"topic\":\"{topic}\",\"type\":\"{type}\"}}");
        }

        private void Update()
        {
            m_webSocket?.DispatchMessageQueue();
            if (!m_isConnected) return;
            UpdateSpheres();
        }

        private void UpdateSpheres()
        {
            for (int i = 0; i < m_trackedJoints.Length; i++)
            {
                var joint = m_trackedJoints[i];

                if (!TryGetFrameWorldPos(joint.linkFrameName, out var worldPos))
                {
                    m_spheres[i].SetActive(false);
                    continue;
                }

                m_spheres[i].SetActive(true);
                m_spheres[i].transform.position = worldPos;

                // トルクで色を決定 (緑 → 赤)
                float effort   = m_torques.TryGetValue(joint.jointName, out var t) ? t : 0f;
                float maxTorq  = joint.maxTorque > 0f ? joint.maxTorque : 50f;
                float norm     = Mathf.Clamp01(Mathf.Abs(effort) / maxTorq);
                m_materials[i].color = Color.Lerp(Color.green, Color.red, norm);
            }
        }

        // -------------------------------------------------------
        // TF ツリーの更新と合成

        private void ParseMessage(string json)
        {
            try
            {
                var base_msg = JsonUtility.FromJson<MsgBase>(json);
                if (base_msg?.op != "publish") return;

                if (base_msg.topic == "/tf" || base_msg.topic == "/tf_static")
                {
                    var msg = JsonUtility.FromJson<TfWrapper>(json);
                    if (msg?.msg?.transforms == null) return;
                    foreach (var ts in msg.msg.transforms)
                        StoreTfFrame(ts);
                }
                else if (base_msg.topic == m_jointStateTopic)
                {
                    var msg = JsonUtility.FromJson<JsWrapper>(json);
                    if (msg?.msg?.name == null || msg.msg.effort == null) return;
                    int count = Mathf.Min(msg.msg.name.Length, msg.msg.effort.Length);
                    for (int i = 0; i < count; i++)
                        m_torques[msg.msg.name[i]] = msg.msg.effort[i];
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RosRobotVisualizer] Parse error: {e.Message}");
            }
        }

        private void StoreTfFrame(TfStamped ts)
        {
            if (ts?.child_frame_id == null || ts.header?.frame_id == null || ts.transform == null)
                return;

            var t = ts.transform.translation;
            var r = ts.transform.rotation;

            m_tfTree[ts.child_frame_id] = new TfFrame
            {
                parentId    = ts.header.frame_id,
                translation = new Vector3(t.x, t.y, t.z),
                rotation    = new Quaternion(r.x, r.y, r.z, r.w),
            };
        }

        /// <summary>
        /// TF ツリーを辿り、指定フレームをベースフレーム相対で合成した後、
        /// キャリブレーション逆変換で Unity ワールド空間に変換する。
        /// </summary>
        private bool TryGetFrameWorldPos(string frameId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;

            if (frameId == m_robotBaseFrame)
            {
                worldPos = RosToWorldPos(Vector3.zero);
                return true;
            }

            // ベースフレームまで親を辿る
            var chain   = new List<TfFrame>();
            var current = frameId;
            const int maxDepth = 32;
            int depth = 0;

            while (current != m_robotBaseFrame && depth++ < maxDepth)
            {
                if (!m_tfTree.TryGetValue(current, out var frame)) return false;
                chain.Add(frame);
                current = frame.parentId;
            }

            if (current != m_robotBaseFrame) return false;

            // ルートから葉へ変換を合成 (ROS 座標系)
            var pos = Vector3.zero;
            var rot = Quaternion.identity;

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                pos = rot * chain[i].translation + pos;
                rot = rot * chain[i].rotation;
            }

            worldPos = RosToWorldPos(pos);
            return true;
        }

        /// <summary>
        /// ROS Z-up 右手系の位置を Unity トラッキング空間に変換する。
        ///   ROS (x前 y左 z上) → Unity (x右 y上 z前) → キャリブ逆変換
        /// </summary>
        private Vector3 RosToWorldPos(Vector3 rosPos)
        {
            // ROS → Unity 座標規約 (UnityToRosPos の逆)
            var unityRobotPos = new Vector3(-rosPos.y, rosPos.z, rosPos.x);

            // キャリブレーション逆変換 (Unity ロボット空間 → トラッキング空間)
            if (m_calibrationManager != null)
                return m_calibrationManager.InverseTransformRobotPos(unityRobotPos);

            return unityRobotPos;
        }

        private async void OnDestroy()
        {
            if (m_webSocket != null && m_webSocket.State == WebSocketState.Open)
                await m_webSocket.Close();

            if (m_spheres != null)
                foreach (var s in m_spheres)
                    if (s != null) Destroy(s);
        }
    }
}
