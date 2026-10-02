using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using VRageMath;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal sealed partial class CockpitRig
    {
        internal sealed class Stick
        {
            public readonly Vector3 Contact,Pivot,Shaft;
            public readonly int Actor,BaseActor;
            private readonly float gripPitch;
            public Stick(Vector3 contact,Vector3 pivot,Vector3 shaft,int actor,int baseActor,float gripPitch=0)
            { Contact=contact; Pivot=pivot; Shaft=Vector3.Normalize(shaft); Actor=actor; BaseActor=baseActor; this.gripPitch=gripPitch; }
            public Matrix Palm(bool left) => CockpitStickMath.GripPalm(left,Contact,Shaft)*CockpitStickMath.Around(Contact,Matrix.CreateRotationX(gripPitch));
        }
        internal sealed class Piece
        {
            public readonly string Material;
            public readonly int MaterialTriangles,Triangles,Actor;
            public readonly Vector3 Center;
            public Piece(string material,int total,int triangles,Vector3 center,int actor)
            { Material=material; MaterialTriangles=total; Triangles=triangles; Center=center; Actor=actor; }
        }
        internal sealed class Lever
        {
            public readonly Vector3 Center,Pivot,Normal,Axis;
            public readonly int Actor,CoverActor;
            public readonly Vector3 CoverCenter,Hinge;
            public readonly float CoverInitial;
            public readonly int TemplateActor;
            public readonly Matrix TemplateTransform;
            public readonly Piece TemplateBase;
            public Lever(Vector3 center,Vector3 pivot,Vector3 normal,Vector3 axis,int actor,Vector3 coverCenter,Vector3 hinge,int coverActor,float initial,int templateActor=-1,Matrix? templateTransform=null,Piece templateBase=null)
            { Center=center; Pivot=pivot; Normal=Vector3.Normalize(normal); Axis=Vector3.Normalize(axis); Actor=actor; CoverCenter=coverCenter; Hinge=hinge; CoverActor=coverActor; CoverInitial=initial; TemplateActor=templateActor; TemplateTransform=templateTransform ?? Matrix.Identity; TemplateBase=templateBase; }
            public Vector3 Up => Vector3.Normalize(Vector3.Cross(Axis,Normal));
            public Matrix Visual(float value)
            {
                var stem=Center-Pivot;
                float rest=(float)Math.Atan2(Vector3.Dot(stem,Up),Vector3.Dot(stem,Normal));
                return CockpitStickMath.Around(Pivot,Matrix.CreateFromAxisAngle(Axis,(MathHelper.Clamp(value,0,1)-.5f)*CockpitSwitchGeometry.Travel-rest));
            }
            public Matrix CoverVisual(float value) => CockpitStickMath.Around(Hinge,Matrix.CreateFromAxisAngle(Axis,(value-CoverInitial)*CockpitCoverGeometry.Travel));
            public MatrixD CoverPose(float value)
            {
                Vector3 center=Vector3.Transform(CoverCenter,CoverVisual(value)),leaf=center-Hinge;
                var normal=Vector3.TransformNormal(Normal,Matrix.CreateFromAxisAngle(Axis,value*CockpitCoverGeometry.Travel));
                return MatrixD.CreateWorld(center+leaf*.45f+normal*.002f,-normal,Vector3.Normalize(leaf));
            }
        }
        public readonly string Subtype,Model;
        private readonly string geometryModel;
        public readonly MatrixD SeatMount;
        public readonly Stick Left,Right;
        public readonly Piece[] Pieces;
        public readonly Lever[] Levers;
        public readonly int ActorCount;
        private CockpitGeometry geometry;
        private CockpitRig(string subtype,string model,string geometryModel,Vector3 panel,Vector3 normal,Stick left,Stick right,Piece[] pieces,Lever[] levers)
        {
            Subtype=subtype; Model=model; this.geometryModel=geometryModel; Left=left; Right=right; Pieces=pieces; Levers=levers;
            var up=Vector3.Normalize(Vector3.Cross(normal,Vector3.Right));
            SeatMount=MatrixD.CreateWorld(panel,-normal,up);
            ActorCount=pieces.Length==0 ? 0 : pieces.Max(p=>p.Actor)+1;
        }
        private static readonly Dictionary<string,CockpitRig> rigs=Create().ToDictionary(p=>p.Subtype,StringComparer.Ordinal);
        internal static IEnumerable<CockpitRig> All => rigs.Values;
        internal static CockpitRig Find(string subtype) => subtype!=null && rigs.TryGetValue(subtype,out var value) ? value:null;
        internal bool HasSticks => Left!=null || Right!=null;
        internal bool Matches(string model) => model!=null && model.Replace('\\','/').EndsWith(Model,StringComparison.OrdinalIgnoreCase);
        internal CockpitGeometry Geometry(string content)
        {
            if(geometry!=null) return geometry;
            var importer=new MyModelImporter();
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(content,geometryModel.Replace('/',Path.DirectorySeparatorChar)))))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {reader,new[] {"Vertices","TexCoords0","MeshParts","Normals","Tangents"}});
            var tags=importer.GetTagData();
            var actors=Enumerable.Range(0,ActorCount).Select(_=>new MyModelData()).ToArray();
            foreach(var actor in actors) actor.Clear();
            int triangles=0;
            foreach(var material in Pieces.GroupBy(p=>p.Material))
            {
                var pieces=material.ToArray();
                var parts=CockpitSwitchGeometry.Partition(tags,material.Key,pieces[0].MaterialTriangles,pieces.Select(p=>p.Center).ToArray(),pieces.Select(p=>p.Triangles).ToArray());
                Append(actors[0],parts[0]);
                for(int i=0;i<pieces.Length;i++) Append(actors[pieces[i].Actor],parts[i+1]);
                triangles+=pieces[0].MaterialTriangles;
            }
            var templateBases=new Dictionary<Piece,MyModelData>();
            foreach(var material in Levers.Where(l=>l?.TemplateBase!=null).Select(l=>l.TemplateBase).Distinct().GroupBy(p=>p.Material))
            {
                var pieces=material.GroupBy(p=>p.Center).Select(g=>g.First()).ToArray();
                var parts=CockpitSwitchGeometry.Partition(tags,material.Key,pieces[0].MaterialTriangles,pieces.Select(p=>p.Center).ToArray(),pieces.Select(p=>p.Triangles).ToArray());
                foreach(var piece in material) templateBases[piece]=parts[Array.FindIndex(pieces,p=>p.Center==piece.Center)+1];
            }
            // Some native closed housings omit the hidden lever. Reuse a measured
            // lever from the same installed model instead of shipping a game mesh.
            foreach(var lever in Levers.Where(l=>l!=null && l.TemplateActor>=0))
            {
                AppendTransformed(actors[lever.Actor],actors[lever.TemplateActor],lever.TemplateTransform);
                var part=lever.TemplateBase;
                if(part!=null)
                    AppendTransformed(actors[0],templateBases[part],lever.TemplateTransform);
            }
            if(actors.Skip(1).Any(a=>a.Indices.Count==0)) throw new InvalidDataException("Cockpit rig has an empty actor: "+Subtype);
            return geometry=new CockpitGeometry(actors,triangles);
        }
        private static void Append(MyModelData target,MyModelData source)
        {
            if(source.Indices.Count==0) return;
            int vertex=target.Positions.Count,index=target.Indices.Count;
            target.Positions.AddRange(source.Positions); target.Indices.AddRange(source.Indices.Select(i=>i+vertex));
            target.Normals.AddRange(source.Normals); target.Tangents.AddRange(source.Tangents); target.TexCoords.AddRange(source.TexCoords);
            foreach(var section in source.Sections) target.Sections.Add(new MyRuntimeSectionInfo { IndexStart=section.IndexStart+index,TriCount=section.TriCount,MaterialName=section.MaterialName });
            target.AABB.Include(source.AABB);
        }
        private static void AppendTransformed(MyModelData target,MyModelData source,Matrix transform)
        {
            int first=target.Positions.Count;
            Append(target,source);
            for(int i=0;i<source.Positions.Count;i++)
            {
                target.Positions[first+i]=Vector3.Transform(source.Positions[i],transform);
                target.Normals[first+i]=Vector3.Normalize(Vector3.TransformNormal(source.Normals[i],transform));
                target.Tangents[first+i]=Vector3.Normalize(Vector3.TransformNormal(source.Tangents[i],transform));
            }
            target.AABB=BoundingBox.CreateInvalid();
            foreach(var position in target.Positions) target.AABB.Include(position);
        }
    }
}
