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
            log("PASS stereo shield: safe zone and shield pixel shader copies compile from the installed source for the transparent pass");
        }
    }
}
#endif
