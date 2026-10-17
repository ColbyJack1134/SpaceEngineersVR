using System;
using Sandbox.Engine.Physics;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Gui;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Grip plus trigger on a loose grid under the finger ray holds it in front of the hand through the grab mod. The trigger
    // then turns the sticks into rotation and distance controls, the other hand's chord turns the grid by hand, and B throws it.
    internal static class HandGrab
    {
        private const float ChordPressure=.5f,GripPress=.55f,GripRelease=.45f;
        private sealed class Hand { public bool Grip,WasChord,WasClick; }
        private static readonly Hand[] hands={new Hand(),new Hand()};
        private static readonly GridGrab.Contact contact=new GridGrab.Contact();
        private static readonly Random jitter=new Random();
        private static GridGrab.Hold hold;
        private static Controller holder;
        private static MatrixD? turnFrame;
        private static MatrixD turnGridStart;
        private static MyCubeGrid highlighted;
        private static int highlight;
        private static MyCharacter owner;
        private static bool disabled;
        private static DateTime nextStrain;
        internal static bool Active => hold!=null;
        internal static bool Holding(Controller hand) => hold!=null && holder==hand;
        internal static string SnapLabel => "Snap held grid to centre: "+(Common.Config?.GrabSnapToCenter==true ? "on":"off");
        private static Hand For(Controller hand) => hands[hand==Player.HandL ? 0:1];
        private static Controller Other(Controller hand) => hand==Player.HandL ? Player.HandR:Player.HandL;
        private static float Trigger(Controller hand) => (hand==Player.HandL ? Controls.Static.LeftTriggerPressure:Controls.Static.PointerPressure).RawPosition.X;
        private static float Grip(Controller hand) => (hand==Player.HandL ? Controls.Static.LeftGripPressure:Controls.Static.RightGripPressure).RawPosition.X;
        private static Control.Analog Stick(Controller hand,bool flying) => flying ? (hand==Player.HandL ? Controls.Static.ThrustLRFB:Controls.Static.ThrustRotate):
            hand==Player.HandL ? Controls.Static.WalkLongitudinal:Controls.Static.WalkRotate;
        internal static void ToggleSnap()
        {
            if(Common.Config==null) return;
            Common.Config.GrabSnapToCenter=!Common.Config.GrabSnapToCenter;
            if(hold==null || holder==null) return;
            hold.SnapToCenter=Common.Config.GrabSnapToCenter;
            // The distance follows the new pulled point so the grid does not lunge on the toggle.
            if(HandInteraction.TryCharacterPointerPose(holder,out var pointer) && GridGrab.Grabbable(hold.Grid,ModGrabBridge.MaxSize))
                hold.Distance=GridGrab.SnapDistance(GridGrab.PulledPoint(hold),pointer.Translation,pointer.Forward,ModGrabBridge.Reach);
        }
        public static void Reset()
        {
            Release(false);
            foreach(var hand in hands) { hand.Grip=hand.WasChord=hand.WasClick=false; }
            contact.Counter=GridGrab.ContactLimit; contact.Settle=0;
            owner=null;
        }
        public static void Update()
        {
            if(disabled) return;
            try { UpdateCore(); }
            catch(Exception ex) { disabled=true; Reset(); Logger.Warning(ex,"Hand grab disabled"); }
        }
        private static void UpdateCore()
        {
            var now=DateTime.UtcNow;
            float dt=1f/60;
            var character=MySession.Static?.LocalCharacter;
            if(owner!=character) { Reset(); owner=character; }
            foreach(var controller in new[] {Player.HandL,Player.HandR})
            {
                var hand=For(controller);
                hand.Grip=Grip(controller)>(hand.Grip ? GripRelease:GripPress);
            }
            // A held grid survives the radial wheel (snap toggle) but not menus, seats, ladders, tools or the third person view.
            bool keep=Main.VrActive && character!=null && !character.IsDead && !character.IsSitting && !character.IsOnLadder &&
                MySession.Static.ControlledEntity==character && character.CurrentWeapon==null && !Main.MenuOpen && !ThirdPersonView.Active &&
                (InputRouter.Gameplay || InputRouter.Mode==InputMode.Radial);
            bool flying=character?.JetpackComp?.TurnedOn==true;
            if(hold!=null)
            {
                var hand=For(holder);
                bool kept=keep && hand.Grip && holder.pose.isTracked && GridGrab.Grabbable(hold.Grid,ModGrabBridge.MaxSize) && ModGrabBridge.Available;
                if(!kept) { Release(hand.Grip); return; }
                HoldUpdate(character,holder,flying,now,dt);
                return;
            }
            GridGrab.Step(contact,false,false);
            bool available=keep && InputRouter.Gameplay && !PlacementControls.OwnsTools && !TouchScreenBridge.OwnsInput && !FloatingWindows.OwnsInput;
            foreach(var controller in new[] {Player.HandL,Player.HandR})
            {
                var hand=For(controller);
                bool chord=hand.Grip && Trigger(controller)>ChordPressure;
                bool attempt=chord && !hand.WasChord;
                hand.WasChord=chord;
                if(!attempt || !available) continue;
                // Grip alone sprints on the left and rests naturally on things; the chord starts a grab, grip alone keeps it.
                bool free=controller.pose.isTracked && (!flying || controller==Handedness.ActionHand) && !SpatialUi.Owns(controller) && !CockpitTouch.Owns(controller) &&
                    !HandInteraction.Owns(controller) && !HelmetHud.Consumes(controller) && !LadderClimb.Holding(controller) && !GridSelection.Owns(controller) &&
                    !TouchScreenBridge.PointingFor(controller) && !ArthurLcdBridge.PointingFor(controller) && !FloatingWindows.PointingFor(controller) &&
                    !HelmetHud.NearHead(controller.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix);
                if(!free || !ModGrabBridge.Available || !HandInteraction.TryCharacterPointerPose(controller,out var pointer)) continue;
                double reach=ModGrabBridge.Reach;
                if(!GridGrab.TryPick(pointer.Translation,pointer.Forward,reach,ModGrabBridge.MaxSize,out var grid,out var local,out double distance,out string rejected))
                {
                    Logger.Info("Grab: nothing to grab under the "+(controller==Player.HandL ? "left":"right")+" hand ray"+
                        (rejected!=null ? "; "+rejected+" at "+distance.ToString("0.00")+" m":"; no grid within "+reach+" m"));
                    continue;
                }
                hold=new GridGrab.Hold {Grid=grid,Local=local,Distance=GridGrab.ClampDistance(distance,reach),SnapToCenter=Common.Config?.GrabSnapToCenter==true};
                if(hold.SnapToCenter) hold.Distance=GridGrab.SnapDistance(GridGrab.PulledPoint(hold),pointer.Translation,pointer.Forward,reach);
                holder=controller; turnFrame=null;
                hand.WasClick=Controls.Static.Unequip.RawPressed;
                For(Other(controller)).WasChord=true;
                nextStrain=now;
                Block(controller);
                CockpitFeedback.Engage(controller);
                Highlight(grid,1);
                Logger.Info("Grab: holding "+grid.DisplayName+" at "+hold.Distance.ToString("0.00")+" m");
                return;
            }
        }
        private static void HoldUpdate(MyCharacter character,Controller controller,bool flying,DateTime now,float dt)
        {
            var c=Controls.Static;
            var hand=For(controller);
            double reach=ModGrabBridge.Reach;
            Block(controller);
            bool sticks=InputRouter.Gameplay;
            bool rotating=Trigger(controller)>ChordPressure && sticks;
            hold.Rotating=rotating;
            var other=Other(controller);
            if(rotating)
            {
                // Rotation mode takes every stick: the other hand pitches and yaws, the holding hand rolls and changes distance.
                foreach(var analog in new[] {c.WalkLongitudinal,c.WalkLatitudinal,c.WalkRotate,c.ThrustLRFB,c.ThrustLRUD,c.ThrustRotate}) analog.BlockUntilRelease();
                var steer=Stick(other,flying).RawPosition; var own=Stick(controller,flying).RawPosition;
                hold.Input=new Vector3(VrMath.Deadzone(steer.Y),VrMath.Deadzone(steer.X),VrMath.Deadzone(own.X));
                float step=VrMath.Deadzone(own.Y)*GridGrab.DistanceRate*dt;
                if(step!=0) hold.Distance=GridGrab.ClampDistance(hold.Distance+step,reach);
            }
            else hold.Input=Vector3.Zero;
            // B throws in rotation mode; its readers run after this update, so the block keeps the press from unequipping or closing anything.
            bool click=rotating && c.Unequip.RawPressed;
            bool throwNow=click && !hand.WasClick;
            hand.WasClick=click;
            if(rotating) c.Unequip.BlockUntilRelease();
            if(!HandInteraction.TryCharacterPointerPose(controller,out var pointer)) { Release(true); return; }
            var grid=hold.Grid;
            // The mod's server pulls toward its own head-based point; the offset moves that point onto the hand ray.
            var head=character.GetHeadMatrix(true,true);
            hold.Point=GridGrab.PulledPoint(hold);
            hold.Target=pointer.Translation+pointer.Forward*hold.Distance;
            hold.Tension=(float)(Vector3D.DistanceSquared(hold.Target,hold.Point)/(reach*reach));
            if(hold.Tension>1) { Logger.Info("Grab: dropped, the hold line stretched past "+reach+" m"); Release(true); return; }
            bool looking=GridGrab.RayHits(pointer.Translation,pointer.Forward,reach,grid);
            long ground=GridGrab.Ground(character);
            if(throwNow)
            {
                ModGrabBridge.Send(grid.EntityId,hold.Local,GridGrab.ThrowDistance,hold.SnapToCenter,Vector3.Zero,rotating,true,GridGrab.ThrowGround,Vector3D.Zero,1,looking);
                controller.Vibrate(0,.06f,80,.6f);
                Release(false,false);
                return;
            }
            TurnUpdate(other,pointer);
            Vector3 torque=Vector3.Zero; float damping=1;
            bool steering=rotating || hold.TurnTarget.HasValue;
            if(steering)
            {
                bool input=rotating && hold.Input.LengthSquared()>1e-4f;
                if(input && hold.Axes==null) hold.Axes=GridGrab.ResolveAxes(grid.WorldMatrix,CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix));
                if(!input) hold.Axes=null;
                if(input) torque+=GridGrab.RotationTorque(hold.Axes,hold.Input);
                damping*=GridGrab.SteeringSpinDamping;
                if(hold.TurnTarget.HasValue)
                {
                    var align=GridGrab.AlignTorque(grid.WorldMatrix,hold.TurnTarget.Value,grid.Physics.Mass);
                    if(align==Vector3.Zero) damping*=GridGrab.AlignedSpinDamping; else torque+=align;
                }
                // The mod's client also kills spin directly while steering.
                grid.Physics.AngularVelocity=grid.Physics.AngularVelocity*GridGrab.ClientSpinDamping;
            }
            double packetDistance=GridGrab.PacketDistance(hold.Distance);
            if(!ModGrabBridge.Send(grid.EntityId,hold.Local,packetDistance,hold.SnapToCenter,torque,steering,true,ground,GridGrab.Offset(head,packetDistance,hold.Target),damping,looking))
            { Release(true,false); return; }
            bool touching=Touching(character,grid);
            if(GridGrab.Step(contact,true,touching)) { Logger.Info("Grab: dropped, the grid touched the player"); Release(true); return; }
            if(touching!=(highlight==2)) { Highlight(grid,touching ? 2:1); if(touching) controller.Vibrate(0,.05f,60,.5f); }
            if(hold.Tension>.3f && now>=nextStrain)
            {
                float strain=Math.Min(1,hold.Tension);
                controller.Vibrate(0,.014f,50+40*strain,.08f+.35f*strain); nextStrain=now.AddMilliseconds(45);
            }
        }
        // The other hand's chord turns the grid by the rotation of the two-fingertip frame since the chord began.
        private static void TurnUpdate(Controller other,MatrixD pointer)
        {
            var hand=For(other);
            bool chord=hand.Grip && Trigger(other)>ChordPressure && other.pose.isTracked && InputRouter.Gameplay;
            bool start=chord && !hand.WasChord;
            hand.WasChord=chord;
            if(!chord) { turnFrame=null; hold.TurnTarget=null; return; }
            if(!HandInteraction.TryCharacterPointerPose(other,out var second)) return;
            var frame=GridGrab.TurnFrame(pointer.Translation,second.Translation,pointer.Up,second.Up);
            if(start || !turnFrame.HasValue) { turnFrame=frame; turnGridStart=hold.Grid.WorldMatrix; CockpitFeedback.Activate(other); }
            Block(other);
            hold.TurnTarget=GridGrab.TurnTarget(turnFrame.Value,frame,turnGridStart);
        }
        // Short rays from the capsule centre; the held grid must be the first thing they hit.
        private static bool Touching(MyCharacter character,MyCubeGrid grid)
        {
            if(MyAPIGateway.Physics==null) return false;
            var world=character.WorldMatrix;
            var centre=character.PositionComp.GetPosition()+world.Up;
            double r=.4;
            var right=world.Right*(jitter.NextDouble()*2-1)*r; var forward=world.Forward*(jitter.NextDouble()*2-1)*r; var up=world.Up*(jitter.NextDouble()*2-1)*r*2;
            var ends=new[] {centre+world.Down*1.2+right+forward,centre+world.Up*1.2+right+forward,centre+world.Right*.6+up+forward,
                centre+world.Left*.6+up+forward,centre+world.Forward*.6+up+right,centre+world.Backward*.6+up+right};
            foreach(var end in ends)
                if(MyAPIGateway.Physics.CastRay(centre,end,out IHitInfo hit,MyPhysics.CollisionLayers.VoxelLod1CollisionLayer) &&
                    (hit.HitEntity as MyEntity)?.GetTopMostParent()==grid) return true;
            return false;
        }
        private static void Block(Controller controller)
        {
            var c=Controls.Static;
            c.BlockGripRoles(controller); c.BlockTriggerRoles(controller);
            // The trigger is rotation mode while holding, never a native use or primary action.
            (controller==Player.HandL ? c.LeftClick:c.Primary).BlockUntilRelease();
            if(controller==Handedness.ActionHand) c.Fire.BlockUntilRelease();
        }
        private static void Release(bool stillGripping,bool notify=true)
        {
            if(hold==null) return;
            if(notify && hold.Grid!=null) ModGrabBridge.Send(hold.Grid.EntityId,Vector3.Zero,0,false,Vector3.Zero,false,false,-1,Vector3D.Zero,1,false);
            if(turnFrame.HasValue) For(Other(holder)).WasChord=true;
            hold=null; turnFrame=null;
            Highlight(null,0);
            if(holder!=null && stillGripping) holder.Vibrate(0,.03f,70,.3f);
            holder=null;
        }
        // The yellow outline marks the held grid; the red pulse warns before a body contact drop.
        private static void Highlight(MyCubeGrid grid,int mode)
        {
            if(highlighted==grid && highlight==mode) return;
            if(highlighted!=null && highlighted!=grid) Set(highlighted,-1,0,Color.Yellow);
            highlighted=grid; highlight=mode;
            if(grid==null) return;
            if(mode==2) Set(grid,20,2,Color.Red*.2f); else if(mode==1) Set(grid,20,0,Color.Yellow*.1f); else Set(grid,-1,0,Color.Yellow);
        }
        private static void Set(MyCubeGrid grid,int thickness,int pulse,Color color)
        {
            if(grid==null || grid.Closed || MySession.Static==null) return;
            long player=MySession.Static.LocalPlayerId;
            MyVisualScriptLogicProvider.SetHighlight(new MyHighlightData {EntityId=grid.EntityId,OutlineColor=color,PulseTimeInFrames=(ulong)pulse,
                Thickness=thickness,PlayerId=player,IgnoreUseObjectData=true,SubPartNames=""},player);
        }
    }
}
