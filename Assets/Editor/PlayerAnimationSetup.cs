namespace OskarMike.EditorTools
{
    // Compatibility entry point. Preserve user-organized clips instead of regenerating them.
    public static class PlayerAnimationSetup
    {
        public static void Build() => PlayerLocomotionSetup.Configure();
    }
}