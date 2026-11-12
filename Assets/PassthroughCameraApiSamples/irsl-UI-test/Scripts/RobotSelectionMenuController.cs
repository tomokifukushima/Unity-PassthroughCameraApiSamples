using TMPro;
using UnityEngine;

namespace PassthroughCameraSamples.IrslUiTest
{
    public class RobotSelectionMenuController : MonoBehaviour
    {
        [Header("Menus")]
        [SerializeField] private GameObject m_currentMenu;
        [SerializeField] private GameObject m_nextMenu;

        [Header("Optional Status Label")]
        [SerializeField] private TMP_Text m_statusLabel;

        [Header("Input Guard")]
        [SerializeField] private bool m_requireFaceButtonReleaseBeforeSelection = true;

        public string SelectedRobotName { get; private set; } = "";
        private bool m_waitingButtonRelease;

        private void OnEnable()
        {
            ArmFaceButtonReleaseGuard();
        }

        public void ArmFaceButtonReleaseGuard()
        {
            m_waitingButtonRelease = m_requireFaceButtonReleaseBeforeSelection && IsFaceButtonPressed();
        }

        private void Update()
        {
            if (!m_waitingButtonRelease)
            {
                return;
            }

            if (!IsFaceButtonPressed())
            {
                m_waitingButtonRelease = false;
            }
        }

        public void SelectAssemblerRobot1()
        {
            SelectRobot("Assembler Robot 1");
        }

        public void SelectUr5e()
        {
            SelectRobot("Ur5e");
        }

        public void SelectKohaku()
        {
            SelectRobot("kohaku");
        }

        private void SelectRobot(string robotName)
        {
            if (m_waitingButtonRelease)
            {
                return;
            }

            SelectedRobotName = robotName;
            Debug.Log($"[RobotSelection] Selected: {SelectedRobotName}");

            if (m_statusLabel != null)
            {
                m_statusLabel.text = $"Selected: {SelectedRobotName}";
            }

            if (m_currentMenu != null)
            {
                m_currentMenu.SetActive(false);
            }

            if (m_nextMenu != null)
            {
                m_nextMenu.SetActive(true);
            }
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
    }
}
