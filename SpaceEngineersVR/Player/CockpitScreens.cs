using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Game.Entities;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;

namespace SpaceEngineersVR.Player
{
    // Cockpit LCD rectangles measured from the installed interior mesh, once per model.
    internal static class CockpitScreens
    {
        internal sealed class Area
        {
            public int Index;
            public Vector3 Center,Normal,Right,Up;
            public float Width,Height;
        }
        private static readonly Dictionary<string,Area[]> cache=new Dictionary<string,Area[]>();
        // Overhead, handlebar, steep or distant screens; windows in these seats use the on-foot default.
        private static readonly HashSet<string> skipped=new HashSet<string> {"SpeederCockpit","SpeederCockpitCompact","SmallBlockStandingCockpit","LargeBlockStandingCockpit",
            "LargeBlockSuspendedControlSeat","SmallBlockSuspendedControlSeat","LargeBlockSuspendedControlSeatB","SmallBlockSuspendedControlSeatB",
            "DBSmallBlockFighterCockpit"};

        internal static Area[] Areas(MyCockpit cockpit)
        {
            var definition=cockpit.BlockDefinition;
            string model=definition.InteriorModel ?? definition.Model;
            if(model==null || definition.ScreenAreas==null || definition.ScreenAreas.Count==0) return new Area[0];
            var materials=definition.ScreenAreas.Select(screen=>screen.Name).ToArray();
            // Area.Index follows this definition's screen order, so definitions sharing a model are cached apart.
            string key=model+"|"+string.Join("|",materials);
            if(cache.TryGetValue(key,out var areas)) return areas;
            try { areas=Measure(model,materials); }
            catch(Exception ex) { Logger.Warning(ex,"Cockpit screens unavailable for {0}",model); areas=new Area[0]; }
            return cache[key]=areas;
        }

        internal static Area[] Measure(string model,string[] materials)
        {
            string path=Path.IsPathRooted(model) ? model:Path.Combine(MyFileSystem.ContentPath,model);
            var tags=HoloSight.Tags(path,"Vertices","MeshParts","GeometryDataAsset");
            var parts=tags.TryGetValue("MeshParts",out var found) ? (List<MyMeshPartInfo>)found:null;
            // Some interiors keep their vertices in a separate geometry asset under Content.
            if(!tags.ContainsKey("Vertices") && tags.TryGetValue("GeometryDataAsset",out var asset) && asset is string geometry)
            {
                if(!geometry.EndsWith(".mwm",StringComparison.OrdinalIgnoreCase)) geometry+=".mwm";
                var shared=HoloSight.Tags(Path.IsPathRooted(geometry) ? geometry:Path.Combine(MyFileSystem.ContentPath,geometry),"Vertices","MeshParts");
                tags["Vertices"]=shared["Vertices"];
                if(parts==null || parts.Count==0) parts=(List<MyMeshPartInfo>)shared["MeshParts"];
            }
            var vertices=(HalfVector4[])tags["Vertices"];
            return materials.Select((name,i)=>Measure(i,parts.FirstOrDefault(p=>p.m_MaterialDesc?.MaterialName==name),vertices)).Where(a=>a!=null).ToArray();
        }

        private static Area Measure(int index,MyMeshPartInfo part,HalfVector4[] vertices)
        {
            if(part==null || part.m_indices.Count<3) return null;
            var points=part.m_indices.Select(i => { var v=vertices[i].ToVector4(); return new Vector3(v.X,v.Y,v.Z); }).ToArray();
            var normal=Vector3.Zero;
            for(int i=0;i+2<points.Length;i+=3) normal+=Vector3.Cross(points[i+1]-points[i],points[i+2]-points[i]);
            if(normal.LengthSquared()<1e-10f) return null;
            normal.Normalize();
            var right=Vector3.Cross(Vector3.Up,normal);
            if(right.LengthSquared()<.01f) right=Vector3.Right;
            right.Normalize();
            var up=Vector3.Cross(normal,right);
            var mean=points.Aggregate(Vector3.Zero,(a,b)=>a+b)/points.Length;
            float minX=float.MaxValue,maxX=float.MinValue,minY=float.MaxValue,maxY=float.MinValue;
            foreach(var p in points)
            {
                float x=Vector3.Dot(p-mean,right),y=Vector3.Dot(p-mean,up);
                minX=Math.Min(minX,x); maxX=Math.Max(maxX,x); minY=Math.Min(minY,y); maxY=Math.Max(maxY,y);
            }
            return new Area {Index=index,Center=mean+right*(minX+maxX)/2+up*(minY+maxY)/2,Normal=normal,Right=right,Up=up,Width=maxX-minX,Height=maxY-minY};
        }

        // The screen that looks biggest from the eye, favoring screens straight ahead; world pose faces the eye.
        internal static bool Main(MyCockpit cockpit,Vector3D eye,out MatrixD pose,out Area main)
        {
            pose=MatrixD.Identity; main=null; double best=0;
            if(skipped.Contains(cockpit.BlockDefinition.Id.SubtypeName)) return false;
            foreach(var area in Areas(cockpit))
            {
                var world=CockpitRender.SurfaceWorld(cockpit,area.Index);
                var local=(Vector3)Vector3D.Transform(eye,MatrixD.Invert(world));
                var toward=area.Center-local; float distance=toward.Length();
                // Screens above the eye, such as the Industrial top row, are skipped.
                if(distance<.05f || toward.Y>0) continue;
                toward/=distance;
                double score=area.Width*area.Height*Math.Abs(Vector3.Dot(area.Normal,toward))/(distance*distance)*Math.Pow(Math.Max(-toward.Z,0),2);
                if(score<=best) continue;
                best=score; main=area;
                var normal=Vector3.Dot(area.Normal,toward)<0 ? area.Normal:-area.Normal;
                pose=MatrixD.CreateWorld(area.Center+normal*.005f,-normal,area.Up)*world;
            }
            return main!=null;
        }

        internal static float Fit(Area area,float aspect,float minimumWidth) =>
            Math.Max(Math.Min(area.Width,area.Height/aspect),minimumWidth);
    }
}
