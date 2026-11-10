namespace OskarMike.Network.Player
{
    public enum HitRegion : byte { Body, Head }

    public static class DamageRules
    {
        // Only authoritative hit detection should select the region.
        public static bool TryCalculate(float baseDamage, HitRegion region, out float damage)
        {
            damage = 0;
            if (float.IsNaN(baseDamage) || float.IsInfinity(baseDamage) || baseDamage <= 0 ||
                (region != HitRegion.Body && region != HitRegion.Head)) return false;
            damage = baseDamage * (region == HitRegion.Head ? 2f : 1f);
            return !float.IsInfinity(damage);
        }
    }
}
