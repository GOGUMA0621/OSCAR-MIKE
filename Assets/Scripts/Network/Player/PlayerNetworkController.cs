using System;
using Unity.Netcode;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine;

namespace OskarMike.Network.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerNetworkController : NetworkBehaviour
    {
        public static event Action<ulong, bool> ReadyStateChangedGlobal;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private string moveActionName = "Move";

        private readonly NetworkVariable<bool> isReady = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private CharacterController characterController;
        private Camera localCamera;
#if ENABLE_INPUT_SYSTEM
        private PlayerInput playerInput;
        private InputAction moveAction;
#endif

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            localCamera = GetComponentInChildren<Camera>(true);
#if ENABLE_INPUT_SYSTEM
            playerInput = GetComponent<PlayerInput>();
#endif
        }

        public bool IsReady => isReady.Value;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            isReady.OnValueChanged += HandleReadyStateChanged;

            bool isLocalOwner = IsOwner;
            if (localCamera != null)
            {
                localCamera.gameObject.SetActive(isLocalOwner);
            }

#if ENABLE_INPUT_SYSTEM
            if (playerInput != null)
            {
                playerInput.enabled = isLocalOwner;
            }

            if (isLocalOwner)
            {
                BindInputActions();
            }
#endif

            ReadyStateChangedGlobal?.Invoke(OwnerClientId, isReady.Value);
        }

        public override void OnNetworkDespawn()
        {
            isReady.OnValueChanged -= HandleReadyStateChanged;
#if ENABLE_INPUT_SYSTEM
            moveAction = null;
#endif
            base.OnNetworkDespawn();
        }

        public void SetReady(bool ready)
        {
            if (!IsOwner)
            {
                return;
            }

            SetReadyServerRpc(ready);
        }

        [ServerRpc]
        private void SetReadyServerRpc(bool ready, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            isReady.Value = ready;
        }

        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            Vector2 moveInput = ReadMoveInput();
            Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            characterController.Move(moveDirection.normalized * (moveSpeed * Time.deltaTime));
        }

        private Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (moveAction != null)
            {
                return moveAction.ReadValue<Vector2>();
            }

            return Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void BindInputActions()
        {
            if (playerInput == null || playerInput.actions == null)
            {
                Debug.LogWarning("[PlayerNetworkController] PlayerInput or InputActionAsset is missing.");
                return;
            }

            moveAction = playerInput.actions.FindAction(moveActionName, throwIfNotFound: false);
            if (moveAction == null)
            {
                Debug.LogWarning($"[PlayerNetworkController] Move action '{moveActionName}' was not found.");
            }
        }
#endif

        private void HandleReadyStateChanged(bool _, bool newValue)
        {
            ReadyStateChangedGlobal?.Invoke(OwnerClientId, newValue);
        }
    }
}
