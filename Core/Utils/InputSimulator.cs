using System;
using System.Runtime.InteropServices;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace MHWildsPathfindingBot.Core.Utils
{
    /// <summary>
    /// Provides methods for simulating XInput controller input
    /// Streamlined version without keyboard support
    /// </summary>
    public static class InputSimulator
    {
        #region XInput Detection
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [DllImport("XInput1_4.dll", CharSet = CharSet.Auto, EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);
        #endregion

        // XInput constants
        private const short XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE = 7849;
        private const short XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE = 8689;
        private const short XINPUT_THUMB_MAX = 32767;

        // ViGEm variables
        private static ViGEmClient vigemClient;
        private static IXbox360Controller virtualController;
        private static bool vigemInitialized = false;

        // Controller state tracking
        private static short currentLeftThumbX = 0;
        private static short currentLeftThumbY = 0;
        private static short currentRightThumbX = 0;
        private static short currentRightThumbY = 0;

        /// <summary>
        /// Initialize ViGEm system
        /// </summary>
        private static void InitializeViGEm()
        {
            if (!vigemInitialized)
            {
                try
                {
                    // Create ViGEm client
                    vigemClient = new ViGEmClient();

                    // Create Xbox 360 controller
                    virtualController = vigemClient.CreateXbox360Controller();

                    // Connect the controller
                    virtualController.Connect();

                    vigemInitialized = true;

                    // Reset controller to default state
                    ResetControllerState();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize ViGEm: {ex.Message}");
                    vigemInitialized = false;
                    throw new InvalidOperationException("Failed to initialize virtual controller. Make sure ViGEmBus is installed.", ex);
                }
            }
        }

        /// <summary>
        /// Checks if a physical controller is connected
        /// </summary>
        public static bool IsControllerConnected()
        {
            XINPUT_STATE state = new XINPUT_STATE();
            return XInputGetState(0, ref state) == 0; // ERROR_SUCCESS = 0
        }

        /// <summary>
        /// Ensures the virtual controller is initialized
        /// </summary>
        public static void EnsureControllerInitialized()
        {
            if (!vigemInitialized)
            {
                InitializeViGEm();
            }
        }

        /// <summary>
        /// Sets the left thumbstick position with full 360-degree control
        /// X: -1.0 (left) to 1.0 (right)
        /// Z: -1.0 (up/forward) to 1.0 (down/backward)
        /// </summary>
        public static void SetLeftThumbstick(float x, float z)
        {
            EnsureControllerInitialized();

            // Convert float values (-1.0 to 1.0) to XInput range (-32768 to 32767)
            currentLeftThumbX = (short)(x * XINPUT_THUMB_MAX);

            // Note: In XInput, pushing up is a negative Y value
            // Our Z represents forward(-)/backward(+), so we negate it
            currentLeftThumbY = (short)(-z * XINPUT_THUMB_MAX);

            // Apply deadzone
            if (Math.Abs(currentLeftThumbX) < XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
                currentLeftThumbX = 0;
            if (Math.Abs(currentLeftThumbY) < XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
                currentLeftThumbY = 0;

            // Send to virtual controller
            virtualController.SetAxisValue(Xbox360Axis.LeftThumbX, currentLeftThumbX);
            virtualController.SetAxisValue(Xbox360Axis.LeftThumbY, currentLeftThumbY);
        }

        /// <summary>
        /// Sets the right thumbstick position (values from -1.0 to 1.0)
        /// </summary>
        public static void SetRightThumbstick(float x, float y)
        {
            EnsureControllerInitialized();

            currentRightThumbX = (short)(x * XINPUT_THUMB_MAX);
            currentRightThumbY = (short)(y * XINPUT_THUMB_MAX);

            // Apply deadzone
            if (Math.Abs(currentRightThumbX) < XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)
                currentRightThumbX = 0;
            if (Math.Abs(currentRightThumbY) < XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)
                currentRightThumbY = 0;

            virtualController.SetAxisValue(Xbox360Axis.RightThumbX, currentRightThumbX);
            virtualController.SetAxisValue(Xbox360Axis.RightThumbY, currentRightThumbY);
        }

        /// <summary>
        /// Sends a controller button press or release
        /// </summary>
        public static void SendControllerButton(Xbox360Button button, bool pressed)
        {
            EnsureControllerInitialized();
            virtualController.SetButtonState(button, pressed);
        }

        /// <summary>
        /// Sets a trigger value (0-255)
        /// </summary>
        public static void SetTriggerValue(Xbox360Slider trigger, byte value)
        {
            EnsureControllerInitialized();
            virtualController.SetSliderValue(trigger, value);
        }

        /// <summary>
        /// Resets the controller state (releases all inputs)
        /// </summary>
        public static void ResetControllerState()
        {
            if (!vigemInitialized) return;

            // Reset thumbsticks to center position
            virtualController.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            virtualController.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            virtualController.SetAxisValue(Xbox360Axis.RightThumbX, 0);
            virtualController.SetAxisValue(Xbox360Axis.RightThumbY, 0);

            // Reset all buttons
            virtualController.SetButtonState(Xbox360Button.A, false);
            virtualController.SetButtonState(Xbox360Button.B, false);
            virtualController.SetButtonState(Xbox360Button.X, false);
            virtualController.SetButtonState(Xbox360Button.Y, false);
            virtualController.SetButtonState(Xbox360Button.LeftShoulder, false);
            virtualController.SetButtonState(Xbox360Button.RightShoulder, false);
            virtualController.SetButtonState(Xbox360Button.LeftThumb, false);
            virtualController.SetButtonState(Xbox360Button.RightThumb, false);
            virtualController.SetButtonState(Xbox360Button.Start, false);
            virtualController.SetButtonState(Xbox360Button.Back, false);
            virtualController.SetButtonState(Xbox360Button.Guide, false);

            // Reset triggers
            virtualController.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
            virtualController.SetSliderValue(Xbox360Slider.RightTrigger, 0);

            // Reset D-pad
            virtualController.SetButtonState(Xbox360Button.Up, false);
            virtualController.SetButtonState(Xbox360Button.Down, false);
            virtualController.SetButtonState(Xbox360Button.Left, false);
            virtualController.SetButtonState(Xbox360Button.Right, false);

            // Reset tracking variables
            currentLeftThumbX = 0;
            currentLeftThumbY = 0;
            currentRightThumbX = 0;
            currentRightThumbY = 0;
        }

        /// <summary>
        /// Clean up ViGEm resources
        /// </summary>
        public static void Cleanup()
        {
            if (vigemInitialized)
            {
                try
                {
                    // Disconnect the controller
                    if (virtualController != null)
                    {
                        virtualController.Disconnect();
                        virtualController = null;
                    }

                    // Dispose ViGEm client
                    if (vigemClient != null)
                    {
                        vigemClient.Dispose();
                        vigemClient = null;
                    }

                    vigemInitialized = false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cleaning up ViGEm: {ex.Message}");
                }
            }
        }
    }
}