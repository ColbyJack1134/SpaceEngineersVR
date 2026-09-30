using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitLayout
    {
        public const string ControlSeat="OpenCockpitLarge";
        public static bool Supported(string subtype) => subtype==FighterProfile.Subtype || subtype==ControlSeat;

        public static int Count(string subtype) => subtype==FighterProfile.Subtype ? CockpitCoverGeometry.Count : 4;

        // Centers measured from connected lever/key meshes in the installed MWM.
        // The hit plane sits just above the lever; the model remains visible.
        public static MatrixD Control(string subtype,int index,out float size)
        {
            Vector3D p,normal,up;
            if(subtype==FighterProfile.Subtype)
            {
                p=CockpitSwitchGeometry.Centers[index]; normal=CockpitSwitchGeometry.Normal; up=CockpitSwitchGeometry.Up;
                size=.018f; p+=normal*.002;
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
