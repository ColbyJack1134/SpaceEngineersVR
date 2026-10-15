using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using SharpDX.Direct3D;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRage.FileSystem;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // The native grass geometry shader bends, scales and LOD-fades each blade from the drawing camera,
    // so the two eyes disagree up close. Copies measure from the head centre; the pixel stage stays native.
    internal static class StereoFoliage
    {
        private static readonly FieldInfo shaders=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyFoliageRenderingPass"),"m_GS");
        private static readonly MethodInfo create=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyGeometryShaders"),"Create"),
            bind=AccessTools.Method(AccessTools.TypeByName("VRage.Render11.RenderContext.MyGeometryStage"),"SetConstantBuffer");
        internal static readonly string[] Includes={"Foliage.hlsli","GrassFoliage.hlsli","RockFoliage.hlsli"};
        private static readonly object[] saved=new object[2];
        private static object[] eye;
        private static int attempted;
        private static bool swapped;
        internal static bool Ready => eye!=null;
        internal static string Folder => Path.Combine(MyFileSystem.UserDataPath,"SEVRShaders");

        // Positions are camera-relative and get_camera_position() is zero, so the offset is head centre minus eye.
        internal static string Source(string original) =>
            "cbuffer StereoFoliage : register(b3) { float4 FoliageEyeOffset; };\n"+
            StereoParticles.Replace(original,"float3 viewVector = get_camera_position() - position.xyz;",
                "float3 viewVector = (get_camera_position() + FoliageEyeOffset.xyz) - position.xyz;");

        // Compiles once per process through the native registry, which caches the result in ShaderCache2 and
        // recompiles the copies from this folder on device reset. Plugins load after the renderer's device init,
        // so this cannot hook the native InitShaders. Warm runs it off the render thread at plugin start
        // (the registries are locked and already serve render worker threads); Begin stays native until it is done.
        internal static void Warm() => Task.Run(() =>
        {
            for(int i=0;i<600 && MyRender11.DeviceInstance==null;i++) Thread.Sleep(100);
            Prepare();
        });
        internal static void Prepare()
        {
            if(Interlocked.Exchange(ref attempted,1)!=0) return;
            try
            {
                string root=Path.Combine(MyFileSystem.ShadersBasePath,"Shaders","Foliage"),folder=Folder;
                Directory.CreateDirectory(folder);
                foreach(string include in Includes) File.Copy(Path.Combine(root,include),Path.Combine(folder,include),true);
                string file=Path.Combine(folder,"Foliage.hlsl");
                File.WriteAllText(file,Source(File.ReadAllText(Path.Combine(root,"Foliage.hlsl"))));
                eye=new[] {create.Invoke(null,new object[] {file,null,null}),
                    create.Invoke(null,new object[] {file,new[] {new ShaderMacro("ROCK_FOLIAGE",null)},null})};
            }
            catch(Exception ex) { eye=null; Logger.Warning(ex,"Stereo foliage shaders unavailable; grass stays per eye"); }
        }

        // Both run on the pass's render job between its Begin and End, on its own deferred context.
        internal static void Begin(object renderingPass)
        {
            if(!StereoRenderState.PhysicalEye) return;
            Prepare();
            var ids=(Array)shaders.GetValue(renderingPass);
            if(eye==null || ids.Length!=2) return;
            for(int i=0;i<2;i++) { saved[i]=ids.GetValue(i); ids.SetValue(eye[i],i); }
            swapped=true;
            var rc=CockpitRender.Member(renderingPass,"RC");
            var offset=MatrixD.Invert(StereoRenderState.CenterView).Translation-MyRender11.Environment_Matrices.CameraPosition;
            bind.Invoke(CockpitRender.Member(rc,"GeometryShader"),new[] {(object)3,StereoParticles.Upload(rc,new Vector4((Vector3)offset,0))});
        }
        internal static void End(object renderingPass)
        {
            if(!swapped) return;
            swapped=false;
            var ids=(Array)shaders.GetValue(renderingPass);
            for(int i=0;i<2;i++) ids.SetValue(saved[i],i);
        }
    }
}
