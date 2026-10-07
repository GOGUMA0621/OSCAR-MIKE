using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.AI
{
    [RequireComponent(typeof(PlayerNetworkController))]
    public sealed class PlayerFootstepEmitter : NetworkBehaviour
    {
        public AINoiseDefinition walk, sprint, crouch, prone;
        private PlayerNetworkController controller;
        private CharacterController body;
        private double nextStep;

        public override void OnNetworkSpawn()
        {
            controller = GetComponent<PlayerNetworkController>();
            body = GetComponent<CharacterController>();
            if (IsServer) controller.ServerMovementCompleted += OnMovement;
        }
        public override void OnNetworkDespawn()
        {
            if (controller != null) controller.ServerMovementCompleted -= OnMovement;
        }
        private void OnMovement(PlayerMoveState state, PlayerPosture posture, Vector3 delta, bool grounded)
        {
            if (!isActiveAndEnabled || !IsServer || !IsSpawned || !grounded || state == PlayerMoveState.Idle) return;
            delta.y = 0;
            if (delta.sqrMagnitude < 0.000001f || Time.timeAsDouble < nextStep) return;
            AINoiseDefinition definition = posture == PlayerPosture.Prone ? prone :
                posture == PlayerPosture.Crouch ? crouch : state == PlayerMoveState.Sprint ? sprint : walk;
            if (definition == null) return;
            nextStep = Time.timeAsDouble + Mathf.Max(0.05f, definition.interval);
            Vector3 footPosition = transform.position;
            footPosition.y = body.bounds.min.y + 0.2f;
            ServerNoiseBus.Emit(definition, footPosition, NetworkObject);
        }
    }
}
