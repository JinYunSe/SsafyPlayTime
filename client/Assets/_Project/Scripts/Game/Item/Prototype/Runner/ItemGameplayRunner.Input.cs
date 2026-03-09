namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void Update()
        {
            TickAudioListenerGuard();
            UpdateFlamethrowerVisualFollow();
            TickLocalDebugController();
        }
    }
}
