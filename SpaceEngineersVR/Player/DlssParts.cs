#if !LINUX
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRage.Render.Scene;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Parts that move against their grid (rotors, pistons, held tools) redraw their own geometry into a stencil index, so motion vectors follow the part's shape.
    [HarmonyPatchCategory("DlssParts")]
    [HarmonyPatch]
    internal static class DlssParts
    {
        internal const int MaxParts=255;
        // Rotation and translation change below this is grid motion rounding, not part motion.
        private const double Still=1e-5;

        private static readonly object sync=new object();
        private static readonly DlssObjects.Tracker worlds=new DlssObjects.Tracker();
        private static readonly Dictionary<uint,int> slots=new Dictionary<uint,int>();
        // Grid cells and characters: the box pass covers them.
        private static readonly HashSet<uint> groups=new HashSet<uint>();
        internal static readonly Matrix[] Transforms=new Matrix[MaxParts];
        internal static int Count { get; private set; }
        private static AccessTools.FieldRef<object,Matrix[]> skinningField;
        private static Func<object,float> voxelLodSize;
        private static Texture2D texture;
        private static DepthStencilView target;
        private static DepthStencilState mark;
        internal static ShaderResourceView Depth { get; private set; }
        internal static ShaderResourceView Stencil { get; private set; }
        private static volatile bool active;
        private static Vector3D camera;
        private static int frame;
        internal static bool Ready { get; private set; }
        internal static int Width => texture?.Description.Width ?? 0;
        internal static int Height => texture?.Description.Height ?? 0;

        private static MethodBase TargetMethod()
        {
            var proxy=NativeHandLayer.Proxy;
            skinningField=AccessTools.FieldRefAccess<object,Matrix[]>(AccessTools.Field(proxy,"SkinningMatrices") ?? throw new MissingFieldException("MyRenderableProxy.SkinningMatrices"));
            voxelLodSize=VoxelLodSize(proxy);
            return NativeHandLayer.GBufferDraw();
        }
        // proxy.VoxelCommonObjectData.VoxelLodSize; NaN for anything but voxels.
        private static Func<object,float> VoxelLodSize(Type proxy)
        {
            var data=AccessTools.Field(proxy,"VoxelCommonObjectData") ?? throw new MissingFieldException("MyRenderableProxy.VoxelCommonObjectData");
            var size=AccessTools.Field(data.FieldType,"VoxelLodSize") ?? throw new MissingFieldException("MyObjectDataVoxelCommon.VoxelLodSize");
            var method=new System.Reflection.Emit.DynamicMethod("VoxelLodSize",typeof(float),new[] {typeof(object)},typeof(DlssParts).Module,true);
            var il=method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass,proxy);
            il.Emit(System.Reflection.Emit.OpCodes.Ldflda,data);
            il.Emit(System.Reflection.Emit.OpCodes.Ldfld,size);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            return (Func<object,float>)method.CreateDelegate(typeof(Func<object,float>));
        }

        // hq: the scene depth is 32-bit float; depth equality needs the same quantisation as the scene.
        internal static void Begin(int width,int height,bool hq,Vector3D position,int tick)
        {
            active=Ready=false;
            lock(sync) { Count=0; slots.Clear(); }
            var device=MyRender11.DeviceInstance;
            var format=hq ? Format.R32G8X24_Typeless : Format.R24G8_Typeless;
            if(texture==null || texture.Device.NativePointer!=device.NativePointer || texture.Description.Width!=width || texture.Description.Height!=height || texture.Description.Format!=format)
            {
                Dispose();
                texture=new Texture2D(device,new Texture2DDescription { Width=width,Height=height,ArraySize=1,MipLevels=1,
                    Format=format,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.DepthStencil|BindFlags.ShaderResource });
                target=new DepthStencilView(device,texture,new DepthStencilViewDescription {Format=hq ? Format.D32_Float_S8X24_UInt : Format.D24_UNorm_S8_UInt,Dimension=DepthStencilViewDimension.Texture2D});
                Depth=View(device,hq ? Format.R32_Float_X8X24_Typeless : Format.R24_UNorm_X8_Typeless);
                Stencil=View(device,hq ? Format.X32_Typeless_G8X24_UInt : Format.X24_Typeless_G8_UInt);
                var replace=new DepthStencilOperationDescription { Comparison=Comparison.Always,PassOperation=StencilOperation.Replace,FailOperation=StencilOperation.Keep,DepthFailOperation=StencilOperation.Keep };
                // Complementary depth: nearer is greater, cleared to 0.
                mark=new DepthStencilState(device,new DepthStencilStateDescription { IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.All,DepthComparison=Comparison.Greater,
                    IsStencilEnabled=true,StencilReadMask=0xFF,StencilWriteMask=0xFF,FrontFace=replace,BackFace=replace });
            }
            device.ImmediateContext.ClearDepthStencilView(target,DepthStencilClearFlags.Depth|DepthStencilClearFlags.Stencil,0,0);
            camera=position; frame=tick;
            active=Ready=true;
        }
        internal static void End() { active=false; }
        internal static void Off() { active=Ready=false; lock(sync) Count=0; }

        internal static void SetGroups(List<MyManualCullTreeData> visible)
        {
            lock(sync)
            {
                groups.Clear();
                foreach(var group in visible) if(group.Actor!=null) groups.Add(group.Actor.ID);
                if((frame&511)==0) worlds.Prune(frame);
            }
        }
        internal static void Forget() { lock(sync) { worlds.Clear(); groups.Clear(); slots.Clear(); Count=0; } }

        private static ShaderResourceView View(SharpDX.Direct3D11.Device device,Format format) => new ShaderResourceView(device,texture,new ShaderResourceViewDescription
            { Format=format,Dimension=SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,Texture2D=new ShaderResourceViewDescription.Texture2DResource {MipLevels=1} });

        // GBuffer jobs record in parallel on deferred contexts.
        private static void Postfix(object __instance,object proxy)
        {
            if(!active) return;
            try { Mark(__instance,proxy); }
            catch(Exception ex) { Dlss.PartsFailed(ex); }
        }
        private static void Mark(object __instance,object proxy)
        {
            var actor=NativeHandLayer.Parent(proxy)?.Owner as MyActor;
            // Skinned meshes do not move rigidly with their actor; voxel cell actors are re-placed on LOD changes while the terrain stays still.
            if(actor==null || actor.IsDestroyed || skinningField(proxy)!=null || !float.IsNaN(voxelLodSize(proxy))) return;
            int slot;
            lock(sync) slot=Slot(actor);
            if(slot!=0) NativeHandLayer.Redraw(__instance,proxy,target,mark,slot);
        }

        // 0 when the actor moves with its grid or has no previous pose.
        private static int Slot(MyActor actor)
        {
            if(slots.TryGetValue(actor.ID,out int known)) return known;
            int slot=0;
            var root=actor;
            while(root.Parent is MyActor up) root=up;
            MatrixD world=actor.WorldMatrix,previous;
            if(worlds.Observe(actor.ID,world,frame,out previous,out bool moved) && moved && Count<MaxParts)
            {
                bool own=true;
                if(root!=actor)
                {
                    MatrixD rootWorld=root.WorldMatrix,rootPrevious;
                    if(worlds.Observe(root.ID,rootWorld,frame,out rootPrevious,out _))
                        own=!Near(world*MatrixD.Invert(rootWorld),previous*MatrixD.Invert(rootPrevious));
                }
                else own=!groups.Contains(actor.ID);
                if(own)
                {
                    Transforms[Count]=DlssObjects.Reproject(world,previous,camera);
                    slot=++Count;
                }
            }
            slots[actor.ID]=slot;
            return slot;
        }
        private static bool Near(in MatrixD a,in MatrixD b) =>
            Math.Abs(a.M11-b.M11)<Still && Math.Abs(a.M12-b.M12)<Still && Math.Abs(a.M13-b.M13)<Still &&
            Math.Abs(a.M21-b.M21)<Still && Math.Abs(a.M22-b.M22)<Still && Math.Abs(a.M23-b.M23)<Still &&
            Math.Abs(a.M31-b.M31)<Still && Math.Abs(a.M32-b.M32)<Still && Math.Abs(a.M33-b.M33)<Still &&
            Math.Abs(a.M41-b.M41)<Still && Math.Abs(a.M42-b.M42)<Still && Math.Abs(a.M43-b.M43)<Still;

        internal static void Dispose()
        {
            Off();
            Depth?.Dispose(); Stencil?.Dispose(); target?.Dispose(); texture?.Dispose(); mark?.Dispose();
            Depth=null; Stencil=null; target=null; texture=null; mark=null;
        }
    }
}
#endif
