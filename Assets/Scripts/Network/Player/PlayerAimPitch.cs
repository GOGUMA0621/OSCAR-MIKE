using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Network.Player
{
    /// <summary>
    /// 상하 조준(카메라 pitch) 네트워크 동기화.
    /// - 오너: 카메라의 실제 상하 각도를 읽어, 바뀌었을 때만 최대 sendRate 로 SetPitchServerRpc 전송.
    /// - 서버: 범위 제한 후 netPitch(0.1° 단위 short, WritePermission.Server)에 기록.
    /// - 원격 피어: 수신 값을 부드럽게 보간. 오너는 로컬 값을 바로 쓴다.
    /// 몸에 반영하는 것은 PlayerWeaponAimPose (실행 순서 -40) 가 Pitch 를 읽어서 한다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerAimPitch : NetworkBehaviour
    {
        [Tooltip("비우면 플레이어 하위 카메라")]
        [SerializeField] private Transform aimCamera;
        [Tooltip("초당 최대 전송 횟수 (각도가 바뀔 때만 전송)")]
        [Min(1f)] [SerializeField] private float sendRate = 15f;
        [Tooltip("이 이상 바뀌어야 전송 (도)")]
        [Min(0.05f)] [SerializeField] private float sendThreshold = 0.3f;
        [Tooltip("원격 피어 보간 속도 (클수록 빠르게 따라감)")]
        [Min(1f)] [SerializeField] private float remoteSmoothing = 15f;

        private const float Unit = 0.1f; // netPitch 1 = 0.1°

        private readonly NetworkVariable<short> netPitch = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float pitch;
        private short lastSent;
        private float nextSendTime;

        /// <summary>현재 표시되는 상하 조준 각도 (위 = +, 도). 오너는 로컬 카메라 값, 원격은 보간된 동기화 값.</summary>
        public float Pitch => pitch;

        private void Awake()
        {
            if (aimCamera == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) aimCamera = cam.transform;
            }
        }

        public override void OnNetworkSpawn()
        {
            pitch = netPitch.Value * Unit;
        }

        private void LateUpdate()
        {
            if (!IsSpawned) return;
            if (IsOwner) UpdateOwnerPitch();
            else pitch = Mathf.Lerp(pitch, netPitch.Value * Unit, 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime));
        }

        private void UpdateOwnerPitch()
        {
            if (aimCamera == null) return;
            // 카메라 정면의 수평면 대비 각도. 몸(루트)은 기울지 않으므로 그대로 상하 조준 각도다.
            pitch = Mathf.Asin(Mathf.Clamp(aimCamera.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

            short quantized = Quantize(pitch);
            if (Mathf.Abs(quantized - lastSent) * Unit < sendThreshold || Time.unscaledTime < nextSendTime) return;
            lastSent = quantized;
            nextSendTime = Time.unscaledTime + 1f / sendRate;
            SetPitchServerRpc(quantized);
        }

        [ServerRpc]
        private void SetPitchServerRpc(short value)
        {
            netPitch.Value = Quantize(value * Unit);
        }

        private static short Quantize(float degrees) =>
            (short)Mathf.RoundToInt(Mathf.Clamp(degrees, -89f, 89f) / Unit);
    }
}
