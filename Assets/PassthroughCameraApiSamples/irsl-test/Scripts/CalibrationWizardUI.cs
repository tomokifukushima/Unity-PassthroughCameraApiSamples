// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using UnityEngine;
using PassthroughCameraSamples.IrslMarker;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// アプリ起動時に3点キャリブレーションを案内するウィザードUI。
    ///
    /// 手順:
    ///   1. Y+B を押す → キャリブレーション開始
    ///   2. 基準点1 (赤) に右コントローラをあてて A
    ///   3. 基準点2 (青) に右コントローラをあてて A
    ///   4. 基準点3 (緑) に右コントローラをあてて A
    ///   5. 完了メッセージ → 通常の操作画面へ
    ///
    /// セットアップ:
    ///   1. 空の GameObject に本コンポーネントをアタッチする
    ///   2. Inspector に CalibrationManager と ThreePointCalibration を割り当てる
    ///   3. CalibrationManager の初期モードは Manual のまま（Wizard が制御する）
    /// </summary>
    public class CalibrationWizardUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("CalibrationManager をアタッチした GameObject")]
        [SerializeField] private CalibrationManager m_calibrationManager;

        [Tooltip("ThreePointCalibration をアタッチした GameObject")]
        [SerializeField] private ThreePointCalibration m_threePointCalibration;

        [Header("Panel Settings")]
        [Tooltip("カメラからパネルまでの距離 (m)")]
        [SerializeField] private float m_panelDistance = 1.5f;

        [Tooltip("TextMesh の文字サイズ")]
        [SerializeField] private int m_fontSize = 160;

        [Tooltip("パネルのワールドスケール")]
        [SerializeField] private float m_textScale = 0.002f;

        // -------------------------------------------------------

        private enum WizardState
        {
            WaitYB,         // Y+B 待機
            CollectPoint0,  // 基準点1 収集中
            CollectPoint1,  // 基準点2 収集中
            CollectPoint2,  // 基準点3 収集中
            Done,           // 完了表示中
            Finished        // 終了（自身を破棄済み）
        }

        private WizardState m_wizardState = WizardState.WaitYB;
        private TextMesh m_textMesh;
        private Transform m_panelRoot;
        private bool m_ybCooldown = false;
        private RosControllerPublisher m_rosPublisher;
        private AprilTagCalibration m_aprilTagCalibration;
        // -------------------------------------------------------

        private void Start()
        {
            // Inspector 未設定の場合はシーン内から自動検索
            if (m_calibrationManager == null)
                m_calibrationManager = FindObjectOfType<CalibrationManager>();
            if (m_threePointCalibration == null)
                m_threePointCalibration = FindObjectOfType<ThreePointCalibration>();

            // ウィザード中は ROS デバッグパネルを非表示
            m_rosPublisher = FindObjectOfType<RosControllerPublisher>();
            if (m_rosPublisher != null) m_rosPublisher.HideDebugPanel = true;
            m_aprilTagCalibration = FindObjectOfType<AprilTagCalibration>();
            if (m_aprilTagCalibration != null) m_aprilTagCalibration.HideDebugPanel = true;

            // ウィザード完了まで CalibrationManager の Update を無効化
            if (m_calibrationManager != null)
                m_calibrationManager.enabled = false;

            BuildPanel();
            ShowStep(WizardState.WaitYB);
        }

        private void Update()
        {
            UpdatePanelTransform();

            switch (m_wizardState)
            {
                case WizardState.WaitYB:
                    if (!m_ybCooldown &&
                        OVRInput.Get(OVRInput.RawButton.Y) &&
                        OVRInput.Get(OVRInput.RawButton.B))
                    {
                        m_threePointCalibration?.StartCalibration();
                        GoTo(WizardState.CollectPoint0);
                        StartCoroutine(YBCooldown());
                    }
                    break;

                case WizardState.CollectPoint0:
                case WizardState.CollectPoint1:
                case WizardState.CollectPoint2:
                    // ThreePointCalibration の入力処理を駆動し、状態変化を反映する
                    m_threePointCalibration?.HandleInput();
                    SyncWithCalibState();
                    break;
            }
        }

        // -------------------------------------------------------

        private void SyncWithCalibState()
        {
            if (m_threePointCalibration == null) return;

            var wanted = ToWizardState(m_threePointCalibration.CurrentState);
            if (wanted == m_wizardState) return;

            GoTo(wanted);

            if (wanted == WizardState.Done)
                StartCoroutine(FinishAfterDelay(2.5f));
        }

        private void GoTo(WizardState next)
        {
            m_wizardState = next;
            ShowStep(next);
        }

        private IEnumerator FinishAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            // CalibrationManager を ThreePoint 完了状態で再開
            if (m_calibrationManager != null)
            {
                m_calibrationManager.SetThreePointDone();
                m_calibrationManager.enabled = true;
            }

            // デバッグパネルを再表示
            if (m_rosPublisher != null) m_rosPublisher.HideDebugPanel = false;
            if (m_aprilTagCalibration != null) m_aprilTagCalibration.HideDebugPanel = false;

            m_wizardState = WizardState.Finished;
            if (m_panelRoot != null)
                Destroy(m_panelRoot.gameObject);
            Destroy(this);
        }

        private IEnumerator YBCooldown()
        {
            m_ybCooldown = true;
            yield return new WaitForSeconds(0.6f);
            m_ybCooldown = false;
        }

        // -------------------------------------------------------

        private static WizardState ToWizardState(ThreePointCalibration.State s)
        {
            switch (s)
            {
                case ThreePointCalibration.State.Point0: return WizardState.CollectPoint0;
                case ThreePointCalibration.State.Point1: return WizardState.CollectPoint1;
                case ThreePointCalibration.State.Point2: return WizardState.CollectPoint2;
                case ThreePointCalibration.State.Done:   return WizardState.Done;
                default:                                  return WizardState.CollectPoint0;
            }
        }

        private static string GetPromptText(WizardState s)
        {
            switch (s)
            {
                case WizardState.WaitYB:
                    return
                        "キャリブレーションを行います。\n\n" +
                        "Y + B  を押してください。";

                case WizardState.CollectPoint0:
                    return
                        "基準点 1 を設定します。\n\n" +
                        "右コントローラの先端を\n" +
                        "原点 にあてて\n" +
                        "A ボタンを押してください。\n\n" +
                        "記録されると  赤い球  が表示されます。\n" +
                        "(X でやり直し)";

                case WizardState.CollectPoint1:
                    return
                        "基準点 2 を設定します。\n\n" +
                        "右コントローラの先端を\n" +
                        "ポイント 2 にあてて\n" +
                        "A ボタンを押してください。\n\n" +
                        "記録されると  青い球  が表示されます。\n" +
                        "(X でやり直し)";

                case WizardState.CollectPoint2:
                    return
                        "基準点 3 を設定します。\n\n" +
                        "右コントローラの先端を\n" +
                        "ポイント 3 にあてて\n" +
                        "A ボタンを押してください。\n\n" +
                        "記録されると  緑の球  が表示されます。\n" +
                        "(X でやり直し)";

                case WizardState.Done:
                    return "基準点設定が完了しました。";

                default:
                    return "";
            }
        }

        // -------------------------------------------------------

        private void BuildPanel()
        {
            m_panelRoot = new GameObject("CalibWizardPanel").transform;

            var textGo           = new GameObject("CalibWizardText");
            textGo.transform.SetParent(m_panelRoot, false);

            m_textMesh           = textGo.AddComponent<TextMesh>();
            m_textMesh.fontSize  = m_fontSize;
            m_textMesh.color     = Color.white;
            m_textMesh.anchor    = TextAnchor.MiddleCenter;
            m_textMesh.alignment = TextAlignment.Center;

            m_panelRoot.localScale = Vector3.one * m_textScale;
        }

        private void UpdatePanelTransform()
        {
            if (m_panelRoot == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            // ピッチ・ロールを除き、ヨー方向のみ追従（常にユーザーの正面に表示）
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();

            m_panelRoot.position = cam.transform.position + fwd * m_panelDistance;
            m_panelRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        private void ShowStep(WizardState s)
        {
            if (m_textMesh != null)
                m_textMesh.text = GetPromptText(s);
        }
    }
}
