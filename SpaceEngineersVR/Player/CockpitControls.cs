using System;
using System.Linq;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Multiplayer;
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
        private static readonly HotasHands.Hand leftHotas=new HotasHands.Hand(),rightHotas=new HotasHands.Hand();
        private static readonly CockpitSteering steering=new CockpitSteering();
        private static readonly CockpitBarTilt barTilt=new CockpitBarTilt();
        private static float throttlePosition;
        private static readonly CockpitThrottle throttle=new CockpitThrottle();
        private static readonly CockpitYoke yoke=new CockpitYoke();
        private static float yokeRoll;
        private static float throttleCommand;
        internal static Matrix SteeringVisual => YokeMode ? Rig.Wheel.YokeVisual(yokeRoll,yoke.Visual) : Rig?.Wheel?.Visual(steering.VisualPosition,barTilt.Visual) ?? Matrix.Identity;
        private static bool YokeMode => VehicleFlight && Rig.Wheel.ThrottleActor<0 && FlightSettings.For(seat).WheelMotion;
        internal static float Throttle => IsSteering && Rig.Wheel.ThrottleActor>=0 && right.Held ? throttleCommand:0;
        internal static float ThrottleVisual { get; private set; }
        internal static bool IsSteering => Rig?.Wheel!=null;
        internal static bool VehicleFlight => IsSteering && FlightSettings.For(seat).FlightMode;
        private static Matrix leftNeutral,rightNeutral,origin;
        private static Vector3 leftAngles,rightAngles;
        private static Multiplayer.FlightTuning tuning=new Multiplayer.FlightTuning();
        private static Matrix leftVisual=Matrix.Identity,rightVisual=Matrix.Identity;
        private static readonly CockpitFeedback.StickPulse leftDetent=new CockpitFeedback.StickPulse(),rightDetent=new CockpitFeedback.StickPulse();
        private static readonly CockpitFeedback.StickPulse barDetent=new CockpitFeedback.StickPulse();
        private static readonly CockpitStickMath.Filter leftFilter=new CockpitStickMath.Filter(),rightFilter=new CockpitStickMath.Filter();
        private static DateTime lastInput;
        private static DateTime leftPulse,rightPulse,barPulse,grabLeft,grabRight;
        private static Vector3 translation,rotation;
        private static Vector2 barCommand;
        private static FlightTuning previousTuning;
        private static bool leftNear,rightNear,leftWas,rightWas;
        private static readonly CockpitFeedback.ProximityPulse leftHover=new CockpitFeedback.ProximityPulse(),rightHover=new CockpitFeedback.ProximityPulse();
        private static readonly StickPlacement placement=new StickPlacement();
        private static Vector3 leftStartOffset,rightStartOffset,fit;
        public static bool Adjusting => placement.Unlocked;
        public static bool CanAdjust => seat!=null && !IsSteering && Eligible(seat) && CockpitRender.Ready && InputRouter.CockpitInteraction && !Main.MenuOpen;
        public static bool RotationOwned => Adjusting || !IsSteering && right.Consumed;
        // A seat with one stick keeps it as a left (translation) stick; the controller supplies the missing stick's axes.
        internal static bool NeedsControllerTranslation => Rig!=null && (Rig.Left==null || IsSteering && Rig.Right==null);
        internal static bool NeedsControllerRotation => Rig!=null && !IsSteering && Rig.Right==null;
        internal static bool OwnsRightThumb => seat!=null && !IsSteering && !Adjusting && right.Held;
        public static bool Held(Controller hand) => hand==Player.HandL ? left.Held : right.Held;
        // Held, or resting on the stick while a HOTAS drives it; only the pose follows, input stays with the controller.
        internal static bool OnStick(Controller hand) => Held(hand) || (hand==Player.HandL ? leftHotas:rightHotas).Active;
        internal static float[] HeldCurl(Controller hand) => IsSteering || !OnStick(hand) ? null : (hand==Player.HandL ? Rig?.Left:Rig?.Right)?.Curl;
        internal static bool AnyHeld => left.Held || right.Held;
        // Only modeled joysticks gate the action trigger; wheels and handlebars are not a stick to hold.
        internal static bool SticksAvailable => Grabbable && Rig?.HasSticks==true;
        private static bool Available => !ThirdPersonView.Active && seat!=null && Eligible(seat) && CockpitRender.Ready && (InputRouter.Mode==InputMode.Piloting || InputRouter.Mode==InputMode.Turret) && !FloatingWindows.OwnsInput && !Main.MenuOpen && !TouchScreenBridge.OwnsInput &&
                Player.Headset.pose.isTracked && Player.HandL.pose.isTracked && Player.HandR.pose.isTracked &&
                (!IsSteering || InputRouter.Mode==InputMode.Piloting);
        private static bool Grabbable => Available && !FlightSettings.IsOpen;
        internal const float CaptureRadius=.14f;
        internal static bool NearGrip(Controller hand) => Available && NearDistance(hand==Player.HandL,GripDistance(hand));
        private static bool NearDistance(bool isLeft,float distance) => IsSteering ?
            (isLeft ? leftHover:rightHover).Contains(distance,CaptureRadius):distance<CaptureRadius;
        private static float GripDistance(Controller hand)
        {
            bool isLeft=hand==Player.HandL;
            var stick=isLeft ? Rig?.Left:Rig?.Right;
            if(Rig!=null && stick==null && !IsSteering) return float.MaxValue;
            var controller=HandLocal(hand);
            var attached=TrackedArms.WristForPalm(hand,StickPalm(isLeft));
            if(IsSteering && !CockpitStickMath.GripAligned(CockpitHandPose.GripWrist(controller),attached)) return float.MaxValue;
            return GripDistance(controller,attached);
        }
        internal static float GripDistance(Matrix controller,Matrix attachedWrist) =>
            Vector3.Distance(CockpitHandPose.GripWrist(controller).Translation,attachedWrist.Translation);
        private static Matrix StickPalm(bool isLeft) =>
            IsSteering ? (YokeMode ? Rig.Wheel.YokePalm(isLeft,yokeRoll,yoke.Visual) : Rig.Wheel.Palm(isLeft,steering.VisualPosition,ThrottleVisual,barTilt.Visual)) :
            (isLeft ? Rig.Left:Rig.Right).Palm(isLeft)*(isLeft ? leftVisual:rightVisual);
        public static string Status => seat==null ? null : Adjusting ? "STICKS UNLOCKED: grip to move; padlock saves" : CockpitRender.Status+(IsSteering ? (left.Held || right.Held ? " | steering":"") : (left.Held ? " | LEFT: translation" : "")+(right.Held ? " | RIGHT: rotation" : ""));

        public static void Reset()
        {
            Release(); leftAngles=rightAngles=Vector3.Zero; seat=null; previousTuning=null; leftWas=rightWas=false; CockpitRender.Reset();
        }
        public static void Release()
        {
            if (Adjusting)
            {
                placement.Cancel(); BlockTranslation(); BlockRotation(); Controls.Static.Primary.BlockUntilRelease();
            }
            if (left.Consumed) BlockGrip(true);
            if (right.Consumed) BlockGrip(false);
            left.Release(); right.Release(); leftHotas.Reset(); rightHotas.Reset();
            leftHover.Sample(false,0,0); rightHover.Sample(false,0,0);
            translation=rotation=Vector3.Zero; steering.Reset(); throttlePosition=0; throttle.Reset(); yoke.Reset(); yokeRoll=0; throttleCommand=ThrottleVisual=0;
            barTilt.Reset(); barCommand=Vector2.Zero;
            leftFilter.Update(false,Vector3.Zero,0,0); rightFilter.Update(false,Vector3.Zero,0,0);
            leftDetent.Sample(false,Vector3.Zero,DateTime.UtcNow); rightDetent.Sample(false,Vector3.Zero,DateTime.UtcNow);
            barDetent.Sample(false,Vector3.Zero,DateTime.UtcNow); barPulse=DateTime.MinValue;
            SetVisuals();
        }
        private static void SetVisuals()
        {
            var rig=Rig;
            if(IsSteering) { leftVisual=rightVisual=SteeringVisual; return; }
            leftVisual=StickPlacement.Visual(rig?.Left!=null ? rig.Left.Visual(leftAngles) : Matrix.Identity,placement.Left);
            rightVisual=StickPlacement.Visual(rig?.Right!=null ? rig.Right.Visual(rightAngles) : Matrix.Identity,placement.Right);
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
        // The Suspended seats' floating right stick moved inboard; an older moved stick stays where the player left it.
        internal static Vector3 SavedRight(StickPlacementSetting saved)
        {
            var right=new Vector3(saved.RightX,saved.RightY,saved.RightZ);
            return saved.Version<1 && right!=Vector3.Zero && saved.Subtype?.Contains("SuspendedControlSeat")==true ? right+new Vector3(.145f,-.01f,.14f):right;
        }
        private static void SavePlacement()
        {
            var value=new StickPlacementSetting { Subtype=seat.BlockDefinition.Id.SubtypeName,LeftX=placement.Left.X,LeftY=placement.Left.Y,LeftZ=placement.Left.Z,
                RightX=placement.Right.X,RightY=placement.Right.Y,RightZ=placement.Right.Z,Version=StickPlacementSetting.Current };
            Common.Config.StickPlacements=Common.Config.StickPlacements.Where(s=>s!=null && s.Subtype!=value.Subtype).Concat(new[] {value}).ToArray();
        }
        private static void BlockGrip(bool isLeft)
        {
            if(IsSteering)
            {
                Controls.Static.ThrustLRFB.BlockUntilRelease();
                if(VehicleFlight && (!isLeft || !right.Held)) BlockRotation();
            }
            else if(isLeft) BlockTranslation();
            else BlockRotation();
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
            var rig=CockpitRig.Find(subtype);
            return rig!=null && (rig.HasSticks || rig.Handles.Length>0 || rig.Wheel!=null) && rig.Matches(model);
        }

        public static bool HasTrackedSeat(MyCharacter character) => Main.VrActive && seat!=null &&
            character==seat.Pilot && Eligible(seat) && CockpitRender.Ready && Common.Config.FighterCockpitSticks;
        public static Matrix HandLocal(Controller hand) => SeatLocal(hand.GripTracking);
        private static Matrix SeatLocal(Matrix tracking)
        {
            // Cancel ship motion before narrowing to float; never subtract world hand positions across ticks.
            MatrixD headLocal=seat.GetHeadMatrix(true,true)*seat.PositionComp.WorldMatrixNormalizedInv;
            return (Matrix)((MatrixD)VrMath.Affine(tracking*Player.PlayerToAbsolute.inverted)*headLocal);
        }
        public static MatrixD WristWorld(Controller hand)
        {
            Matrix local=CockpitHandPose.GripWrist(HandLocal(hand));
            if (OnStick(hand))
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
                    saved==null ? Vector3.Zero : SavedRight(saved));
                SetVisuals();
            }
            if (origin!=Player.PlayerToAbsolute.matrix || fit!=SeatFit.Offset) { Release(); origin=Player.PlayerToAbsolute.matrix; fit=SeatFit.Offset; }
            var nextTuning=FlightSettings.For(seat);
            if(previousTuning!=nextTuning)
            {
                if(previousTuning!=null && previousTuning.FlightMode!=nextTuning.FlightMode) Controls.Static.BlockUntilRelease();
                if(previousTuning==null || !previousTuning.Same(nextTuning)) Release();
                previousTuning=nextTuning;
            }
            RefreshVisuals();
            bool available=Grabbable;
            if (!available && Adjusting) Release();
            var c=Controls.Static;
            bool leftDown=left.AnalogDown(c.ThrustDown.Position.X,c.ThrustDown.RawPosition.X);
            bool rightDown=right.Consumed ? c.ThrustRoll.RawPressed : c.ThrustRoll.IsPressed;
            Matrix l=available ? HandLocal(Player.HandL) : Matrix.Identity;
            Matrix r=available ? HandLocal(Player.HandR) : Matrix.Identity;
            Vector3 lp=WeaponPose.Palm(l),rp=WeaponPose.Palm(r);
            float leftDistance=available ? GripDistance(Player.HandL):float.MaxValue;
            float rightDistance=available ? GripDistance(Player.HandR):float.MaxValue;
            if(leftHover.Sample(available,leftDistance,CaptureRadius,leftDown || left.Consumed || CockpitTouch.Owns(Player.HandL)))
                CockpitFeedback.Hover(Player.HandL);
            if(rightHover.Sample(available,rightDistance,CaptureRadius,rightDown || right.Consumed || CockpitTouch.OwnsRight))
                CockpitFeedback.Hover(Player.HandR);
            leftNear=available && NearDistance(true,leftDistance);
            rightNear=available && NearDistance(false,rightDistance);
            if (left.Update(available && !CockpitTouch.Owns(Player.HandL) && !ArthurLcdBridge.Owns(Player.HandL),leftDown,leftNear,true,Common.Config.TapHoldSticks && !Adjusting,Multiplayer.MultiplayerRuntime.Now))
            {
                grabLeft=DateTime.UtcNow; leftDetent.Sample(false,Vector3.Zero,grabLeft);
                leftStartOffset=placement.Left;
                leftNeutral=l; leftAngles=Vector3.Zero; BlockGrip(true); Player.HandL.Vibrate(0,0.055f,110,0.5f);
            }
            if (right.Update(available && !CockpitTouch.OwnsRight && !ArthurLcdBridge.Owns(Player.HandR),rightDown,rightNear,true,Common.Config.TapHoldSticks && !Adjusting,Multiplayer.MultiplayerRuntime.Now))
            {
                grabRight=DateTime.UtcNow; rightDetent.Sample(false,Vector3.Zero,grabRight);
                rightStartOffset=placement.Right;
                rightNeutral=r; rightAngles=Vector3.Zero; BlockGrip(false); Player.HandR.Vibrate(0,0.055f,110,0.5f);

            }
            if (leftWas && !left.Held) BlockGrip(true);
            if (rightWas && !right.Held) BlockGrip(false);
            tuning=nextTuning;
            float deadzone=tuning.TiltDeadzone;
            bool twist=Common.Config.StickTwist;
            float sensitivity=tuning.Rotation;
            var now=DateTime.UtcNow;
            float dt=(float)Math.Min(.05,Math.Max(0,(now-lastInput).TotalSeconds)); lastInput=now;
            Vector3 hotasMove=Vector3.Zero,hotasTurn=Vector3.Zero;
            bool hotas=available && !IsSteering && !Adjusting && HotasHands.TryRead(out hotasMove,out hotasTurn);
            bool leftOn=leftHotas.Active,rightOn=rightHotas.Active;
            if(leftHotas.Update(hotas && Rig.Left!=null && !left.Held && !CockpitTouch.Owns(Player.HandL),hotasMove!=Vector3.Zero,l,now) && !leftOn) grabLeft=now;
            if(rightHotas.Update(hotas && Rig.Right!=null && !right.Held && !CockpitTouch.OwnsRight,hotasTurn!=Vector3.Zero,r,now) && !rightOn) grabRight=now;
            if (Adjusting)
            {
                if (left.Held) placement.Move(true,leftStartOffset,WeaponPose.Palm(leftNeutral),lp);
                if (right.Held) placement.Move(false,rightStartOffset,WeaponPose.Palm(rightNeutral),rp);
                // Released frames must rearm the trigger for the seat pad's next click.
                if(c.Primary.RawPressed) c.Primary.BlockUntilRelease();
                c.Secondary.BlockUntilRelease();
            }
            Vector3 feedbackTranslation,feedbackRotation;
            if(IsSteering)
            {
                // Positive model rotation turns left; native wheel X is positive to the right.
                float value=-steering.Update(Rig.Wheel,left.Held,lp,right.Held,rp,dt);
                bool pairWas=barTilt.Active;
                Matrix rawLeft=available ? SeatLocal(Player.HandL.pose.deviceToAbsolute.matrix):Matrix.Identity;
                Matrix rawRight=available ? SeatLocal(Player.HandR.pose.deviceToAbsolute.matrix):Matrix.Identity;
                barCommand=barTilt.Update(available && VehicleFlight && Rig.Wheel.ThrottleActor>=0 && tuning.BarTiltEnabled && left.Held && right.Held,
                    rawLeft,rawRight,Rig.Wheel,tuning,dt,Rig.Wheel.Visual(steering.Position));
                bool pairChanged=pairWas!=barTilt.Active;
                if(pairChanged)
                {
                    leftDetent.Sample(false,Vector3.Zero,now); rightDetent.Sample(false,Vector3.Zero,now);
                    barDetent.Sample(false,Vector3.Zero,now); barPulse=now.AddMilliseconds(80);
                    leftPulse=rightPulse=now.AddMilliseconds(80);
                    if(barTilt.Active) CockpitFeedback.Engage(grabLeft>grabRight ? Player.HandR:Player.HandL);
                }
                if(Rig.Wheel.ThrottleActor>=0)
                {
                    Matrix throttleFrame=barTilt.Active ? barTilt.ThrottleFrame(rawLeft,rawRight,Rig.Wheel):Rig.Wheel.Visual(steering.Position).GetOrientation();
                    throttlePosition=throttle.Update(right.Held,barTilt.Active,r,throttleFrame,Rig.Wheel.RightShaft,Rig.Wheel.ThrottleRange);
                    throttleCommand=rightFilter.Update(right.Held,new Vector3(CockpitStickMath.Axis((throttlePosition)*tuning.ThrottleSensitivity,tuning.TwistDeadzone),0,0),dt,tuning.Smoothing).X;
                    float trigger=available && seat.Toolbar?.SelectedItem==null && c.Primary.IsPressed ? MathHelper.Clamp(c.PointerPressure.Position.X,0,1):0;
                    float target=Math.Max(right.Held ? throttleCommand:0,trigger);
                    ThrottleVisual=right.Held || trigger>0 ? CockpitStickMath.SmoothVisual(new Vector3(ThrottleVisual,0,0),new Vector3(target,0,0),dt).X:
                        CockpitStickMath.ReturnVisual(new Vector3(ThrottleVisual,0,0),dt).X;
                }
                feedbackTranslation=new Vector3(steering.Position,0,0);
                feedbackRotation=new Vector3(steering.Position,right.Held ? throttleCommand:0,0);
                if(barTilt.Active)
                {
                    feedbackTranslation=new Vector3(-CockpitStickMath.Axis(barTilt.Raw.X/tuning.BarPitchTravel,tuning.BarPitchDeadzone/tuning.BarPitchTravel),
                        CockpitStickMath.Axis(value*tuning.Translation,deadzone),CockpitStickMath.Axis(barTilt.Raw.Y/tuning.BarRollTravel,tuning.BarRollDeadzone/tuning.BarRollTravel));
                    feedbackRotation=feedbackTranslation; feedbackRotation.Y=throttleCommand;
                }
                float pitch=yoke.Update(YokeMode,left.Held,rawLeft.Translation,right.Held,rawRight.Translation,Rig.Wheel.Axis,tuning,dt);
                float wheelCommand=CockpitStickMath.Axis(value*Rig.Wheel.Range/tuning.WheelRollTravel,tuning.TiltDeadzone);
                float rollTarget=CockpitStickMath.Response(wheelCommand*tuning.RollSensitivity,tuning.RotationCurve)*tuning.WheelRollTravel/Rig.Wheel.Range;
                yokeRoll=YokeMode && (left.Held || right.Held) ? CockpitStickMath.SmoothVisual(new Vector3(yokeRoll,0,0),new Vector3(-rollTarget,0,0),dt).X:CockpitStickMath.ReturnVisual(new Vector3(yokeRoll,0,0),dt).X;
                if(YokeMode) { feedbackTranslation=new Vector3(CockpitStickMath.Response(pitch*tuning.PitchSensitivity,tuning.RotationCurve),0,CockpitStickMath.Response(wheelCommand*tuning.RollSensitivity,tuning.RotationCurve)); feedbackRotation=feedbackTranslation; }
                translation=leftFilter.Update(left.Held || right.Held,new Vector3(CockpitStickMath.Axis(value*tuning.Translation,deadzone),0,0),dt,tuning.Smoothing);
                rotation=Vector3.Zero;
            }
            else
            {
                translation=left.Held && !Adjusting ? CockpitStickMath.Translation(leftNeutral,l,deadzone,twist,tuning.Translation,Rig.Left.Frame,tuning.TwistDeadzone) : Vector3.Zero;
                rotation=right.Held && !Adjusting ? CockpitStickMath.Rotation(rightNeutral,r,deadzone,twist,sensitivity,Rig.Right.Frame,tuning.TwistDeadzone) : Vector3.Zero;
                translation=leftFilter.Update(left.Held && !Adjusting,translation,dt,tuning.Smoothing);
                rotation=rightFilter.Update(right.Held && !Adjusting,rotation,dt,tuning.Smoothing);
                feedbackTranslation=CockpitStickMath.Response(translation,InputRouter.Mode==InputMode.Turret ? 1:tuning.TranslationCurve);
                feedbackRotation=CockpitStickMath.Response(rotation,tuning.RotationCurve);
                leftAngles=left.Held && !Adjusting ? CockpitStickMath.SmoothVisual(leftAngles,CockpitStickMath.HandVisual(leftNeutral,l,twist,tuning.Translation,Rig.Left.Frame),dt):
                    leftHotas.Active ? CockpitStickMath.SmoothVisual(leftAngles,HotasHands.LeftAngles(hotasMove),dt):CockpitStickMath.ReturnVisual(leftAngles,dt);
                rightAngles=right.Held && !Adjusting ? CockpitStickMath.SmoothVisual(rightAngles,CockpitStickMath.HandVisual(rightNeutral,r,twist,sensitivity,Rig.Right.Frame),dt):
                    rightHotas.Active ? CockpitStickMath.SmoothVisual(rightAngles,HotasHands.RightAngles(hotasTurn),dt):CockpitStickMath.ReturnVisual(rightAngles,dt);
            }
            SetVisuals();
            int barEvent=barDetent.Sample(barTilt.Active || YokeMode && yoke.Active,new Vector3(feedbackTranslation.X,0,feedbackTranslation.Z),now);
            if(barEvent!=0)
            {
                if(left.Held) Pulse(Player.HandL,barEvent,ref leftPulse);
                if(right.Held) Pulse(Player.HandR,barEvent,ref rightPulse);
                barPulse=now.AddMilliseconds(130);
            }
            bool sharedFeedback=barTilt.Active || YokeMode && yoke.Active;
            var leftFeedback=sharedFeedback ? new Vector3(steering.Position,0,0):feedbackTranslation;
            var rightFeedback=sharedFeedback ? new Vector3(steering.Position,throttleCommand,0):feedbackRotation;
            Feedback(Player.HandL,left.Held && !Adjusting,leftFeedback,leftDetent,ref leftPulse,sharedFeedback && now<barPulse);
            Feedback(Player.HandR,right.Held && !Adjusting,rightFeedback,rightDetent,ref rightPulse,sharedFeedback && now<barPulse);
            CockpitFeedback.Motion(Player.HandL,left.Held && !Adjusting,feedbackTranslation,DateTime.UtcNow<leftPulse || (DateTime.UtcNow-grabLeft).TotalSeconds<.08);
            CockpitFeedback.Motion(Player.HandR,right.Held && !Adjusting,feedbackRotation,DateTime.UtcNow<rightPulse || (DateTime.UtcNow-grabRight).TotalSeconds<.08);
            if (leftWas && !left.Held) Player.HandL.Vibrate(0,0.025f,75,0.2f);
            if (rightWas && !right.Held) Player.HandR.Vibrate(0,0.025f,75,0.2f);
            leftWas=left.Held; rightWas=right.Held;
            RefreshVisuals();
        }
        private static void Feedback(Controller hand,bool held,Vector3 axes,CockpitFeedback.StickPulse detent,ref DateTime next,bool suppressed=false)
        {
            var now=DateTime.UtcNow;
            int pulse=detent.Sample(held,axes,now);
            if(pulse==0 || suppressed) return;
            Pulse(hand,pulse,ref next);
        }
        private static void Pulse(Controller hand,int pulse,ref DateTime next)
        {
            bool center=pulse==1;
            hand.Vibrate(0,center ? .018f : .045f,center ? 150 : 75,center ? .32f : .38f);
            next=DateTime.UtcNow.AddMilliseconds(130);
        }
        public static void RefreshVisuals()
        {
            if (seat==null || !Eligible(seat)) return;
            CockpitRender.Update(seat,leftVisual,rightVisual,OnStick(Player.HandL),OnStick(Player.HandR),placement.Left,placement.Right);
        }
        public static void ApplyTurret(float speed,ref Vector2 aim,ref float zoom)
        {
            if(seat==null || IsSteering || !Eligible(seat) || !CockpitRender.Ready) return;
            CockpitStickMath.ApplyTurret(right.Consumed,left.Consumed,
                rotation,translation,speed,tuning.RotationCurve,ref aim,ref zoom,
                OwnsRightThumb ? -Controls.Static.ThrustRotate.Position.Y:0);
        }
        public static void ApplyFlight(float speed,ref Vector3 move,ref Vector2 rotate,ref float roll)
        {
            if (seat==null || !Eligible(seat) || !CockpitRender.Ready || InputRouter.Mode!=InputMode.Piloting && !(InputRouter.Mode==InputMode.Turret && TurretHeadAim.Active)) return;
            if (Adjusting) { move=Vector3.Zero; rotate=Vector2.Zero; roll=0; return; }
            if(IsSteering)
            {
                bool held=left.Held || right.Held;
                bool useBarTilt=barTilt.Active;
                var thumb=right.Held ? Controls.Static.ThrustRotate.Position:Vector2.Zero;
                if(YokeMode)
                {
                    float steer=-steering.Position*Rig.Wheel.Range/tuning.WheelRollTravel;
                    CockpitStickMath.ApplyYoke(tuning,left.Consumed || right.Consumed,held,right.Held,CockpitStickMath.Axis(steer,tuning.TiltDeadzone),yoke.Command,
                        thumb,speed,Common.Config.ShipRollSensitivity,ref move,ref rotate,ref roll);
                    return;
                }
                CockpitStickMath.ApplySteering(tuning,VehicleFlight,left.Consumed || right.Consumed,held,right.Held,useBarTilt,
                    translation.X,Throttle,barCommand,thumb,speed,Common.Config.ShipRollSensitivity,ref move,ref rotate,ref roll);
                return;
            }
            var c=Controls.Static;
            CockpitStickMath.ApplyFlight(left.Consumed,right.Consumed,left.Held ? translation : Vector3.Zero,right.Held ? rotation : Vector3.Zero,
                left.Held ? c.ThrustLRFB.Position.Y : 0,OwnsRightThumb ? c.ThrustRotate.Position.X : 0,speed,Common.Config.ShipRollSensitivity,ref move,ref rotate,ref roll,tuning.RotationCurve,tuning.TranslationCurve,left.Held ? c.ThrustLRFB.Position.X:0);
        }
        public static void Draw()
        {
            if (!Common.Config.DeveloperTools || seat==null || !CockpitRender.Ready || InputRouter.Mode!=InputMode.Piloting) return;
            if(Rig?.Left!=null) DrawContact(Vector3.Transform(Rig.Left.Contact,leftVisual),left.Held,leftNear);
            if(Rig?.Right!=null) DrawContact(Vector3.Transform(Rig.Right.Contact,rightVisual),right.Held,rightNear);
        }
        private static void DrawContact(Vector3 local,bool held,bool near)
        {
            Vector3D p=Vector3D.Transform(local,seat.WorldMatrix);
            var color=(held ? new Color(70,255,130) : near ? new Color(255,215,75) : new Color(80,160,200)).ToVector4();
            MySimpleObjectDraw.DrawLine(p-seat.WorldMatrix.Right*0.02,p+seat.WorldMatrix.Right*0.02,MyStringId.GetOrCompute("Square"),ref color,0.008f);
        }
    }
}
