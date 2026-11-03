using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Wrappers;

namespace SpaceEngineersVR.Player
{
    internal sealed class RemoteExposure : IDisposable
    {
        private static readonly FieldInfo autoExposure=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyEyeAdaptation"),"m_autoExposure");
        internal const int Camera=0,Lcd=1;
        // Each slot keeps its own adaptation history so feeds pointing elsewhere do not pull each other's exposure.
        private static readonly Texture2D[][] history=new[] {new Texture2D[2],new Texture2D[2]};
        private static readonly Texture2D[] physical=new Texture2D[2];
        private static readonly bool[] initialized=new bool[2];
        private readonly int slot;
        private readonly Array pair;
        private readonly object[] originals=new object[2];
        private int saved;
        private readonly DeviceContext context=MyRender11.DeviceInstance.ImmediateContext;
        private static Texture2D Resource(object value) => (Texture2D)CockpitRender.Member(value,"Resource");
        public RemoteExposure(int slot=Camera)
        {
            this.slot=slot;
            pair=(Array)autoExposure.GetValue(null);
            try
            {
                for(int i=0;i<2;i++)
                {
                    originals[i]=pair.GetValue(i);
                    var texture=Resource(originals[i]);
                    var description=texture.Description;
                    description.BindFlags=BindFlags.None;
                    if(physical[i]==null) physical[i]=new Texture2D(texture.Device,description);
                    if(history[slot][i]==null) history[slot][i]=new Texture2D(texture.Device,description);
                    context.CopyResource(texture,physical[i]); saved++;
                    if(initialized[slot]) context.CopyResource(history[slot][i],texture);
                }
            }
            catch { Restore(); throw; }
        }

        public void Dispose()
        {
            // Native eye adaptation swaps its two history textures. Keep camera
            // history separate and restore both physical pixels and their order.
            try
            {
                for(int i=0;i<2;i++) context.CopyResource(Resource(pair.GetValue(i)),history[slot][i]);
                initialized[slot]=true;
            }
            finally { Restore(); }
        }
        private void Restore()
        {
            for(int i=0;i<saved;i++)
            {
                pair.SetValue(originals[i],i);
                context.CopyResource(physical[i],Resource(originals[i]));
            }
            saved=0;
        }
        public static void Reset(int slot)
        {
            for(int i=0;i<2;i++) { history[slot][i]?.Dispose(); history[slot][i]=null; }
            initialized[slot]=false;
            if(history.All(h=>h[0]==null)) for(int i=0;i<2;i++) { physical[i]?.Dispose(); physical[i]=null; }
        }
    }
}
