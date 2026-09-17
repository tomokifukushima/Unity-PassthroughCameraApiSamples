// Copyright (c) Meta Platforms, Inc. and affiliates.

using PassthroughCameraSamples.IrslMarker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// irsl-test2 専用: 起動直後に AprilTag キャリブレーション案内を表示し、
    /// 完了後に通常のデバッグパネル表示へ切り替える。
    /// </summary>
    public class AprilTagCalibrationGuide : MonoBehaviour
    {
        private const string TargetSceneName = "irsl-test2";
        private static bool s_sceneHooked = false;

        private AprilTagCalibration m_aprilTag;
        private RosControllerPublisher m_rosPublisher;
        private TextMesh m_textMesh;
        private Transform m_root;
        private bool m_finished = false;
        private const float GuideDistance = 1.1f;
        private const float GuideScale = 0.0034f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (s_sceneHooked) return;
            s_sceneHooked = true;

            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawnGuide(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TrySpawnGuide(scene);
        }

        private static void TrySpawnGuide(Scene scene)
        {
            // シーン名またはパスのどちらでも判定できるようにしておく
            bool isTarget = scene.name == TargetSceneName ||
                scene.path.Replace('\\', '/').Contains("/irsl-test2/");
            if (!isTarget) return;

            if (FindObjectOfType<AprilTagCalibrationGuide>() != null) return;

            var go = new GameObject("AprilTagCalibrationGuide");
            go.AddComponent<AprilTagCalibrationGuide>();
        }

        private void Start()
        {
            m_aprilTag = FindObjectOfType<AprilTagCalibration>();
            m_rosPublisher = FindObjectOfType<RosControllerPublisher>();

            if (m_aprilTag == null)
            {
                Debug.LogWarning("[AprilTagCalibrationGuide] AprilTagCalibration not found.");
                Destroy(this);
                return;
            }

            BuildGuideText();
            SetOtherPanelsVisible(false);
        }

        private void Update()
        {
            if (m_finished) return;
            UpdateGuidePose();

            if (m_aprilTag != null && m_aprilTag.IsCalibrated)
            {
                m_finished = true;
                SetOtherPanelsVisible(true);
                DestroyGuide();
                Destroy(this);
            }
        }

        private void SetOtherPanelsVisible(bool visible)
        {
            if (m_rosPublisher != null)
                m_rosPublisher.HideDebugPanel = !visible;
            if (m_aprilTag != null)
                m_aprilTag.HideDebugPanel = !visible;
        }

        private void BuildGuideText()
        {
            m_root = new GameObject("AprilTagGuidePanel").transform;

            BuildFallbackTextMesh();

            m_root.localScale = Vector3.one * GuideScale;
            UpdateGuidePose();
        }

        private void BuildFallbackTextMesh()
        {
            var textGo = new GameObject("GuideText");
            textGo.transform.SetParent(m_root, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0f);

            m_textMesh = textGo.AddComponent<TextMesh>();
            m_textMesh.fontSize = 154;
            m_textMesh.color = Color.white;
            m_textMesh.anchor = TextAnchor.MiddleCenter;
            m_textMesh.alignment = TextAlignment.Center;
            m_textMesh.text =
                "AprilTagを視界にいれて、\n" +
                "キャリブレーションを行ってください。\n" +
                "AprilTagを認識したら、Aボタンを押してください。";
        }

        private void UpdateGuidePose()
        {
            if (m_root == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();

            m_root.position = cam.transform.position + fwd * GuideDistance;
            m_root.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        private void DestroyGuide()
        {
            if (m_root != null)
                Destroy(m_root.gameObject);
        }

        private void OnDestroy()
        {
            if (!m_finished)
                SetOtherPanelsVisible(true);
            DestroyGuide();
        }
    }
}
