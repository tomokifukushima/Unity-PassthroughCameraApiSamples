// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using NativeWebSocket;
using UnityEngine;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// /divided_robot/joint_states を rosbridge 経由で購読し、
    /// 関節角度を HUD (OnGUI) に表示する。
    ///
    /// 購読トピック:
    ///   /divided_robot/joint_states  sensor_msgs/JointState
    /// </summary>
    public class RosJointStateSubscriber : MonoBehaviour
    {
        [Header("ROS Bridge")]
        [Tooltip("例: ws://133.15.97.117:9090")]
        [SerializeField] private string m_rosBridgeUrl = "ws://133.15.97.117:9090";
        [SerializeField] private string m_topic = "/divided_robot/joint_states";

        [Header("Display")]
        [Tooltip("HUD の左上 X 座標")]
        [SerializeField] private int m_hudX = 10;
        [Tooltip("HUD の左上 Y 座標")]
        [SerializeField] private int m_hudY = 10;
        [SerializeField] private int m_fontSize = 28;

        // --- 内部状態 ---
        private WebSocket m_webSocket;
        private bool m_isConnected = false;
        private string[] m_jointNames = new string[0];
        private float[] m_positions = new float[0];
        private string m_statusText = "Not connected";
        private float m_lastReceiveTime = -999f;
        private GUIStyle m_guiStyle;

        // --- JSON 解析用クラス ---
        [Serializable]
        private class JointStateMsg
        {
            public string[] name;
            public float[] position;
        }

        [Serializable]
        private class RosbridgeJointState
        {
            public string op;
            public JointStateMsg msg;
        }

        // -------------------------------------------------------

        private IEnumerator Start()
        {
            // RosControllerPublisher と同時接続を避けるため少し遅らせる
            yield return new WaitForSeconds(2f);
            ConnectAsync();
        }

        private async void ConnectAsync()
        {
            m_webSocket = new WebSocket(m_rosBridgeUrl);

            m_webSocket.OnOpen += () =>
            {
                m_isConnected = true;
                m_statusText = $"Connected: {m_rosBridgeUrl}";
                Debug.Log("[RosJointStateSubscriber] rosbridge 接続成功");
                SubscribeTopic();
            };

            m_webSocket.OnError += (e) =>
            {
                m_statusText = $"ERROR: {e}";
                Debug.LogError($"[RosJointStateSubscriber] WebSocket エラー: {e}");
            };

            m_webSocket.OnClose += (e) =>
            {
                m_isConnected = false;
                m_statusText = "Disconnected";
                Debug.Log($"[RosJointStateSubscriber] 切断: {e}");
            };

            m_webSocket.OnMessage += (bytes) =>
            {
                var json = System.Text.Encoding.UTF8.GetString(bytes);
                ParseMessage(json);
            };

            await m_webSocket.Connect();
        }

        private void SubscribeTopic()
        {
            m_webSocket.SendText(
                $"{{\"op\":\"subscribe\",\"topic\":\"{m_topic}\",\"type\":\"sensor_msgs/JointState\"}}");
            Debug.Log($"[RosJointStateSubscriber] subscribe: {m_topic}");
        }

        private void ParseMessage(string json)
        {
            try
            {
                var parsed = JsonUtility.FromJson<RosbridgeJointState>(json);
                if (parsed == null || parsed.op != "publish" || parsed.msg == null) return;

                m_jointNames = parsed.msg.name   ?? new string[0];
                m_positions  = parsed.msg.position ?? new float[0];
                m_lastReceiveTime = Time.time;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RosJointStateSubscriber] Parse error: {e.Message}");
            }
        }

        private void Update()
        {
            m_webSocket?.DispatchMessageQueue();
        }

        private void OnApplicationQuit()
        {
            m_webSocket?.Close();
        }

        private void OnGUI()
        {
            if (m_guiStyle == null)
            {
                m_guiStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = m_fontSize,
                    normal = { textColor = Color.white }
                };
            }

            int x = m_hudX;
            int y = m_hudY;
            const int lineH = 36;
            const int w = 700;

            // 接続状態ヘッダ
            GUI.Label(new Rect(x, y, w, lineH), $"[JointState] {m_statusText}", m_guiStyle);
            y += lineH;

            if (m_jointNames.Length == 0)
            {
                GUI.Label(new Rect(x, y, w, lineH), "  Waiting for data...", m_guiStyle);
                return;
            }

            // 最終受信からの経過時間
            float age = Time.time - m_lastReceiveTime;
            GUI.Label(new Rect(x, y, w, lineH), $"  Updated {age:F1}s ago", m_guiStyle);
            y += lineH;

            // 関節ごとの値
            int count = Mathf.Min(m_jointNames.Length, m_positions.Length);
            for (int i = 0; i < count; i++)
            {
                float deg = m_positions[i] * Mathf.Rad2Deg;
                GUI.Label(new Rect(x, y, w, lineH),
                    $"  {m_jointNames[i],30}: {m_positions[i]:F4} rad  ({deg:F1}°)",
                    m_guiStyle);
                y += lineH;
            }
        }
    }
}
