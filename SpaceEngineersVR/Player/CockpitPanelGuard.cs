using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitPanelGuard
    {
        private const float OutwardDepth=.05f;
        internal struct Region
        {
            internal Matrix Frame;
            internal BoundingBox Bounds;
            private Matrix inverse;
            internal Region(Matrix frame,BoundingBox bounds) { Frame=frame; Bounds=bounds; inverse=Matrix.Invert(frame); }
            internal bool Contains(Vector3 point) => point.IsValid() && Bounds.Contains(Vector3.Transform(point,inverse))!=ContainmentType.Disjoint;
            internal bool Intersects(CockpitProbe probe) => probe.Transform(inverse).Intersects(Bounds);
        }
        private static Region Bank(string subtype,int first,int last)
        {
            Matrix frame=(Matrix)CockpitLayout.Control(subtype,first,out _),inverse=Matrix.Invert(frame);
            var bounds=BoundingBox.CreateInvalid();
            for(int i=first;i<=last;i++) bounds.Include(Vector3.Transform((Vector3)CockpitLayout.Control(subtype,i,out _).Translation,inverse));
            bounds.Min-=new Vector3(.045f,.045f,.035f);
            bounds.Max+=new Vector3(.045f,.045f,OutwardDepth);
            return new Region(frame,bounds);
        }
        private static Region Plane(Vector3 center,Vector3 normal,Vector3 up,float width,float height) =>
            new Region(Matrix.CreateWorld(center,-normal,up),new BoundingBox(new Vector3(-width/2-.03f,-height/2-.03f,-.035f),
                new Vector3(width/2+.03f,height/2+.03f,OutwardDepth)));
        private static readonly Dictionary<CockpitRig,Region[]> regions=new Dictionary<CockpitRig,Region[]>();
        internal static Region[] Regions(CockpitRig rig)
        {
            if(regions.TryGetValue(rig,out var result)) return result;
            return regions[rig]=rig.Banks.Select(b=>Bank(rig.Subtype,b.First,b.Last))
                .Concat(rig.Bars.Select(b=>Plane(b.Front,b.Normal,b.Up,b.Width,b.Height)))
                .Concat(rig.Screens.Select(s=>Plane(s.Center,s.Normal,s.Up,s.Width,s.Height))).ToArray();
        }
        internal static bool Contains(string subtype,CockpitProbe probe)
        {
            var rig=CockpitRig.Find(subtype);
            if(rig==null) return false;
            foreach(var region in Regions(rig)) if(region.Intersects(probe)) return true;
            return false;
        }
        internal static bool NearSurface(SurfaceView surface,CockpitProbe probe)
        {
            var bounds=new BoundingBox(new Vector3(-surface.Width/2-.03f,-surface.Height/2-.03f,-.035f),
                new Vector3(surface.Width/2+.03f,surface.Height/2+.03f,OutwardDepth));
            return probe.Transform(MatrixD.Invert(surface.Pose)).Intersects(bounds);
        }
    }
}
