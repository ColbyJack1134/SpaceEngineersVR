using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // A failure disables Defense Shields support until the next world instead of stopping VR.
    internal static class ShieldsIntegration
    {
        internal static void Refresh() { if(Main.WorldAvailable) ShieldsBridge.Load(); }

        internal static void Update() => ShieldsPanel.Update();

        internal static void Reset() { ShieldsPanel.Reset(); ShieldsBridge.Unload(); }
    }
}
