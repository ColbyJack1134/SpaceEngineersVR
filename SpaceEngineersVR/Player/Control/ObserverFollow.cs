using System;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal enum ObserverMode { Ship,Heading,Fixed }

    internal sealed class ObserverFollow
    {
        private MatrixD ship=MatrixD.Identity,heading=MatrixD.Identity;
        private Vector3D up=Vector3D.Up;
        public void Reset(MatrixD pose,Vector3D vertical)
        {
            ship=pose.GetOrientation(); up=Vector3D.Normalize(vertical);
            heading=VrMath.Level(ship,up);
        }
        public void Advance(MatrixD pose)
        {
            var next=pose.GetOrientation();
            // Incremental heading remains defined while the ship points straight up/down.
            double yaw=Twist(MatrixD.Transpose(ship)*next,up);
            heading=VrMath.Rigid(heading*MatrixD.CreateFromAxisAngle(up,yaw));
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
