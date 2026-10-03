using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal struct MarkerBillboard
    {
        public const double IconAngle = Math.PI / 90; // Two degrees, independent of eye target resolution.
        public Vector3D Center, Right, Up;
        public double Scale;

        public static bool TryCreate(Vector3D position, MatrixD head, out MarkerBillboard billboard)
        {
            billboard=default(MarkerBillboard);
            Vector3D towardHead=head.Translation-position;
            double distance=towardHead.Length();
            if (!position.IsValid() || !head.IsValid() || !distance.IsValid() || distance<0.1) return false;
            billboard=new MarkerBillboard { Center=position,Right=Vector3D.Normalize(head.Right),Up=Vector3D.Normalize(head.Up),
                Scale=2*distance*Math.Tan(IconAngle/2) };
            return true;
        }

        public Vector3D Point(double x, double y) => Center+Scale*(Right*x-Up*y);

        public bool Project(RectangleF bounds, MatrixD view, MatrixD projection, ref NativeSprite sprite)
        {
            if (!Clip(Point(bounds.X,bounds.Y),view,projection,out sprite.TopLeft) ||
                !Clip(Point(bounds.X+bounds.Width,bounds.Y),view,projection,out sprite.TopRight) ||
                !Clip(Point(bounds.X,bounds.Y+bounds.Height),view,projection,out sprite.BottomLeft) ||
                !Clip(Point(bounds.X+bounds.Width,bounds.Y+bounds.Height),view,projection,out sprite.BottomRight)) return false;
            sprite.Projected=true;
            return true;
        }

        private static bool Clip(Vector3D world, MatrixD view, MatrixD projection, out Vector4 clip)
        {
            // Subtract the eye in double precision before sending small clip coordinates to D3D.
            var local=Vector3D.Transform(world,view);
            var projected=Vector4D.Transform(new Vector4D(local,1),projection);
            clip=(Vector4)projected;
            return local.IsValid() && local.Z<-0.05 && projected.W>0 &&
                (projected.X+projected.Y+projected.Z+projected.W).IsValid();
        }
    }
}
