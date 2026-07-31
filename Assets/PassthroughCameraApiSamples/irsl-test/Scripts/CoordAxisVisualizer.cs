// Copyright (c) Meta Platforms, Inc. and affiliates.

using UnityEngine;

namespace PassthroughCameraSamples.IrslTest
{
    /// <summary>
    /// XYZ 座標軸を Unity プリミティブで自動生成して表示する。
    /// Calibration.m_coord にアタッチする GameObject に付ける。
    ///
    ///   X 軸 … 赤  (+X 方向)
    ///   Y 軸 … 緑  (+Y 方向)
    ///   Z 軸 … 青  (+Z 方向)
    /// </summary>
    public class CoordAxisVisualizer : MonoBehaviour
    {
        [Tooltip("軸の長さ (m)")]
        [SerializeField] private float m_axisLength = 0.3f;

        [Tooltip("軸の太さ (m)")]
        [SerializeField] private float m_axisRadius = 0.01f;

        [Tooltip("先端球の半径 (m)")]
        [SerializeField] private float m_tipRadius = 0.025f;

        private void Awake()
        {
            CreateAxis(Vector3.right,   Color.red,   "X");
            CreateAxis(Vector3.up,      Color.green, "Y");
            CreateAxis(Vector3.forward, Color.blue,  "Z");
            CreateOriginSphere();
        }

        private void CreateAxis(Vector3 direction, Color color, string label)
        {
            // --- シャフト (Cylinder) ---
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = $"Axis_{label}_Shaft";
            shaft.transform.SetParent(transform, false);

            // Unity の Cylinder は Y 軸方向に伸びるので回転で向きを変える
            shaft.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            shaft.transform.localPosition = direction * (m_axisLength * 0.5f);
            shaft.transform.localScale    = new Vector3(m_axisRadius * 2f, m_axisLength * 0.5f, m_axisRadius * 2f);

            SetColor(shaft, color);
            RemoveCollider(shaft);

            // --- 先端球 (Sphere) ---
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = $"Axis_{label}_Tip";
            tip.transform.SetParent(transform, false);
            tip.transform.localPosition = direction * m_axisLength;
            tip.transform.localScale    = Vector3.one * m_tipRadius * 2f;

            SetColor(tip, color * 1.5f); // 先端を少し明るく
            RemoveCollider(tip);
        }

        private void CreateOriginSphere()
        {
            var origin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            origin.name = "Axis_Origin";
            origin.transform.SetParent(transform, false);
            origin.transform.localPosition = Vector3.zero;
            origin.transform.localScale    = Vector3.one * m_tipRadius * 2.5f;

            SetColor(origin, Color.white);
            RemoveCollider(origin);
        }

        private static void SetColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            // URP / Legacy どちらでも機能するようにシンプルに設定
            var mat = new Material(renderer.sharedMaterial)
            {
                color = color
            };
            renderer.material = mat;
        }

        private static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
    }
}
