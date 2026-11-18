#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StereoShieldTests
    {
        internal static void Run(Device device,string shaders,Action<string> log)
        {
            // The macros MyShaderBundleManager uses for the transparent pass.
            var macros=new[] {new ShaderMacro("RENDERING_PASS",5),new ShaderMacro("USE_SIMPLE_INSTANCING",null),new ShaderMacro("USE_SIMPLE_INSTANCING_COLORING",null)};
            foreach(string name in new[] {"Shield","ShieldLit"})
            {
                string local=Path.Combine(shaders,"Geometry","Materials",name);
                string source=StereoShield.Source(File.ReadAllText(Path.Combine(local,"Pixel.hlsl")));
                using(var includes=new HiddenAreaMask.Includes(shaders,local))
                using(var code=ShaderBytecode.Compile(source,"__pixel_shader","ps_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,macros,includes))
                using(new PixelShader(device,code)) { }
            }
            try { StereoShield.Source("float3 refractVecProj = 0;"); throw new Exception("Changed shield layout was accepted"); }
            catch(InvalidOperationException) { }
            HeadRotation(device,log);
            log("PASS stereo shield: safe zone and shield pixel shader copies compile from the installed source for the transparent pass");
        }

        private static void HeadRotation(Device device,Action<string> log)
        {
            const int samples=32;
            string source=@"
struct Environment { float4x4 projection_matrix; };
struct Frame { Environment Environment; };
RWStructuredBuffer<float> error;
static float3x3 rotation;
float3 world_to_view(float3 direction) { return mul(direction,rotation); }
float2 project(float3 direction, float4x4 projection)
{
    float4 clip = mul(float4(world_to_view(direction),0), projection);
    return 0.5 + float2(0.5,-0.5)*clip.xy/clip.w;
}
[numthreads(32,1,1)]
void main(uint i : SV_GroupIndex)
{
    float eye = (i&1) ? 1 : -1;
    float angle = (i&2) ? 0.52 : -0.25, roll = (i&4) ? 0.65 : 0;
    float c=cos(angle), s=sin(angle), cr=cos(roll), sr=sin(roll);
    rotation=mul(float3x3(c,0,s, 0,1,0, -s,0,c),float3x3(cr,sr,0, -sr,cr,0, 0,0,1));
    Frame frame_;
    frame_.Environment.projection_matrix=float4x4(.69+eye*.001,0,0,0, 0,.7,0,0, .16*eye,-.18,0,-1, 0,0,.03,0);
    float3 viewVector=float3(0,0,1);
    float3 refractVec=float3(.8,-.4,sqrt(.2));
    float refractionDistortionStrength=.025;
    float3 expected=float3(.04,-.02,-1);
    if((i>>3)==1) { refractionDistortionStrength=0; expected=-viewVector; }
    if(i&16) { refractVec=(i&8) ? 0 : viewVector; expected=-viewVector; }
"+StereoShield.ProjectBend+@"
    float2 sampleUv=project(-viewVector,frame_.Environment.projection_matrix)+refractUv;
    float2 delta=sampleUv-project(expected,frame_.Environment.projection_matrix);
    error[i]=length(delta)*2480;
}";
            using(var code=ShaderBytecode.Compile(source,"main","cs_5_0"))
            using(var shader=new ComputeShader(device,code))
            using(var result=new SharpDX.Direct3D11.Buffer(device,new BufferDescription(samples*4,ResourceUsage.Default,
                BindFlags.UnorderedAccess,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,4)))
            using(var output=new UnorderedAccessView(device,result))
            {
                var context=device.ImmediateContext;
                context.ComputeShader.Set(shader);
                context.ComputeShader.SetUnorderedAccessView(0,output);
                try { context.Dispatch(1,1,1); }
                finally { context.ComputeShader.SetUnorderedAccessView(0,null); context.ComputeShader.Set(null); }
                float worst=0;
                foreach(float px in HiddenAreaTests.Read<float>(device,result))
                {
                    if(!(px<.005f)) throw new Exception("Shield sample moved under head rotation by "+px+" px");
                    worst=Math.Max(worst,px);
                }
                log("PASS shield refraction: fixed world directions under yaw and roll, asymmetric eyes and zero bend; maximum drift "+worst+" px at 2480 pixels");
            }
        }
    }
}
#endif
