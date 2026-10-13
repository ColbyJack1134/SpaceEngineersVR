using System;
using System.Linq;
using VRage.Plugins;

namespace SpaceEngineersVR.Multiplayer
{
    public sealed class Plugin : IPlugin
    {
        // The VR plugin compiles in the same multiplayer support; patching twice would duplicate every hook.
        public void Init(object gameInstance)
        {
            if(AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic && a.GetType("SpaceEngineersVR.Plugin.Main",false)!=null))
            {
                MultiplayerRuntime.Log("Companion inactive; the VR plugin provides multiplayer support");
                return;
            }
            MultiplayerSupport.Start();
            MultiplayerRuntime.Log("Companion active");
        }
        public void Update() => MultiplayerSupport.Update();
        public void Dispose() => MultiplayerSupport.Stop();
    }
}
