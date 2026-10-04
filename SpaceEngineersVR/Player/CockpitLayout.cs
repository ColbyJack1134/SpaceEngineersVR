using System;
using System.Linq;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitLayout
    {
        public const string ControlSeat="OpenCockpitLarge";
        public static bool Supported(string subtype) => subtype==FighterProfile.Subtype || Count(subtype)>0;

        public static int Count(string subtype) => subtype==FighterProfile.Subtype ? CockpitSwitchGeometry.Count+1 : (CockpitRig.Find(subtype) is CockpitRig rig ? rig.Levers.Length+rig.Handles.Length:0);
        internal static int MaximumCount => Math.Max(CockpitSwitchGeometry.Count+1,CockpitRig.All.Max(r=>r.Levers.Length+r.Handles.Length));

        // Centers measured from connected lever/key meshes in the installed MWM.
        // The fighter cap is about 7 mm above its mesh center.
        public static MatrixD Control(string subtype,int index,out float size)
        {
            Vector3D p,normal,up;
            if(subtype==FighterProfile.Subtype)
            {
                if(index==CockpitBarGeometry.Slot) { size=.020f; return CockpitBarGeometry.TouchPose; }
                p=CockpitSwitchGeometry.Centers[index]; normal=CockpitSwitchGeometry.NormalFor(index); up=CockpitSwitchGeometry.UpFor(index);
                size=.018f; p+=normal*.007;
            }
            else if(CockpitRig.Find(subtype)?.HandleAt(index) is CockpitRig.Handle handle)
            { size=handle.HalfWidth*2; return handle.TouchPose; }
            else if(CockpitRig.Find(subtype)?.Levers[index] is CockpitRig.Lever lever)
            {
                p=lever.Center+lever.Normal*.007f; normal=lever.Normal; up=lever.Up; size=.018f;
            }
            else
            {
                p=new Vector3D(new[] {.6736,.6992,.7249,.7505}[index],-.5302,-.3159);
                normal=Vector3D.Up; up=Vector3D.Forward; size=.022f;
            }
            return MatrixD.CreateWorld(p,-normal,up);
        }
    }
}
