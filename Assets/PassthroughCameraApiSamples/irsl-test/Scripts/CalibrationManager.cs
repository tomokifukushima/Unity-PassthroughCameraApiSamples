// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using UnityEngine;
using PassthroughCameraSamples.IrslMarker;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// 手動キャリブレーション (Calibration) ・
    /// マーカーキャリブレーション (AprilTagCalibration) ・
    /// 3点キャリブレーション (ThreePointCalibration) を統合管理する。
    ///
    /// ボタン操作:
    ///   Y + B 同時押し … Manual → Marker → ThreePoint → Manual サイクル
    ///
    ///   [Manual モード]
    ///     X + A … 手動編集モード ON/OFF (編集中は ROS 送信を停止)
    ///     編集中: 右IndexTrig=回転 / 左IndexTrig=移動 / Thumbstick=リセット
    ///
    ///   [Marker モード]
    ///     A ボタン … 現在検出中のマーカーでキャリブレーションを確定
    ///
    ///   [ThreePoint モード]
    ///     A ボタン … 現在の右コントローラー位置を基準点として記録
    ///     X ボタン … キャリブレーションやり直し
    ///     (3点記録中は ROS 送信を停止)
    ///
    /// セットアップ:
    ///   各キャリブレーションコンポーネントを Inspector に割り当てる。
    ///   RosControllerPublisher の M Calibration Manager に本コンポーネントを割り当てる。
    /// </summary>
    public class CalibrationManager : MonoBehaviour
    {
        public enum CalibMode { Manual, Marker, ThreePoint }

        [Header("Calibration Sources")]
        [Tooltip("手動キャリブレーション (Calibration.cs をアタッチした GameObject)")]
        [SerializeField] private Calibration m_manual;

        [Tooltip("マーカーキャリブレーション (AprilTagCalibration.cs をアタッチした GameObject)")]
        [SerializeField] private AprilTagCalibration m_marker;

        [Tooltip("3点キャリブレーション (ThreePointCalibration.cs をアタッチした GameObject)")]
        [SerializeField] private ThreePointCalibration m_threePoint;

        [Header("Active Mode (Runtime)")]
        [SerializeField] private CalibMode m_mode = CalibMode.Manual;

        private bool m_cooldown = false;
        private bool m_manualEditing = false;

        // -------------------------------------------------------

        /// <summary>手動編集中 or 3点収集中は送信を抑制</summary>
        public bool IsSuppressingPublish => m_manualEditing ||
            (m_mode == CalibMode.ThreePoint && m_threePoint != null && m_threePoint.IsCollecting);

        /// <summary>デバッグパネル用ステータス文字列</summary>
        public string StatusText { get; private set; } = "";

        /// <summary>現在のキャリブレーションモード (ROS ステータストピック用)</summary>
        public CalibMode CurrentMode => m_mode;

        /// <summary>
        /// 現在モードでキャリブレーション済みか (ROS ステータストピック用)。
        /// Manual は常に true、Marker/ThreePoint は各コンポーネントの状態に依存する。
        /// </summary>
        public bool IsCalibrated =>
            m_mode == CalibMode.Marker     ? (m_marker     != null && m_marker.IsCalibrated)     :
            m_mode == CalibMode.ThreePoint ? (m_threePoint != null && m_threePoint.IsCalibrated) :
            true; // Manual は常にキャリブレーション済みとみなす

        // -------------------------------------------------------

        private void Update()
        {
            // Y + B 同時押し → モード切り替え
            if (!m_cooldown &&
                OVRInput.Get(OVRInput.RawButton.Y) && OVRInput.Get(OVRInput.RawButton.B))
            {
                SwitchMode();
                StartCoroutine(Cooldown());
                return;
            }

            if (m_mode == CalibMode.Manual)
                HandleManualMode();
            else if (m_mode == CalibMode.Marker)
                HandleMarkerMode();
            else
                HandleThreePointMode();
        }

        private void HandleManualMode()
        {
            // X + A 同時押し → 手動編集 ON/OFF
            if (!m_cooldown &&
                OVRInput.Get(OVRInput.RawButton.X) && OVRInput.Get(OVRInput.RawButton.A))
            {
                m_manualEditing = !m_manualEditing;
                m_manual?.SwitchAlign(m_manualEditing);
                Debug.Log($"[CalibrationManager] Manual editing = {m_manualEditing}");
                StartCoroutine(Cooldown());
            }

            StatusText = m_manualEditing
                ? "[Manual CALIB ON] X+A \u3067\u7d42\u4e86 | \u53f3Trig=\u56de\u8ee2 \u5de6Trig=\u79fb\u52d5"
                : "[Manual] X+A \u3067\u5c65\u6b74\u958b\u59cb | Y+B \u3067\u30de\u30fc\u30ab\u30fc\u30e2\u30fc\u30c9\u3078";
        }

        private void HandleMarkerMode()
        {
            // Marker モードでは手動編集は無効
            if (m_manualEditing)
            {
                m_manualEditing = false;
                m_manual?.SwitchAlign(false);
            }

            if (m_marker == null)
            {
                StatusText = "[Marker] AprilTagCalibration \u672a\u5272\u308a\u5f53\u3066 | Y+B \u3067\u624b\u52d5\u30e2\u30fc\u30c9\u3078";
                return;
            }

            string calibState = m_marker.IsCalibrated ? "Calibrated OK" : "Searching...";
            StatusText = $"[Marker] {calibState} | A=\u78ba\u5b9a | Y+B \u3067\u624b\u52d5\u30e2\u30fc\u30c9\u3078";
        }

        private void HandleThreePointMode()
        {
            // 手動編集中なら終了
            if (m_manualEditing)
            {
                m_manualEditing = false;
                m_manual?.SwitchAlign(false);
            }

            if (m_threePoint == null)
            {
                StatusText = "[ThreePoint] ThreePointCalibration 未割り当て | Y+B でマーカーモードへ";
                return;
            }

            m_threePoint.HandleInput();
            StatusText = m_threePoint.StatusText;
        }

        private void SwitchMode()
        {
            // 編集中なら編集を終了してからモード切り替え
            if (m_manualEditing)
            {
                m_manualEditing = false;
                m_manual?.SwitchAlign(false);
            }

            // Manual → Marker → ThreePoint → Manual のサイクル
            m_mode = (CalibMode)(((int)m_mode + 1) % 3);

            // ThreePoint に切り替えたときは自動でキャリブレーション開始
            if (m_mode == CalibMode.ThreePoint)
                m_threePoint?.StartCalibration();

            Debug.Log($"[CalibrationManager] Mode → {m_mode}");
        }

        private IEnumerator Cooldown()
        {
            m_cooldown = true;
            yield return new WaitForSeconds(0.5f);
            m_cooldown = false;
        }

        // -------------------------------------------------------

        /// <summary>
        /// トラッキング空間の位置・回転をキャリブレーション済み座標に変換する。
        /// キャリブレーション未設定の場合はパススルー。
        /// </summary>
        public (Vector3 pos, Quaternion rot) Transform(Vector3 trackingPos, Quaternion trackingRot)
        {
            if (m_mode == CalibMode.Manual)
            {
                if (m_manual == null) return (trackingPos, trackingRot);
                return (m_manual.GetPosition(trackingPos), m_manual.GetRotation(trackingRot));
            }
            else if (m_mode == CalibMode.Marker)
            {
                if (m_marker == null || !m_marker.IsCalibrated) return (trackingPos, trackingRot);
                var pose = m_marker.TransformToRobot(trackingPos, trackingRot);
                return (pose.position, pose.rotation);
            }
            else // ThreePoint
            {
                if (m_threePoint == null || !m_threePoint.IsCalibrated) return (trackingPos, trackingRot);
                var pose = m_threePoint.TransformToRobot(trackingPos, trackingRot);
                return (pose.position, pose.rotation);
            }
        }

        /// <summary>
        /// CalibrationWizardUI がキャリブレーション完了後に呼ぶ。
        /// モードを ThreePoint に設定し、CalibrationManager を再有効化できる状態にする。
        /// </summary>
        public void SetThreePointDone()
        {
            m_mode = CalibMode.ThreePoint;
            m_manualEditing = false;
            StatusText = m_threePoint != null ? m_threePoint.StatusText : "[ThreePoint] キャリブ完了";
        }

        /// <summary>
        /// ロボット座標系 (ROS Z-up 右手系) の位置を
        /// Unity トラッキング空間に逆変換する。
        /// VR 空間でのロボット関節ビジュアライズ用。
        /// </summary>
        public Vector3 InverseTransformRobotPos(Vector3 robotPosUnity)
        {
            if (m_mode == CalibMode.Marker && m_marker != null && m_marker.IsCalibrated)
                return m_marker.RobotToTrackingPos(robotPosUnity);
            if (m_mode == CalibMode.ThreePoint && m_threePoint != null && m_threePoint.IsCalibrated)
                return m_threePoint.RobotToTrackingPos(robotPosUnity);
            return robotPosUnity; // キャリブ未完了の場合はそのまま返す
        }
    }
}
