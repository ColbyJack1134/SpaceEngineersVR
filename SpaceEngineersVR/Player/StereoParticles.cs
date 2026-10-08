using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Wrappers;
using VRage.FileSystem;
using VRageMath;
using Device=SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Player
{
    internal static class StereoParticles
    {
        private static readonly Type native=AccessTools.TypeByName("VRageRender.MyGPUParticleRenderer"),
            common=AccessTools.TypeByName("VRageRender.MyCommon"),context=AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext"),
            mapping=AccessTools.TypeByName("VRageRender.MyMapping");
        private static readonly FieldInfo data=AccessTools.Field(common,"FrameConstantsData"),
            environment=AccessTools.Field(data.FieldType,"Environment"),
            cameraDelta=AccessTools.Field(environment.FieldType,"CameraPositionDelta");
        private static readonly MethodInfo objectBuffer=AccessTools.Method(context,"GetObjectCB"),
            map=AccessTools.Method(mapping,"MapDiscard",new[] {context,AccessTools.TypeByName("VRage.Render11.Resources.IBuffer")}),
            write=Writer(data.FieldType),vectorWrite=Writer(typeof(Vector4)),
            unmap=AccessTools.Method(mapping,"Unmap"),
            vertexBuffer=AccessTools.Method(AccessTools.TypeByName("VRage.Render11.RenderContext.MyVertexStage"),"SetConstantBuffer"),
            getShader=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyVertexShaders"),"GetShader");
        private static readonly FieldInfo debugId=AccessTools.Field(native,"m_vsShadowDebug");
        private static readonly int frameSize=Marshal.SizeOf(data.FieldType);
        private static Device device;
        private static VertexShader vertex,debugVertex;
        private static Vector3D particleCamera;
        private static bool hasCamera;
        internal static bool Advance => StereoRenderState.AdvanceScene;

        internal static object FrameConstants(object original,object rc)
        {
            // Auxiliary cameras leave the simulation buffer in the primary camera's coordinates.
            var camera=MyRender11.Environment_Matrices.CameraPosition;
            if(!Advance) return original;
            var constants=data.GetValue(null);
            var env=environment.GetValue(constants);
            cameraDelta.SetValue(env,hasCamera ? (Vector3)(camera-particleCamera) : Vector3.Zero);
            environment.SetValue(constants,env);
            var buffer=objectBuffer.Invoke(rc,new object[] {frameSize});
            var mapped=map.Invoke(null,new[] {rc,buffer});
            try { write.Invoke(mapped,new[] {constants}); }
            finally { unmap.Invoke(mapped,null); }
            particleCamera=camera; hasCamera=true;
            return buffer;
        }

        internal static VertexShader Select(VertexShader original,object rc)
        {
            if(!StereoRenderState.Active && !RemoteScene.Active) return original;
            Ensure(MyRender11.DeviceInstance);
            var head=MatrixD.Invert(StereoRenderState.PhysicalEye ? StereoRenderState.CenterView : MyRender11.Environment_Matrices.ViewD);
            var values=new[] {new Vector4((Vector3)(hasCamera ? particleCamera-MyRender11.Environment_Matrices.CameraPosition : Vector3D.Zero),0),
                (Vector4)new Vector4D(MyRender11.Environment_Matrices.CameraPosition-head.Translation,0),
                new Vector4((Vector3)head.Up,0)};
            var buffer=objectBuffer.Invoke(rc,new object[] {48});
            var mapped=map.Invoke(null,new[] {rc,buffer});
            try
            {
                foreach(var value in values) vectorWrite.Invoke(mapped,new object[] {value});
            }
            finally { unmap.Invoke(mapped,null); }
            vertexBuffer.Invoke(CockpitRender.Member(rc,"VertexShader"),new[] {(object)2,buffer});
            var debug=(VertexShader)getShader.Invoke(null,new[] {debugId.GetValue(null)});
            return original.NativePointer==debug.NativePointer ? debugVertex : vertex;
        }

        private static MethodInfo Writer(Type type) => mapping.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic)
            .Single(m=>m.Name=="WriteAndPosition" && m.IsGenericMethodDefinition && m.GetParameters().Length==1).MakeGenericMethod(type);

        internal static void Ensure(Device target,string shaders=null)
        {
            if(device!=null && device.NativePointer==target.NativePointer) return;
            Dispose();
            string root=shaders ?? Path.Combine(MyFileSystem.ShadersBasePath,"Shaders"),local=Path.Combine(root,"Transparent","GPUParticles");
            string source=File.ReadAllText(Path.Combine(local,"Render.hlsl"));
            source=Replace(source,"Particle pa = g_ParticleBuffer[index];","Particle pa = g_ParticleBuffer[index]; pa.Position += PositionOffset.xyz; pa.Origin += PositionOffset.xyz;");
            source=Replace(source,"float distance = length(pa.Position);","float distance = length(pa.Position + StereoOffset.xyz);");
            source=Replace(source,"float3 cameraVector = pa.Position / distance;","float3 cameraVector = (pa.Position + StereoOffset.xyz) / max(distance, 1e-6);");
            source=Replace(source,"float3 upCamera = frame_.Environment.view_matrix._12_22_32;","float3 upCamera = StereoUp.xyz;");
            source="cbuffer StereoBasis : register(b2) { float4 PositionOffset; float4 StereoOffset; float4 StereoUp; };\n"+source;
            for(int debug=0;debug<2;debug++)
            {
                var macros=debug==0 ? new[] {new ShaderMacro("STREAKS",null),new ShaderMacro("LIT_PARTICLE",null)} :
                    new[] {new ShaderMacro("STREAKS",null),new ShaderMacro("LIT_PARTICLE",null),new ShaderMacro("OIT",null),new ShaderMacro("DEBUG_SHADOWS",null)};
                using(var includes=new HiddenAreaMask.Includes(root,local))
                using(var code=ShaderBytecode.Compile(source,"__vertex_shader","vs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,macros,includes))
                {
                    var shader=new VertexShader(target,code);
                    if(debug==0) vertex=shader; else debugVertex=shader;
                }
            }
            device=target;
        }
        private static string Replace(string source,string before,string after)
        {
            if(source.IndexOf(before,StringComparison.Ordinal)<0 || source.IndexOf(before,StringComparison.Ordinal)!=source.LastIndexOf(before,StringComparison.Ordinal))
                throw new InvalidOperationException("Native particle shader layout changed");
            return source.Replace(before,after);
        }
        internal static void ResetCamera() { hasCamera=false; }
        internal static void Dispose()
        { vertex?.Dispose(); debugVertex?.Dispose(); vertex=null; debugVertex=null; device=null; }
    }
}
