using System;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;

namespace SpaceEngineersVR.Player
{
    // Reuse the native glove draw, including its skinning and material alpha, for UI occlusion.
    [HarmonyPatch]
    internal static class NativeHandLayer
    {
        internal static volatile uint Actor=uint.MaxValue;
        internal static ShaderResourceView Depth => ready ? view:null;
        private static ShaderResourceView view;
        private static bool ready;
        private static Texture2D texture;
        private static DepthStencilView target;
        private static volatile bool active;
        private static readonly FieldInfo parentField=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyRenderableProxy"),"Parent");
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyGBufferPass"),"RecordCommandsInternal",new[] {AccessTools.TypeByName("VRageRender.MyRenderableProxy")});
        internal static void Begin(int width,int height,bool enabled)
        {
            active=ready=false;
            if(!enabled || Actor==uint.MaxValue) return;
            if(texture==null || texture.Description.Width!=width || texture.Description.Height!=height)
            {
                Reset();
                texture=new Texture2D(MyRender11.DeviceInstance,new Texture2DDescription { Width=width,Height=height,ArraySize=1,MipLevels=1,
                    Format=Format.R32_Typeless,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.DepthStencil|BindFlags.ShaderResource });
                target=new DepthStencilView(MyRender11.DeviceInstance,texture,new DepthStencilViewDescription {Format=Format.D32_Float,Dimension=DepthStencilViewDimension.Texture2D});
                view=new ShaderResourceView(MyRender11.DeviceInstance,texture,new ShaderResourceViewDescription {Format=Format.R32_Float,Dimension=SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,Texture2D=new ShaderResourceViewDescription.Texture2DResource {MipLevels=1}});
            }
            MyRender11.DeviceInstance.ImmediateContext.ClearDepthStencilView(target,DepthStencilClearFlags.Depth,0,0);
            active=ready=true;
        }
        internal static void End() { active=false; }
        internal static void Reset()
        {
            active=ready=false; view?.Dispose(); view=null; target?.Dispose(); target=null; texture?.Dispose(); texture=null;
        }
        private static void Postfix(object __instance,object proxy)
        {
            if(!active) return;
            var parent=(VRage.Render.Scene.Components.MyActorComponent)parentField.GetValue(proxy);
            if(parent?.Owner.ID!=Actor) return;
            string material=CockpitRender.Member(CockpitRender.Member(CockpitRender.Member(proxy,"Material"),"Info"),"Name").ToString();
            if(material!="LeftGlove" && material!="RightGlove") return;
            var context=(DeviceContext)CockpitRender.Member(CockpitRender.Member(__instance,"RC"),"DeviceContext");
            var buffer=CockpitRender.Member(__instance,"GBuffer");
            var depth=(DepthStencilView)CockpitRender.Member(CockpitRender.Member(CockpitRender.Member(buffer,"DepthStencil"),"Dsv"),"Dsv");
            var colors=(RenderTargetView[])CockpitRender.Member(buffer,"GbufferRtvs");
            // Restore the exact bindings without changing the engine's cached state.
            context.OutputMerger.SetRenderTargets(target,new RenderTargetView[0]);
            try
            {
                var submesh=CockpitRender.Member(proxy,"DrawSubmesh");
                int count=Convert.ToInt32(CockpitRender.Member(submesh,"IndexCount")),start=Convert.ToInt32(CockpitRender.Member(submesh,"StartIndex")),vertex=Convert.ToInt32(CockpitRender.Member(submesh,"BaseVertex"));
                int instances=Convert.ToInt32(CockpitRender.Member(proxy,"InstanceCount"));
                if(instances==0) context.DrawIndexed(count,start,vertex);
                else context.DrawIndexedInstanced(count,instances,start,vertex,Convert.ToInt32(CockpitRender.Member(proxy,"StartInstance")));
            }
            finally { context.OutputMerger.SetRenderTargets(depth,colors); }
        }
    }
}
