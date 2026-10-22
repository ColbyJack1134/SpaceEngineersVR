using System;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // The camera of the last feed image, so an image point maps to a world ray. Written on the render thread, read on the game thread.
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
        private static Snapshot Fresh(long source)
        {
            var snapshot=latest;
            return snapshot==null || snapshot.Source!=source || (DateTime.UtcNow-snapshot.Time).TotalSeconds>MaxAgeSeconds ? null:snapshot;
        }
        internal static bool TryCamera(long source,out Vector3D position,out MatrixD rotation)
        {
            var snapshot=Fresh(source);
            position=snapshot?.Position ?? Vector3D.Zero;
            rotation=snapshot?.Rotation ?? MatrixD.Identity;
            return snapshot!=null;
        }

        // uv: (0,0) top-left, (1,1) bottom-right of the feed image.
        internal static bool TryRay(long source,Vector2 uv,out Vector3D origin,out Vector3D direction)
        {
            origin=direction=Vector3D.Zero;
            var snapshot=Fresh(source);
            if(snapshot==null) return false;
            double ndcX=uv.X*2-1,ndcY=1-uv.Y*2;
            var view=new Vector3D(ndcX*snapshot.TanX,ndcY*snapshot.TanY,-1);
            direction=Vector3D.Normalize(Vector3D.TransformNormal(view,snapshot.Rotation));
            origin=snapshot.Position;
            return direction.IsValid();
        }

        internal static bool TryCentre(long source,out Vector3D origin,out Vector3D direction) =>
            TryRay(source,new Vector2(.5f,.5f),out origin,out direction);

        // Inverse of TryRay for a sphere: its box on the image (centre and half extents in uv), clipped to the image. False when
        // behind the camera or off the image. Called on the render thread, so the snapshot matches the image on show.
        internal static bool TryProject(long source,Vector3D centre,double radius,out Vector4 rect)
        {
            rect=Vector4.Zero;
            var snapshot=Fresh(source);
            if(snapshot==null) return false;
            var view=Vector3D.TransformNormal(centre-snapshot.Position,MatrixD.Transpose(snapshot.Rotation));
            if(view.Z>-.1) return false;
            double depth=-view.Z,ndcX=view.X/depth/snapshot.TanX,ndcY=view.Y/depth/snapshot.TanY;
            double angle=Math.Max(radius/depth,.03*snapshot.TanX),halfX=angle/snapshot.TanX,halfY=angle/snapshot.TanY;
            double left=Math.Max(-1,ndcX-halfX),right=Math.Min(1,ndcX+halfX),bottom=Math.Max(-1,ndcY-halfY),top=Math.Min(1,ndcY+halfY);
            if(left>=right || bottom>=top) return false;
            rect=new Vector4((float)((left+right)/4+.5),(float)(.5-(top+bottom)/4),(float)((right-left)/4),(float)((top-bottom)/4));
            return true;
        }
    }
}
