using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SegmentDisk
    {
        internal static Vector3 Closest(Vector3 point,BoundingBox bounds)
        {
            var center=bounds.Center;
            float radius=Math.Min(bounds.HalfExtents.X,bounds.HalfExtents.Y);
            var delta=new Vector2(point.X-center.X,point.Y-center.Y);
            float length=delta.Length();
            if(length>radius) delta*=radius/length;
            return new Vector3(center.X+delta.X,center.Y+delta.Y,MathHelper.Clamp(point.Z,bounds.Min.Z,bounds.Max.Z));
        }
        internal static float DistanceSquared(Vector3D start,Vector3D end,BoundingBox bounds)
        {
            if(!start.IsValid() || !end.IsValid() || bounds.HalfExtents.X<=0 || bounds.HalfExtents.Y<=0) return float.MaxValue;
            var a=(Vector3)start; var d=(Vector3)(end-start);
            float low=0,high=1;
            // Distance to a convex cap has a monotone derivative along the finger segment.
            for(int i=0;i<24;i++)
            {
                float t=(low+high)*.5f; var point=a+d*t;
                if(Vector3.Dot(d,point-Closest(point,bounds))<0) low=t; else high=t;
            }
            var nearest=a+d*((low+high)*.5f);
            return Vector3.DistanceSquared(nearest,Closest(nearest,bounds));
        }
    }
}
