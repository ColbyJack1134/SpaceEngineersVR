using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class WristKnob
    {
        internal static readonly RectangleF Bounds=new RectangleF(.0175f,.80f,.10f,.17778f);
        internal static readonly RectangleF Reserved=new RectangleF(0,.78f,.135f,.22f);
        internal const float Travel=MathHelper.Pi*1.5f;
        internal static SurfaceKey Key() => new SurfaceKey("Tint",Bounds.X,Bounds.Y,Bounds.Width,Bounds.Height) {
            Knob=Common.Config?.WristSignalTint/.75f ?? .2f,Invisible=true,DirectOnly=true,Round=true };
        internal static Vector3 Center(SurfaceView s) => new Vector3((Bounds.Center.X-.5f)*s.Width,(.5f-Bounds.Center.Y)*s.Height,.013f*s.Width/.4f);
        internal sealed class Turn
        {
            private Matrix previous,grab;
            private readonly Vector3 axis;
            private readonly float travel,direction;
            public Turn(Vector3? axis=null,float travel=Travel,float direction=-1)
            { this.axis=Vector3.Normalize(axis ?? Vector3.Backward); this.travel=travel; this.direction=direction; }
            private float initial;
            public float Value {get; private set;}
            public Matrix Wrist {get; private set;}
            public void Begin(Matrix local,float value) {previous=grab=local; initial=Value=value; Wrist=local;}
            public void Move(Matrix local)
            {
                if(!local.IsValid()) return;
                var delta=Quaternion.CreateFromRotationMatrix(Matrix.Invert(previous.GetOrientation())*local.GetOrientation());
                float projected=Vector3.Dot(new Vector3(delta.X,delta.Y,delta.Z),axis);
                float length=(float)Math.Sqrt(projected*projected+delta.W*delta.W);
                if(length<.001f) return;
                float angle=2*(float)Math.Atan2(projected/length,delta.W/length);
                if(angle>MathHelper.Pi) angle-=MathHelper.TwoPi;
                if(angle < -MathHelper.Pi) angle+=MathHelper.TwoPi;
                previous=local;
                Value=MathHelper.Clamp(Value+direction*angle/travel,0,1);
                Wrist=grab*Matrix.CreateFromAxisAngle(axis,(Value-initial)*travel/direction);
            }
        }
        internal static void Add(List<NativeSprite> sprites,ShaderResourceView texture,SurfaceView panel,float value,MatrixD view,MatrixD projection)
        {
            float unit=panel.Width/.4f,r=.015f*unit;
            var center=Center(panel); center.Z=0;
            var pose=MatrixD.CreateTranslation(center)*panel.Pose;
            var faces=new List<Tuple<double,NativeSprite>>();
            var uv=new Vector4(1022.5f/1024,638.5f/640,0,0);
            Action<Vector3D,Vector3D,Vector3D,Vector3D,Vector4> face=(a,b,c,d,color)=> {
                var transform=pose*view*projection;
                Func<Vector3D,Vector4> clip=p=>(Vector4)Vector4D.Transform(new Vector4D(p,1),transform);
                faces.Add(Tuple.Create(Vector3D.Transform((a+b+c+d)*.25,pose*view).Z,new NativeSprite(null,default(RectangleF),color) {Texture=texture,
                    Projected=true,UV=uv,TopLeft=clip(a),TopRight=clip(b),BottomLeft=clip(c),BottomRight=clip(d) }));
            };
            double rotation=(value-.5)*Travel;
            Func<double,double,double,Vector3D> point=(angle,radius,z)=>new Vector3D(Math.Sin(angle)*radius,Math.Cos(angle)*radius,z);
            for(int i=0;i<32;i++)
            {
                double a=i*Math.PI/16+rotation,b=(i+1)*Math.PI/16+rotation;
                double ra=r*(i%2==0 ? 1:.92),rb=r*((i+1)%2==0 ? 1:.92);
                float light=.62f+.24f*(float)Math.Cos((a+b)*.5-.7);
                face(point(a,r*1.25,0),point(b,r*1.25,0),point(a,r*1.25,.002*unit),point(b,r*1.25,.002*unit),new Vector4(.24f,.29f,.32f,1));
                face(point(a,ra,.002*unit),point(b,rb,.002*unit),point(a,ra,.011*unit),point(b,rb,.011*unit),new Vector4(.25f*light,.29f*light,.32f*light,1));
                face(point(a,ra,.011*unit),point(b,rb,.011*unit),point(a,r*.80,.014*unit),point(b,r*.80,.014*unit),new Vector4(.42f*light,.47f*light,.5f*light,1));
                face(point(a,r*.80,.014*unit),point(b,r*.80,.014*unit),new Vector3D(0,0,.014*unit),new Vector3D(0,0,.014*unit),new Vector4(.095f,.12f,.135f,1));
            }
            sprites.AddRange(faces.OrderBy(f=>f.Item1).Select(f=>f.Item2));
            var indexPose=MatrixD.CreateRotationZ(-rotation)*pose;
            sprites.Add(PhysicalSurface.Quad(texture,indexPose,new RectangleF(-.001f*unit,r*.76f,.002f*unit,r*.57f),uv,new Vector4(.9f,.95f,.91f,1),view,projection,.0142f*unit));
            for(int tick=0;tick<7;tick++)
            {
                var tickPose=MatrixD.CreateRotationZ((tick/6.0-.5)*Travel)*pose;
                sprites.Add(PhysicalSurface.Quad(texture,tickPose,new RectangleF(-.00045f*unit,r*1.43f,.0009f*unit,.002f*unit),uv,new Vector4(.48f,.62f,.67f,1),view,projection,.0022f*unit));
            }
        }
    }
}
