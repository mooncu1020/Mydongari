using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using MyDongari.Core;

namespace MyDongari.Input
{
    /// <summary>
    /// 2인 대전에서 P1/P2의 PlayerInput에 장치를 고정 배정한다.
    /// PlayerInput 기본 동작(자동 스킴 전환)에 맡기면 패드 두 개가 한 플레이어에 몰리거나
    /// 버튼을 먼저 누른 쪽이 P1이 되는 식으로 흔들려서, 연결 순서대로 직접 묶는다.
    /// </summary>
    public class VersusInputPairing : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private PlayerInput player1Input;
        [SerializeField] private PlayerInput player2Input;

        [Header("설정")]
        [Tooltip("P1에 키보드·마우스도 같이 붙인다 (디버그용). 패드가 하나뿐이면 이 값과 무관하게 P1은 키보드·마우스")]
        [SerializeField] private bool allowKeyboardForP1 = true;
        [SerializeField] private bool debugLog = false;

        private void Start()
        {
            // 1인(더미) 모드에서는 P1 PlayerInput 기본 동작(자동 스킴 전환)을 그대로 쓴다
            if (GameManager.Instance != null && !GameManager.Instance.VersusMode) return;

            Apply();
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad)) return;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected)
            {
                Apply();
            }
        }

        /// <summary>
        /// 패드 2개 이상: P1=패드0, P2=패드1. 패드 1개: P2=패드, P1=키보드·마우스. 패드 0개: P1=키보드·마우스, P2 없음.
        /// </summary>
        public void Apply()
        {
            if (player1Input == null || player2Input == null) return;

            var pads = Gamepad.all;
            Gamepad p1Pad = null;
            Gamepad p2Pad = null;
            if (pads.Count >= 2)
            {
                p1Pad = pads[0];
                p2Pad = pads[1];
            }
            else if (pads.Count == 1)
            {
                p2Pad = pads[0];
            }

            var p1Devices = new List<InputDevice>();
            if (p1Pad != null) p1Devices.Add(p1Pad);
            if (allowKeyboardForP1 || p1Pad == null)
            {
                // 장치 "핸들"만 얻어 페어링하는 용도. 입력 값은 여전히 PlayerInput 액션으로만 읽는다.
                Keyboard keyboard = InputSystem.GetDevice<Keyboard>();
                Mouse mouse = InputSystem.GetDevice<Mouse>();
                if (keyboard != null) p1Devices.Add(keyboard);
                if (mouse != null) p1Devices.Add(mouse);
            }

            var p2Devices = new List<InputDevice>();
            if (p2Pad != null) p2Devices.Add(p2Pad);

            Pair(player1Input, p1Devices);
            Pair(player2Input, p2Devices);

            if (debugLog)
            {
                Debug.Log($"[VersusInputPairing] P1: {string.Join(", ", p1Devices)} / P2: {string.Join(", ", p2Devices)}");
            }
        }

        private static void Pair(PlayerInput playerInput, List<InputDevice> devices)
        {
            playerInput.neverAutoSwitchControlSchemes = true;

            InputUser user = playerInput.user;
            if (user.valid)
            {
                user.UnpairDevices();
                foreach (InputDevice device in devices)
                {
                    InputUser.PerformPairingWithDevice(device, user);
                }
            }

            // P1은 패드와 키보드를 같이 쓰므로 스킴 하나로 거르면 한쪽이 막힌다.
            // 스킴 필터를 풀고, 대신 액션이 볼 수 있는 장치를 이 플레이어 것으로만 제한한다.
            playerInput.actions.bindingMask = null;
            playerInput.actions.devices = devices.ToArray();
        }
    }
}
