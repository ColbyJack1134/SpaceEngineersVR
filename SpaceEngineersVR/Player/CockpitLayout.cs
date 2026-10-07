using System.Linq;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitLayout
    {
        public const string ControlSeat="OpenCockpitLarge",Fighter="DBSmallBlockFighterCockpit";
        public static bool Supported(string subtype) => Count(subtype)>0;

        public static int Count(string subtype) => CockpitRig.Find(subtype)?.Count ?? 0;
        internal static int MaximumCount => CockpitRig.All.Max(r=>r.Count);

        // Lever centers are measured from connected meshes in the installed MWM; caps sit about 7 mm above.
        public static MatrixD Control(string subtype,int index,out float size)
        {
            var rig=CockpitRig.Find(subtype);
            if(rig?.HandleAt(index) is CockpitRig.Handle handle) { size=handle.HalfWidth*2; return handle.TouchPose; }
            if(rig?.BarAt(index) is CockpitRig.Bar bar) { size=bar.Width; return bar.TouchPose; }
            if(rig?.ButtonAt(index) is CockpitRig.Button button) { size=button.Size; return button.TouchPose; }
            var lever=rig.LeverAt(index);
            size=lever.Width;
            return MatrixD.CreateWorld(lever.Center+lever.ContactNormal*lever.ContactOffset,-lever.ContactNormal,lever.Up);
        }
    }
}
