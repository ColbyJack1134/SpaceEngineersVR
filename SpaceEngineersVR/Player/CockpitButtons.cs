using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitButtons
    {
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
        internal static CockpitTouch.Target[] Targets { get; private set; }=new CockpitTouch.Target[0];
        public static int HoveredSwitch { get; private set; }=-1;
        internal static SurfaceView Preview(string subtype,int index)
        {
            var pose=CockpitLayout.Control(subtype,index,out float size);
            return new SurfaceView { Id="CockpitControl"+index,Style=SurfaceStyle.ModelControl,
                Pose=pose,Width=size,Height=size,GeometryFeedback=subtype==FighterProfile.Subtype,
                Keys=new[] {new SurfaceKey("",0,0,1,1)} };
        }
        private static void Release() { HoveredSwitch=-1; Targets=new CockpitTouch.Target[0]; Views=new SurfaceView[0]; }
        private static void ResetCovers()
        { for(int i=0;i<covers.Length;i++) { covers[i]=CockpitCoverGeometry.Initial(i); open[i]=covers[i]>0; } }
        public static void Reset()
        {
            failed=false; Release(); ResetCovers(); CockpitTouch.Reset();
            Array.Clear(positions,0,positions.Length); Array.Clear(pulses,0,pulses.Length);
            lastUpdate=DateTime.MinValue; owner=null; CockpitActions.Reset();
        }
        public static void Prepare()
        {
            if(failed) return;
            try
            {
                var seat=SeatFit.Seat;
                bool eligible=SeatFit.Eligible(seat) && CockpitLayout.Supported(seat.BlockDefinition.Id.SubtypeName);
                CockpitActions.Update(eligible ? seat : null);
                if(!ReferenceEquals(owner,seat))
                { owner=seat; Release(); ResetCovers(); Array.Clear(positions,0,positions.Length); Array.Clear(pulses,0,pulses.Length); }
                Release();
                if(!eligible || InputRouter.Mode!=InputMode.Piloting || Main.MenuOpen || CockpitControls.Adjusting) return;
                string subtype=seat.BlockDefinition.Id.SubtypeName;
                bool fighter=subtype==FighterProfile.Subtype;
                int count=CockpitLayout.Count(subtype);
                var targets=new List<CockpitTouch.Target>();
                for(int i=0;i<count;i++)
                {
                    if(fighter && CockpitRender.Ready)
                    {
                        var cover=new SurfaceView { Id="CockpitCover"+i,Style=SurfaceStyle.ModelControl,GeometryFeedback=true,
                            Pose=CockpitCoverGeometry.TouchPose(i,covers[i])*seat.WorldMatrix,Width=.019f,Height=.035f,
                            Keys=new[] {new SurfaceKey("",0,0,1,1)} };
                        var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                        if(Vector3D.Dot(cover.Pose.Backward,head-cover.Pose.Translation)<0) cover.Pose=MatrixD.CreateRotationY(Math.PI)*cover.Pose;
                        targets.Add(new CockpitTouch.Target { Surface=cover,Slot=i,Cover=true,Position=covers[i],
                            Pivot=CockpitCoverGeometry.Hinges[i],Axis=CockpitSwitchGeometry.Axis,Travel=CockpitCoverGeometry.Travel });
                    }
                    bool accessible=!fighter || (CockpitRender.Ready ? open[i] && covers[i]>.98f : i<9);
                    if(!accessible) continue;
                    var s=Preview(subtype,i);
                    s.GeometryFeedback=fighter && CockpitRender.Ready;
                    if(s.GeometryFeedback) s.Pose*=CockpitSwitchGeometry.Visual(i,positions[i]);
                    var native=s.Pose*seat.WorldMatrix;
                    string key=Alignment.SeatKey("control"+i);
                    s.Pose=Alignment.Apply(key,native); s.Width*=Alignment.Scale(key); s.Height*=Alignment.Scale(key);
                    MatrixD correction=seat.WorldMatrix*MatrixD.Invert(native)*s.Pose*seat.PositionComp.WorldMatrixNormalizedInv;
                    targets.Add(new CockpitTouch.Target { Surface=s,Slot=i,Lever=fighter && CockpitRender.Ready,Position=positions[i],
                        Pivot=(Vector3)Vector3D.Transform(CockpitSwitchGeometry.Pivots[i],correction),
                        Axis=(Vector3)Vector3D.TransformNormal(CockpitSwitchGeometry.Axis,correction),Travel=CockpitSwitchGeometry.Travel });
                }
                Targets=targets.ToArray(); Views=targets.Select(t=>t.Surface).ToArray();
            }
            catch(Exception ex) { Fail(ex); }
        }
        public static void Update()
        {
            if(failed || Targets.Length==0) return;
            try
            {
                var now=DateTime.UtcNow;
                float step=(float)Math.Min(.05,Math.Max(0,(now-lastUpdate).TotalSeconds))*12; lastUpdate=now;
                foreach(var target in Targets)
                {
                    var s=target.Surface; int i=target.Slot;
                    var input=CockpitTouch.Read(s.Id);
                    s.Hover=input.Hover; s.Pressed=input.Held;
                    if(target.Cover)
                    {
                        if(input.Requested.HasValue) { open[i]=input.Requested.Value; CockpitFeedback.Click(input.Actor,cover:true); }
                        covers[i]=input.Position ?? covers[i]+MathHelper.Clamp((open[i] ? 1 : 0)-covers[i],-step*.65f,step*.65f);
                        continue;
                    }
                    if(s.Hover>=0 && HoveredSwitch<0) HoveredSwitch=i;
                    bool activate=target.Lever ? input.Requested.HasValue : input.Pressed;
                    if(activate)
                    {
                        if(CockpitActions.Activate(i,input.Requested))
                        { pulses[i]=now.AddSeconds(.28); CockpitFeedback.Click(input.Actor); }
                        if(Main.MenuOpen) { CockpitTouch.Reset(); Release(); return; }
                    }
                }
                // Covered levers still reflect changes made through terminals or other controls.
                for(int i=0;i<CockpitLayout.Count(SeatFit.Seat.BlockDefinition.Id.SubtypeName);i++)
                {
                    float state=CockpitActions.ReadState(i,out float actual) ? actual : now<pulses[i] ? 1 : 0;
                    var input=CockpitTouch.Read("CockpitControl"+i);
                    positions[i]=input.Position ?? positions[i]+MathHelper.Clamp(state-positions[i],-step,step);
                }
            }
            catch(Exception ex) { Fail(ex); }
        }
        internal static SurfaceView Label(CockpitTouch.Target target,int key)
        {
            var label=new SurfaceView { Style=SurfaceStyle.Label,Width=.14f,Height=.028f };
            if(target.Slot>=0)
            {
                var item=CockpitActions.Toolbar?.GetItemAtIndex(target.Slot);
                string name=item?.DisplayName?.ToString();
                label.Title=target.Cover ? "Cover · "+(target.Slot+1) : string.IsNullOrWhiteSpace(name) ? "Assign · "+(target.Slot+1) : name;
                label.Text=target.Cover ? "↕" : item==null ? "+" : null;
                label.Icons=item?.Icons ?? new string[0]; label.SubIcon=item?.SubIcon; label.Enabled=item?.Enabled ?? true;
                label.Levels=CockpitActions.ReadState(target.Slot,out float state) ? new[] { state } : null;
            }
            else
            {
                label.Title=key==7 ? target.Surface.Handle==1 ? "Lock stick position" : "Unlock stick position" : SeatPanel.Label(key);
                if(key<9) label.Text=key<7 ? target.Surface.Keys[key].Label : key==7 ? "↔" : "↺";
                if(key>=9) label.Icons=new[] { NativeSprites.Hud(SeatPanel.IconNames[key-9]) };
            }
            return label;
        }
        private static void Fail(Exception ex)
        {
            failed=true; Release(); CockpitTouch.Reset(); CockpitActions.Reset();
            Logger.Warning(ex,"Cockpit controls disabled; flight input retained");
        }
    }
}
