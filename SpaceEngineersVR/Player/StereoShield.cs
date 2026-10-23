using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRageRender.Import;

namespace SpaceEngineersVR.Player
{
    // Native refraction offsets by the whole projected view direction, which swims with the head and splits the eyes; the copies offset by the bend only.
    [HarmonyPatch]
    internal static class StereoShield
    {
        private const string Refraction="float3 refractVecProj = mul(float4(refractVec, 1), frame_.Environment.view_projection_matrix).xyz;";
        private static readonly object gate=new object();
        private static string[] files;
        private static bool attempted;

        internal static string Source(string original) => StereoParticles.Replace(original,Refraction,
            "float3 refractVecProj = mul(float4(refractVec - viewVector, 0), frame_.Environment.view_projection_matrix).xyz;");

        private static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("VRage.Render11.GeometryStage2.Rendering.MyShaderBundleManager"),"GetShaderFilepath");

        // Render-job thread; __args[1] is the private shader type, 1 = pixel.
        private static void Postfix(object[] __args,ref string __result)
        {
            var technique=(MyMeshDrawTechnique)__args[0];
            if(Convert.ToInt32(__args[1])!=1 || technique!=MyMeshDrawTechnique.SHIELD && technique!=MyMeshDrawTechnique.SHIELD_LIT) return;
            string[] copies=Copies();
            if(copies!=null) __result=copies[technique==MyMeshDrawTechnique.SHIELD ? 0:1];
        }

        private static string[] Copies()
        {
            lock(gate)
            {
                if(attempted) return files;
                attempted=true;
                try
                {
                    var prepared=new string[2];
                    string[] names={"Shield","ShieldLit"};
                    for(int i=0;i<2;i++)
                    {
                        string root=Path.Combine(MyFileSystem.ShadersBasePath,"Shaders","Geometry","Materials",names[i]);
                        string folder=Path.Combine(StereoFoliage.Folder,names[i]);
                        Directory.CreateDirectory(folder);
                        File.Copy(Path.Combine(root,"Declarations.hlsli"),Path.Combine(folder,"Declarations.hlsli"),true);
                        prepared[i]=Path.Combine(folder,"Pixel.hlsl");
                        File.WriteAllText(prepared[i],Source(File.ReadAllText(Path.Combine(root,"Pixel.hlsl"))));
                    }
                    files=prepared;
                }
                catch(Exception ex) { Logger.Warning(ex,"Stereo shield shaders unavailable; safe zone refraction stays native"); }
                return files;
            }
        }
    }
}
