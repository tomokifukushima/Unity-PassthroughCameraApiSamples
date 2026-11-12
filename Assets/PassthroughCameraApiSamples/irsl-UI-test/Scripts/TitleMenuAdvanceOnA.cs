using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System.Collections.Generic;
using UnityEngine.UI;

namespace PassthroughCameraSamples.IrslUiTest
{
    public class TitleMenuAdvanceOnA : MonoBehaviour
    {
        [Header("Menus")]
        [SerializeField] private GameObject m_titleMenu;
        [SerializeField] private GameObject m_robotSelectionMenu;

        [Header("Input")]
        [SerializeField] private bool m_enableKeyboardFallback = true;
        [SerializeField] private bool m_enableRightTriggerFallback = true;
        [SerializeField] private bool m_useInputManagerFallback = true;
        [SerializeField] private bool m_verboseInputLog = true;

        [Header("State")]
        [SerializeField] private bool m_lockedAfterTransition = true;
        [SerializeField] private bool m_requireFaceButtonReleaseForRobotMenu = true;
        [SerializeField] private int m_layoutFixFramesAfterTransition = 90;
        [SerializeField] private bool m_enforceRobotLayoutContinuously = true;

        [Header("Robot Selection Layout")]
        [SerializeField] private Vector2 m_robotButtonSize = new Vector2(220f, 44f);
        [SerializeField] private float m_robotButtonSpacing = 52f;

        private bool m_hasTransitioned;
        private bool m_waitingFaceButtonReleaseForRobotMenu;
        private int m_layoutFixFramesRemaining;
        private bool m_prevPrimaryPressed;
        private bool m_prevTriggerPressed;
        private float m_nextInputLogTime;

        private void Start()
        {
            Debug.Log("[UIFlow] TitleMenuAdvanceOnA ready.");
            DebugDumpState();
        }

        private void OnEnable()
        {
            // Reset on each enable to avoid stale state when domain reload is disabled.
            m_hasTransitioned = false;
            ResolveMenusIfNeeded();
            DebugDumpState();
        }

        private void Update()
        {
            HandlePostTransitionState();

            if (m_lockedAfterTransition && m_hasTransitioned)
            {
                return;
            }

            bool curPrimaryPressed = OVRInput.Get(OVRInput.Button.One) ||
                                     OVRInput.Get(OVRInput.RawButton.A) ||
                                     OVRInput.Get(OVRInput.RawButton.A, OVRInput.Controller.RTouch);
            bool pressedAButton = curPrimaryPressed && !m_prevPrimaryPressed;
            m_prevPrimaryPressed = curPrimaryPressed;

            bool curTriggerPressed = m_enableRightTriggerFallback &&
                                     (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger) ||
                                      OVRInput.Get(OVRInput.RawButton.RIndexTrigger) ||
                                      OVRInput.Get(OVRInput.RawButton.RIndexTrigger, OVRInput.Controller.RTouch));
            bool pressedRightTrigger = curTriggerPressed && !m_prevTriggerPressed;
            m_prevTriggerPressed = curTriggerPressed;

            bool pressedByInputManager = false;
            if (m_useInputManagerFallback)
            {
                try
                {
                    pressedByInputManager = InputManager.IsButtonADownOrPinchStarted();
                }
                catch
                {
                    // If InputManager is not initialized yet, just ignore this fallback for the frame.
                }
            }

            bool pressedKeyboardFallback = m_enableKeyboardFallback && IsKeyboardAdvancePressed();

            if (m_verboseInputLog && Time.unscaledTime >= m_nextInputLogTime)
            {
                m_nextInputLogTime = Time.unscaledTime + 2f;
                Debug.Log($"[UIFlow] InputWatch activeController={OVRInput.GetActiveController()} A={curPrimaryPressed} Trigger={curTriggerPressed}");
            }

            if (!pressedAButton && !pressedRightTrigger && !pressedByInputManager && !pressedKeyboardFallback)
            {
                return;
            }

            Debug.Log($"[UIFlow] A detected. title={(m_titleMenu != null ? m_titleMenu.name : "null")} robot={(m_robotSelectionMenu != null ? m_robotSelectionMenu.name : "null")}");

            TransitionToRobotMenu();
        }

        [ContextMenu("Debug Dump State")]
        public void DebugDumpState()
        {
            Debug.Log($"[UIFlow] Dump title={(m_titleMenu != null ? m_titleMenu.name : "null")} active={(m_titleMenu != null && m_titleMenu.activeSelf)} " +
                      $"robot={(m_robotSelectionMenu != null ? m_robotSelectionMenu.name : "null")} active={(m_robotSelectionMenu != null && m_robotSelectionMenu.activeSelf)}");
        }

        [ContextMenu("Debug Transition To Robot Menu")]
        public void TransitionToRobotMenu()
        {
            ResolveMenusIfNeeded();

            DisableRobotMenuAutoHideIfPresent();

            if (m_titleMenu != null)
            {
                m_titleMenu.SetActive(false);
            }
            else
            {
                Debug.LogWarning("[UIFlow] Title menu is null.");
            }

            if (m_robotSelectionMenu != null)
            {
                m_robotSelectionMenu.SetActive(true);
                ForceRobotMenuVisible();
                m_layoutFixFramesRemaining = Mathf.Max(0, m_layoutFixFramesAfterTransition);

                m_waitingFaceButtonReleaseForRobotMenu = m_requireFaceButtonReleaseForRobotMenu && IsFaceButtonPressed();
                if (m_waitingFaceButtonReleaseForRobotMenu)
                {
                    SetRobotMenuInteractionEnabled(false);
                }

                var selectionController = FindObjectOfType<RobotSelectionMenuController>();
                if (selectionController != null)
                {
                    selectionController.ArmFaceButtonReleaseGuard();
                }

                // If a legacy component hid it during first activation, force active one more time.
                if (!m_robotSelectionMenu.activeSelf)
                {
                    m_robotSelectionMenu.SetActive(true);
                }
            }
            else
            {
                Debug.LogWarning("[UIFlow] Robot selection menu is null.");
            }

            m_hasTransitioned = true;
            Debug.Log("[UIFlow] Title -> RobotSelection");
            DebugDumpState();
        }

        private void HandlePostTransitionState()
        {
            if (!m_hasTransitioned || m_robotSelectionMenu == null)
            {
                return;
            }

            if (m_enforceRobotLayoutContinuously)
            {
                ForceRobotMenuVisible();
            }

            if (m_layoutFixFramesRemaining > 0)
            {
                ForceRobotMenuVisible();
                m_layoutFixFramesRemaining--;
            }

            if (!m_waitingFaceButtonReleaseForRobotMenu)
            {
                return;
            }

            if (IsFaceButtonPressed())
            {
                SetRobotMenuInteractionEnabled(false);
                return;
            }

            m_waitingFaceButtonReleaseForRobotMenu = false;
            SetRobotMenuInteractionEnabled(true);
        }

        private void ResolveMenusIfNeeded()
        {
            if (m_titleMenu == null)
            {
                var title = GameObject.Find("TitleMenuRoot");
                if (title != null)
                {
                    m_titleMenu = title;
                }
            }

            if (m_robotSelectionMenu == null)
            {
                var robotMenu = GameObject.Find("DebugUITestCanvas");
                if (robotMenu != null)
                {
                    m_robotSelectionMenu = robotMenu;
                }
            }
        }

        private static bool IsKeyboardAdvancePressed()
        {
    #if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null &&
               (keyboard.aKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame ||
                keyboard.enterKey.wasPressedThisFrame);
    #elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.A) ||
               Input.GetKeyDown(KeyCode.Space) ||
               Input.GetKeyDown(KeyCode.Return);
    #else
            return false;
    #endif
        }

        private static bool IsFaceButtonPressed()
        {
            return OVRInput.Get(OVRInput.Button.One) ||
                   OVRInput.Get(OVRInput.RawButton.A) ||
                   OVRInput.Get(OVRInput.RawButton.A, OVRInput.Controller.RTouch) ||
                   OVRInput.Get(OVRInput.Button.Two) ||
                   OVRInput.Get(OVRInput.RawButton.B) ||
                   OVRInput.Get(OVRInput.RawButton.B, OVRInput.Controller.RTouch) ||
                   OVRInput.Get(OVRInput.RawButton.X) ||
                   OVRInput.Get(OVRInput.RawButton.X, OVRInput.Controller.LTouch) ||
                   OVRInput.Get(OVRInput.RawButton.Y) ||
                   OVRInput.Get(OVRInput.RawButton.Y, OVRInput.Controller.LTouch);
        }

        private void SetRobotMenuInteractionEnabled(bool enabled)
        {
            if (m_robotSelectionMenu == null)
            {
                return;
            }

            foreach (var raycaster in m_robotSelectionMenu.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                raycaster.enabled = enabled;
            }

            foreach (var button in m_robotSelectionMenu.GetComponentsInChildren<Button>(true))
            {
                button.interactable = enabled;
            }
        }

        private void ForceRobotMenuVisible()
        {
            if (m_robotSelectionMenu == null)
            {
                return;
            }

            // Keep menu in front of user's head.
            m_robotSelectionMenu.transform.localPosition = new Vector3(0f, 0f, 1f);
            m_robotSelectionMenu.transform.localRotation = Quaternion.identity;
            m_robotSelectionMenu.transform.localScale = new Vector3(0.003f, 0.003f, 0.003f);

            // Ensure rendering components are enabled.
            foreach (var canvas in m_robotSelectionMenu.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = true;
            }
            foreach (var scaler in m_robotSelectionMenu.GetComponentsInChildren<CanvasScaler>(true))
            {
                scaler.enabled = true;
            }
            foreach (var raycaster in m_robotSelectionMenu.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                raycaster.enabled = true;
            }

            // This sample component auto-hides itself in Awake, so keep it off.
            DisableRobotMenuAutoHideIfPresent();

            // Normalize robot-selection button positions and size.
            var buttonRects = CollectRobotButtonRects();
            if (buttonRects.Count == 0)
            {
                return;
            }

            float mid = (buttonRects.Count - 1) * 0.5f;
            for (int i = 0; i < buttonRects.Count; i++)
            {
                var rt = buttonRects[i];
                if (rt == null) continue;

                rt.gameObject.SetActive(true);
                rt.localPosition = Vector3.zero;
                rt.localScale = Vector3.one;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, (mid - i) * m_robotButtonSpacing);
                rt.sizeDelta = m_robotButtonSize;

                if (!rt.TryGetComponent<LayoutElement>(out var layoutElement))
                {
                    layoutElement = rt.gameObject.AddComponent<LayoutElement>();
                }

                layoutElement.minWidth = m_robotButtonSize.x;
                layoutElement.minHeight = m_robotButtonSize.y;
                layoutElement.preferredWidth = m_robotButtonSize.x;
                layoutElement.preferredHeight = m_robotButtonSize.y;
                layoutElement.flexibleWidth = 0f;
                layoutElement.flexibleHeight = 0f;
            }
        }

        private List<RectTransform> CollectRobotButtonRects()
        {
            var result = new List<RectTransform>(3);

            // Preferred order by expected names.
            string[] buttonNames = { "Button_AssemblerRobot1", "Button_Ur5e", "Button_Kohaku" };
            for (int i = 0; i < buttonNames.Length; i++)
            {
                var t = FindChildRecursive(m_robotSelectionMenu.transform, buttonNames[i]);
                if (t is RectTransform rt)
                {
                    result.Add(rt);
                }
            }

            if (result.Count > 0)
            {
                return result;
            }

            // Fallback: any interactable Button in this menu.
            var buttons = m_robotSelectionMenu.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].TryGetComponent<RectTransform>(out var rt))
                {
                    result.Add(rt);
                }
            }

            return result;
        }

        private static Transform FindChildRecursive(Transform root, string objectName)
        {
            if (root.name == objectName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void DisableRobotMenuAutoHideIfPresent()
        {
            if (m_robotSelectionMenu == null)
            {
                return;
            }

            var debugBuilders = m_robotSelectionMenu.GetComponentsInChildren<Behaviour>(true);
            bool disabledAny = false;
            for (int i = 0; i < debugBuilders.Length; i++)
            {
                var behaviour = debugBuilders[i];
                if (behaviour == null) continue;
                if (behaviour.GetType().Name != "DebugUIBuilder") continue;
                if (!behaviour.enabled) continue;

                behaviour.enabled = false;
                disabledAny = true;
            }

            if (disabledAny)
            {
                Debug.Log("[UIFlow] Disabled DebugUIBuilder auto-hide on robot menu.");
            }
        }
    }
}
