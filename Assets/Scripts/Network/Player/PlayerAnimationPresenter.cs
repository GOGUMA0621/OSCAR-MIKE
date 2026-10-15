using UnityEngine;

namespace OskarMike.Network.Player
{
    /// <summary>Presentation of server state; never moves the gameplay root.</summary>
    [RequireComponent(typeof(PlayerNetworkController))]
    public sealed class PlayerAnimationPresenter : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        private PlayerNetworkController controller;
        private Renderer[] visuals;
        private int[] originalLayers;
        private Camera ownerCamera;
        private int localVisualLayer;
        private string currentState;
        private PlayerPosture previousPosture;
        private float transitionUntil;
        private float displayedLean;

        private void Awake()
        {
            controller = GetComponent<PlayerNetworkController>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                visuals = animator.GetComponentsInChildren<Renderer>(true);
                originalLayers = new int[visuals.Length];
                for (int i = 0; i < visuals.Length; i++) originalLayers[i] = visuals[i].gameObject.layer;
                ownerCamera = GetComponentInChildren<Camera>(true);
                localVisualLayer = LayerMask.NameToLayer("LocalPlayerVisual");
            }
        }

        private void Update()
        {
            if (animator == null || !controller.IsSpawned) return;
            // A camera mask hides only our own body; Scene view can still inspect the animation.
            if (localVisualLayer >= 0)
            {
                for (int i = 0; i < visuals.Length; i++)
                    visuals[i].gameObject.layer = controller.IsOwner ? localVisualLayer : originalLayers[i];
                if (controller.IsOwner && ownerCamera != null)
                    ownerCamera.cullingMask &= ~(1 << localVisualLayer);
            }

            var posture = controller.Posture;
            var action = controller.ActionState;
            bool moving = controller.MoveState != PlayerMoveState.Idle;
            string next;
            if (action == PlayerActionState.Sliding) next = "SlidePlaceholder";
            else if (action == PlayerActionState.Diving) next = "DivePlaceholder";
            else if (action == PlayerActionState.Vaulting) next = "VaultPlaceholder";
            else if (action == PlayerActionState.Jumping) next = "Jump";
            else if (action == PlayerActionState.Falling) next = "Fall";
            else if (posture != previousPosture && posture != PlayerPosture.Stand)
            {
                next = posture == PlayerPosture.Crouch ? "CrouchEnter" : "ProneEnter";
                transitionUntil = Time.time + 0.25f;
            }
            else if (Time.time < transitionUntil && (currentState == "CrouchEnter" || currentState == "ProneEnter"))
                next = currentState;
            else if (posture == PlayerPosture.Prone) next = moving ? "Crawl" : "ProneIdle";
            else if (posture == PlayerPosture.Crouch) next = moving ? "CrouchWalk" : "CrouchIdle";
            else if (!moving) next = "Idle";
            else if (controller.MoveState == PlayerMoveState.Walk) next = "Walk";
            else next = controller.MoveState == PlayerMoveState.FullSprint ? "Sprint" : "TacticalRun";
            previousPosture = posture;

            if (next != currentState)
            {
                animator.CrossFadeInFixedTime(next, 0.15f, 0);
                currentState = next;
            }

            displayedLean = Mathf.MoveTowards(displayedLean, controller.Lean, 5f * Time.deltaTime);
            float lean = displayedLean;
            animator.SetLayerWeight(1, Mathf.Abs(lean));
            animator.SetFloat("LeanPoseTime", 1f);
            if (lean != 0f) animator.Play(lean < 0f ? "LeanLeft" : "LeanRight", 1, 1f);
        }
    }
}
