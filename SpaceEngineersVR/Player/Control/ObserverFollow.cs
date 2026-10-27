using System;
using System.Diagnostics;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal enum ObserverMode { Ship,Heading,Fixed }

    internal sealed class ObserverFollow
    {
        private const double CorrectionTime=.6,MaxCorrection=Math.PI/3;
        private MatrixD ship=MatrixD.Identity,heading=MatrixD.Identity;
        private Vector3D up=Vector3D.Up;
        private long time;
        public void Reset(MatrixD pose,Vector3D vertical)
        {
            ship=pose.GetOrientation(); up=Vector3D.Normalize(vertical);
            heading=VrMath.Level(ship,up); time=0;
        }
        public void Advance(MatrixD pose,Vector3D gravity,long timestamp)
        {
            double seconds=time==0 ? 0 : MathHelper.Clamp((double)(timestamp-time)/Stopwatch.Frequency,0,.1);
            time=timestamp;
            var next=pose.GetOrientation();
            if(gravity.LengthSquared()>.01) // Zero gravity keeps the last vertical.
            {
                var vertical=-Vector3D.Normalize(gravity);
                var axis=Vector3D.Cross(up,vertical);
                if(axis.LengthSquared()<=1e-18 && Vector3D.Dot(up,vertical)<0) axis=Vector3D.Cross(up,heading.Forward);
                double angle=Math.Min(Math.Atan2(axis.Length(),Vector3D.Dot(up,vertical)),MaxCorrection*seconds);
                if(axis.LengthSquared()>1e-18 && angle>0)
                {
                    var swing=MatrixD.CreateFromAxisAngle(Vector3D.Normalize(axis),angle);
                    heading*=swing; up=Vector3D.Normalize(Vector3D.TransformNormal(up,swing));
                }
            }
            // Incremental heading remains defined while the ship points straight up/down.
            double yaw=Twist(MatrixD.Transpose(ship)*next,up);
            heading*=MatrixD.CreateFromAxisAngle(up,yaw);
            // Twist alone misses turns made while tilted; ease to the nose only while upright so pitch flips keep the view.
            double upright=MathHelper.Clamp((Vector3D.Dot(next.Up,up)-.5)/.2,0,1);
            if(upright>0 && seconds>0)
            {
                var nose=VrMath.Level(next,up).Forward;
                double error=Math.Atan2(Vector3D.Dot(Vector3D.Cross(heading.Forward,nose),up),Vector3D.Dot(heading.Forward,nose));
                double step=upright*Math.Min(Math.Abs(error)*(1-Math.Exp(-seconds/CorrectionTime)),MaxCorrection*seconds);
                heading*=MatrixD.CreateFromAxisAngle(up,Math.Sign(error)*step);
            }
            heading=VrMath.Level(heading,up);
            ship=next;
        }
        public MatrixD Reference(ObserverMode mode) => mode==ObserverMode.Ship ? ship : mode==ObserverMode.Heading ? heading : MatrixD.Identity;
        internal static double Twist(MatrixD rotation,Vector3D axis)
        {
            var q=QuaternionD.CreateFromRotationMatrix(rotation);
            double along=q.X*axis.X+q.Y*axis.Y+q.Z*axis.Z;
            return Math.Abs(along)+Math.Abs(q.W)<1e-10 ? 0 : 2*Math.Atan2(along,q.W);
        }
    }
}
