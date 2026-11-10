using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Testing
{
    // Range-only target. Deliberately does not add gameplay/network state to the player prefab.
    public sealed class RangeTargetDummy : MonoBehaviour
    {
        public float maximum = 100f;
        public float resetDelay = 3f;
        public float Current { get; private set; } = 100f;
        public string LastHit { get; private set; } = "";
        public int HitCount { get; private set; }
        private double resetAt;
        private double flashUntil;
        private Renderer[] visuals;
        private MaterialPropertyBlock tint;
        private void Awake() { Current = maximum; visuals = GetComponentsInChildren<Renderer>(); tint = new MaterialPropertyBlock(); }
        public bool ApplyHit(float damage, HitRegion region)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsHost || Current <= 0 || !DamageRules.TryCalculate(damage, region, out float actual)) return false;
            Current = Mathf.Max(0, Current - actual);
            LastHit = (region == HitRegion.Head ? "머리" : "몸") + " -" + actual.ToString("0");
            HitCount++;
            flashUntil = Time.timeAsDouble + .12;
            if (Current <= 0) resetAt = Time.timeAsDouble + resetDelay;
            return true;
        }
        public void ResetTarget() { Current = maximum; LastHit = ""; HitCount = 0; resetAt = 0; flashUntil = 0; }
        private void Update()
        {
            if (resetAt > 0 && Time.timeAsDouble >= resetAt) ResetTarget();
            if (visuals == null) return;
            foreach (var r in visuals)
            {
                if (r == null) continue;
                if (Current <= 0 || Time.timeAsDouble < flashUntil)
                {
                    tint.SetColor("_BaseColor", Current <= 0 ? Color.gray : Color.yellow);
                    r.SetPropertyBlock(tint);
                }
                else r.SetPropertyBlock(null);
            }
        }
    }
}