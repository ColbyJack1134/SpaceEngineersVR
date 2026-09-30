using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitButtons
    {
        private static readonly CockpitTouch[] input=Enumerable.Range(0,13).Select(_=>new CockpitTouch()).ToArray();
        private static readonly CockpitTouch[] coverInput=Enumerable.Range(0,13).Select(_=>new CockpitTouch()).ToArray();
        private static readonly float[] positions=new float[13];
        private static readonly DateTime[] pulses=new DateTime[13];
        private static readonly float[] covers=Enumerable.Range(0,13).Select(CockpitCoverGeometry.Initial).ToArray();
        private static readonly bool[] open=Enumerable.Range(0,13).Select(i=>CockpitCoverGeometry.Initial(i)>0).ToArray();
        private static DateTime lastUpdate;
        internal static float SwitchPosition(int slot) => positions[slot];
        internal static float CoverPosition(int slot) => covers[slot];
        private static object owner;
        private static bool failed;
        public static SurfaceView[] Views { get; private set; }=new SurfaceView[0];
        public static int HoveredSwitch { get; private set; }=-1;
        internal static SurfaceView Preview(string subtype,int index)
        {
            var pose=CockpitLayout.Control(subtype,index,out float size);
            return new SurfaceView { Id="CockpitControl"+index,Style=SurfaceStyle.ModelControl,
                Title=(index+1).ToString(),Pose=pose,Width=size,Height=size,
                Keys=new[] {new SurfaceKey("",0,0,1,1)} };
        }
        private static void Release() { HoveredSwitch=-1; foreach(var hand in input.Concat(coverInput)) hand.Reset(); }
        private static void ResetCovers()
        { for(int i=0;i<covers.Length;i++) { covers[i]=CockpitCoverGeometry.Initial(i); open[i]=covers[i]>0; } }
        public static void Reset() { failed=false; Release(); ResetCovers(); Array.Clear(positions,0,positions.Length); Array.Clear(pulses,0,pulses.Length); lastUpdate=DateTime.MinValue; Views=new SurfaceView[0]; owner=null; CockpitActions.Reset(); }
        public static void Update()
        {
            if(failed) return;
            try { UpdatePanel(); }
            catch(Exception ex) { failed=true; Release(); Views=new SurfaceView[0]; CockpitActions.Reset(); Logger.Warning(ex,"Cockpit controls disabled; flight input retained"); }
        }
        private static void UpdatePanel()
        {
            var seat=SeatFit.Seat;
            bool eligible=SeatFit.Eligible(seat) && CockpitLayout.Supported(seat.BlockDefinition.Id.SubtypeName);
            CockpitActions.Update(eligible ? seat : null);
            if(!ReferenceEquals(owner,seat)) { owner=seat; Release(); ResetCovers(); Array.Clear(positions,0,positions.Length); Array.Clear(pulses,0,pulses.Length); }
            bool available=eligible && InputRouter.Mode==InputMode.Piloting && !Main.MenuOpen && !CockpitControls.Adjusting &&
                Player.Headset.pose.isTracked && Player.HandR.pose.isTracked && Player.HandL.pose.isTracked && MenuPointer.GameFocused;
            Views=new SurfaceView[0];
            HoveredSwitch=-1;
            if(!available) { Release(); return; }
            string subtype=seat.BlockDefinition.Id.SubtypeName;
            int count=CockpitLayout.Count(subtype);
            var now=DateTime.UtcNow;
            float step=(float)Math.Min(.05,Math.Max(0,(now-lastUpdate).TotalSeconds))*12; lastUpdate=now;
            var views=new List<SurfaceView>();
            SurfaceView label=null;
            bool fighter=subtype==FighterProfile.Subtype;
            if(fighter && CockpitRender.Ready) for(int i=0;i<count;i++)
            {
                covers[i]+=MathHelper.Clamp((open[i] ? 1 : 0)-covers[i],-step*.65f,step*.65f);
                var s=new SurfaceView { Id="CockpitCover"+i,Style=SurfaceStyle.ModelControl,
                    Pose=CockpitCoverGeometry.TouchPose(i,covers[i])*seat.WorldMatrix,Width=.019f,Height=.035f,
                    Keys=new[] { new SurfaceKey("",0,0,1,1) } };
                var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                if(Vector3D.Dot(s.Pose.Backward,head-s.Pose.Translation)<0) s.Pose=MatrixD.CreateRotationY(Math.PI)*s.Pose;
                bool settled=Math.Abs(covers[i]-(open[i] ? 1 : 0))<.001f;
                if(coverInput[i].Update(s,available && settled,triggerOnly:open[i])>=0) open[i]=!open[i];
                s.Hover=coverInput[i].Hover; s.Pressed=coverInput[i].Held;
                views.Add(s);
            }
            for(int i=0;i<count;i++)
            {
                var s=Preview(subtype,i);
                string key=Alignment.SeatKey("control"+i);
                s.Pose=Alignment.Apply(key,s.Pose*seat.WorldMatrix);
                s.Width*=Alignment.Scale(key); s.Height*=Alignment.Scale(key);
                if(Common.Config.DeveloperTools) s.Handle=1;
                bool lever=fighter;
                bool accessible=!lever || (CockpitRender.Ready ? open[i] && covers[i]>.98f : i<9);
                int clicked=input[i].Update(s,available && accessible,isSwitch:lever);
                s.Hover=input[i].Hover; s.Pressed=input[i].Held;
                if(clicked>=0)
                {
                    if(CockpitActions.Activate(i,input[i].RequestedState)) pulses[i]=now.AddSeconds(.28);
                    if(Main.MenuOpen) { Release(); Views=new SurfaceView[0]; return; }
                }
                else if(s.Hover>=0 && label==null)
                {
                    HoveredSwitch=i;
                    var item=CockpitActions.Toolbar?.GetItemAtIndex(i);
                    var pose=s.Pose; pose.Translation-=pose.Up*.042; pose.Translation+=pose.Backward*.006;
                    label=new SurfaceView { Id="CockpitSwitchLabel",Style=SurfaceStyle.Label,Pose=pose,Width=.042f,Height=.042f,
                        Title=(i+1).ToString(),Icons=item?.Icons ?? new string[0],SubIcon=item?.SubIcon,
                        Enabled=item?.Enabled ?? true,Levels=CockpitActions.ReadState(i,out float hoverState) ? new[] { hoverState } : null };
                }
                float target=CockpitActions.ReadState(i,out float state) ? state : now<pulses[i] ? 1 : 0;
                positions[i]+=MathHelper.Clamp(target-positions[i],-step,step);
                views.Add(s);
            }
            if(label!=null) views.Add(label);
            Views=views.ToArray();
        }
    }
}
