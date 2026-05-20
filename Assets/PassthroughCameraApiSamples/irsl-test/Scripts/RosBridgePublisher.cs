// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using Meta.XR;
using NativeWebSocket;
using UnityEngine;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// PassthroughCameraAccess のカメラ映像を rosbridge_websocket 経由で
    /// sensor_msgs/CompressedImage (JPEG) として ROS 1 トピックに配信する。
    /// </summary>
    public class RosBridgePublisher : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [Header("ROS Bridge")]
        [Tooltip("例: ws://192.168.1.10:9090")]
        [SerializeField] private string m_rosBridgeUrl = "ws://192.168.1.10:9090";
        [SerializeField] private string m_topicName = "/quest_camera/compressed";
        [SerializeField] private string m_frameId = "quest_camera";

        [Header("Publish Settings")]
        [Range(1, 30)]
        [SerializeField] private int m_publishRateHz = 10;
        [Range(1, 100)]
        [SerializeField] private int m_jpegQuality = 75;

        private string m_debugStatus = "Not started";

        private WebSocket m_webSocket;
        private bool m_isConnected = false;
        private int m_seq = 0;
        private float m_nextPublishTime = 0f;
        private Texture2D m_encodeTex;

        private IEnumerator Start()
        {
            if (m_cameraAccess == null)
            {
                m_debugStatus = "ERROR: CameraAccess not set";
                Debug.LogError("[RosBridgePublisher] m_cameraAccess が未設定です。");
                enabled = false;
                yield break;
            }

            // カメラが起動するまで待機
            m_debugStatus = "Waiting for camera...";
            while (!m_cameraAccess.IsPlaying)
                yield return null;

            m_debugStatus = "Connecting to rosbridge...";
            ConnectAsync();
        }

        private async void ConnectAsync()
        {
            m_webSocket = new WebSocket(m_rosBridgeUrl);

            m_webSocket.OnOpen += () =>
            {
                Debug.Log("[RosBridgePublisher] rosbridge 接続成功");
                m_isConnected = true;
                m_debugStatus = $"Connected: {m_rosBridgeUrl}";
                AdvertiseTopic();
            };

            m_webSocket.OnError += (e) =>
            {
                m_debugStatus = $"WS ERROR: {e}";
                Debug.LogError($"[RosBridgePublisher] WebSocket エラー: {e}");
            };

            m_webSocket.OnClose += (e) =>
            {
                Debug.Log($"[RosBridgePublisher] 切断: {e}");
                m_isConnected = false;
                m_debugStatus = $"Disconnected: {e}";
            };

            await m_webSocket.Connect();
        }

        private void AdvertiseTopic()
        {
            var json = $"{{\"op\":\"advertise\",\"topic\":\"{m_topicName}\"," +
                       $"\"type\":\"sensor_msgs/CompressedImage\"}}";
            m_webSocket.SendText(json);
            Debug.Log($"[RosBridgePublisher] advertise: {m_topicName}");
        }

        private void Update()
        {
            // NativeWebSocket はメインスレッドでのディスパッチが必要
            m_webSocket?.DispatchMessageQueue();

            if (!m_isConnected || !m_cameraAccess.IsPlaying)
                return;

            if (Time.time < m_nextPublishTime)
                return;

            m_nextPublishTime = Time.time + 1f / m_publishRateHz;
            PublishFrame();
        }

        private void PublishFrame()
        {
            var resolution = m_cameraAccess.CurrentResolution;

            // テクスチャを解像度変更時だけ再生成
            if (m_encodeTex == null ||
                m_encodeTex.width != resolution.x ||
                m_encodeTex.height != resolution.y)
            {
                if (m_encodeTex != null)
                    Destroy(m_encodeTex);
                m_encodeTex = new Texture2D(resolution.x, resolution.y, TextureFormat.RGBA32, false);
            }

            // カメラフレームを取得して JPEG にエンコード
            var colors = m_cameraAccess.GetColors();
            m_encodeTex.LoadRawTextureData(colors);
            m_encodeTex.Apply();

            var jpegBytes = m_encodeTex.EncodeToJPG(m_jpegQuality);
            var base64Data = Convert.ToBase64String(jpegBytes);

            // ROS タイムスタンプ
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var secs = now / 1000;
            var nsecs = (now % 1000) * 1_000_000;

            var msg =
                $"{{\"op\":\"publish\",\"topic\":\"{m_topicName}\",\"msg\":{{" +
                $"\"header\":{{\"seq\":{m_seq++}," +
                $"\"stamp\":{{\"secs\":{secs},\"nsecs\":{nsecs}}}," +
                $"\"frame_id\":\"{m_frameId}\"}}," +
                $"\"format\":\"jpeg\"," +
                $"\"data\":\"{base64Data}\"" +
                $"}}}}";

            m_webSocket.SendText(msg);
            m_debugStatus = $"Publishing seq={m_seq - 1} to {m_topicName}";
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 28,
                alignment = TextAnchor.UpperLeft
            };
            style.normal.textColor = Color.white;
            GUI.Box(new Rect(10, 10, 700, 60), $"ROS WS: {m_debugStatus}", style);
        }

        private async void OnDestroy()
        {
            if (m_webSocket != null && m_webSocket.State == WebSocketState.Open)
                await m_webSocket.Close();

            if (m_encodeTex != null)
                Destroy(m_encodeTex);
        }
    }
}
