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
            private readonly Vector3 gripShaft;
            internal readonly Matrix Frame;
            public readonly float GripLift,GripInset;
            public Stick(Vector3 contact,Vector3 pivot,Vector3 shaft,int actor,int baseActor,float gripPitch=0,float gripLift=0,float gripInset=0,Vector3? gripShaft=null)
            {
                Contact=contact; Pivot=pivot; Shaft=Vector3.Normalize(shaft); Actor=actor; BaseActor=baseActor; this.gripPitch=gripPitch; GripLift=gripLift; GripInset=gripInset;
                this.gripShaft=Vector3.Normalize(gripShaft ?? Shaft);
                Frame=CockpitStickMath.ShaftFrame(Shaft);
            }
            internal Matrix Visual(Vector3 axes) => CockpitStickMath.Visual(Pivot,axes,Frame);
            public Matrix Palm(bool left) => CockpitStickMath.RaiseGrip(
                CockpitStickMath.GripPalm(left,Contact,gripShaft)*CockpitStickMath.Around(Contact,Matrix.CreateRotationX(gripPitch)),gripShaft,GripLift,GripInset);
        }
        internal sealed class Piece
        {
            public readonly string Material;
            public readonly int MaterialTriangles,Triangles,Actor,StaticActor;
            public readonly int FirstTriangle,MovingTriangles;
            public readonly Vector3 Center;
            public Piece(string material,int total,int triangles,Vector3 center,int actor,int staticActor=0,int firstTriangle=0,int movingTriangles=-1)
            { Material=material; MaterialTriangles=total; Triangles=triangles; Center=center; Actor=actor; StaticActor=staticActor;
                FirstTriangle=firstTriangle; MovingTriangles=movingTriangles<0 ? triangles:movingTriangles; }
        }
        internal sealed class Handle
        {
            public readonly Vector3 Center;
            public readonly int Actor;
            internal const float Rear=-.085f,Front=-.258f,Travel=Rear-Front;
            // Upper slot contour measured from the installed Control Seat housing.
            private static readonly Vector2[] track={new Vector2(-.0767212f,-.621582f),new Vector2(-.0938721f,-.6147461f),
                new Vector2(-.1877441f,-.5917969f),new Vector2(-.1989746f,-.5893555f),new Vector2(-.210083f,-.5888672f),
                new Vector2(-.2322998f,-.5922852f),new Vector2(-.2487793f,-.5952148f),new Vector2(-.2658691f,-.6000977f),new Vector2(-.2885742f,-.6088867f)};
            public Handle(float x,int actor) { Center=new Vector3(x,-.54296875f,-.20050049f); Actor=actor; }
            public readonly Vector3 Axis=Vector3.Forward,Pivot;
            public readonly float Range=Travel,HalfWidth=.06f,Radius=.0155f;
            public readonly bool Hinged;
            public readonly bool Pinch;
            private readonly Matrix frame=Matrix.Identity;
            private readonly Vector3 bottom,top;
            private readonly float bottomAngle;
            private readonly bool rotateGrip;
            private readonly bool contour=true;
            public Handle(Vector3 center,int actor,Vector3 shaft,Vector3 normal,Vector3 bottom,Vector3 top,float halfWidth,float radius,bool pinch=false)
            {
                Center=center; Actor=actor; HalfWidth=halfWidth; Radius=radius;
                frame=Frame(shaft,normal); this.bottom=bottom; this.top=top;
                Axis=Vector3.Normalize(top-bottom); Range=Vector3.Distance(bottom,top); contour=false; Pinch=pinch;
            }
            public Handle(Vector3 center,int actor,Vector3 shaft,Vector3 normal,Vector3 pivot,Vector3 axis,float bottomAngle,float topAngle,float halfWidth,float radius,bool rotateGrip=false)
            {
                Center=center; Actor=actor; HalfWidth=halfWidth; Radius=radius;
                frame=Frame(shaft,normal); Pivot=pivot; Axis=Vector3.Normalize(axis);
                this.bottomAngle=bottomAngle; Range=topAngle-bottomAngle; Hinged=true; contour=false; this.rotateGrip=rotateGrip;
            }
            private static Matrix Frame(Vector3 shaft,Vector3 normal)
            {
                var result=Matrix.Identity; result.Right=Vector3.Normalize(shaft);
                result.Up=Vector3.Normalize(normal-result.Right*Vector3.Dot(normal,result.Right));
                result.Backward=Vector3.Cross(result.Right,result.Up); return result;
            }
            private static float Height(float z)
            {
                for(int i=1;i<track.Length;i++) if(z>=track[i].X)
                    return MathHelper.Lerp(track[i-1].Y,track[i].Y,(z-track[i-1].X)/(track[i].X-track[i-1].X));
                return track[track.Length-1].Y;
            }
            internal Matrix Visual(float position)
            {
                position=MathHelper.Clamp(position,0,1);
                if(Hinged) return CockpitStickMath.Around(Pivot,Matrix.CreateFromAxisAngle(Axis,bottomAngle+Range*position));
                if(!contour) return Matrix.CreateTranslation(Vector3.Lerp(bottom,top,position)-Center);
                float z=MathHelper.Lerp(Rear,Front,position);
                return Matrix.CreateTranslation(0,Height(z)-Height(Center.Z),z-Center.Z);
            }
            internal Matrix Palm(bool left,float position,float offset=0)
            {
                var grip=CockpitStickMath.GripPalm(left,Vector3.Right*MathHelper.Clamp(offset,-GripOffset,GripOffset),left ? Vector3.Right:Vector3.Left)*frame;
                // Round crossbars allow a fixed wrist roll; shaped grips turn with the handle.
                return rotateGrip ? grip*Matrix.CreateTranslation(Center)*Visual(position) :
                    grip*Matrix.CreateTranslation(Vector3.Transform(Center,Visual(position)));
            }
            internal float GripOffset => Math.Min(.02f,Math.Max(0,HalfWidth-.035f));
            internal MatrixD TouchPose => MatrixD.CreateWorld(Center+frame.Up*Radius,-frame.Up,frame.Forward);
        }
        internal sealed class Lever
        {
            internal const float Travel=1.05f;
            // 65 degrees between the installed open and closed cover leaf faces.
            internal const float CoverTravel=1.134464f;
            public readonly Vector3 Center,Pivot,Normal,Axis,Up;
            public readonly int Actor,CoverActor;
            public readonly Vector3 CoverCenter,Hinge;
            public readonly float CoverInitial;
            public readonly int TemplateActor;
            public readonly Matrix TemplateTransform;
            public readonly Piece TemplateBase;
            private readonly float rest;
            public readonly float AngularTravel,Width,Height,ContactOffset;
            public readonly bool FingerSlide;
            public const float FingerTravel=.020f;
            public Vector3 SlideAxis => Up;
            public Lever(Vector3 center,Vector3 pivot,Vector3 normal,Vector3 axis,int actor,Vector3 coverCenter,Vector3 hinge,int coverActor,float initial,int templateActor=-1,Matrix? templateTransform=null,Piece templateBase=null,bool offAtRest=false,
                float travel=Travel,float width=.018f,float height=.018f,float contactOffset=.007f,bool fingerSlide=false)
            {
                Center=center; Pivot=pivot; Normal=Vector3.Normalize(normal); Axis=Vector3.Normalize(axis); Up=Vector3.Normalize(Vector3.Cross(Axis,Normal));
                Actor=actor; CoverCenter=coverCenter; Hinge=hinge; CoverActor=coverActor; CoverInitial=initial; TemplateActor=templateActor; TemplateTransform=templateTransform ?? Matrix.Identity; TemplateBase=templateBase;
                AngularTravel=travel; Width=width; Height=height; ContactOffset=contactOffset; FingerSlide=fingerSlide;
                // Some installed banks model the off detent; others sit between the detents.
                var stem=Center-Pivot;
                rest=offAtRest ? -AngularTravel*.5f : (float)Math.Atan2(Vector3.Dot(stem,Up),Vector3.Dot(stem,Normal));
            }
            public Matrix Visual(float value) => CockpitStickMath.Around(Pivot,Matrix.CreateFromAxisAngle(Axis,(MathHelper.Clamp(value,0,1)-.5f)*AngularTravel-rest));
            public Matrix CoverVisual(float value) => CockpitStickMath.Around(Hinge,Matrix.CreateFromAxisAngle(Axis,(MathHelper.Clamp(value,0,1)-CoverInitial)*CoverTravel));
            public MatrixD CoverPose(float value)
            {
                Vector3 center=Vector3.Transform(CoverCenter,CoverVisual(value)),leaf=center-Hinge;
                var normal=Vector3.TransformNormal(Normal,Matrix.CreateFromAxisAngle(Axis,value*CoverTravel));
                return MatrixD.CreateWorld(center+leaf*.45f+normal*.002f,-normal,Vector3.Normalize(leaf));
            }
        }
        internal sealed class Button
        {
            public readonly Vector3 Center,Normal,Up;
            public readonly float Size;
            public readonly int Actor;
            public readonly float Travel;
            public readonly bool Round;
            public Button(Vector3 center,Vector3 normal,Vector3 up,float size,int actor=-1,float travel=0,bool round=false)
            { Center=center; Normal=normal; Up=up; Size=size; Actor=actor; Travel=travel; Round=round; }
            internal MatrixD TouchPose => MatrixD.CreateWorld(Center,-Normal,Up);
            internal Matrix Visual(bool pressed) => Matrix.CreateTranslation(pressed ? -Normal*Travel:Vector3.Zero);
        }
        internal sealed class Bar
        {
            public readonly Vector3 Front,Normal,Up;
            public readonly float Travel,Width,Height;
            public readonly int Actor;
            public readonly float StemDepth;
            public Bar(Vector3 front,Vector3 normal,Vector3 up,float travel,float width,float height,int actor,float stemDepth)
            { Front=front; Normal=normal; Up=up; Travel=travel; Width=width; Height=height; Actor=actor; StemDepth=stemDepth; }
            internal MatrixD TouchPose => MatrixD.CreateWorld(Front,-Normal,Up);
            internal Matrix Visual(float position) => Matrix.CreateTranslation(Normal*(Travel*MathHelper.Clamp(position,0,1)));
            internal void ExtendStem(MyModelData mesh)
            {
                // Extra stem slides inside the opaque housing; no per-frame mesh uploads or new actor.
                var moved=new bool[mesh.Positions.Count];
                mesh.AABB=BoundingBox.CreateInvalid();
                for(int i=0;i<mesh.Positions.Count;i++)
                {
                    moved[i]=Vector3.Dot(mesh.Positions[i],Normal)<StemDepth;
                    if(moved[i]) mesh.Positions[i]-=Normal*Travel;
                    mesh.AABB.Include(mesh.Positions[i]);
                }
                for(int i=0;i<mesh.Indices.Count;i+=3)
                {
                    int a=mesh.Indices[i],b=mesh.Indices[i+1],c=mesh.Indices[i+2];
                    if(!moved[a] && !moved[b] && !moved[c]) continue;
                    Vector3 ab=mesh.Positions[b]-mesh.Positions[a],ac=mesh.Positions[c]-mesh.Positions[a];
                    Vector3 normal=Vector3.Normalize(Vector3.Cross(ab,ac));
                    if(Vector3.Dot(normal,mesh.Normals[a])<0) normal=-normal;
                    Vector2 du=mesh.TexCoords[b]-mesh.TexCoords[a],dv=mesh.TexCoords[c]-mesh.TexCoords[a];
                    float determinant=du.X*dv.Y-du.Y*dv.X;
                    Vector3 tangent=Math.Abs(determinant)>1e-8f ? (ab*dv.Y-ac*du.Y)/determinant : mesh.Tangents[a];
                    tangent=Vector3.Normalize(tangent-normal*Vector3.Dot(normal,tangent));
                    for(int j=0;j<3;j++) { int v=mesh.Indices[i+j]; mesh.Normals[v]=normal; mesh.Tangents[v]=tangent; }
                }
            }
        }
        internal sealed class Screen
        {
            public readonly Vector3 Center,Normal,Up;
            public readonly float Width,Height;
            public Screen(Vector3 center,Vector3 normal,Vector3 up,float width,float height) { Center=center; Normal=normal; Up=up; Width=width; Height=height; }
        }
        public readonly string Subtype,Model;
        private readonly string geometryModel;
        public readonly MatrixD SeatMount;
        public readonly Stick Left,Right;
        public readonly Piece[] Pieces;
        // Assignment slots, in persisted order: buttons, levers, handles, then bars.
        public readonly Button[] Buttons;
        public readonly Lever[] Levers;
        public readonly Handle[] Handles;
        public readonly Bar[] Bars;
        public readonly Screen[] Screens;
        private readonly int[] coverIndices;
        public readonly int ActorCount;
        public readonly int[] StaticActors;
        private CockpitGeometry geometry;
        private CockpitRig(string subtype,string model,string geometryModel,Vector3 panel,Vector3 normal,Stick left,Stick right,Piece[] pieces,Lever[] levers,Handle[] handles=null,
            Button[] buttons=null,Bar[] bars=null,Screen[] screens=null,bool packedCovers=false)
        {
            Subtype=subtype; Model=model; this.geometryModel=geometryModel; Left=left; Right=right; Pieces=pieces;
            Buttons=buttons ?? new Button[0]; Levers=levers; Handles=handles ?? new Handle[0]; Bars=bars ?? new Bar[0];
            Screens=screens ?? new Screen[0];
            var up=Vector3.Normalize(Vector3.Cross(normal,Vector3.Right));
            SeatMount=MatrixD.CreateWorld(panel,-normal,up);
            ActorCount=pieces.Length==0 ? 0 : pieces.Max(p=>Math.Max(p.Actor,p.StaticActor))+1;
            StaticActors=pieces.Select(p=>p.StaticActor).Distinct().ToArray();
            // Saved Fighter cover states predate slot-indexed storage and number covers consecutively.
            coverIndices=new int[Count];
            for(int slot=0,packed=0;slot<Count;slot++)
                coverIndices[slot]=LeverAt(slot)?.CoverActor>=0 ? packedCovers ? packed++ : slot : -1;
        }
        private static readonly Dictionary<string,CockpitRig> rigs=Create().ToDictionary(p=>p.Subtype,StringComparer.Ordinal);
        internal static IEnumerable<CockpitRig> All => rigs.Values;
        internal static CockpitRig Find(string subtype) => subtype!=null && rigs.TryGetValue(subtype,out var value) ? value:null;
        internal bool HasSticks => Left!=null || Right!=null;
        internal bool Matches(string model) => model!=null && model.Replace('\\','/').EndsWith(Model,StringComparison.OrdinalIgnoreCase);
        internal int Count => Buttons.Length+Levers.Length+Handles.Length+Bars.Length;
        internal Button ButtonAt(int slot) => At(Buttons,slot);
        internal Lever LeverAt(int slot) => At(Levers,slot-Buttons.Length);
        internal Handle HandleAt(int slot) => At(Handles,slot-Buttons.Length-Levers.Length);
        internal Bar BarAt(int slot) => At(Bars,slot-Buttons.Length-Levers.Length-Handles.Length);
        private static T At<T>(T[] items,int index) where T:class => index>=0 && index<items.Length ? items[index]:null;
        internal int CoverIndex(int slot) => slot>=0 && slot<coverIndices.Length ? coverIndices[slot]:-1;
        internal CockpitGeometry Geometry(string content)
        {
            if(geometry!=null) return geometry;
            long started=FeatureTiming.Start();
            try { return geometry=Load(content); }
            finally { FeatureTiming.End(FeatureTiming.Area.CockpitGeometry,started); }
        }
        private CockpitGeometry Load(string content)
        {
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
                var parts=CockpitGeometry.Partition(tags,material.Key,pieces[0].MaterialTriangles,pieces.Select(p=>p.Center).ToArray(),pieces.Select(p=>p.Triangles).ToArray(),
                    pieces.Select(p=>p.FirstTriangle).ToArray(),pieces.Select(p=>p.MovingTriangles).ToArray());
                Append(actors[pieces[0].StaticActor],parts[0]);
                for(int i=0;i<pieces.Length;i++) Append(actors[pieces[i].Actor],parts[i+1]);
                triangles+=pieces[0].MaterialTriangles;
            }
            var templateBases=new Dictionary<Piece,MyModelData>();
            foreach(var material in Levers.Where(l=>l.TemplateBase!=null).Select(l=>l.TemplateBase).Distinct().GroupBy(p=>p.Material))
            {
                var pieces=material.GroupBy(p=>p.Center).Select(g=>g.First()).ToArray();
                var parts=CockpitGeometry.Partition(tags,material.Key,pieces[0].MaterialTriangles,pieces.Select(p=>p.Center).ToArray(),pieces.Select(p=>p.Triangles).ToArray());
                foreach(var piece in material) templateBases[piece]=parts[Array.FindIndex(pieces,p=>p.Center==piece.Center)+1];
            }
            // Some native closed housings omit the hidden lever. Reuse a measured
            // lever from the same installed model instead of shipping a game mesh.
            foreach(var lever in Levers.Where(l=>l.TemplateActor>=0))
            {
                AppendTransformed(actors[lever.Actor],actors[lever.TemplateActor],lever.TemplateTransform);
                var part=lever.TemplateBase;
                if(part!=null)
                    AppendTransformed(actors[0],templateBases[part],lever.TemplateTransform);
            }
            foreach(var bar in Bars) bar.ExtendStem(actors[bar.Actor]);
            if(actors.Skip(1).Any(a=>a.Indices.Count==0)) throw new InvalidDataException("Cockpit rig has an empty actor: "+Subtype);
            // Native runtime meshes use 16-bit indices.
            if(actors.Any(a=>a.Positions.Count>ushort.MaxValue+1)) throw new InvalidDataException("Cockpit actor exceeds native index range: "+Subtype);
            return new CockpitGeometry(actors,triangles);
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
