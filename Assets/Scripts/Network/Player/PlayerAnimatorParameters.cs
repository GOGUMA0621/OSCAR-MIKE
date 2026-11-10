using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Network.Player
{
    // Sends game state to Animator parameters. State selection lives entirely in the Controller.
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerNetworkController))]
    public sealed class PlayerAnimatorParameters : NetworkBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField, Min(0)] private float damping = 0.1f;
        private readonly NetworkVariable<float> horizontalSpeed = new(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private PlayerNetworkController controller;
        private PlayerHealth health;
        private double lastMovement;
        private int leanLayer;
        private float lean;
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Posture = Animator.StringToHash("Posture");
        private static readonly int Action = Animator.StringToHash("Action");
        private static readonly int Lean = Animator.StringToHash("Lean");
        private static readonly int LeanPoseTime = Animator.StringToHash("LeanPoseTime");

        private void Awake()
        {
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            leanLayer = animator != null ? animator.GetLayerIndex("Lean") : -1;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) controller.ServerMovementCompleted += OnMovement;

            lean = 0;
            WriteParameters(0f);
        }

        public override void OnNetworkDespawn()
        {
            controller.ServerMovementCompleted -= OnMovement;

        }



        private void OnMovement(PlayerMoveState state, PlayerPosture posture, Vector3 delta, bool grounded)
        {
            lastMovement = Time.timeAsDouble;
            delta.y = 0;
            float speed = delta.magnitude / Mathf.Max(Time.deltaTime, .0001f);
            // Quantization avoids sending small CharacterController contact jitter.
            horizontalSpeed.Value = Mathf.Round(Mathf.Clamp(speed, 0, 20) * 100f) / 100f;
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer && ((health != null && health.Dead) ||
                controller.ActionState != PlayerActionState.None || Time.timeAsDouble - lastMovement > .15))
                horizontalSpeed.Value = 0;
            WriteParameters(Time.deltaTime);
        }

        private void WriteParameters(float dt)
        {
            if (animator == null) return;
            bool dead = health != null && health.Dead;
            float speed = dead ? 0 : horizontalSpeed.Value;
            if (dt > 0) animator.SetFloat(Speed, speed, damping, dt);
            else animator.SetFloat(Speed, speed);
            animator.SetInteger(Posture, (int)controller.Posture);
            animator.SetInteger(Action, dead ? 0 : (int)controller.ActionState);
            lean = Mathf.MoveTowards(lean, dead ? 0 : controller.Lean, 5f * dt);
            animator.SetFloat(Lean, lean);
            animator.SetFloat(LeanPoseTime, 1f);
            if (leanLayer >= 0) animator.SetLayerWeight(leanLayer, Mathf.Abs(lean));
        }
    }
}