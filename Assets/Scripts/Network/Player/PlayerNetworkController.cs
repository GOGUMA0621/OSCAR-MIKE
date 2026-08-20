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

        [Header("Acceleration")]
        [SerializeField] private float groundAcceleration = 28f;
        [SerializeField] private float groundDeceleration = 34f;
        [SerializeField] private float airAcceleration    = 8f;

        // ── 자세 설정 ──────────────────────────────────────
        [Header("Posture - CharacterController Heights")]
        [SerializeField] private float heightStand  = 2.0f;
        [SerializeField] private float heightCrouch = 1.2f;
        [SerializeField] private float heightProne  = 0.6f;
        [SerializeField] private float postureTransitionSpeed = 8f;

        // ── 액션 설정 ──────────────────────────────────────
        [Header("Movement Actions")]
        [SerializeField] private float sprintGraceTime = 0.25f;
        [SerializeField] private float slideDuration = 0.75f;
        [SerializeField] private float slideStartSpeedMultiplier = 1.05f;
        [SerializeField] private float diveDuration = 0.6f;
        [SerializeField] private float diveStartSpeedMultiplier = 1.1f;
        [SerializeField] private float diveRecoveryLockout = 0.35f;

        // ── 파쿠르 설정 ────────────────────────────────────
        [Header("Vault")]
        [SerializeField] private float vaultMinHeight = 0.4f;
        [SerializeField] private float vaultDuration = 0.45f;
        [SerializeField] private float vaultForwardOffset = 0.9f;
        [SerializeField] private float vaultArcHeight = 0.35f;
        [SerializeField] private float parkourMaxHeight   = 1.0f;  // 골반 높이 기준 (m)
        [SerializeField] private float parkourCheckDistance = 0.6f;
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

        private readonly NetworkVariable<PlayerActionState> netActionState = new NetworkVariable<PlayerActionState>(
            PlayerActionState.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // ── 서버 전용 상태 ──────────────────────────────────
        private float            verticalVelocity  = 0f;
        private PlayerPosture    serverPosture     = PlayerPosture.Stand;
        private PlayerMoveState  serverMoveState   = PlayerMoveState.Idle;
        private PlayerActionState serverActionState = PlayerActionState.None;
        private Vector3          horizontalVelocity = Vector3.zero;
        private Vector3          actionDirection    = Vector3.forward;
        private Vector3          vaultStartPosition = Vector3.zero;
        private Vector3          vaultEndPosition   = Vector3.zero;
        private float            actionTimer        = 0f;
        private float            actionDuration     = 0f;
        private float            actionLockoutTimer = 0f;
        private float            recentSprintTimer  = 0f;

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
        public PlayerActionState ActionState => netActionState.Value;

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
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);

            if (yawDelta != 0f)
                transform.Rotate(Vector3.up * yawDelta);

            TickActionLockout(dt);

            bool canSprintBeforePosture = CanSprint(moveInput, isSprinting, characterController.isGrounded);
            UpdateRecentSprintTimer(canSprintBeforePosture, dt);

            if (IsTimedActionActive())
            {
                serverMoveState = PlayerMoveState.Idle;
                TickStamina(dt);
                TickTimedAction(dt);
                UpdateCharacterControllerHeight(dt);
                SyncAirActionState();
                return;
            }

            if (TryStartVault(jumpPressed)
                || TryStartDive(pronePressed, moveInput)
                || TryStartSlide(crouchPressed, moveInput))
            {
                serverMoveState = PlayerMoveState.Idle;
                TickStamina(dt);
                TickTimedAction(dt);
                UpdateCharacterControllerHeight(dt);
                SyncAirActionState();
                return;
            }

            UpdatePosture(crouchPressed, pronePressed);
            UpdateMoveState(moveInput, CanSprint(moveInput, isSprinting, characterController.isGrounded));
            TickStamina(dt);
            TryJump(jumpPressed);
            ApplyGravity(dt);
            MoveNormally(moveInput, dt);
            UpdateCharacterControllerHeight(dt);
            SyncAirActionState();
        }

        private void UpdateMoveState(Vector2 moveInput, bool canSprint)
        {
            if (moveInput.sqrMagnitude < 0.01f)
                serverMoveState = PlayerMoveState.Idle;
            else if (canSprint)
                serverMoveState = PlayerMoveState.Sprint;
            else
                serverMoveState = PlayerMoveState.Walk;
        }

        private void MoveNormally(Vector2 moveInput, float dt)
        {
            Vector3 targetHorizontal = GetMoveDirection(moveInput) * GetSpeed();
            float acceleration = GetHorizontalAcceleration(targetHorizontal);
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetHorizontal, acceleration * dt);

            CollisionFlags flags = characterController.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
                verticalVelocity = -2f;
        }

        private void UpdateCharacterControllerHeight(float dt)
        {
            float targetH = PostureToHeight(serverPosture);
            if (!Mathf.Approximately(characterController.height, targetH))
            {
                characterController.height = Mathf.MoveTowards(
                    characterController.height, targetH, postureTransitionSpeed * dt);
                characterController.center = Vector3.up * (characterController.height * 0.5f);
            }
        }

        private void TryJump(bool jumpPressed)
        {
            if (!CanJump(jumpPressed)) return;
            if (stamina == null || !stamina.ServerConsumeJump()) return;

            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            SetActionState(PlayerActionState.Jumping);
        }

        private void ApplyGravity(float dt)
        {
            if (characterController.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            else
                verticalVelocity += gravity * dt;
        }

        private float GetHorizontalAcceleration(Vector3 targetHorizontal)
        {
            if (!characterController.isGrounded)
                return airAcceleration;

            return targetHorizontal.sqrMagnitude > horizontalVelocity.sqrMagnitude
                ? groundAcceleration
                : groundDeceleration;
        }

        private void TickStamina(float dt)
        {
            if (stamina != null)
                stamina.ServerTick(serverMoveState, serverPosture, false, dt);
        }

        private void TickActionLockout(float dt)
        {
            if (actionLockoutTimer > 0f)
                actionLockoutTimer = Mathf.Max(0f, actionLockoutTimer - dt);
        }

        private void UpdateRecentSprintTimer(bool canSprint, float dt)
        {
            if (canSprint)
                recentSprintTimer = sprintGraceTime;
            else
                recentSprintTimer = Mathf.Max(0f, recentSprintTimer - dt);
        }

        // ══════════════════════════════════════════════════
        // 서버 - 자세 전환
        // ══════════════════════════════════════════════════

        private void UpdatePosture(bool crouchPressed, bool pronePressed)
        {
            if (crouchPressed)
            {
                SetServerPosture(serverPosture == PlayerPosture.Crouch
                    ? PlayerPosture.Stand
                    : PlayerPosture.Crouch);
            }
            else if (pronePressed)
            {
                SetServerPosture(serverPosture == PlayerPosture.Prone
                    ? PlayerPosture.Stand
                    : PlayerPosture.Prone);
            }
        }

        private void SetServerPosture(PlayerPosture posture)
        {
            serverPosture = posture;
            if (netPosture.Value != serverPosture)
                netPosture.Value = serverPosture;
        }

        // ══════════════════════════════════════════════════
        // 서버 - 특수 이동 액션
        // ══════════════════════════════════════════════════

        private bool TryStartVault(bool jumpPressed)
        {
            if (!CanVault(jumpPressed)) return false;
            if (!TryFindVaultTarget(out Vector3 vaultTarget)) return false;
            if (stamina == null || !stamina.ServerConsumeParkour()) return false;

            SetServerPosture(PlayerPosture.Stand);
            vaultStartPosition = transform.position;
            vaultEndPosition = vaultTarget;
            verticalVelocity = 0f;
            horizontalVelocity = Vector3.zero;
            BeginTimedAction(PlayerActionState.Vaulting, vaultDuration, transform.forward);
            return true;
        }

        private bool TryStartDive(bool pronePressed, Vector2 moveInput)
        {
            if (!CanDive(pronePressed, moveInput)) return false;
            if (stamina == null || !stamina.ServerConsumeDive()) return false;

            SetServerPosture(PlayerPosture.Stand);
            verticalVelocity = -2f;
            BeginTimedAction(PlayerActionState.Diving, diveDuration, GetActionDirection(moveInput));
            return true;
        }

        private bool TryStartSlide(bool crouchPressed, Vector2 moveInput)
        {
            if (!CanSlide(crouchPressed, moveInput)) return false;
            if (stamina == null || !stamina.ServerConsumeSlide()) return false;

            SetServerPosture(PlayerPosture.Stand);
            verticalVelocity = -2f;
            BeginTimedAction(PlayerActionState.Sliding, slideDuration, GetActionDirection(moveInput));
            return true;
        }

        private bool CanSprint(Vector2 moveInput, bool sprintHeld, bool isGrounded)
        {
            return sprintHeld
                   && isGrounded
                   && serverPosture == PlayerPosture.Stand
                   && actionLockoutTimer <= 0f
                   && !IsTimedActionActive()
                   && moveInput.y > 0.1f
                   && moveInput.sqrMagnitude >= 0.01f
                   && stamina != null
                   && stamina.CanSprint;
        }

        private bool CanJump(bool jumpPressed)
        {
            return jumpPressed
                   && characterController.isGrounded
                   && serverPosture == PlayerPosture.Stand
                   && actionLockoutTimer <= 0f
                   && !IsTimedActionActive()
                   && stamina != null
                   && stamina.CanJump;
        }

        private bool CanVault(bool jumpPressed)
        {
            return jumpPressed
                   && characterController.isGrounded
                   && serverPosture == PlayerPosture.Stand
                   && actionLockoutTimer <= 0f
                   && !IsTimedActionActive()
                   && stamina != null
                   && stamina.CanVault;
        }

        private bool CanDive(bool pronePressed, Vector2 moveInput)
        {
            return pronePressed
                   && characterController.isGrounded
                   && serverPosture == PlayerPosture.Stand
                   && actionLockoutTimer <= 0f
                   && recentSprintTimer > 0f
                   && moveInput.y > 0.1f
                   && stamina != null
                   && stamina.CanDive;
        }

        private bool CanSlide(bool crouchPressed, Vector2 moveInput)
        {
            return crouchPressed
                   && characterController.isGrounded
                   && serverPosture == PlayerPosture.Stand
                   && actionLockoutTimer <= 0f
                   && recentSprintTimer > 0f
                   && moveInput.y > 0.1f
                   && stamina != null
                   && stamina.CanSlide;
        }

        private void BeginTimedAction(PlayerActionState actionState, float duration, Vector3 direction)
        {
            actionTimer = 0f;
            actionDuration = Mathf.Max(0.01f, duration);
            actionDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
            SetActionState(actionState);
        }

        private void TickTimedAction(float dt)
        {
            if (!IsTimedActionActive()) return;

            actionTimer += dt;
            float t = Mathf.Clamp01(actionTimer / actionDuration);

            switch (serverActionState)
            {
                case PlayerActionState.Sliding:
                    MoveGroundBurstAction(dt, t, speedSprintStand * slideStartSpeedMultiplier, speedWalkCrouch);
                    if (t >= 1f) EndTimedAction(PlayerPosture.Crouch);
                    break;
                case PlayerActionState.Diving:
                    MoveGroundBurstAction(dt, t, speedSprintStand * diveStartSpeedMultiplier, speedWalkProne);
                    if (t >= 1f) EndTimedAction(PlayerPosture.Prone);
                    break;
                case PlayerActionState.Vaulting:
                    MoveVaultAction(t);
                    if (t >= 1f) EndTimedAction(PlayerPosture.Stand);
                    break;
            }
        }

        private void MoveGroundBurstAction(float dt, float t, float startSpeed, float endSpeed)
        {
            ApplyGravity(dt);
            float speed = Mathf.Lerp(startSpeed, endSpeed, t);
            horizontalVelocity = actionDirection * speed;

            CollisionFlags flags = characterController.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
                verticalVelocity = -2f;
        }

        private void MoveVaultAction(float t)
        {
            Vector3 flatPosition = Vector3.Lerp(vaultStartPosition, vaultEndPosition, t);
            Vector3 arcedPosition = flatPosition + Vector3.up * (Mathf.Sin(t * Mathf.PI) * vaultArcHeight);
            Vector3 delta = arcedPosition - transform.position;

            characterController.Move(delta);
            verticalVelocity = 0f;
            horizontalVelocity = Vector3.zero;
        }

        private void EndTimedAction(PlayerPosture endPosture)
        {
            PlayerActionState endingAction = serverActionState;
            actionTimer = 0f;
            actionDuration = 0f;
            SetServerPosture(endPosture);

            if (endingAction == PlayerActionState.Diving)
                actionLockoutTimer = Mathf.Max(actionLockoutTimer, diveRecoveryLockout);

            SetActionState(characterController.isGrounded ? PlayerActionState.None : PlayerActionState.Falling);
        }

        private bool IsTimedActionActive()
        {
            return serverActionState == PlayerActionState.Vaulting
                   || serverActionState == PlayerActionState.Diving
                   || serverActionState == PlayerActionState.Sliding;
        }

        private void SyncAirActionState()
        {
            if (IsTimedActionActive()) return;

            PlayerActionState nextState = PlayerActionState.None;
            if (!characterController.isGrounded)
                nextState = verticalVelocity > 0.1f ? PlayerActionState.Jumping : PlayerActionState.Falling;

            SetActionState(nextState);
        }

        private void SetActionState(PlayerActionState actionState)
        {
            serverActionState = actionState;
            if (netActionState.Value != serverActionState)
                netActionState.Value = serverActionState;
        }

        private bool TryFindVaultTarget(out Vector3 vaultTarget)
        {
            vaultTarget = Vector3.zero;

            Vector3 forward = transform.forward;
            Vector3 basePosition = transform.position;
            Vector3 minHeightOrigin = basePosition + Vector3.up * vaultMinHeight;

            if (!Physics.Raycast(
                    minHeightOrigin,
                    forward,
                    parkourCheckDistance,
                    parkourLayerMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Vector3 maxHeightOrigin = basePosition + Vector3.up * (parkourMaxHeight + characterController.radius);
            if (Physics.Raycast(
                    maxHeightOrigin,
                    forward,
                    parkourCheckDistance,
                    parkourLayerMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Vector3 landingProbe = basePosition
                                   + forward * (parkourCheckDistance + vaultForwardOffset)
                                   + Vector3.up * (parkourMaxHeight + 0.5f);

            if (!Physics.Raycast(
                    landingProbe,
                    Vector3.down,
                    out RaycastHit groundHit,
                    parkourMaxHeight + 1.2f,
                    parkourLayerMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            float landingDelta = groundHit.point.y - basePosition.y;
            if (landingDelta > parkourMaxHeight || landingDelta < -0.5f)
                return false;

            Vector3 candidate = groundHit.point + Vector3.up * 0.02f;
            if (!HasStandingSpace(candidate))
                return false;

            vaultTarget = candidate;
            return true;
        }

        private bool HasStandingSpace(Vector3 basePosition)
        {
            float radius = Mathf.Max(0.05f, characterController.radius - characterController.skinWidth);
            Vector3 bottom = basePosition + Vector3.up * (radius + characterController.skinWidth);
            Vector3 top = basePosition + Vector3.up * (heightStand - radius);

            return !Physics.CheckCapsule(
                bottom,
                top,
                radius,
                parkourLayerMask,
                QueryTriggerInteraction.Ignore);
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

        private Vector3 GetMoveDirection(Vector2 moveInput)
        {
            Vector3 direction = transform.right * moveInput.x + transform.forward * moveInput.y;
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        private Vector3 GetActionDirection(Vector2 moveInput)
        {
            Vector3 direction = GetMoveDirection(moveInput);
            return direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
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
