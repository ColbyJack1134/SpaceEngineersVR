using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SegmentBox
    {
        internal static float DistanceSquared(Vector3D start,Vector3D endPoint,BoundingBox box,out Vector3 contact)
        {
            contact=Vector3.Zero;
            if(!start.IsValid() || !endPoint.IsValid()) return float.MaxValue;
            var a=(Vector3)start; var d=(Vector3)(endPoint-start);
            float best=float.MaxValue,begin=0;
            // Segment-to-box distance is quadratic between crossings of the six box planes.
            for(int interval=0;interval<7;interval++)
            {
                float end=1;
                for(int axis=0;axis<3;axis++)
                {
                    float delta=Component(d,axis),origin=Component(a,axis);
                    if(Math.Abs(delta)<1e-8f) continue;
                    float lo=(Component(box.Min,axis)-origin)/delta,hi=(Component(box.Max,axis)-origin)/delta;
                    if(lo>begin+1e-6f && lo<end) end=lo;
                    if(hi>begin+1e-6f && hi<end) end=hi;
                }
                float middle=(begin+end)*.5f,aa=0,ab=0;
                for(int axis=0;axis<3;axis++)
                {
                    float delta=Component(d,axis),origin=Component(a,axis),min=Component(box.Min,axis),max=Component(box.Max,axis);
                    float at=origin+delta*middle;
                    if(at>=min && at<=max) continue;
                    float offset=origin-(at<min ? min : max);
                    aa+=delta*delta; ab+=delta*offset;
                }
                float t=aa>1e-12f ? MathHelper.Clamp(-ab/aa,begin,end) : middle;
                var point=a+d*t; var nearest=Vector3.Clamp(point,box.Min,box.Max);
                float distance=Vector3.DistanceSquared(point,nearest);
                if(distance<best) { best=distance; contact=nearest; }
                if(end>=1) break;
                begin=end;
            }
            return best;
        }
        private static float Component(Vector3 v,int axis) => axis==0 ? v.X : axis==1 ? v.Y : v.Z;
    }
}
