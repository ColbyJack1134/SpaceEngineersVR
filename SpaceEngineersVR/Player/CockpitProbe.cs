using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal struct CockpitProbe
    {
        internal const float Radius=.010f,Length=.012f,TipExtension=.003f;
        internal Vector3D Tip,Start,End;
        internal CockpitProbe(MatrixD pointer)
        {
            Tip=pointer.Translation;
            Start=Tip+pointer.Forward*(TipExtension-Radius);
            End=Start-pointer.Forward*Length;
        }
        internal CockpitProbe Transform(MatrixD matrix) => new CockpitProbe {
            Tip=Vector3D.Transform(Tip,matrix),Start=Vector3D.Transform(Start,matrix),End=Vector3D.Transform(End,matrix) };
        internal float DistanceSquared(BoundingBox box,out Vector3 contact)
        {
            contact=Vector3.Zero;
            if(!Start.IsValid() || !End.IsValid()) return float.MaxValue;
            var a=(Vector3)Start; var d=(Vector3)(End-Start);
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
        internal bool Intersects(BoundingBox bounds,float padding=0) => DistanceSquared(bounds,out _) <= (Radius+padding)*(Radius+padding);
    }
}
