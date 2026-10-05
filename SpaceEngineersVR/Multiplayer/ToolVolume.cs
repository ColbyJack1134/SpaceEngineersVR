using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal struct ToolVolume
    {
        internal MatrixD Frame;
        internal double Length,Radius,HalfHeight;
        internal bool Disc;
        internal Vector3D Start => Frame.Translation;
        internal Vector3D End => Start+Frame.Forward*Length;
        internal BoundingSphereD Bounds => new BoundingSphereD((Start+End)*.5,Length*.5+(Disc ? System.Math.Sqrt(Radius*Radius+HalfHeight*HalfHeight):Radius));
        internal static ToolVolume For(WeaponProfile profile,MatrixD model)
        {
            if(profile.Kind==ItemKind.Welder) return new ToolVolume {Frame=HeldItemPose.Working(model,profile),Length=.185,Radius=.038};
            var frame=model;
            frame.Translation=Vector3D.Transform(profile.Kind==ItemKind.Grinder ? new Vector3D(.059738,-.016757,-.165594):new Vector3D(0,0,-.61),model);
            return new ToolVolume {Frame=frame,Length=profile.Kind==ItemKind.Drill ? .15:0,Radius=profile.Kind==ItemKind.Grinder ? .108:.10,HalfHeight=.018,Disc=profile.Kind==ItemKind.Grinder};
        }
        internal LineD Probe(int index)
        {
            Vector3D center=(Start+End)*.5, direction;
            double reach;
            if(Disc)
            {
                double angle=index*System.Math.PI/6;
                direction=index==6 ? Frame.Up:Frame.Right*System.Math.Cos(angle)+Frame.Forward*System.Math.Sin(angle);
                reach=index==6 ? HalfHeight:Radius;
            }
            else
            {
                direction=index<3 ? (index==0 ? Frame.Right:index==1 ? Frame.Up:Frame.Forward):
                    Vector3D.Normalize(Frame.Forward+Frame.Right*System.Math.Cos(index*System.Math.PI/2)+Frame.Up*System.Math.Sin(index*System.Math.PI/2));
                reach=Radius+Length*.5;
            }
            return new LineD(center-direction*(reach+.004),center+direction*(reach+.004));
        }
        internal bool Contains(Vector3D world,double tolerance=.002)
        {
            var point=Vector3D.Transform(world,MatrixD.Invert(Frame));
            if(Disc) return System.Math.Abs(point.Y)<=HalfHeight+tolerance && point.X*point.X+point.Z*point.Z<=(Radius+tolerance)*(Radius+tolerance);
            var nearest=new Vector3D(0,0,MathHelper.Clamp(point.Z,-Length,0));
            return Vector3D.DistanceSquared(point,nearest)<=(Radius+tolerance)*(Radius+tolerance);
        }
    }
}
