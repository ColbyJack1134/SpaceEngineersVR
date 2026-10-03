using VRage.Plugins;

namespace SpaceEngineersVR.Multiplayer
{
    public sealed class Plugin : IPlugin
    {
        public void Init(object gameInstance) => MultiplayerSupport.Start();
        public void Update() => MultiplayerSupport.Update();
        public void Dispose() => MultiplayerSupport.Stop();
    }
}
