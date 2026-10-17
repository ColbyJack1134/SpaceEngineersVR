#if SEVR_DIAGNOSTICS
using System;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class WindowPlacementTests
    {
        private static void Require(bool value,string label) { if(!value) throw new Exception(label); }
        private static Matrix At(float degrees,float distance) => Matrix.CreateTranslation(0,0,-distance)*Matrix.CreateRotationY(MathHelper.ToRadians(degrees));
        public static void Run(Action<string> log)
        {
            var head=Matrix.CreateTranslation(.3f,1.7f,-.2f);
            Require(WindowSpots.InView(At(0,1.5f)*head,head) && WindowSpots.InView(At(60,1.5f)*head,head) && WindowSpots.InView(At(-65,2),head),"A window ahead or to the side reopens in front");
            Require(!WindowSpots.InView(At(80,1.5f)*head,head) && !WindowSpots.InView(At(180,1.5f)*head,head),"A window behind the player stays behind");
            Require(!WindowSpots.InView(At(0,3.5f)*head,head),"A window left several meters away stays there");

            // Seated: tracking to cockpit is PlayerToAbsolute inverted times the seat head, so a window must keep its cockpit pose across a recenter.
            var oldOrigin=Matrix.CreateRotationY(.4f)*Matrix.CreateTranslation(.2f,0,-.1f); var newOrigin=Matrix.CreateRotationY(-.9f)*Matrix.CreateTranslation(-.3f,0,.25f);
            var seatHead=Matrix.CreateRotationY(1.1f)*Matrix.CreateTranslation(5,2,-7); var window=At(30,1.5f)*head;
            var before=window*Matrix.Invert(oldOrigin)*seatHead; var after=WindowSpots.Follow(window,oldOrigin,newOrigin)*Matrix.Invert(newOrigin)*seatHead;
            Require(Vector3.Distance(before.Translation,after.Translation)<1e-4f && Vector3.Distance(before.Forward,after.Forward)<1e-4f,"A seated window moved relative to the cockpit on recenter");

            Require(!WindowSpots.Current(new MenuWindowSetting {Screen="MyGuiScreenTerminal"}) && !WindowSpots.Current(new MenuWindowSetting {Screen="Desktop"}) &&
                !WindowSpots.Current(new MenuWindowSetting {Screen="Remote/RoverCockpit",World="w",Cockpit=5}),"Head-relative or per-entity spots survive");
            Require(WindowSpots.Current(new MenuWindowSetting {Screen="Menu/RoverCockpit"}) && WindowSpots.Current(new MenuWindowSetting {Screen="Mirror"}) &&
                WindowSpots.Current(new MenuWindowSetting {Screen="CameraSignals",World="w",Cockpit=5}),"Current spots or camera signal modes are dropped");

            // Sizes match the offline survey of the installed interiors.
            foreach(var (model,width,height) in new[] {("Models/Cubes/Large/CockpitInterior.mwm",.41f,.27f),("Models/Cubes/Large/CockpitLarge.mwm",1.366f,.769f),("Models/Cubes/Large/OpenCockpitLarge.mwm",.644f,.356f)})
            {
                var areas=CockpitScreens.Measure(model,new[] {"CockpitScreen_01"});
                Require(areas.Length==1 && Math.Abs(areas[0].Width-width)<.015f && Math.Abs(areas[0].Height-height)<.015f,model+" main screen measured "+(areas.Length==1 ? areas[0].Width+" x "+areas[0].Height:"missing"));
                Require(Math.Abs(areas[0].Normal.Length()-1)<.001f && Math.Abs(Vector3.Dot(areas[0].Normal,areas[0].Up))<.001f,model+" screen frame is not orthonormal");
            }
            var small=new CockpitScreens.Area {Width=.41f,Height=.27f};
            Require(CockpitScreens.Fit(small,9f/16,MenuWindow.MinWidth)==MenuWindow.MinWidth,"A small LCD shrank the window below its minimum");
            Require(Math.Abs(CockpitScreens.Fit(new CockpitScreens.Area {Width=1.366f,Height=.769f},9f/16,MenuWindow.MinWidth)-1.366f)<.001f,"A large LCD did not set the window size");
            Require(Math.Abs(CockpitScreens.Fit(new CockpitScreens.Area {Width=1.6f,Height=.3f},9f/16,MenuWindow.MinWidth)-.7f)<.001f,"A wide LCD did not fit the window by height");
            log?.Invoke("PASS window placement: keep zone, seated windows across a recenter, old spot pruning, installed cockpit screen sizes and window fit");
        }
    }
}
#endif
