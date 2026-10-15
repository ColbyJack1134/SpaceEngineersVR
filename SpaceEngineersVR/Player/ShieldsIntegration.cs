using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // Defense Shields support as one Integration (see Main): a failure disables it until the next world instead of stopping VR.
    internal static class ShieldsIntegration
    {
        internal static void Refresh() { if(Main.WorldAvailable) ShieldsBridge.Load(); }

        internal static void Update() => ShieldsPanel.Update();

        internal static void Reset() { ShieldsPanel.Reset(); ShieldsBridge.Unload(); }
    }
}
