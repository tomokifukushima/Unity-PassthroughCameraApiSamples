// Copyright (c) Meta Platforms, Inc. and affiliates.
// Based on TactAR_APP Calibration.cs (https://github.com/xiaoxiaoxh/TactAR_APP)

using UnityEngine;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// VR コントローラでロボット座標系を手動キャリブレーションする。
    ///
    /// 操作 (RosControllerPublisher から SwitchAlign を呼んで起動):
    ///   右 IndexTrigger 握りながらコントローラを横に振る … 座標系を回転 (Y軸のみ)
    ///   左 IndexTrigger 握りながらコントローラを動かす  … 座標系を平行移動
    ///   A or X ボタン                                   … 座標軸ビジュアルを縮小
    ///   B or Y ボタン                                   … 座標軸ビジュアルを拡大
    ///   左 or 右 Thumbstick クリック                    … 位置・スケールをリセット
    ///
    /// セットアップ:
    ///   1. 空の GameObject (例: "CalibrationRoot") を作成し、このコンポーネントをアタッチ。
    ///   2. 子 GameObject として座標軸ビジュアル Prefab を作成し、m_coord に割り当てる。
    ///      (単純な XYZ 軸矢印でよい。なければ空の GameObject でも動作する)
    ///   3. RosControllerPublisher の m_calibration フィールドにこの GameObject を割り当てる。
    /// </summary>
    public class Calibration : MonoBehaviour
    {
        public static Calibration instance;

        [Header("Axis Visualizer")]
        [Tooltip("キャリブレーション中に表示する座標軸 GameObject (子オブジェクト推奨)")]
        [SerializeField] private GameObject m_coord;

        [Tooltip("座標軸スケールの最小値")]
        [SerializeField] private float m_scaleMin = 0.1f;

        [Tooltip("座標軸スケールの変化量")]
        [SerializeField] private float m_scaleStep = 0.1f;

        private bool m_running = false;
        private Vector3 m_startPosRight;
        private Vector3 m_startPosLeft;
        private Vector3 m_startTransformPos;

        // -------------------------------------------------------

        private void Awake()
        {
            instance = this;
            m_running = false;
            if (m_coord != null) m_coord.SetActive(false);
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>キャリブレーションモードを切り替える。</summary>
        public void SwitchAlign(bool run)
        {
            m_running = run;
            if (m_coord != null) m_coord.SetActive(run);
        }

        private void Update()
        {
            if (!m_running) return;

            // --- 回転: 右 IndexTrigger 握りながらコントローラを横に振る ---
            if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                m_startPosRight = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);

            if (OVRInput.Get(OVRInput.RawButton.RIndexTrigger))
            {
                Vector3 offset = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch) - m_startPosRight;
                offset.y = 0f; // Y 軸回転のみ
                if (offset.magnitude > 0.001f)
                    transform.LookAt(transform.position + offset);
            }

            // --- 移動: 左 IndexTrigger 握りながらコントローラを動かす ---
            if (OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger))
            {
                m_startTransformPos = transform.position;
                m_startPosLeft = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch);
            }

            if (OVRInput.Get(OVRInput.RawButton.LIndexTrigger))
            {
                Vector3 offset = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch) - m_startPosLeft;
                transform.position = m_startTransformPos + offset;
            }

            // --- スケール縮小: A or X ---
            if (OVRInput.GetDown(OVRInput.RawButton.A) || OVRInput.GetDown(OVRInput.RawButton.X))
            {
                if (m_coord != null && m_coord.transform.localScale.magnitude > m_scaleMin)
                    m_coord.transform.localScale -= Vector3.one * m_scaleStep;
            }

            // --- スケール拡大: B or Y ---
            if (OVRInput.GetDown(OVRInput.RawButton.B) || OVRInput.GetDown(OVRInput.RawButton.Y))
            {
                if (m_coord != null)
                    m_coord.transform.localScale += Vector3.one * m_scaleStep;
            }

            // --- リセット: 左 or 右 Thumbstick クリック ---
            if (OVRInput.GetDown(OVRInput.RawButton.LThumbstick) || OVRInput.GetDown(OVRInput.RawButton.RThumbstick))
            {
                transform.position = Vector3.zero;
                transform.rotation = Quaternion.identity;
                if (m_coord != null)
                    m_coord.transform.localScale = Vector3.one;
            }
        }

        // -------------------------------------------------------

        /// <summary>
        /// コントローラのトラッキング空間座標をキャリブレーション済み座標に変換する。
        /// RosControllerPublisher.PublishPose から呼び出す。
        /// </summary>
        public Vector3 GetPosition(Vector3 trackingPosition)
        {
            return transform.InverseTransformPoint(trackingPosition);
        }

        /// <summary>
        /// コントローラのトラッキング空間回転をキャリブレーション済み回転に変換する。
        /// </summary>
        public Quaternion GetRotation(Quaternion trackingRotation)
        {
            return Quaternion.Inverse(transform.rotation) * trackingRotation;
        }
    }
}
