using System;
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
        internal static readonly Region[] Fighter={
            Bank(FighterProfile.Subtype,0,12),Bank(FighterProfile.Subtype,13,16),Bank(FighterProfile.Subtype,17,20),
            Bank(FighterProfile.Subtype,21,26),Bank(FighterProfile.Subtype,27,32),
            Bank(FighterProfile.Subtype,33,36),Bank(FighterProfile.Subtype,37,40),
            Plane(CockpitBarGeometry.Front,CockpitBarGeometry.Normal,(Vector3)CockpitBarGeometry.TouchPose.Up,.020f,.120f),
            // Installed fighter LCD faces, independent of whether a touchscreen app is running.
            Plane(new Vector3(0,-.095151f,-.703736f),new Vector3(0,.267448f,.963572f),new Vector3(0,.963572f,-.267448f),.572754f,.319561f),
            Plane(new Vector3(-.117348f,-.313173f,-.369986f),new Vector3(0,.632355f,.774679f),new Vector3(0,.774679f,-.632355f),.188158f,.122515f),
            Plane(new Vector3(.116913f,-.313173f,-.369986f),new Vector3(0,.632355f,.774679f),new Vector3(0,.774679f,-.632355f),.188051f,.122515f),
            Plane(new Vector3(0,-.439749f,-.131529f),new Vector3(0,.968906f,.247429f),new Vector3(0,.247429f,-.968906f),.425537f,.187618f),
            Plane(new Vector3(.000083f,-.511029f,.066264f),new Vector3(-.000146f,.900853f,.434125f),new Vector3(-.0013f,.434125f,-.900852f),.116417f,.169781f),
            Plane(new Vector3(.371786f,-.407533f,.133268f),new Vector3(-.552985f,.828555f,.087777f),new Vector3(.831528f,.555463f,-.004667f),.122952f,.154297f)
        };
        private static readonly Region[] controlSeat={Bank(CockpitLayout.ControlSeat,0,3)};
        internal static bool Contains(string subtype,CockpitProbe probe)
        {
            var regions=subtype==FighterProfile.Subtype ? Fighter : subtype==CockpitLayout.ControlSeat ? controlSeat : null;
            if(regions==null) return false;
            foreach(var region in regions) if(region.Intersects(probe)) return true;
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
