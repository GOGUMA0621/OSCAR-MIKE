using System;
using Unity.Netcode;
using Unity.Netcode.Components;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine;

namespace OskarMike.Network.Player
{
    /// <summary>
    /// Host-authoritative 플레이어 컨트롤러.
    ///
    /// [오너 클라이언트]
    ///   - 입력 수집 (WASD / Sprint / Crouch / Prone / Jump)
    ///   - 카메라 Pitch 로컬 처리
    ///   - 조준 흔들림 로컬 처리 (스태미나 낮음 / 질주 직후)
    ///   - MoveServerRpc 로 서버에 입력 전송
    ///
    /// [서버]
    ///   - CharacterController.Move() 실행
    ///   - 자세(Posture) / 이동상태(MoveState) 결정
    ///   - 스태미나 갱신 (PlayerStamina.ServerTick)
    ///   - 파쿠르 감지 및 처리
    ///   - NetworkTransform 이 transform 변경을 모든 클라이언트에 전파
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(PlayerStamina))]
    public class PlayerNetworkController : NetworkBehaviour
    {
        public static event Action<ulong, bool> ReadyStateChangedGlobal;

        // ── 이동 설정 ──────────────────────────────────────
        [Header("Move Speed")]
        [SerializeField] private float speedWalkStand  = 4.5f;
        [SerializeField] private float speedSprintStand = 8.0f;
        [SerializeField] private float speedWalkCrouch = 2.5f;
        [SerializeField] private float speedWalkProne  = 1.2f;

        [Header("Jump & Gravity")]
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity    = -15f;

        // ── 자세 설정 ──────────────────────────────────────
        [Header("Posture - CharacterController Heights")]
        [SerializeField] private float heightStand  = 2.0f;
        [SerializeField] private float heightCrouch = 1.2f;
        [SerializeField] private float heightProne  = 0.6f;
        [SerializeField] private float postureTransitionSpeed = 8f;

        // ── 파쿠르 설정 ────────────────────────────────────
        [Header("Parkour")]
        [SerializeField] private float parkourMaxHeight   = 1.0f;  // 골반 높이 기준 (m)
        [SerializeField] private float parkourCheckDistance = 0.6f;
        [SerializeField] private float parkourBoostSpeed  = 5.0f;
        [SerializeField] private LayerMask parkourLayerMask = ~0;

        // ── 마우스 룩 ──────────────────────────────────────
        [Header("Mouse Look")]
        [SerializeField] private float mouseSensitivity = 100f;
        [SerializeField] private Transform cameraHolder;

        // ── 조준 흔들림 ────────────────────────────────────
        [Header("Aim Sway")]
        [SerializeField] private float swayAmplitude  = 0.03f;
        [SerializeField] private float swayFrequency  = 8f;

        // ── 입력 액션 이름 ─────────────────────────────────
        [Header("Input Action Names")]
        [SerializeField] private string moveActionName   = "Move";
        [SerializeField] private string lookActionName   = "Look";
        [SerializeField] private string jumpActionName   = "Jump";
        [SerializeField] private string sprintActionName = "Sprint";
        [SerializeField] private string crouchActionName = "Crouch";
        [SerializeField] private string proneActionName  = "Prone";

        // ── NetworkVariable ────────────────────────────────
        private readonly NetworkVariable<bool> isReady = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<PlayerPosture> netPosture = new NetworkVariable<PlayerPosture>(
            PlayerPosture.Stand,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // ── 서버 전용 상태 ──────────────────────────────────
        private float            verticalVelocity  = 0f;
        private PlayerPosture    serverPosture     = PlayerPosture.Stand;
        private PlayerMoveState  serverMoveState   = PlayerMoveState.Idle;
        private float            targetCCHeight    = 2.0f;

        // ── 컴포넌트 참조 ──────────────────────────────────
        private CharacterController characterController;
        private PlayerStamina        stamina;
        private Camera               localCamera;

        // ── 오너 클라이언트 전용 ───────────────────────────
        private float localVerticalRotation = 0f;
        private float swayTimer             = 0f;
        private Vector3 cameraBaseLocalPos;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput  playerInput;
        private InputAction  moveAction;
        private InputAction  lookAction;
        private InputAction  jumpAction;
        private InputAction  sprintAction;
        private InputAction  crouchAction;
        private InputAction  proneAction;
#endif

        // ── 공개 프로퍼티 ──────────────────────────────────
        public bool          IsReady  => isReady.Value;
        public PlayerPosture Posture  => netPosture.Value;

        /// <summary>
        /// 서버 권한 이동 발판이 플레이어를 자식화하지 않고 함께 운반할 때 사용한다.
        /// </summary>
        public void ApplyServerPlatformMotion(Vector3 platformDelta)
        {
            if (!IsServer || characterController == null || !characterController.enabled)
                return;

            characterController.Move(platformDelta);
        }

        // ══════════════════════════════════════════════════
        // Unity 생명주기
        // ══════════════════════════════════════════════════

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            stamina             = GetComponent<PlayerStamina>();
            localCamera         = GetComponentInChildren<Camera>(true);
#if ENABLE_INPUT_SYSTEM
            playerInput = GetComponent<PlayerInput>();
#endif
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            isReady.OnValueChanged   += HandleReadyStateChanged;
            netPosture.OnValueChanged += HandlePostureChanged;

            if (localCamera != null)
                localCamera.gameObject.SetActive(IsOwner);

            if (IsOwner)
            {
                if (cameraHolder != null)
                    cameraBaseLocalPos = cameraHolder.localPosition;
                else if (localCamera != null)
                    cameraBaseLocalPos = localCamera.transform.localPosition;

                NetworkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
                RefreshGameplayState();
            }
            else
            {
#if ENABLE_INPUT_SYSTEM
                if (playerInput != null) playerInput.enabled = false;
#endif
            }

            ReadyStateChangedGlobal?.Invoke(OwnerClientId, isReady.Value);
        }

        public override void OnNetworkDespawn()
        {
            isReady.OnValueChanged   -= HandleReadyStateChanged;
            netPosture.OnValueChanged -= HandlePostureChanged;

            if (IsOwner)
            {
                if (NetworkManager != null && NetworkManager.SceneManager != null)
                    NetworkManager.SceneManager.OnSceneEvent -= HandleSceneEvent;
                DisableGameplayInput();
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsOwner) return;

            HandleLocalCameraPitch();
            HandleAimSway();
            SendInputToServer();
        }

        // ══════════════════════════════════════════════════
        // 오너 클라이언트 - 입력 전송
        // ══════════════════════════════════════════════════

        private void SendInputToServer()
        {
            Vector2 moveInput  = ReadMoveInput();
            Vector2 lookInput  = ReadLookInput();
            float   yawDelta   = lookInput.x * mouseSensitivity * Time.deltaTime;
            bool    jumpPressed   = ReadButtonOnce(ref jumpAction);
            bool    isSprinting   = ReadButtonHeld(sprintAction);
            bool    crouchPressed = ReadButtonOnce(ref crouchAction);
            bool    pronePressed  = ReadButtonOnce(ref proneAction);

            MoveServerRpc(moveInput, yawDelta, jumpPressed, isSprinting, crouchPressed, pronePressed);
        }

        // ══════════════════════════════════════════════════
        // 서버 - 이동 처리
        // ══════════════════════════════════════════════════

        [ServerRpc]
        private void MoveServerRpc(
            Vector2 moveInput,
            float   yawDelta,
            bool    jumpPressed,
            bool    isSprinting,
            bool    crouchPressed,
            bool    pronePressed)
        {
            float dt = Time.deltaTime;

            // 1) 자세 전환
            UpdatePosture(crouchPressed, pronePressed);

            // 2) 스프린트 가능 여부 (앉기/엎드리기 중엔 불가)
            bool canSprint = serverPosture == PlayerPosture.Stand
                             && moveInput.y > 0.1f
                             && isSprinting
                             && stamina.Stamina > 0f;

            // 3) 이동 상태 결정
            if (moveInput.sqrMagnitude < 0.01f)
                serverMoveState = PlayerMoveState.Idle;
            else if (canSprint)
                serverMoveState = PlayerMoveState.Sprint;
            else
                serverMoveState = PlayerMoveState.Walk;

            // 4) 스태미나 갱신
            stamina.ServerTick(serverMoveState, serverPosture, false, dt);

            // 5) 이동 속도
            float speed = GetSpeed();

            // 6) 수평 회전
            if (yawDelta != 0f)
                transform.Rotate(Vector3.up * yawDelta);

            // 7) 파쿠르 감지
            TryParkour(moveInput);

            // 8) 점프
            if (jumpPressed && characterController.isGrounded)
            {
                if (stamina.ServerConsumeJump())
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            // 9) 중력
            if (characterController.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            else
                verticalVelocity += gravity * dt;

            // 10) CharacterController.Move
            Vector3 horizontal = (transform.right * moveInput.x + transform.forward * moveInput.y);
            if (horizontal.sqrMagnitude > 1f) horizontal.Normalize();
            horizontal *= speed;

            Vector3 motion = horizontal + Vector3.up * verticalVelocity;
            characterController.Move(motion * dt);

            // 11) CC 높이 부드럽게 전환
            float targetH = PostureToHeight(serverPosture);
            if (!Mathf.Approximately(characterController.height, targetH))
            {
                characterController.height = Mathf.MoveTowards(
                    characterController.height, targetH, postureTransitionSpeed * dt);
                characterController.center = Vector3.up * (characterController.height * 0.5f);
            }
        }

        // ══════════════════════════════════════════════════
        // 서버 - 자세 전환
        // ══════════════════════════════════════════════════

        private void UpdatePosture(bool crouchPressed, bool pronePressed)
        {
            if (crouchPressed)
            {
                serverPosture = serverPosture == PlayerPosture.Crouch
                    ? PlayerPosture.Stand
                    : PlayerPosture.Crouch;
            }
            else if (pronePressed)
            {
                serverPosture = serverPosture == PlayerPosture.Prone
                    ? PlayerPosture.Stand
                    : PlayerPosture.Prone;
            }

            if (netPosture.Value != serverPosture)
                netPosture.Value = serverPosture;
        }

        // ══════════════════════════════════════════════════
        // 서버 - 파쿠르
        // ══════════════════════════════════════════════════

        private void TryParkour(Vector2 moveInput)
        {
            if (serverPosture != PlayerPosture.Stand) return;
            if (moveInput.sqrMagnitude < 0.01f) return;
            if (!characterController.isGrounded) return;

            Vector3 forward   = transform.forward;
            Vector3 origin    = transform.position + Vector3.up * 0.1f;

            // 앞에 낮은 장애물이 있는지 확인
            if (!Physics.Raycast(origin, forward, parkourCheckDistance, parkourLayerMask))
                return;

            // 장애물 위에 올라갈 공간이 있는지 확인
            Vector3 overOrigin = transform.position + Vector3.up * parkourMaxHeight + forward * parkourCheckDistance;
            if (Physics.Raycast(overOrigin, Vector3.down, parkourMaxHeight, parkourLayerMask))
                return; // 너무 높음

            // 파쿠르 실행
            if (stamina.ServerConsumeParkour())
            {
                verticalVelocity = parkourBoostSpeed;
            }
        }

        // ══════════════════════════════════════════════════
        // 오너 클라이언트 - 카메라 처리
        // ══════════════════════════════════════════════════

        private void HandleLocalCameraPitch()
        {
            Vector2 lookInput = ReadLookInput();
            localVerticalRotation -= lookInput.y * mouseSensitivity * Time.deltaTime;
            localVerticalRotation  = Mathf.Clamp(localVerticalRotation, -80f, 80f);

            Transform camT = cameraHolder != null ? cameraHolder : localCamera?.transform;
            if (camT != null)
                camT.localRotation = Quaternion.Euler(localVerticalRotation, 0f, 0f);
        }

        /// <summary>
        /// 스태미나 부족 / 질주 직후 조준 흔들림 (카메라 위치 진동).
        /// </summary>
        private void HandleAimSway()
        {
            Transform camT = cameraHolder != null ? cameraHolder : localCamera?.transform;
            if (camT == null) return;

            if (stamina != null && stamina.ShouldSway)
            {
                swayTimer += Time.deltaTime * swayFrequency;
                float offsetX = Mathf.Sin(swayTimer)           * swayAmplitude;
                float offsetY = Mathf.Sin(swayTimer * 0.7f)    * swayAmplitude * 0.5f;
                camT.localPosition = cameraBaseLocalPos + new Vector3(offsetX, offsetY, 0f);
            }
            else
            {
                swayTimer = 0f;
                camT.localPosition = Vector3.Lerp(camT.localPosition, cameraBaseLocalPos, Time.deltaTime * 10f);
            }
        }

        // ══════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════

        private float GetSpeed()
        {
            return serverPosture switch
            {
                PlayerPosture.Crouch => speedWalkCrouch,
                PlayerPosture.Prone  => speedWalkProne,
                _ => serverMoveState == PlayerMoveState.Sprint ? speedSprintStand : speedWalkStand
            };
        }

        private float PostureToHeight(PlayerPosture posture) => posture switch
        {
            PlayerPosture.Crouch => heightCrouch,
            PlayerPosture.Prone  => heightProne,
            _                    => heightStand
        };

        // ══════════════════════════════════════════════════
        // 게임플레이 입력 (씬 전환 시 호출)
        // ══════════════════════════════════════════════════

        public void RefreshGameplayState()
        {
            if (!IsOwner) return;

            if (IsInGameplayScene())
                EnableGameplayInput();
            else
                DisableGameplayInput();
        }

        public void EnableGameplayInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (playerInput != null) playerInput.enabled = true;
            BindInputActions();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
#endif
        }

        public void DisableGameplayInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (playerInput != null) playerInput.enabled = false;
            ClearInputActions();
#endif
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        private static bool IsInGameplayScene()
        {
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return activeScene == "GameMap" || activeScene == "Hideout";
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneEventType == SceneEventType.LoadComplete
                || sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete)
            {
                RefreshGameplayState();
            }
        }

        // ══════════════════════════════════════════════════
        // Ready 상태
        // ══════════════════════════════════════════════════

        public void SetReady(bool ready)
        {
            if (!IsOwner) return;
            SetReadyServerRpc(ready);
        }

        [ServerRpc]
        private void SetReadyServerRpc(bool ready, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            isReady.Value = ready;
        }

        // ══════════════════════════════════════════════════
        // NetworkVariable 콜백
        // ══════════════════════════════════════════════════

        private void HandleReadyStateChanged(bool _, bool newValue)
            => ReadyStateChangedGlobal?.Invoke(OwnerClientId, newValue);

        private void HandlePostureChanged(PlayerPosture _, PlayerPosture newPosture)
        {
            // 비오너 클라이언트: 시각적 자세 갱신 (애니메이션 등 추후 연동)
            // 현재는 CC 높이는 서버에서만 처리하므로 비주얼 처리 용도
        }

        // ══════════════════════════════════════════════════
        // 입력 읽기 헬퍼
        // ══════════════════════════════════════════════════

        private Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            return moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
#endif
        }

        private Vector2 ReadLookInput()
        {
#if ENABLE_INPUT_SYSTEM
            return lookAction?.ReadValue<Vector2>() ?? Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
        }

        /// 버튼 한 번 눌림 감지 후 소비 (연속 입력 방지)
        private bool ReadButtonOnce(ref InputAction action)
        {
#if ENABLE_INPUT_SYSTEM
            if (action != null && action.WasPressedThisFrame()) return true;
#endif
            return false;
        }

        private bool ReadButtonHeld(InputAction action)
        {
#if ENABLE_INPUT_SYSTEM
            return action != null && action.IsPressed();
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void BindInputActions()
        {
            if (playerInput?.actions == null)
            {
                Debug.LogWarning("[PlayerNetworkController] PlayerInput or ActionAsset missing.");
                return;
            }

            moveAction   = Bind(moveActionName);
            lookAction   = Bind(lookActionName);
            jumpAction   = Bind(jumpActionName);
            sprintAction = Bind(sprintActionName);
            crouchAction = Bind(crouchActionName);
            proneAction  = Bind(proneActionName);
        }

        private InputAction Bind(string actionName)
        {
            var a = playerInput.actions.FindAction(actionName, throwIfNotFound: false);
            if (a == null) Debug.LogWarning($"[PlayerNetworkController] Action '{actionName}' not found.");
            return a;
        }

        private void ClearInputActions()
        {
            moveAction = lookAction = jumpAction = sprintAction = crouchAction = proneAction = null;
        }
#endif
    }
}
