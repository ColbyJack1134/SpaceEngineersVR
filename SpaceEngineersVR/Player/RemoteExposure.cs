using System;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Wrappers;

namespace SpaceEngineersVR.Player
{
    internal sealed class RemoteExposure : IDisposable
    {
        private static readonly FieldInfo history=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyEyeAdaptation"),"m_autoExposure");
        private static readonly Texture2D[] camera=new Texture2D[2],physical=new Texture2D[2];
        private static bool initialized;
        private readonly Array pair;
        private readonly object[] originals=new object[2];
        private int saved;
        private readonly DeviceContext context=MyRender11.DeviceInstance.ImmediateContext;
        private static Texture2D Resource(object value) => (Texture2D)CockpitRender.Member(value,"Resource");
        public RemoteExposure()
        {
            pair=(Array)history.GetValue(null);
            try
            {
                for(int i=0;i<2;i++)
                {
                    originals[i]=pair.GetValue(i);
                    var texture=Resource(originals[i]);
                    if(physical[i]==null)
                    {
                        var description=texture.Description;
                        description.BindFlags=BindFlags.None;
                        physical[i]=new Texture2D(texture.Device,description);
                        camera[i]=new Texture2D(texture.Device,description);
                    }
                    context.CopyResource(texture,physical[i]); saved++;
                    if(initialized) context.CopyResource(camera[i],texture);
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
                for(int i=0;i<2;i++) context.CopyResource(Resource(pair.GetValue(i)),camera[i]);
                initialized=true;
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
        public static void Reset()
        {
            for(int i=0;i<2;i++) {camera[i]?.Dispose(); physical[i]?.Dispose(); camera[i]=physical[i]=null;}
            initialized=false;
        }
    }
}
