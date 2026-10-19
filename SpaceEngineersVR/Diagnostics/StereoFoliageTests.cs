#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StereoFoliageTests
    {
        internal static void Run(Device device,string shaders,Action<string> log)
        {
            string local=Path.Combine(shaders,"Foliage");
            foreach(string include in StereoFoliage.Includes) Require(File.Exists(Path.Combine(local,include)),"Missing foliage include "+include);
            string source=StereoFoliage.Source(File.ReadAllText(Path.Combine(local,"Foliage.hlsl")));
            foreach(var macros in new ShaderMacro[][] {null,new[] {new ShaderMacro("ROCK_FOLIAGE",null)}})
            using(var includes=new HiddenAreaMask.Includes(shaders,local))
            using(var code=ShaderBytecode.Compile(source,"__geometry_shader","gs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,macros,includes))
            using(var reflection=new ShaderReflection(code))
            using(new GeometryShader(device,code))
            {
                var buffers=Enumerable.Range(0,reflection.Description.BoundResources).Select(reflection.GetResourceBindingDescription)
                    .Where(b=>b.Type==ShaderInputType.ConstantBuffer).ToList();
                Require(buffers.Any(b=>b.Name=="StereoFoliage" && b.BindPoint==3),"Foliage eye offset is not read from b3");
                Require(buffers.Count(b=>b.BindPoint==3)==1,"Foliage b3 is shared with another constant buffer");
            }
            try { StereoFoliage.Source("float3 viewVector = -position.xyz;"); throw new Exception("Changed foliage layout was accepted"); }
            catch(InvalidOperationException) { }
            log("PASS stereo foliage: grass and pebble geometry shader copies compile from the installed source and read the head offset on b3");
        }
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
#endif
