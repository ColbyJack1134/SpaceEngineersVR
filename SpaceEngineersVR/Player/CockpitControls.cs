using System;
using System.Linq;
using SpaceEngineersVR.Config;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitControls
    {
        private static MyCockpit seat;
        private static CockpitRig Rig => seat==null ? null : CockpitRig.Find(seat.BlockDefinition.Id.SubtypeName);
        private static readonly GripCapture left=new GripCapture(),right=new GripCapture();
        private static Matrix leftNeutral,rightNeutral,origin;
        private static Matrix leftVisual=Matrix.Identity,rightVisual=Matrix.Identity;
        private static int leftDetents,rightDetents;
        private static DateTime leftPulse,rightPulse,grabLeft,grabRight;
        private static Vector3 translation,rotation;
        private static bool leftNear,rightNear,leftWas,rightWas;
        private static readonly CockpitFeedback.ProximityPulse leftHover=new CockpitFeedback.ProximityPulse(),rightHover=new CockpitFeedback.ProximityPulse();
        private static readonly StickPlacement placement=new StickPlacement();
        private static Vector3 leftStartOffset,rightStartOffset,fit;
        public static bool Adjusting => placement.Unlocked;
        public static bool CanAdjust => seat!=null && Eligible(seat) && CockpitRender.Ready && InputRouter.CockpitInteraction && !Main.MenuOpen;
        public static bool RotationOwned => Adjusting || right.Consumed || Rig!=null && Rig.Right==null && left.Consumed;
        internal static bool NeedsControllerTranslation => Rig!=null && (Rig.Left==null || Rig.Right==null);
        private static bool SingleLeft => Rig!=null && Rig.Right==null;
        internal static bool OwnsRightThumb => seat!=null && !Adjusting && (right.Held || SingleLeft && left.Held);
        public static bool Held(Controller hand) => hand==Player.HandL ? left.Held : right.Held;
        private static bool Available => !ThirdPersonView.Active && seat!=null && Eligible(seat) && CockpitRender.Ready && (InputRouter.Mode==InputMode.Piloting || InputRouter.Mode==InputMode.Turret) && !RemoteView.OwnsInput && !Main.MenuOpen && !TouchScreenBridge.OwnsInput &&
                Player.Headset.pose.isTracked && Player.HandL.pose.isTracked && Player.HandR.pose.isTracked;
        internal static bool NearGrip(Controller hand) => Available && GripDistance(hand)<FighterProfile.CaptureRadius;
        private static float GripDistance(Controller hand)
        {
            bool isLeft=hand==Player.HandL;
            var stick=isLeft ? Rig?.Left:Rig?.Right;
            if(Rig!=null && stick==null) return float.MaxValue;
            return GripDistance(HandLocal(hand),TrackedArms.WristForPalm(hand,StickPalm(isLeft)));
        }
        internal static float GripDistance(Matrix controller,Matrix attachedWrist) =>
            Vector3.Distance(CockpitHandPose.GripWrist(controller).Translation,attachedWrist.Translation);
        private static Matrix StickPalm(bool isLeft) =>
            (Rig==null ? CockpitStickMath.GripPalm(isLeft):(isLeft ? Rig.Left:Rig.Right).Palm(isLeft))*(isLeft ? leftVisual:rightVisual);
        public static string Status => seat==null ? null : Adjusting ? "STICKS UNLOCKED: grip to move; padlock saves" : CockpitRender.Status+(left.Held ? Rig!=null && Rig.Right==null ? " | LEFT: rotation" : " | LEFT: translation" : "")+(right.Held ? " | RIGHT: rotation" : "");

        public static void Reset()
        {
            Release(); seat=null; leftWas=rightWas=false; CockpitRender.Reset();
        }
        public static void Release()
        {
            if (Adjusting)
            {
                placement.Cancel(); BlockTranslation(); BlockRotation(); Controls.Static.Primary.BlockUntilRelease();
            }
            if (left.Consumed) { BlockTranslation(); if(Rig!=null && Rig.Right==null) BlockRotation(); }
            if (right.Consumed) BlockRotation();
            left.Release(); right.Release();
            leftHover.Sample(false,0,0); rightHover.Sample(false,0,0);
            translation=rotation=Vector3.Zero;
            SetVisuals();
        }
        private static void SetVisuals()
        {
            var rig=Rig;
            leftVisual=StickPlacement.Visual(rig?.Left!=null ? CockpitStickMath.Visual(rig.Left.Pivot,rig.Right==null ? rotation : new Vector3(-translation.Z,translation.Y,translation.X)) : CockpitStickMath.LeftVisual(translation),placement.Left);
            rightVisual=StickPlacement.Visual(rig?.Right!=null ? CockpitStickMath.Visual(rig.Right.Pivot,rotation) : CockpitStickMath.RightVisual(rotation),placement.Right);
        }
        public static void ToggleAdjustment()
        {
            if (!CanAdjust) return;
            bool locking=Adjusting;
            if (locking) { placement.Lock(); SavePlacement(); }
            Release();
            if (!locking) placement.Unlock();
            Controls.Static.BlockUntilRelease();
        }
        public static void ResetPlacement()
        {
            if (!CanAdjust) return;
            Release(); placement.Load(Vector3.Zero,Vector3.Zero); SetVisuals(); SavePlacement();
            Controls.Static.BlockUntilRelease();
        }
        private static void SavePlacement()
        {
            var value=new StickPlacementSetting { Subtype=seat.BlockDefinition.Id.SubtypeName,LeftX=placement.Left.X,LeftY=placement.Left.Y,LeftZ=placement.Left.Z,
                RightX=placement.Right.X,RightY=placement.Right.Y,RightZ=placement.Right.Z };
            Common.Config.StickPlacements=Common.Config.StickPlacements.Where(s=>s!=null && s.Subtype!=value.Subtype).Concat(new[] {value}).ToArray();
        }
        private static void BlockTranslation()
        {
            var c=Controls.Static;
            c.ThrustLRFB.BlockUntilRelease(); c.ThrustLRUD.BlockUntilRelease();
            c.ThrustUp.BlockUntilRelease(); c.ThrustDown.BlockUntilRelease();
            c.ThrustForward.BlockUntilRelease(); c.ThrustBackward.BlockUntilRelease();
        }
        private static void BlockRotation()
        {
            Controls.Static.ThrustRotate.BlockUntilRelease(); Controls.Static.ThrustRoll.BlockUntilRelease();
            Controls.Static.Secondary.BlockUntilRelease();
        }
        private static bool Eligible(MyCockpit cockpit) => cockpit!=null && !cockpit.Closed && !cockpit.MarkedForClose &&
            ModelSupported(cockpit) &&
            cockpit.Pilot==MySession.Static?.LocalCharacter && cockpit.Pilot!=null && !cockpit.Pilot.IsDead &&
            (MySession.Static.CameraController==cockpit || RemoteView.UsesSeat(cockpit)) && (cockpit.IsInFirstPersonView || cockpit.ForceFirstPersonCamera || RemoteView.UsesSeat(cockpit));

        private static bool ModelSupported(MyCockpit cockpit)
        {
            string subtype=cockpit.BlockDefinition.Id.SubtypeName,model=cockpit.BlockDefinition.InteriorModel ?? cockpit.BlockDefinition.Model;
            if(subtype==FighterProfile.Subtype) return model!=null && model.Replace('\\','/').EndsWith(FighterProfile.Model,StringComparison.OrdinalIgnoreCase);
            var rig=CockpitRig.Find(subtype);
            return rig!=null && (rig.HasSticks || rig.Handles.Length>0) && rig.Matches(model);
        }

        public static bool HasTrackedSeat(MyCharacter character) => Main.VrActive && seat!=null &&
            character==seat.Pilot && Eligible(seat) && CockpitRender.Ready && Common.Config.FighterCockpitSticks;
        public static Matrix HandLocal(Controller hand)
        {
            // Cancel ship motion before narrowing to float; never subtract world hand positions across ticks.
            MatrixD headLocal=seat.GetHeadMatrix(true,true)*seat.PositionComp.WorldMatrixNormalizedInv;
            return (Matrix)((MatrixD)VrMath.Affine(hand.GripTracking*Player.PlayerToAbsolute.inverted)*headLocal);
        }
        public static MatrixD WristWorld(Controller hand)
        {
            Matrix local=CockpitHandPose.GripWrist(HandLocal(hand));
            if (Held(hand))
            {
                bool isLeft=hand==Player.HandL;
                Matrix attached=TrackedArms.WristForPalm(hand,StickPalm(isLeft));
                float t=MathHelper.Clamp((float)(DateTime.UtcNow-(isLeft ? grabLeft : grabRight)).TotalSeconds/0.12f,0,1);
                Quaternion q=Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(local),Quaternion.CreateFromRotationMatrix(attached),t);
                Matrix blended=Matrix.CreateFromQuaternion(q);
                blended.Translation=Vector3.Lerp(local.Translation,attached.Translation,t);
                local=blended;
            }
            return (MatrixD)local*seat.WorldMatrix;
        }
        public static void Update()
        {
            long started=FeatureTiming.Start();
            try { UpdateCore(); }
            finally { FeatureTiming.End(FeatureTiming.Area.Cockpit,started); }
        }
        private static void UpdateCore()
        {
            var next=RemoteView.HomeSeat ?? MySession.Static?.ControlledEntity as MyCockpit;
            if (!Main.VrActive || !Common.Config.FighterCockpitSticks || !Eligible(next))
            { if (seat!=null) Reset(); return; }
            if (seat!=next)
            {
                Reset(); seat=next; origin=Player.PlayerToAbsolute.matrix; fit=SeatFit.Offset;
                var saved=Common.Config.StickPlacements.FirstOrDefault(s=>s!=null && s.Subtype==seat.BlockDefinition.Id.SubtypeName);
                placement.Load(saved==null ? Vector3.Zero : new Vector3(saved.LeftX,saved.LeftY,saved.LeftZ),
                    saved==null ? Vector3.Zero : new Vector3(saved.RightX,saved.RightY,saved.RightZ));
                SetVisuals();
            }
            if (origin!=Player.PlayerToAbsolute.matrix || fit!=SeatFit.Offset) { Release(); origin=Player.PlayerToAbsolute.matrix; fit=SeatFit.Offset; }
            RefreshVisuals();
            bool available=Available;
            if (!available && Adjusting) Release();
            var c=Controls.Static;
            bool leftDown=left.AnalogDown(c.ThrustDown.Position.X,c.ThrustDown.RawPosition.X);
            bool rightDown=right.Consumed ? c.ThrustRoll.RawPressed : c.ThrustRoll.IsPressed;
            Matrix l=available ? HandLocal(Player.HandL) : Matrix.Identity;
            Matrix r=available ? HandLocal(Player.HandR) : Matrix.Identity;
            Vector3 lp=WeaponPose.Palm(l),rp=WeaponPose.Palm(r);
            float leftDistance=available ? GripDistance(Player.HandL):float.MaxValue;
            float rightDistance=available ? GripDistance(Player.HandR):float.MaxValue;
            leftNear=available && leftDistance<FighterProfile.CaptureRadius;
            rightNear=available && rightDistance<FighterProfile.CaptureRadius;
            if(leftHover.Sample(available,leftDistance,FighterProfile.CaptureRadius,leftDown || left.Consumed || CockpitTouch.Owns(Player.HandL)))
                CockpitFeedback.Hover(Player.HandL);
            if(rightHover.Sample(available,rightDistance,FighterProfile.CaptureRadius,rightDown || right.Consumed || CockpitTouch.OwnsRight))
                CockpitFeedback.Hover(Player.HandR);
            if (left.Update(available && !CockpitTouch.Owns(Player.HandL),leftDown,leftNear,!left.Held || Vector3.Distance(lp,WeaponPose.Palm(leftNeutral))<0.45f))
            {
                grabLeft=DateTime.UtcNow; leftDetents=0;
                leftStartOffset=placement.Left;
                leftNeutral=l; BlockTranslation(); if(Rig!=null && Rig.Right==null) BlockRotation(); Player.HandL.Vibrate(0,0.055f,110,0.5f);
            }
            if (right.Update(available && !CockpitTouch.OwnsRight,rightDown,rightNear,!right.Held || Vector3.Distance(rp,WeaponPose.Palm(rightNeutral))<0.45f))
            {
                grabRight=DateTime.UtcNow; rightDetents=0;
                rightStartOffset=placement.Right;
                rightNeutral=r; BlockRotation(); Player.HandR.Vibrate(0,0.055f,110,0.5f);
            }
            if (leftWas && !left.Held) { BlockTranslation(); if(Rig!=null && Rig.Right==null) BlockRotation(); }
            if (rightWas && !right.Held) BlockRotation();
            float deadzone=Common.Config.PhysicalStickDeadzone;
            bool twist=Common.Config.StickTwist;
            if (Adjusting)
            {
                if (left.Held) placement.Move(true,leftStartOffset,WeaponPose.Palm(leftNeutral),lp);
                if (right.Held) placement.Move(false,rightStartOffset,WeaponPose.Palm(rightNeutral),rp);
                // Released frames must rearm the trigger for the seat pad's next click.
                if(c.Primary.RawPressed) c.Primary.BlockUntilRelease();
                c.Secondary.BlockUntilRelease();
            }
            translation=left.Held && !Adjusting && !(Rig!=null && Rig.Right==null) ? CockpitStickMath.Translation(leftNeutral,l,deadzone,twist) : Vector3.Zero;
            rotation=Adjusting ? Vector3.Zero : Rig!=null && Rig.Right==null && left.Held ? CockpitStickMath.Rotation(leftNeutral,l,deadzone,twist) : right.Held ? CockpitStickMath.Rotation(rightNeutral,r,deadzone,twist) : Vector3.Zero;
            SetVisuals();
            Feedback(Player.HandL,left.Held,Rig!=null && Rig.Right==null ? rotation:translation,ref leftDetents,ref leftPulse);
            Feedback(Player.HandR,right.Held,rotation,ref rightDetents,ref rightPulse);
            CockpitFeedback.Motion(Player.HandL,left.Held && !Adjusting,Rig!=null && Rig.Right==null ? rotation:translation,DateTime.UtcNow<leftPulse || (DateTime.UtcNow-grabLeft).TotalSeconds<.08);
            CockpitFeedback.Motion(Player.HandR,right.Held && !Adjusting,rotation,DateTime.UtcNow<rightPulse || (DateTime.UtcNow-grabRight).TotalSeconds<.08);
            if (leftWas && !left.Held) Player.HandL.Vibrate(0,0.025f,75,0.2f);
            if (rightWas && !right.Held) Player.HandR.Vibrate(0,0.025f,75,0.2f);
            leftWas=left.Held; rightWas=right.Held;
            RefreshVisuals();
        }
        private static void Feedback(Controller hand,bool held,Vector3 axes,ref int previous,ref DateTime next)
        {
            int current=CockpitStickMath.Detents(axes);
            bool limit=(current & ~previous & 56)!=0, center=(previous & ~current & 7)!=0;
            previous=current;
            if (!held || (!limit && !center) || DateTime.UtcNow<next) return;
            hand.Vibrate(0,limit ? 0.04f : 0.018f,limit ? 85 : 140,limit ? 0.35f : 0.15f);
            next=DateTime.UtcNow.AddMilliseconds(130);
        }
        public static void RefreshVisuals()
        {
            if (seat==null || !Eligible(seat)) return;
            CockpitRender.Update(seat,leftVisual,rightVisual,left.Held,right.Held,placement.Left,placement.Right);
        }
        public static void ApplyTurret(float speed,ref Vector2 aim,ref float zoom)
        {
            if(seat==null || !Eligible(seat) || !CockpitRender.Ready) return;
            bool singleLeft=Rig!=null && Rig.Right==null;
            if(right.Consumed || singleLeft && left.Consumed)
                aim=new Vector2(rotation.X,rotation.Z)*speed*Common.Config.PhysicalStickSensitivity;
            if(left.Consumed && !singleLeft) zoom=translation.Z;
        }
        public static void ApplyFlight(float speed,ref Vector3 move,ref Vector2 rotate,ref float roll)
        {
            if (seat==null || !Eligible(seat) || !CockpitRender.Ready || InputRouter.Mode!=InputMode.Piloting) return;
            if (Adjusting) { move=Vector3.Zero; rotate=Vector2.Zero; roll=0; return; }
            bool singleLeft=Rig!=null && Rig.Right==null;
            var c=Controls.Static;
            CockpitStickMath.ApplyFlight(left.Consumed && !singleLeft,right.Consumed || singleLeft && left.Consumed,left.Held ? translation : Vector3.Zero,right.Held || singleLeft && left.Held ? rotation : Vector3.Zero,
                left.Held && !singleLeft ? c.ThrustLRFB.Position.Y : 0,OwnsRightThumb ? c.ThrustRotate.Position.X : 0,speed,Common.Config.PhysicalStickSensitivity,Common.Config.ShipRollSensitivity,ref move,ref rotate,ref roll);
        }
        public static void Draw()
        {
            if (!Common.Config.DeveloperTools || seat==null || !CockpitRender.Ready || InputRouter.Mode!=InputMode.Piloting) return;
            DrawContact(Vector3.Transform(Rig?.Left?.Contact ?? FighterProfile.LeftContact,leftVisual),left.Held,leftNear);
            DrawContact(Vector3.Transform(Rig?.Right?.Contact ?? FighterProfile.RightContact,rightVisual),right.Held,rightNear);
        }
        private static void DrawContact(Vector3 local,bool held,bool near)
        {
            Vector3D p=Vector3D.Transform(local,seat.WorldMatrix);
            var color=(held ? new Color(70,255,130) : near ? new Color(255,215,75) : new Color(80,160,200)).ToVector4();
            MySimpleObjectDraw.DrawLine(p-seat.WorldMatrix.Right*0.02,p+seat.WorldMatrix.Right*0.02,MyStringId.GetOrCompute("Square"),ref color,0.008f);
        }
    }
}
