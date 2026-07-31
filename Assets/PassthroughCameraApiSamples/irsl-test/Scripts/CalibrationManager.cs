// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using UnityEngine;
using PassthroughCameraSamples.IrslMarker;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// 手動キャリブレーション (Calibration) と
    /// マーカーキャリブレーション (AprilTagCalibration) を統合管理する。
    ///
    /// ボタン操作:
    ///   Y + B 同時押し … Manual / Marker モード切り替え
    ///
    ///   [Manual モード]
    ///     X + A … 手動編集モード ON/OFF (編集中は ROS 送信を停止)
    ///     編集中: 右IndexTrig=回転 / 左IndexTrig=移動 / Thumbstick=リセット
    ///
    ///   [Marker モード]
    ///     A ボタン … 現在検出中のマーカーでキャリブレーションを確定
    ///     (ROS 送信は止めない。カメラがマーカーを検出するまで変換はパススルー)
    ///
    /// セットアップ:
    ///   1. CalibrationRoot (Calibration.cs) と AprilTagCalibration を持つ
    ///      GameObject をそれぞれ Inspector に割り当てる。
    ///   2. RosControllerPublisher の M Calibration Manager に本コンポーネントを割り当てる。
    /// </summary>
    public class CalibrationManager : MonoBehaviour
    {
        public enum CalibMode { Manual, Marker }

        [Header("Calibration Sources")]
        [Tooltip("手動キャリブレーション (Calibration.cs をアタッチした GameObject)")]
        [SerializeField] private Calibration m_manual;

        [Tooltip("マーカーキャリブレーション (AprilTagCalibration.cs をアタッチした GameObject)")]
        [SerializeField] private AprilTagCalibration m_marker;

        [Header("Active Mode (Runtime)")]
        [SerializeField] private CalibMode m_mode = CalibMode.Manual;

        private bool m_cooldown = false;
        private bool m_manualEditing = false;

        // -------------------------------------------------------

        /// <summary>手動編集中かどうか (true のとき ROS 送信を抑制すべき)</summary>
        public bool IsSuppressingPublish => m_manualEditing;

        /// <summary>デバッグパネル用ステータス文字列</summary>
        public string StatusText { get; private set; } = "";

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
            else
                HandleMarkerMode();
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

        private void SwitchMode()
        {
            // 編集中なら編集を終了してからモード切り替え
            if (m_manualEditing)
            {
                m_manualEditing = false;
                m_manual?.SwitchAlign(false);
            }

            m_mode = (m_mode == CalibMode.Manual) ? CalibMode.Marker : CalibMode.Manual;
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
            else // Marker
            {
                if (m_marker == null || !m_marker.IsCalibrated) return (trackingPos, trackingRot);
                var pose = m_marker.TransformToRobot(trackingPos, trackingRot);
                return (pose.position, pose.rotation);
            }
        }
    }
}
