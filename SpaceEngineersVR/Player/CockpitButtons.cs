using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitButtons
    {
        private static readonly float[] positions=new float[CockpitLayout.MaximumCount];
        private static readonly DateTime[] pulses=new DateTime[CockpitLayout.MaximumCount];
        private static readonly float[] covers=new float[CockpitLayout.MaximumCount];
        private static readonly bool[] open=new bool[CockpitLayout.MaximumCount];
        private static string subtype=FighterProfile.Subtype;
        static CockpitButtons() { ResetCovers(); }
        private static int CoverIndex(int slot) => subtype==FighterProfile.Subtype ? CockpitSwitchGeometry.CoverIndex(slot) : CockpitRig.Find(subtype)?.Levers[slot]?.CoverActor>=0 ? slot : -1;
        private static DateTime lastUpdate;
        internal static float SwitchPosition(int slot,string model=null) => model!=null && model!=subtype ? 0 : positions[slot];
        internal static float CoverPosition(int slot,string model=null)
        {
            if(model!=null && model!=subtype) return 0;
            int index=CoverIndex(slot);
            return index<0 ? 0 : covers[index];
        }
        private static Sandbox.Game.Entities.MyCockpit owner;
        private static bool failed;
        public static SurfaceView[] Views { get; private set; }=new SurfaceView[0];
        internal static CockpitTouch.Target[] Targets { get; private set; }=new CockpitTouch.Target[0];
        public static int HoveredSwitch { get; private set; }=-1;
        internal static SurfaceView Preview(string subtype,int index)
        {
            var pose=CockpitLayout.Control(subtype,index,out float size);
            return new SurfaceView { Id="CockpitControl"+index,Style=SurfaceStyle.ModelControl,
                Pose=pose,Width=size,Height=subtype==FighterProfile.Subtype && index==CockpitBarGeometry.Slot ? .120f : size,GeometryFeedback=subtype==FighterProfile.Subtype,
                Keys=new[] {new SurfaceKey("",0,0,1,1)} };
        }
        private static void Release() { HoveredSwitch=-1; Targets=new CockpitTouch.Target[0]; Views=new SurfaceView[0]; }
        private static readonly double[] pendingCovers=new double[CockpitLayout.MaximumCount];
        private static void ResetCovers()
        {
            Array.Clear(covers,0,covers.Length); Array.Clear(open,0,open.Length); Array.Clear(pendingCovers,0,pendingCovers.Length);
            var saved=owner==null ? null : Common.Config.CockpitStates.FirstOrDefault(s=>s.World==Sandbox.Game.World.MySession.Static.CurrentPath && s.Cockpit==owner.EntityId)?.Covers;
            if(saved!=null) for(int i=0;i<Math.Min(saved.Length,covers.Length);i++) { open[i]=saved[i]; covers[i]=saved[i] ? 1 : 0; }
            SynchronizeCovers(true);
        }
        private static void SaveCovers(int index)
        {
            if(owner==null) return;
            pendingCovers[index]=MultiplayerRuntime.Now+5;
            MultiplayerRuntime.SaveCover(owner,index,open[index]);
        }
        private static void SynchronizeCovers(bool snap=false)
        {
            if(owner==null || !MultiplayerRuntime.Get(owner,out var shared)) return;
            for(int i=0;i<open.Length;i++)
            {
                bool value=i<shared.Covers.Length && shared.Covers[i];
                if(!snap && MultiplayerRuntime.Now<pendingCovers[i] && open[i]!=value) continue;
                pendingCovers[i]=0; open[i]=value;
                if(snap) covers[i]=open[i] ? 1:0;
            }
        }
        public static void Reset()
        {
            failed=false; owner=null; Release(); ResetCovers(); CockpitTouch.Reset();
            Array.Clear(positions,0,positions.Length); Array.Clear(pulses,0,pulses.Length);
            lastUpdate=DateTime.MinValue; CockpitActions.Reset();
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
                { owner=seat; subtype=seat?.BlockDefinition.Id.SubtypeName; Release(); ResetCovers(); Array.Clear(positions,0,positions.Length); Array.Clear(pulses,0,pulses.Length); }
                Release(); SynchronizeCovers();
                if(!eligible || ThirdPersonView.Active || !InputRouter.CockpitInteraction || Main.MenuOpen || CockpitControls.Adjusting) return;
                subtype=seat.BlockDefinition.Id.SubtypeName;
                var rig=CockpitRig.Find(subtype);
                bool fighter=subtype==FighterProfile.Subtype;
                int count=CockpitLayout.Count(subtype);
                var targets=new List<CockpitTouch.Target>();
                for(int i=0;i<count;i++)
                {
                    bool bar=fighter && i==CockpitBarGeometry.Slot;
                    var lever=rig?.Levers[i];
                    int coverIndex=CoverIndex(i);
                    bool covered=coverIndex>=0;
                    if(covered && CockpitRender.Ready)
                    {
                        var cover=new SurfaceView { Id="CockpitCover"+i,Style=SurfaceStyle.ModelControl,GeometryFeedback=true,
                            Pose=(fighter ? CockpitCoverGeometry.TouchPose(coverIndex,covers[coverIndex]) : lever.CoverPose(covers[coverIndex]))*seat.WorldMatrix,Width=.019f,Height=.035f,
                            Keys=new[] {new SurfaceKey("",0,0,1,1)} };
                        var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                        if(Vector3D.Dot(cover.Pose.Backward,head-cover.Pose.Translation)<0) cover.Pose=MatrixD.CreateRotationY(Math.PI)*cover.Pose;
                        targets.Add(new CockpitTouch.Target { Surface=cover,Slot=i,Cover=true,Position=covers[coverIndex],
                            Pivot=fighter ? CockpitCoverGeometry.Hinges[coverIndex] : lever.Hinge,Axis=fighter ? CockpitSwitchGeometry.AxisFor(i) : lever.Axis,Travel=CockpitCoverGeometry.Travel });
                    }
                    bool accessible=fighter ? CockpitRender.Ready ? !covered || open[coverIndex] && covers[coverIndex]>.98f : i<9 : lever==null || CockpitRender.Ready && (!covered || open[coverIndex] && covers[coverIndex]>.98f);
                    if(!accessible) continue;
                    var s=Preview(subtype,i);
                    s.GeometryFeedback=(fighter || lever!=null) && CockpitRender.Ready;
                    if(s.GeometryFeedback) s.Pose*=bar ? CockpitBarGeometry.Visual(positions[i]) : fighter ? CockpitSwitchGeometry.Visual(i,positions[i]) : lever.Visual(positions[i]);
                    var native=s.Pose*seat.WorldMatrix;
                    string key=Alignment.SeatKey("control"+i);
                    s.Pose=Alignment.Apply(key,native); s.Width*=Alignment.Scale(key); s.Height*=Alignment.Scale(key);
                    MatrixD correction=seat.WorldMatrix*MatrixD.Invert(native)*s.Pose*seat.PositionComp.WorldMatrixNormalizedInv;
                    targets.Add(new CockpitTouch.Target { Surface=s,Slot=i,Lever=(fighter || lever!=null) && !bar && CockpitRender.Ready,Pull=bar,Position=positions[i],
                        Pivot=bar ? Vector3.Zero : (Vector3)Vector3D.Transform(fighter ? CockpitSwitchGeometry.Pivots[i] : lever?.Pivot ?? Vector3.Zero,correction),
                        Axis=(Vector3)Vector3D.TransformNormal(bar ? CockpitBarGeometry.Normal : fighter ? CockpitSwitchGeometry.AxisFor(i) : lever?.Axis ?? Vector3.Right,correction),
                        Travel=bar ? CockpitBarGeometry.Travel : CockpitSwitchGeometry.Travel });
                }
                Targets=targets.ToArray(); Views=targets.Select(t=>t.Surface).ToArray();
            }
            catch(Exception ex) { Fail(ex); }
        }
        public static void Update()
        {
            long started=FeatureTiming.Start();
            try { UpdateCore(); }
            finally { FeatureTiming.End(FeatureTiming.Area.CockpitButtons,started); }
        }
        private static void UpdateCore()
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
                        int coverIndex=CoverIndex(i);
                        if(input.Requested.HasValue && CockpitActions.SharedReady) { open[coverIndex]=input.Requested.Value; SaveCovers(coverIndex); CockpitFeedback.Click(input.Actor,cover:true); }
                        covers[coverIndex]=input.Position ?? covers[coverIndex]+MathHelper.Clamp((open[coverIndex] ? 1 : 0)-covers[coverIndex],-step*.65f,step*.65f);
                        continue;
                    }
                    if(s.Hover>=0 && HoveredSwitch<0) HoveredSwitch=i;
                    bool activate=target.Lever || target.Pull ? input.Requested.HasValue : input.Pressed;
                    if(activate)
                    {
                        bool? requested=input.Requested;
                        if(target.Pull && requested==false && !CockpitActions.ReadState(i,out _)) continue;
                        if(CockpitActions.Activate(i,requested))
                        { pulses[i]=now.AddSeconds(.28); CockpitFeedback.Click(input.Actor); }
                        if(Main.MenuOpen) { CockpitTouch.Reset(); Release(); return; }
                    }
                }
                // Covered levers still reflect changes made through terminals or other controls.
                for(int i=0;i<CockpitLayout.Count(SeatFit.Seat.BlockDefinition.Id.SubtypeName);i++)
                {
                    bool stateful=CockpitActions.ReadState(i,out float actual);
                    float state=stateful ? actual : now<pulses[i] ? 1 : 0;
                    var input=CockpitTouch.Read("CockpitControl"+i);
                    positions[i]=input.Position ?? positions[i]+MathHelper.Clamp(state-positions[i],-step,step);
                }
            }
            catch(Exception ex) { Fail(ex); }
        }
        internal static SurfaceView Label(CockpitTouch.Target target,int key)
        {
            var label=new SurfaceView { Style=SurfaceStyle.Label,Width=.14f,Height=.0215f };
            if(target.Slot>=0)
            {
                var item=CockpitActions.Toolbar?.GetItemAtIndex(target.Slot);
                string name=item?.DisplayName?.ToString();
                label.Title=target.Cover ? "Cover · "+(target.Slot+1) : string.IsNullOrWhiteSpace(name) ? "Assign · "+(target.Slot+1) : name;
                if(!target.Cover && item is Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock block)
                {
                    label.Title=block.GetBlockName(); label.Action=block.GetActionName();
                    label.Argument=string.Join(", ",block.Parameters.Select(p=>p.Value?.ToString()).Where(p=>!string.IsNullOrEmpty(p)));
                }
                else if(!target.Cover && item is Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalGroup group)
                {
                    label.Title=((Sandbox.Common.ObjectBuilders.MyObjectBuilder_ToolbarItemTerminalGroup)group.GetObjectBuilder()).GroupName;
                    label.Action=group.AllActions.FirstOrDefault(a=>a.Id==group.ActionId)?.Name.ToString() ?? group.ActionId;
                    label.Argument=string.Join(", ",group.Parameters.Select(p=>p.Value?.ToString()).Where(p=>!string.IsNullOrEmpty(p)));
                }
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
