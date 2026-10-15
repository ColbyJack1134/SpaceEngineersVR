using System;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // The camera the picture-in-picture feed was last rendered with, so a point on the image maps back to a world ray.
    // Captured on the render thread straight after the feed's DrawGameScene; read on the game thread.
    internal static class FeedCamera
    {
        private sealed class Snapshot
        {
            public long Source;
            public Vector3D Position;
            public MatrixD Rotation;   // view-to-world rotation (InvViewAt0 has no translation)
            public double TanX,TanY;   // 1/Projection.M11 and 1/Projection.M22
            public DateTime Time;
        }
        private static volatile Snapshot latest;
        private const double MaxAgeSeconds=.5;

        internal static void Capture(long source)
        {
            var matrices=MyRender11.Environment_Matrices;
            var projection=matrices.Projection;
            if(projection.M11==0 || projection.M22==0) return;
            latest=new Snapshot
            {
                Source=source,
                Position=matrices.CameraPosition,
                Rotation=(MatrixD)matrices.InvViewAt0,
                TanX=1.0/projection.M11,
                TanY=1.0/projection.M22,
                Time=DateTime.UtcNow,
            };
        }

        internal static void Reset() { latest=null; }

        // uv: (0,0) top-left, (1,1) bottom-right of the feed image.
        internal static bool TryRay(long source,Vector2 uv,out Vector3D origin,out Vector3D direction)
        {
            origin=direction=Vector3D.Zero;
            var snapshot=latest;
            if(snapshot==null || snapshot.Source!=source || (DateTime.UtcNow-snapshot.Time).TotalSeconds>MaxAgeSeconds) return false;
            double ndcX=uv.X*2-1,ndcY=1-uv.Y*2;
            var view=new Vector3D(ndcX*snapshot.TanX,ndcY*snapshot.TanY,-1);
            direction=Vector3D.Normalize(Vector3D.TransformNormal(view,snapshot.Rotation));
            origin=snapshot.Position;
            return direction.IsValid();
        }

        internal static bool TryCentre(long source,out Vector3D origin,out Vector3D direction) =>
            TryRay(source,new Vector2(.5f,.5f),out origin,out direction);

        // The reverse of TryRay for a sphere: its centre on the image (uv) and half extents (uv), at least a small square;
        // false when the centre is behind the camera or off the image.
        internal static bool TryProject(long source,Vector3D centre,double radius,out Vector4 rect)
        {
            rect=Vector4.Zero;
            var snapshot=latest;
            if(snapshot==null || snapshot.Source!=source || (DateTime.UtcNow-snapshot.Time).TotalSeconds>MaxAgeSeconds) return false;
            var view=Vector3D.TransformNormal(centre-snapshot.Position,MatrixD.Transpose(snapshot.Rotation));
            if(view.Z>-.1) return false;
            double depth=-view.Z,ndcX=view.X/depth/snapshot.TanX,ndcY=view.Y/depth/snapshot.TanY;
            if(Math.Abs(ndcX)>1 || Math.Abs(ndcY)>1) return false;
            double angle=Math.Max(radius/depth,.03*snapshot.TanX);
            rect=new Vector4((float)((ndcX+1)/2),(float)((1-ndcY)/2),(float)Math.Min(.5,angle/snapshot.TanX/2),(float)Math.Min(.5,angle/snapshot.TanY/2));
            return true;
        }
    }
}
