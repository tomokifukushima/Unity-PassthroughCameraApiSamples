// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using UnityEngine;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// 3点対応によるコントローラーとロボット座標系のキャリブレーション。
    ///
    /// 原理:
    ///   ロボット座標系上で位置が既知の3点に右コントローラーを順番に当て、
    ///   トラッキング空間での座標を計測する。3点対応から直交フレームを構築し、
    ///   回転行列 R = F * E^T と並進 T = r_c - R * t_c を求める。
    ///
    /// 使い方:
    ///   1. Inspector の Robot Point 0/1/2 にロボット座標系上の3点の位置(m)を入力する
    ///      ※ 3点は同一直線上にないこと (例: 原点・X方向30cm・Z方向30cm)
    ///   2. CalibrationManager で ThreePoint モードに切り替える (Y+B×2回)
    ///   3. 右コントローラーを基準点0に当てて A を押す
    ///   4. 基準点1、2も同様に A を押す
    ///   5. 自動的にキャリブレーション完了
    ///   6. やり直す場合は X ボタン
    /// </summary>
    public class ThreePointCalibration : MonoBehaviour
    {
        public enum State { Idle, Point0, Point1, Point2, Done }

        [Header("Robot Frame Reference Points (meters)")]
        [Tooltip("ロボット座標系での基準点0 (例: ロボット原点)")]
        [SerializeField] private Vector3 m_robotPoint0 = new Vector3(0f, 0f, 0f);
        [Tooltip("ロボット座標系での基準点1 (例: X方向 30cm)")]
        [SerializeField] private Vector3 m_robotPoint1 = new Vector3(0.3f, 0f, 0f);
        [Tooltip("ロボット座標系での基準点2 (例: Z方向 30cm)")]
        [SerializeField] private Vector3 m_robotPoint2 = new Vector3(0f, 0f, 0.3f);

        [Header("Status (ReadOnly)")]
        [SerializeField] private State m_state = State.Idle;
        [SerializeField] private string m_statusText = "Idle";

        private readonly Vector3[] m_trackingPoints = new Vector3[3];
        private Matrix4x4 m_trackingToRobot = Matrix4x4.identity;
        private readonly GameObject[] m_indicators = new GameObject[3];
        private bool m_buttonCooldown = false;

        // -------------------------------------------------------

        /// <summary>キャリブレーション済みかどうか</summary>
        public bool IsCalibrated => m_state == State.Done;

        /// <summary>現在点を収集中かどうか (true のとき ROS 送信を抑制)</summary>
        public bool IsCollecting =>
            m_state == State.Point0 || m_state == State.Point1 || m_state == State.Point2;

        /// <summary>ステータス文字列 (CalibrationManager に表示させる)</summary>
        public string StatusText => m_statusText;

        // -------------------------------------------------------

        /// <summary>CalibrationManager からモード切り替え時に呼ぶ</summary>
        public void StartCalibration()
        {
            m_state = State.Point0;
            ClearIndicators();
            m_statusText = BuildPrompt(0);
            Debug.Log("[ThreePointCalib] Started");
        }

        /// <summary>CalibrationManager から毎フレーム呼ぶ (入力ハンドリング)</summary>
        public void HandleInput()
        {
            if (m_buttonCooldown) return;

            // A: 現在のコントローラー位置を記録 (収集中のみ)
            if (IsCollecting && OVRInput.GetDown(OVRInput.RawButton.A))
            {
                RecordCurrentPoint();
                StartCoroutine(ButtonCooldown());
                return;
            }

            // X: やり直し
            if (OVRInput.GetDown(OVRInput.RawButton.X))
            {
                StartCalibration();
                StartCoroutine(ButtonCooldown());
            }
        }

        // -------------------------------------------------------

        private void RecordCurrentPoint()
        {
            int idx = (int)m_state; // Point0=1, Point1=2, Point2=3 → 0-index: idx-1
            // State enum: Idle=0, Point0=1, Point1=2, Point2=3, Done=4
            int pointIdx = (int)m_state - 1;

            m_trackingPoints[pointIdx] = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
            PlaceIndicator(pointIdx, m_trackingPoints[pointIdx]);
            Debug.Log($"[ThreePointCalib] Point[{pointIdx}] recorded: {m_trackingPoints[pointIdx]}");

            if (m_state == State.Point2)
                Compute();
            else
            {
                m_state = (State)((int)m_state + 1);
                m_statusText = BuildPrompt(pointIdx + 1);
            }
        }

        private void Compute()
        {
            var rp = new[] { m_robotPoint0, m_robotPoint1, m_robotPoint2 };
            var tp = m_trackingPoints;

            // --- 直線チェック ---
            var rawCross = Vector3.Cross(tp[1] - tp[0], tp[2] - tp[0]);
            if (rawCross.magnitude < 0.005f)
            {
                m_statusText = "ERROR: 3点が直線上にあります。X でやり直してください。";
                Debug.LogWarning("[ThreePointCalib] Collinear points detected!");
                m_state = State.Point0;
                ClearIndicators();
                return;
            }

            // --- トラッキング空間の直交フレーム構築 ---
            var te1 = (tp[1] - tp[0]).normalized;
            var te3 = Vector3.Cross(te1, tp[2] - tp[0]).normalized;
            var te2 = Vector3.Cross(te3, te1).normalized;

            // --- ロボット空間の直交フレーム構築 ---
            var re1 = (rp[1] - rp[0]).normalized;
            var re3 = Vector3.Cross(re1, rp[2] - rp[0]).normalized;
            var re2 = Vector3.Cross(re3, re1).normalized;

            // --- 回転行列 R = F * E^T (R[i,j] = Σ_k re_k[i] * te_k[j]) ---
            var R = new Matrix4x4();
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    R[i, j] = re1[i] * te1[j] + re2[i] * te2[j] + re3[i] * te3[j];
            R[3, 3] = 1f;

            // --- 並進 T (重心ベース) ---
            var rc = (rp[0] + rp[1] + rp[2]) / 3f;
            var tc = (tp[0] + tp[1] + tp[2]) / 3f;
            var Rtc = new Vector3(
                R[0, 0] * tc.x + R[0, 1] * tc.y + R[0, 2] * tc.z,
                R[1, 0] * tc.x + R[1, 1] * tc.y + R[1, 2] * tc.z,
                R[2, 0] * tc.x + R[2, 1] * tc.y + R[2, 2] * tc.z);
            var T = rc - Rtc;

            // --- 最終変換行列 TRS ---
            m_trackingToRobot = Matrix4x4.TRS(T, R.rotation, Vector3.one);

            // --- 誤差検証 (デバッグ用) ---
            float maxErr = 0f;
            for (int i = 0; i < 3; i++)
            {
                var predicted = m_trackingToRobot.MultiplyPoint3x4(tp[i]);
                maxErr = Mathf.Max(maxErr, Vector3.Distance(predicted, rp[i]));
            }

            m_state = State.Done;
            m_statusText = $"完了! 残差={maxErr * 1000f:F1}mm | X でやり直し";
            Debug.Log($"[ThreePointCalib] Done. T={T:F4} Rot={R.rotation.eulerAngles:F2} maxErr={maxErr * 1000f:F2}mm");
        }

        // -------------------------------------------------------

        private string BuildPrompt(int idx)
        {
            var robotPts = new[] { m_robotPoint0, m_robotPoint1, m_robotPoint2 };
            return $"基準点[{idx}] ({robotPts[idx]:F2}m) に\n右コントローラーを当てて A を押す\n(X でやり直し)";
        }

        private void PlaceIndicator(int idx, Vector3 worldPos)
        {
            if (m_indicators[idx] != null) Destroy(m_indicators[idx]);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"ThreePoint_Indicator_{idx}";
            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * 0.025f;
            Destroy(go.GetComponent<Collider>());

            var colors = new[] { Color.red, Color.green, Color.blue };
            var mat = new Material(Shader.Find("Standard")) { color = colors[idx] };
            go.GetComponent<Renderer>().material = mat;
            m_indicators[idx] = go;
        }

        private void ClearIndicators()
        {
            for (int i = 0; i < 3; i++)
            {
                if (m_indicators[i] != null) Destroy(m_indicators[i]);
                m_indicators[i] = null;
            }
        }

        private IEnumerator ButtonCooldown()
        {
            m_buttonCooldown = true;
            yield return new WaitForSeconds(0.4f);
            m_buttonCooldown = false;
        }

        private void OnDestroy() => ClearIndicators();

        // -------------------------------------------------------
        // CalibrationManager から呼ばれる座標変換 API

        /// <summary>トラッキング空間の座標をロボット座標系に変換する</summary>
        public Vector3 GetPosition(Vector3 trackingPos)
            => m_trackingToRobot.MultiplyPoint3x4(trackingPos);

        /// <summary>トラッキング空間の回転をロボット座標系に変換する</summary>
        public Quaternion GetRotation(Quaternion trackingRot)
            => m_trackingToRobot.rotation * trackingRot;

        /// <summary>位置・回転をまとめてロボット座標系に変換する</summary>
        public Pose TransformToRobot(Vector3 pos, Quaternion rot)
            => new Pose(GetPosition(pos), GetRotation(rot));
    }
}
