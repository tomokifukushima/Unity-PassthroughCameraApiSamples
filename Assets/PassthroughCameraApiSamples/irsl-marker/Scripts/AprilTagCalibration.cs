// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using AprilTag;

namespace PassthroughCameraSamples.IrslMarker
{
    /// <summary>
    /// AprilTag マーカーを継続的に検出し、検出中はマーカー位置にビジュアルを表示する。
    /// A ボタンでキャリブレーションを確定する。
    ///
    /// 使い方:
    ///   1. 16cm × 16cm の tagStandard41h12 マーカー (ID=0 など) をロボット原点に置く
    ///   2. Quest でマーカーを見ると Marker Indicator がマーカー上に表示される
    ///   3. 位置が安定したら A ボタンを押してキャリブレーション確定
    /// </summary>
    public class AprilTagCalibration : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [Header("AprilTag Settings")]
        [Tooltip("マーカーの一辺のサイズ（メートル）。16cm なら 0.16")]
        [SerializeField] private float m_tagSize = 0.20f;
        [Tooltip("検出対象のマーカー ID")]
        [SerializeField] private int m_targetTagId = 0;
        [Tooltip("処理を間引く倍率。大きいほど速いが精度低下。1 or 2 を推奨")]
        [Range(1, 4)]
        [SerializeField] private int m_decimation = 4;
        [Tooltip("検出処理の実行レート（Hz）。高いほど負荷大")]
        [Range(1, 15)]
        [SerializeField] private int m_detectionRateHz = 5;

        [Header("Visualization")]
        [Tooltip("マーカー検出中に表示するオブジェクト。未設定なら自動生成（軸表示）")]
        [SerializeField] private GameObject m_markerIndicator;

        [Header("Status (ReadOnly)")]
        [SerializeField] private bool m_isCalibrated = false;
        [SerializeField] private string m_statusText = "Not calibrated";

        // トラッキング空間からロボット空間への変換行列
        private Matrix4x4 m_trackingToRobot = Matrix4x4.identity;

        private TagDetector m_detector;
        private Color32[] m_colorBuffer;
        private float m_nextDetectionTime = 0f;
        private bool m_tagVisible = false;

        /// <summary>キャリブレーション済みかどうか</summary>
        public bool IsCalibrated => m_isCalibrated;

        /// <summary>トラッキング空間 → ロボット空間の変換行列</summary>
        public Matrix4x4 TrackingToRobot => m_trackingToRobot;

        private IEnumerator Start()
        {
            m_statusText = "Waiting for camera...";
            Debug.Log("[AprilTag] Start: waiting for camera...");

            if (m_cameraAccess == null)
            {
                m_statusText = "ERROR: CameraAccess not set";
                Debug.LogError("[AprilTag] ERROR: m_cameraAccess is null!");
                enabled = false;
                yield break;
            }

            int waitFrames = 0;
            while (!m_cameraAccess.IsPlaying)
            {
                waitFrames++;
                if (waitFrames % 60 == 0)
                    Debug.Log($"[AprilTag] Still waiting for IsPlaying... frames={waitFrames}");
                yield return null;
            }
            Debug.Log($"[AprilTag] IsPlaying=true after {waitFrames} frames");

            // 解像度が有効になるまで待機（(0,0) のままだと native crash する）
            var res = m_cameraAccess.CurrentResolution;
            int resWaitFrames = 0;
            while (res.x == 0 || res.y == 0)
            {
                resWaitFrames++;
                if (resWaitFrames % 60 == 0)
                    Debug.Log($"[AprilTag] Still waiting for resolution... frames={resWaitFrames} res={res}");
                yield return null;
                res = m_cameraAccess.CurrentResolution;
            }
            Debug.Log($"[AprilTag] Resolution ready: {res.x}x{res.y}");

            m_detector = new TagDetector(res.x, res.y, m_decimation);
            m_colorBuffer = new Color32[res.x * res.y];
            Debug.Log($"[AprilTag] Detector created. decimation={m_decimation}");

            // Indicator が未設定なら軸表示オブジェクトを自動生成
            if (m_markerIndicator == null)
                m_markerIndicator = CreateAxisIndicator();

            m_markerIndicator.SetActive(false);
            m_statusText = "Searching for tag...";
        }

        private void Update()
        {
            if (m_detector == null || !m_cameraAccess.IsPlaying)
            {
                if (Time.frameCount % 300 == 0)
                    Debug.Log($"[AprilTag] Update blocked: detector={(m_detector != null ? "ok" : "null")} IsPlaying={m_cameraAccess?.IsPlaying}");
                return;
            }

            // A ボタンで現在の検出結果をキャリブレーションとして確定
            if (OVRInput.GetDown(OVRInput.Button.One) && m_tagVisible)
            {
                LockCalibration();
            }

            // 一定レートで検出処理を実行
            if (Time.time < m_nextDetectionTime) return;
            m_nextDetectionTime = Time.time + 1f / m_detectionRateHz;

            DetectAndUpdateIndicator();
        }

        private void DetectAndUpdateIndicator()
        {
            Debug.Log("[AprilTag] Detecting...");
            NativeArray<Color32> colors;
            try
            {
                colors = m_cameraAccess.GetColors();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AprilTag] GetColors() threw: {e.GetType().Name}: {e.Message}");
                return;
            }
            if (!colors.IsCreated || colors.Length == 0)
            {
                Debug.Log($"[AprilTag] GetColors() invalid: IsCreated={colors.IsCreated} Length={colors.Length}");
                return;
            }

            Debug.Log($"[AprilTag] colors.Length={colors.Length} buf={m_colorBuffer.Length}");

            // GetColors() は内部バッファサイズ = width*height*4 で返す。
            // 実際のRGBAピクセルデータは先頭 width*height 要素のみ。
            int pixelCount = m_colorBuffer.Length; // = width * height
            if (colors.Length < pixelCount)
            {
                Debug.Log($"[AprilTag] Insufficient data: colors={colors.Length} need={pixelCount}");
                return;
            }
            NativeArray<Color32>.Copy(colors, m_colorBuffer, pixelCount);

            try { /* CopyTo の代わりに上でコピー済み */ }
            catch (System.Exception e) { Debug.LogError($"[AprilTag] CopyTo failed: {e.Message}"); return; }

            var intrinsics = m_cameraAccess.Intrinsics;
            var res = m_cameraAccess.CurrentResolution;
            if (intrinsics.FocalLength.y == 0)
            {
                Debug.Log($"[AprilTag] Intrinsics invalid: FocalLength={intrinsics.FocalLength}");
                return;
            }
            // ProcessImage は垂直FoV（ラジアン）を期待する
            // Camera.main.fieldOfView と同じ規約: vertical FoV in radians
            float fovV = 2f * Mathf.Atan(res.y / (2f * intrinsics.FocalLength.y));

            try
            {
                m_detector.ProcessImage(m_colorBuffer, fovV, m_tagSize);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AprilTagCalibration] ProcessImage failed: {e.Message}");
                return;
            }

            // デバッグ: 検出された全タグを表示
            var allTags = new System.Collections.Generic.List<int>();
            foreach (var t in m_detector.DetectedTags) allTags.Add(t.ID);
            m_statusText = $"res={res.x}x{res.y} fovV={fovV * Mathf.Rad2Deg:F1}deg detected=[{string.Join(",", allTags)}] target={m_targetTagId}";
            Debug.Log($"[AprilTag] {m_statusText}");

            bool found = false;
            foreach (var tag in m_detector.DetectedTags)
            {
                if (tag.ID != m_targetTagId) continue;

                var tagLocalPose = new Pose(tag.Position, tag.Rotation);
                var cameraPose = m_cameraAccess.GetCameraPose();

                var markerWorldPos = cameraPose.position + cameraPose.rotation * tagLocalPose.position;
                var markerWorldRot = cameraPose.rotation * tagLocalPose.rotation;

                // Indicator をマーカー位置に移動・表示
                if (m_markerIndicator != null)
                {
                    m_markerIndicator.transform.SetPositionAndRotation(markerWorldPos, markerWorldRot);
                    m_markerIndicator.SetActive(true);
                }

                m_tagVisible = true;
                found = true;

                string calibHint = m_isCalibrated ? "[Calibrated]" : "[Press A to lock]";
                m_statusText = $"{calibHint} Tag {tag.ID} dist={tagLocalPose.position.magnitude:F2}m";
                break;
            }

            if (!found)
            {
                if (m_markerIndicator != null) m_markerIndicator.SetActive(false);
                m_tagVisible = false;
                m_statusText = m_isCalibrated ? "[Calibrated] Tag lost." : "Searching for tag...";
            }
        }

        /// <summary>現在検出中のマーカー位置でキャリブレーションを確定する</summary>
        public void LockCalibration()
        {
            if (!m_tagVisible) return;

            var pos = m_markerIndicator.transform.position;
            var rot = m_markerIndicator.transform.rotation;
            var T_tracking_robot = Matrix4x4.TRS(pos, rot, Vector3.one);
            m_trackingToRobot = T_tracking_robot.inverse;

            m_isCalibrated = true;
            m_statusText = $"Calibrated! Pos={pos:F3}";
            Debug.Log($"[AprilTagCalibration] Locked. MarkerPos={pos}, MarkerRot={rot.eulerAngles}");
        }

        /// <summary>トラッキング空間の位置・姿勢をロボット座標系に変換する</summary>
        public Pose TransformToRobot(Vector3 trackingPos, Quaternion trackingRot)
        {
            var mat = m_trackingToRobot * Matrix4x4.TRS(trackingPos, trackingRot, Vector3.one);
            return new Pose(mat.GetPosition(), mat.rotation);
        }

        // X=赤, Y=緑, Z=青 の軸表示オブジェクトを生成
        private GameObject CreateAxisIndicator()
        {
            var root = new GameObject("MarkerIndicator");
            CreateAxisArrow(root.transform, Vector3.right,   Quaternion.Euler(0, 0, -90), Color.red);
            CreateAxisArrow(root.transform, Vector3.up,      Quaternion.identity,          Color.green);
            CreateAxisArrow(root.transform, Vector3.forward, Quaternion.Euler(90, 0, 0),  Color.blue);
            return root;
        }

        private void CreateAxisArrow(Transform parent, Vector3 dir, Quaternion rot, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.SetParent(parent);
            go.transform.localPosition = dir * 0.05f;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(0.005f, 0.05f, 0.005f);
            Destroy(go.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Standard")) { color = color };
            go.GetComponent<Renderer>().material = mat;
        }

        private void OnDestroy()
        {
            m_detector?.Dispose();
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 500, 30), $"[AprilTag] {m_statusText}");
        }
    }
}

