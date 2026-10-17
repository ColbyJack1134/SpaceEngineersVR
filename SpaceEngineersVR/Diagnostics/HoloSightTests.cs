#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceEngineersVR.Player;
using VRage.FileSystem;
using VRageMath;
using VRageRender.Import;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class HoloSightTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private static int[] Indices(string file) => ((List<MyMeshPartInfo>)HoloSight.Tags(file,"MeshParts")["MeshParts"])[0].m_indices.ToArray();
        public static void Run(Action<string> log,string output=null)
        {
            string folder=output ?? Path.Combine(Path.GetTempPath(),"SEVR-holo-sights");
            foreach(string item in HoloSight.Items)
            {
                var profile=WeaponProfile.All.Single(p=>p.Item==item);
                string source=Path.Combine(MyFileSystem.ContentPath,profile.Model),copy=Path.Combine(folder,profile.Model);
                var reticle=HoloSight.Build(item,source,copy,folder);
                int[] before=Indices(source),after=Indices(copy);
                Require(before.Length==after.Length && new FileInfo(source).Length==new FileInfo(copy).Length,item+": model copy changed size");
                int changed=0;
                for(int t=0;t<before.Length/3;t++)
                {
                    if(before[3*t]==after[3*t] && before[3*t+1]==after[3*t+1] && before[3*t+2]==after[3*t+2]) continue;
                    Require(after[3*t]==after[3*t+1] && after[3*t+1]==after[3*t+2],item+": a changed triangle is not degenerate");
                    changed++;
                }
                Require(changed==HoloSight.Triangles(item),item+": "+changed+" triangles removed");
                Require(File.Exists(reticle.Texture),item+": reticle mask missing");
                foreach(var dummy in ((Dictionary<string,MyModelDummy>)HoloSight.Tags(source,"Dummies")["Dummies"]).Where(d=>d.Key.Contains("subpart_") && d.Value.CustomData.ContainsKey("file")))
                {
                    string name=(string)dummy.Value.CustomData["file"]+".mwm";
                    Require(!File.Exists(Path.Combine(Path.GetDirectoryName(source),name)) || File.Exists(Path.Combine(Path.GetDirectoryName(copy),name)),item+": subpart "+name+" missing beside the copy");
                }
                var t2=reticle.Tangents;
                Require(t2.Width>0 && t2.Height>0 && t2.X<0 && t2.X+t2.Width>0 && t2.Y>0 && t2.Y-t2.Height<0,item+": reticle does not surround the aim point");
            }
            var window=new Vector4(-.013f,.013f,-.003f,.01f);
            Vector3D center=new Vector3D(0,.11,-.011),forward=Vector3D.Forward,right=Vector3D.Right,up=Vector3D.Up;
            Require(Math.Abs((HoloSight.Along(center+new Vector3D(.008,.004,.2),center,forward,right,up,window) ?? 0)-.2)<1e-9,"Eye behind the window does not see the reticle");
            Require(HoloSight.Along(center+new Vector3D(.032,0,.2),center,forward,right,up,window)==null,"Eye beside the window sees the reticle");
            Require(HoloSight.Along(center+new Vector3D(0,-.01,.2),center,forward,right,up,window)==null,"Eye under the window sees the reticle");
            Require(HoloSight.Along(center+new Vector3D(0,0,-.1),center,forward,right,up,window)==null,"Eye in front of the sight sees the reticle");
            foreach(var corners in new[] {new[] {new Vector2(-.2f,.3f),new Vector2(.4f,.3f),new Vector2(.4f,-.1f),new Vector2(-.2f,-.1f)},
                new[] {new Vector2(-.2f,-.1f),new Vector2(.4f,-.1f),new Vector2(.4f,.3f),new Vector2(-.2f,.3f)}})
            {
                var clips=HoloSight.Crop(new[] {corners,corners.Select(c=>c*1.5f).ToArray()},new Vector2(.1f,.1f));
                bool Kept(Vector2 p) => clips.All(c=>c.X*p.X+c.Y*p.Y+c.Z>=0);
                Require(Kept(new Vector2(.35f,.25f)) && !Kept(new Vector2(.45f,.1f)) && !Kept(new Vector2(.1f,-.15f)) && !Kept(new Vector2(-.25f,.2f)) && !Kept(new Vector2(0,.35f)),
                    "Reticle crop does not follow the projected opening");
            }
            var rear=new[] {new Vector2(-.2f,.2f),new Vector2(.2f,.2f),new Vector2(.2f,-.2f),new Vector2(-.2f,-.2f)};
            var front=rear.Select(c=>c*1.3f+new Vector2(.15f,0)).ToArray();
            var both=HoloSight.Crop(new[] {rear,front},Vector2.Zero);
            bool Inside(Vector2 p) => both.All(c=>c.X*p.X+c.Y*p.Y+c.Z>=0);
            Require(Inside(new Vector2(.19f,.19f)) && !Inside(new Vector2(-.21f,0)) && !Inside(new Vector2(.21f,0)) && !Inside(new Vector2(0,.21f)),"Two-rim crop does not keep the nearer edges");
            log("PASS holographic sights: five installed models lose only their painted reticle and keep their magazine subparts, reticle masks surround the aim point, window gating of the parallel sight line, crop to the nearest projected rim");
        }
    }
}
#endif
