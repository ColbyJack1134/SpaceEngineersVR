#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceEngineersVR.Player;
using VRage.FileSystem;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;

namespace SpaceEngineersVR.Diagnostics
{
    public static class LeftHandedModelTests
    {
        private static void Require(bool ok,string reason) { if(!ok) throw new Exception(reason); }
        // Builds every left-handed copy from the installed models into output and checks that only the side parts moved.
        public static void Run(Action<string> log,string output=null)
        {
            output=output ?? Path.Combine(Path.GetTempPath(),"SEVR-left-handed-test");
            int models=0,moved=0;
            foreach(var profile in WeaponProfile.All.Where(p=>p.SideAxis.HasValue))
            {
                string source=Path.Combine(MyFileSystem.ContentPath,profile.Model.Replace('/',Path.DirectorySeparatorChar));
                if(!File.Exists(source)) continue;
                string copy=Path.Combine(output,profile.Model.Replace('/',Path.DirectorySeparatorChar));
                float axis=profile.SideAxis.Value;
                LeftHandedModels.Build(source,copy,output,axis,profile.SideCut);
                var before=HoloSight.Tags(source,"Vertices","MeshParts","BoundingBox");
                var after=HoloSight.Tags(copy,"Vertices","MeshParts","BoundingBox","Dummies");
                var a=((HalfVector4[])before["Vertices"]).Select(v=>VF_Packer.UnpackPosition(v)).ToArray();
                var b=((HalfVector4[])after["Vertices"]).Select(v=>VF_Packer.UnpackPosition(v)).ToArray();
                var partsBefore=(List<MyMeshPartInfo>)before["MeshParts"]; var partsAfter=(List<MyMeshPartInfo>)after["MeshParts"];
                var selected=LeftHandedModels.Selected(a,partsBefore,profile.SideCut);
                Require(a.Length==b.Length && selected.Count>0,profile.Item+": left-handed copy lost vertices or moved no parts");
                for(int v=0;v<a.Length;v++)
                {
                    var expected=selected.Contains(v) ? new Vector3(2*axis-a[v].X,a[v].Y,a[v].Z):a[v];
                    Require(Vector3.Distance(expected,b[v])<.002f,profile.Item+": vertex "+v+" not where the side reflection puts it");
                }
                Require(selected.Average(v=>a[v].X)<axis,profile.Item+": the moved parts were not on the tool's left side");
                for(int p=0;p<partsBefore.Count;p++)
                    for(int i=0;i<partsBefore[p].m_indices.Count;i+=3)
                    {
                        var x=partsBefore[p].m_indices; var y=partsAfter[p].m_indices;
                        bool flip=selected.Contains(x[i]);
                        Require(y[i]==x[i] && y[i+1]==(flip ? x[i+2]:x[i+1]) && y[i+2]==(flip ? x[i+1]:x[i+2]),profile.Item+": moved triangle winding not reversed");
                    }
                // Half-precision positions can sit a fraction of a millimetre outside the stored bounds.
                var box=(BoundingBox)after["BoundingBox"]; box.Inflate(.002f);
                Require(selected.All(v=>box.Contains(b[v])!=ContainmentType.Disjoint),profile.Item+": bounds miss the moved parts");
                foreach(var dummy in ((Dictionary<string,MyModelDummy>)after["Dummies"]).Where(d=>d.Key.Contains("subpart_")))
                    if(dummy.Value.CustomData?.TryGetValue("file",out var file)==true && file is string name)
                        Require(File.Exists(Path.Combine(Path.GetDirectoryName(copy),name+".mwm")),profile.Item+": subpart "+name+" not copied beside the model");
                models++; moved+=selected.Count;
            }
            Require(models>0,"No installed side-handled models to test");
            log("PASS left-handed models: "+models+" copies, "+moved+" side-part vertices reflected, winding reversed, bounds and subparts kept.");
        }
    }
}
#endif
