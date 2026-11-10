using System;
using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OskarMike.Testing
{
    // Local host shooting harness for RangeCourse; not a multiplayer weapon implementation.
    public sealed class RangeTestGun : MonoBehaviour
    {
        public Transform pickupPoint;
        public GameObject weaponModel;
        public Font koreanFont;
        public RangeTargetDummy[] targets;
        public int Magazine { get; private set; } = 30;
        public int Reserve { get; private set; } = 90;
        public bool Equipped { get; private set; }
        public bool Reloading => reloadAt > 0;
        public string LastResult { get; private set; } = "";
        private PlayerNetworkController player;
        private Camera view;
        private GameObject held;
        private double nextShot, reloadAt, markerUntil;
        private float kick;
        private GUIStyle text;
        private bool inRange;

        private bool Ready()
        {
            var nm = NetworkManager.Singleton;
            if (gameObject.scene.name != "RangeCourse" || nm == null || !nm.IsHost || nm.LocalClient == null) return false;
            var obj = nm.LocalClient.PlayerObject;
            if (obj == null) return false;
            if (player == null || player.gameObject != obj.gameObject)
            {
                player = obj.GetComponent<PlayerNetworkController>();
                view = obj.GetComponentInChildren<Camera>(true);
            }
            var hp = obj.GetComponent<PlayerHealth>();
            return player != null && player.GameplayInputEnabled && view != null && (hp == null || !hp.Dead);
        }

        private void Update()
        {
            if (!Ready()) { reloadAt = 0; if (held != null) held.SetActive(false); return; }
            inRange = pickupPoint != null && Vector3.Distance(player.transform.position, pickupPoint.position) < 3f;
            if (Keyboard.current != null)
            {
                if (inRange && Keyboard.current.eKey.wasPressedThisFrame) EquipAndRefill();
                if (Keyboard.current.rKey.wasPressedThisFrame) BeginReload();
                if (Keyboard.current.f5Key.wasPressedThisFrame)
                    foreach (var target in targets) if (target != null) target.ResetTarget();
            }
            CompleteReload();
            if (Equipped && Cursor.lockState == CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.isPressed)
                Fire(view.transform.position, view.transform.forward);
            if (held != null)
            {
                held.SetActive(Equipped);
                kick = Mathf.MoveTowards(kick, 0, Time.deltaTime * 1.8f);
                held.transform.localPosition = new Vector3(.22f, -.23f, .48f - kick);
            }
        }

        public bool EquipAndRefill()
        {
            if (!Ready() || pickupPoint == null || Vector3.Distance(player.transform.position, pickupPoint.position) >= 3f) return false;
            Equipped = true; Magazine = 30; Reserve = 90; reloadAt = 0;
            if (held == null && weaponModel != null)
            {
                held = Instantiate(weaponModel, view.transform);
                held.name = "Range test rifle (local view)";
                held.transform.localRotation = Quaternion.identity;
                foreach (var c in held.GetComponentsInChildren<Collider>()) c.enabled = false;
                foreach (var r in held.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return true;
        }

        public bool BeginReload()
        {
            if (!Ready() || !Equipped || Reloading || Magazine >= 30 || Reserve <= 0) return false;
            reloadAt = Time.timeAsDouble + 2.3;
            return true;
        }
        public void CompleteReload()
        {
            if (!Reloading || Time.timeAsDouble < reloadAt) return;
            int amount = Mathf.Min(30 - Magazine, Reserve);
            Magazine += amount; Reserve -= amount; reloadAt = 0;
        }

        public bool Fire(Vector3 origin, Vector3 direction)
        {
            if (!Ready() || !Equipped || Reloading || Magazine <= 0 || Time.timeAsDouble < nextShot) return false;
            if (player.MoveState == PlayerMoveState.FullSprint || player.ActionState == PlayerActionState.Vaulting ||
                player.ActionState == PlayerActionState.Diving || player.ActionState == PlayerActionState.Sliding) return false;
            nextShot = Time.timeAsDouble + .1; Magazine--; kick = .07f;
            LastResult = "빗나감";
            var hits = Physics.RaycastAll(origin, direction, 350f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(player.transform)) continue;
                var box = hit.collider.GetComponent<RangeTargetHitbox>();
                if (box != null && box.target != null && box.target.ApplyHit(20f, box.region))
                {
                    LastResult = box.target.name + " / " + box.target.LastHit;
                    markerUntil = Time.timeAsDouble + .15;
                }
                else LastResult = "장애물 명중";
                break;
            }
            return true;
        }

        private void OnDestroy() { if (held != null) Destroy(held); }
        private void OnGUI()
        {
            if (text == null) text = new GUIStyle(GUI.skin.label) { font = koreanFont, normal = { textColor = Color.white } };
            bool ready = Ready();
            var previous = GUI.matrix;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1100f, Screen.height / 650f), .55f, 1f);
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                text.fontSize = 18;
                GUI.Box(new Rect(12, 12, 570, 160), "");
                GUI.Label(new Rect(24, 18, 550, 26), "사격장 · 로컬 호스트 테스트", text);
                GUI.Label(new Rect(24, 46, 550, 26), !ready ? "호스트 플레이어를 기다리는 중입니다." :
                    Equipped ? "좌클릭 발사 · R 재장전 · F5 더미 초기화" : "왼쪽 총기대에서 E 키로 총을 받으세요.", text);
                GUI.Label(new Rect(24, 74, 550, 26), Equipped ? "탄약 " + Magazine + " / " + Reserve + (Reloading ? " · 재장전 중..." : "") : "총기 미장착 · 총기대 (-4, 3)", text);
                GUI.Label(new Rect(24, 102, 550, 26), inRange ? "E : 소총 지급 / 탄약 보충" : "몸 20 / 머리 40 · 더미 파괴 후 3초 복구", text);
                GUI.Label(new Rect(24, 130, 550, 26), LastResult, text);
            }
            finally { GUI.matrix = previous; }
            if (!ready) return;
            text.fontSize = Mathf.Clamp(Mathf.RoundToInt(18 * scale), 12, 18);
            if (Equipped) GUI.Label(new Rect(Screen.width/2f - 5, Screen.height/2f - 10, 24, 24), Time.timeAsDouble < markerUntil ? "×" : "+", text);
            if (targets == null) return;
            foreach (var target in targets)
            {
                if (target == null) continue;
                Vector3 p = view.WorldToScreenPoint(target.transform.position + Vector3.up * 2.15f);
                if (p.z > 0) GUI.Label(new Rect(p.x - 65, Screen.height - p.y, 220, 45), target.name + "\n" + target.Current.ToString("0") + " / 100 " + target.LastHit, text);
            }
        }
    }
}