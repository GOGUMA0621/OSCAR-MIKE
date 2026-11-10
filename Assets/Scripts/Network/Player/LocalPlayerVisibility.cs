using UnityEngine;

namespace OskarMike.Network.Player
{
    // Camera visibility is independent of animation state selection.
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerNetworkController))]
    public sealed class LocalPlayerVisibility : MonoBehaviour
    {
        private PlayerNetworkController controller;
        private Renderer[] visuals;
        private int[] layers;
        private Camera ownerCamera;
        private int originalMask;
        private int localLayer;
        private bool hidden;

        private void Awake()
        {
            controller = GetComponent<PlayerNetworkController>();
            var animator = GetComponentInChildren<Animator>(true);
            visuals = animator != null ? animator.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            layers = new int[visuals.Length];
            for (int i = 0; i < visuals.Length; i++) layers[i] = visuals[i].gameObject.layer;
            ownerCamera = GetComponentInChildren<Camera>(true);
            if (ownerCamera != null) originalMask = ownerCamera.cullingMask;
            localLayer = LayerMask.NameToLayer("LocalPlayerVisual");
        }

        private void LateUpdate()
        {
            bool shouldHide = controller.IsSpawned && controller.IsOwner && localLayer >= 0;
            if (shouldHide != hidden) Apply(shouldHide);
        }

        private void Apply(bool hide)
        {
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null) visuals[i].gameObject.layer = hide ? localLayer : layers[i];
            if (ownerCamera != null)
                ownerCamera.cullingMask = hide ? originalMask & ~(1 << localLayer) : originalMask;
            hidden = hide;
        }

        private void OnDisable() { if (hidden) Apply(false); }
    }
}