using VRageMath;
using System.Collections.Generic;

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
                    SeatControl=key,Enabled=key==7 ? sticks : key!=8 || sticks && unlocked };
            }
            return keys;
        }
        private static readonly Vector3[] directions={ Vector3.Up,Vector3.Forward,Vector3.Down,Vector3.Left,Vector3.Zero,Vector3.Right,Vector3.Backward };
        internal static void WristKeys(List<SurfaceKey> keys,bool available,bool sticks,bool unlocked)
        {
            foreach(var source in Keys(sticks,unlocked))
            {
                var b=source.Bounds;
                keys.Add(new SurfaceKey(source.Label,.29f+b.X*.42f,.15f+b.Y*.83f,b.Width*.42f,b.Height*.83f) {
                    SeatControl=source.SeatControl,Enabled=available && source.Enabled });
            }
        }
        internal static void UpdateInput(int clicked,int held)
        {
            if(!SeatFit.Eligible(SeatFit.Seat) || Plugin.Main.MenuOpen || !InputRouter.Gameplay) return;
            if(clicked==7) CockpitControls.ToggleAdjustment();
            else if(clicked==8 && CockpitControls.Adjusting) CockpitControls.ResetPlacement();
            else if(clicked>=9 && clicked<14) Activate(clicked);
            if(held>=0 && held<directions.Length && !CockpitControls.Held(Player.HandR) && !CockpitControls.Held(Player.HandL))
                SeatFit.Move(directions[held],held==4);
        }
        internal static float[] States()
        {
            var seat=SeatFit.Seat;
            if(seat==null) return new float[5];
            var control=(VRage.Game.ModAPI.Interfaces.IMyControllableEntity)seat;
            var broadcast=seat.CubeGrid.GridSystems.RadioSystem.AntennasBroadcasterEnabled;
            return new[] { control.EnabledReactors ? 1f : 0,control.EnabledDamping ? 1f : 0,
                seat.CubeGrid.IsParked ? 1f : 0,control.EnabledLights ? 1f : 0,(broadcast==VRage.MyMultipleEnabledEnum.AllEnabled || broadcast==VRage.MyMultipleEnabledEnum.Mixed) ? 1f : 0 };
        }
        internal static Sandbox.Game.Entities.MyCockpit RemoteOwner(Sandbox.Game.Entities.IMyControllableEntity controlled,Sandbox.Game.Entities.MyCockpit home) =>
            RemoteView.ExitControlled(controlled) ? home:null;
        internal static void Activate(int key)
        {
            if(RemoteOwner(Sandbox.Game.World.MySession.Static?.ControlledEntity,RemoteView.HomeSeat) is Sandbox.Game.Entities.IMyControllableEntity cockpit)
            {
                switch(key)
                {
                    case 9: cockpit.SwitchReactors(); break;
                    case 10: cockpit.SwitchDamping(); break;
                    case 11: if(cockpit.CanSwitchLandingGears) cockpit.SwitchLandingGears(); break;
                    case 12: cockpit.SwitchLights(); break;
                    case 13: cockpit.SwitchBroadcasting(); break;
                }
                return;
            }
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
            var rig=CockpitRig.Find(subtype);
            if(rig==null) return false;
            local=rig.SeatMount; width=.108f; height=.120f;
            local.Translation+=local.Backward*.009;
            return true;
        }
    }
}
