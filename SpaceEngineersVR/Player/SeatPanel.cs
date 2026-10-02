using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SeatPanel
    {
        internal static readonly string[] IconNames={ "GridPowerOn","Dampeners","Handbrake","Light","GridBroadcastingOnCenter" };
        private static readonly string[] labels= { "Seat up","Seat forward","Seat down","Seat left","Center seat","Seat right","Seat back",
            "Lock stick position","Reset sticks","Power","Dampeners","Park","Ship lights","Broadcast" };
        private static readonly SurfaceKey[] lockedKeys=MakeKeys(true,false),unlockedKeys=MakeKeys(true,true),fixedKeys=MakeKeys(false,false);
        internal static string Label(int key) => labels[key];
        internal static SurfaceView View()
        {
            var seat=SeatFit.Seat;
            if(!SeatFit.Eligible(seat) || Plugin.Main.MenuOpen ||
                !TryMount(seat.BlockDefinition.Id.SubtypeName,out var local,out float width,out float height)) return null;
            var key=Alignment.SeatKey("seat");
            return new SurfaceView { Id="Seat",Pose=Alignment.Apply(key,local*seat.WorldMatrix),
                Width=width*Alignment.Scale(key),Height=height*Alignment.Scale(key),Title="SEAT",
                Handle=CockpitControls.Adjusting ? 1 : 0,Keys=Keys(CockpitControls.CanAdjust,CockpitControls.Adjusting),Levels=States() };
        }
        internal static SurfaceKey[] Keys(bool sticks=false,bool unlocked=false) => !sticks ? fixedKeys : unlocked ? unlockedKeys : lockedKeys;
        private static SurfaceKey[] MakeKeys(bool sticks,bool unlocked)
        {
            var keys=new SurfaceKey[labels.Length];
            int[] order={9,11,10,12,13,2,1,0,3,6,5,4,7,8};
            string[] symbols={"▲","↑","▼","←","seat center","→","↓","lock","stick reset"};
            for(int i=0;i<order.Length;i++)
            {
                int key=order[i];
                int row=i<2 ? 0 : 1+(i-2)/3,col=i<2 ? i : (i-2)%3;
                float y=.035f+row*.185f+(row>=2 ? .02f : 0);
                keys[key]=new SurfaceKey(key<9 ? symbols[key] : labels[key],(row==0 ? .215f : .065f)+col*.30f,y,.27f,.17f) {
                    Enabled=key==7 ? sticks : key!=8 || sticks && unlocked };
            }
            return keys;
        }
        internal static float[] States()
        {
            var seat=SeatFit.Seat;
            if(seat==null) return new float[5];
            var control=(VRage.Game.ModAPI.Interfaces.IMyControllableEntity)seat;
            return new[] { control.EnabledReactors ? 1f : 0,control.EnabledDamping ? 1f : 0,
                seat.CubeGrid.IsParked ? 1f : 0,control.EnabledLights ? 1f : 0,EssentialHud.Current?.ShipBroadcasting==true ? 1f : 0 };
        }
        internal static void Activate(int key)
        {
            switch(key)
            {
                case 9: GameActions.PowerAction.Run(); break;
                case 10: Sandbox.Game.World.MySession.Static?.ControlledEntity?.SwitchDamping(); break;
                case 11: GameActions.ParkAction.Run(); break;
                case 12: GameActions.LightsAction.Run(); break;
                case 13: GameActions.BroadcastAction.Run(); break;
            }
        }

        internal static bool TryMount(string subtype,out MatrixD local,out float width,out float height)
        {
            local=MatrixD.Identity; width=height=0;
            if(subtype==FighterProfile.Subtype)
            {
                // Between the knees, below CockpitScreen_05, leaving the LCD clear.
                local=MatrixD.CreateWorld(new Vector3D(0,-.605,.29),
                    new Vector3D(0,-.9007,-.4344),new Vector3D(0,.4344,-.9007));
                width=.108f; height=.120f;
            }
            else
            {
                var rig=CockpitRig.Find(subtype);
                if(rig==null) return false;
                local=rig.SeatMount; width=.108f; height=.120f;
            }
            local.Translation+=local.Backward*.009;
            return true;
        }
    }
}
