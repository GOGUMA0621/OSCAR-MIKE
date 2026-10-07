using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OskarMike.AI.Testing
{
    // Only attached to the test player variant. Production scene input rules stay intact.
    public sealed class EnemyAITestPlayer : NetworkBehaviour
    {
        private PlayerNetworkController controller;
        private PlayerInput input;
        private bool menu;
        private void Awake() { controller = GetComponent<PlayerNetworkController>(); input = GetComponent<PlayerInput>(); }
        private void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || SceneManager.GetActiveScene().name != "EnemyAITest") return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                menu = !menu;
                if (menu) controller.DisableGameplayInput();
            }
            if (!menu && !input.enabled) controller.EnableGameplayInput();
        }
    }
}
