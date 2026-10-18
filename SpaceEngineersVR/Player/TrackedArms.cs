using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender.Animations;
using Arm = SpaceEngineersVR.Player.ArmSkeleton.Arm;

namespace SpaceEngineersVR.Player
{
    internal static class TrackedArms
    {
        private static MyCharacter owner;
        private static Multiplayer.PlayerPose latestPose;
        private static double poseTime;
        private sealed class ArmPair { public Arm Left, Right; public ArmSkeleton.SavedBone Head,Eye; public SpineLean.Chain Spine; }
        private static readonly Dictionary<MyCharacterBone[], ArmPair> buffers = new Dictionary<MyCharacterBone[], ArmPair>();
        private static Arm left,right;
        private static bool disabled,reportedPose;
        public static bool Applied => !disabled && left?.Applied==true && right?.Applied==true;
        public static void Reset()
        {
            latestPose=null;
            if (owner != null) Restore(owner);
            if(owner!=null && !owner.Closed) owner.AnimationController.UpdateTransformations();
            BodyFit.Reset(); BodyPlacement.Reset();
            owner=null; buffers.Clear(); idleCollars.Clear(); left=right=null; disabled=reportedPose=false;
        }
        public static void Restore(MyCharacter character)
        {
            if(character==null || owner!=character) return;
            // Animation workers own the inactive buffer; restore only the displayed bones.
            var bones=character.AnimationController.CharacterBones;
            if (bones!=null && buffers.TryGetValue(bones, out var pair))
            {
                SpineLean.Restore(pair.Spine);
                pair.Left?.Restore(); pair.Right?.Restore();
                pair.Eye?.Restore(false);
                if(pair.Head!=null && pair.Head.Restore(false)) pair.Head.Bone.ComputeAbsoluteTransform(true,true);
            }
            BodyPlacement.Restore(character);
            BodyFit.Restore(character);
        }
        public static void Update(MyCharacter character)
        {
            long started=FeatureTiming.Start();
            try { UpdateCore(character); }
            finally { FeatureTiming.End(FeatureTiming.Area.Arms,started); }
        }
        private static void UpdateCore(MyCharacter character)
        {
            latestPose=null;
            try
            {
                if(owner!=character) { Reset(); owner=character; }
                var skeleton = character.AnimationController.CharacterBones;
                if (skeleton==null) return;
                if (!buffers.TryGetValue(skeleton, out var pair))
                {
                    if (buffers.Count >= 2) buffers.Clear();
                    var definition=character.Definition;
                    pair = new ArmPair {
                        Left=ArmSkeleton.Find(character,definition.LeftHandIKStartBone,definition.LeftForearmBone,definition.LeftHandIKEndBone,-1),
                        Right=ArmSkeleton.Find(character,definition.RightHandIKStartBone,definition.RightForearmBone,definition.RightHandIKEndBone,1) };
                    var head=ArmSkeleton.Head(name=>character.AnimationController.FindBone(name,out _),definition.HeadBone);
                    if(head!=null) pair.Head=new ArmSkeleton.SavedBone {Bone=head};
                    pair.Eye=ArmSkeleton.Below(head,character.AnimationController.FindBone(definition.HeadBone,out _));
                    pair.Spine=SpineLean.Find(name=>character.AnimationController.FindBone(name,out _),head,pair.Eye);
                    buffers.Add(skeleton, pair);
                    Logger.Info("Tracked arm buffer initialized: left="+(pair.Left!=null)+"; right="+(pair.Right!=null));
                }
                left=pair.Left; right=pair.Right;
                Restore(character);
                bool seated=SeatFit.Eligible(SeatFit.Seat) && SeatFit.Seat.Pilot==character;
                if(disabled || !Main.VrActive || ThirdPersonView.Character || SpectatorView.Active ||
                    character!=MySession.Static?.LocalCharacter || (!seated && ((MySession.Static.ControlledEntity!=character && !RemoteView.CharacterAnchor) ||
                    character.IsSitting || !CameraRig.Owns(character))) || character.IsDead || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu && InputRouter.Mode!=InputMode.Radial) ||
                    !Player.Headset.pose.isTracked)
                { character.AnimationController.UpdateTransformations(); return; }
                BodyFit.Apply(character);
                if(seated && TryLean(character,out var lean,out float twist)) SpineLean.Apply(pair.Spine,lean,twist);
                if(Common.Config.TrackedArms && Handedness.Swapped && character.CurrentWeapon!=null && !character.IsSitting && left!=null && right!=null)
                    ArmSkeleton.MirrorCollars(left,right);
                if(Common.Config.TrackedArms) { IdleCollar(character,left,Player.HandL); IdleCollar(character,right,Player.HandR); }
                // The native ladder pose places the body against the ladder; seats place it through the seat fit.
                if(!seated && !character.IsOnLadder && pair.Head!=null && TryHead(character,out var headset))
                    BodyPlacement.Apply(character,pair.Spine,pair.Head.Bone,pair.Eye?.Bone,left,right,headset);
                bool applied=Common.Config.TrackedArms && (Apply(left,Player.HandL,character) | Apply(right,Player.HandR,character));
                // First person hides the head; a camera feed shows it, so turn it as other players see it.
                if(RemoteView.Current!=null && pair.Eye!=null && TryHead(character,out var look)) ArmSkeleton.Look(pair.Head,pair.Eye,look,ArmSkeleton.HeadLimit);
                character.AnimationController.UpdateTransformations();
                Multiplayer.ReloadAnimation.Apply(character);
                latestPose=CreatePose(); poseTime=Multiplayer.MultiplayerRuntime.Now;
                if(applied && !reportedPose) { reportedPose=true; Logger.Info("TRACKED ARMS applied after physics/vanilla hand animation (local cosmetic pose)"); }
            }
            catch(Exception ex)
            {
                left?.Restore(true); right?.Restore(true); BodyPlacement.Restore(character); BodyFit.Restore(character); disabled=true;
                var bones=character.AnimationController?.CharacterBones;
                if(bones!=null && buffers.TryGetValue(bones,out var failed)) SpineLean.Restore(failed.Spine,true);
                Logger.Warning(ex,"Tracked arms disabled for this character; vanilla arms retained");
            }
        }
        private static bool Apply(Arm arm,Controller hand,MyCharacter character)
        {
            if(arm==null || !hand.pose.isTracked) return false;
            MatrixD world=WristWorld(character,hand);
            Matrix target=(Matrix)(world*character.PositionComp.WorldMatrixNormalizedInv);
            if(!target.IsValid()) return false;
            float screen=hand==Player.HandL ? ScreenWeight(arm,target,character):0;
            bool posed=ArmSkeleton.Apply(arm,target,new ArmSolve.Options(Common.Config.AdaptiveArms,Common.Config.AdaptiveArms,screen),
                BodyFit.ScaleFor(character),FingerMode(character,hand),Trigger(hand),FreeFingers(character,hand) ? hand.Fingers.Curls:null, WeaponHandling.Profile, hand==Handedness.SupportHand,CockpitControls.HeldCurl(hand));
            if(posed) Diagnostics.ArmPoseCapture.Record(character,hand,arm.Upper.Bone,arm.Lower.Bone,arm.Palm.Bone);
            return posed;
        }
        private static readonly Dictionary<Arm,(Quaternion rotation,Vector3 translation)> idleCollars=new Dictionary<Arm,(Quaternion,Vector3)>();
        // The weapon animations pose both collarbones for a two-handed hold; a hand that is not on the item keeps its no-tool collar.
        private static void IdleCollar(MyCharacter character,Arm arm,Controller hand)
        {
            if(arm?.Collar==null) return;
            var collar=arm.Collar;
            if(character.CurrentWeapon==null) { idleCollars[arm]=(collar.Bone.Rotation,collar.Bone.Translation); return; }
            if(WeaponHandling.Profile==null || WeaponHandling.Holding(hand) || !idleCollars.TryGetValue(arm,out var idle)) return;
            collar.SaveOnce();
            collar.Bone.Rotation=idle.rotation; collar.Bone.Translation=idle.translation;
            collar.Bone.ComputeAbsoluteTransform(true,true);
            ArmSkeleton.Remember(collar);
        }
        private static float screenWeight=1;
        private static long screenTime;
        // Left wrist screen weight: 1 while the screen can be used, 0 while the left hand holds something.
        private static float ScreenWeight(Arm arm,Matrix target,MyCharacter character)
        {
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            float seconds=screenTime==0 ? 1 : (float)Math.Min(.1,(now-screenTime)/(double)System.Diagnostics.Stopwatch.Frequency);
            screenTime=now;
            if(CockpitControls.Held(Player.HandL) || WeaponHandling.Holding(Player.HandL) || LadderClimb.Holding(Player.HandL)) return screenWeight=0;
            float goal=SpatialUi.WristInUse ? 1 : ScreenAttention(arm,target,character);
            screenWeight=goal>screenWeight ? Math.Min(goal,screenWeight+seconds/.15f) : Math.Max(goal,screenWeight-seconds/.5f);
            return screenWeight;
        }
        internal static float ScreenAttention(Arm arm,Matrix target,MyCharacter character)
        {
            var cuff=character.AnimationController.FindBone("SE_RigLForearm2",out _);
            if(cuff==null || !Player.Headset.pose.isTracked) return 1;
            Matrix palm=arm.PalmOffset*target.GetOrientation(); palm.Translation=target.Translation;
            MatrixD screen=(MatrixD)(WristScreenLocal*cuff.GetAbsoluteRigTransform()*Matrix.Invert(arm.Palm.Bone.GetAbsoluteRigTransform())*palm)*character.WorldMatrix;
            return ScreenAttention(screen,SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix));
        }
        // Screen face (Backward) toward the eye within 55 degrees, and the screen within 30 degrees of view; released by 80 and 45.
        internal static float ScreenAttention(MatrixD screen,MatrixD head)
        {
            var toHead=head.Translation-screen.Translation;
            if(toHead.LengthSquared()<1e-6) return 1;
            toHead.Normalize();
            float facing=(float)Vector3D.Dot(screen.Backward,toHead),gaze=(float)Vector3D.Dot(head.Forward,-toHead);
            return Ramp(facing,.174f,.574f)*Ramp(gaze,.707f,.866f);
        }
        private static float Ramp(float value,float from,float to) => MathHelper.SmoothStep(0,1,MathHelper.Clamp((value-from)/(to-from),0,1));
        internal static Multiplayer.PlayerPose CapturePose(uint sequence)
        {
            if(latestPose==null || Multiplayer.MultiplayerRuntime.Now-poseTime>.25) return null;
            latestPose.Sequence=sequence; return latestPose;
        }
        private static Multiplayer.PlayerPose CreatePose()
        {
            var character=MySession.Static?.LocalCharacter;
            if(character==null || character!=owner || character.Closed || character.IsDead) return null;
            var pose=new Multiplayer.PlayerPose {Character=character.EntityId,Seat=character.Parent?.EntityId ?? 0,
                Left=Matrix.Identity,Right=Matrix.Identity};
            if(!disabled && Main.VrActive && !ThirdPersonView.Character)
            {
                if(left?.Applied==true && Player.HandL.pose.isTracked)
                { pose.Tracked|=1; pose.Left=(Matrix)(WristWorld(character,Player.HandL)*character.PositionComp.WorldMatrixNormalizedInv); }
                if(right?.Applied==true && Player.HandR.pose.isTracked)
                { pose.Tracked|=2; pose.Right=(Matrix)(WristWorld(character,Player.HandR)*character.PositionComp.WorldMatrixNormalizedInv); }
                if(WeaponHandling.ToolEquipped && TryFreePointPose(Handedness.ActionHand,out var ray))
                { pose.Tracked|=Multiplayer.PlayerPose.ToolRayTracked; pose.ToolRay=(Matrix)(ray*character.PositionComp.WorldMatrixNormalizedInv); }
                if(WeaponHandling.Supported) pose.Tracked|=Multiplayer.PlayerPose.ItemSupported;
                if(Handedness.Swapped && character.CurrentWeapon!=null && !character.IsSitting) pose.Tracked|=Multiplayer.PlayerPose.ItemMirrored;
                pose.LeftFingers=FingerMode(character,Player.HandL); pose.RightFingers=FingerMode(character,Player.HandR);
                pose.LeftTrigger=Trigger(Player.HandL); pose.RightTrigger=Trigger(Player.HandR);
                if((pose.Tracked&1)!=0 && FreeFingers(character,Player.HandL)) { pose.Tracked|=Multiplayer.PlayerPose.LeftCurls; Array.Copy(Player.HandL.Fingers.Curls,pose.LeftCurl,5); }
                if((pose.Tracked&2)!=0 && FreeFingers(character,Player.HandR)) { pose.Tracked|=Multiplayer.PlayerPose.RightCurls; Array.Copy(Player.HandR.Fingers.Curls,pose.RightCurl,5); }
                if(TryHead(character,out var head)) { pose.Tracked|=Multiplayer.PlayerPose.HeadTracked; pose.Head=head; }
            }
            return pose;
        }
        // Headset offset from the seat's camera anchor, the frame ViewGuard and the eye use; the main camera lags a tick on a moving ship.
        private static bool TryLean(MyCharacter character,out Vector3 delta,out float twist)
        {
            delta=Vector3.Zero; twist=0;
            var seat=SeatFit.Seat;
            if(seat==null) return false;
            MatrixD anchor=seat.GetHeadMatrix(true,true);
            MatrixD head=(MatrixD)VrMath.Affine(Player.Headset.pose.deviceToAbsolute.matrix*Player.PlayerToAbsolute.inverted)*anchor;
            MatrixD inverse=character.PositionComp.WorldMatrixNormalizedInv;
            delta=(Vector3)Vector3D.TransformNormal(head.Translation-anchor.Translation,inverse);
            twist=SpineLean.Twist((Vector3)Vector3D.TransformNormal(head.Forward,inverse));
            return delta.IsValid();
        }
        private static bool TryHead(MyCharacter character,out Matrix head)
        {
            head=Matrix.Identity;
            // Seated heads need the first-person seat frame; observer views would send a stale camera pose.
            bool frame=character.IsSitting ? SeatFit.Eligible(character.Parent as Sandbox.Game.Entities.MyCockpit) : !ThirdPersonView.Active && CameraRig.Owns(character);
            if(!Player.Headset.pose.isTracked || !frame) return false;
            head=(Matrix)(SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix)*character.PositionComp.WorldMatrixNormalizedInv);
            return true;
        }
        internal static float Trigger(Controller hand) => CockpitTouch.HoldingBar(hand) || LadderClimb.Holding(hand) ? 1 : (hand==Player.HandL ? Controls.Static.LeftTriggerPressure:Controls.Static.PointerPressure).RawPosition.X;
        private static bool RequiresPointing(Controller hand) => GridSelection.Owns(hand) || Main.MenuOpen || MenuKeyboard.IsOpen || CockpitTouch.Attached(hand) ||
            TouchScreenBridge.PoseFor(hand) || ArthurLcdBridge.PoseFor(hand) || FloatingWindows.PointingFor(hand) || HandInteraction.PointingFor(hand) ||
            (hand==Player.HandL ? CockpitTouch.LeftPointing:CockpitTouch.RightPointing || SpatialUi.Pointing) || hand==Handedness.ActionHand && BlockInspection.Current!=null;
        private static bool FreeFingers(MyCharacter character,Controller hand) => !ThirdPersonView.Active && Empty(character,hand) &&
            !CockpitControls.Held(hand) && !RequiresPointing(hand);
        // A hand that is not on the tracked item behaves as an empty hand.
        private static bool Empty(MyCharacter character,Controller hand) => character.CurrentWeapon==null || WeaponHandling.Profile!=null && !WeaponHandling.Holding(hand);
        internal static ArmSkeleton.Fingers FingerMode(MyCharacter character,Controller hand)
        {
            if(hand==Handedness.ActionHand && WeaponHandling.Profile?.Tool==true && !Main.MenuOpen)
                return WeaponHandling.Supported || WeaponHandling.ToolContactActive ? ArmSkeleton.Fingers.Stick:ArmSkeleton.Fingers.Point;
            if(hand==Handedness.SupportHand && WeaponHandling.Reloading && !WeaponHandling.Supported) return ArmSkeleton.Fingers.Native;
            if(WeaponHandling.Profile!=null && !RequiresPointing(hand) && (hand==Handedness.ActionHand || WeaponHandling.Supported)) return ArmSkeleton.Fingers.Stick;
            if(CockpitControls.Held(hand) || CockpitTouch.HoldingBar(hand) || LadderClimb.Holding(hand)) return ArmSkeleton.Fingers.Stick;
            if(Empty(character,hand) || RequiresPointing(hand))
                return CockpitTouch.Pinching(hand) || hand==Player.HandR && SpatialUi.PinchingKnob ? ArmSkeleton.Fingers.Pinch:ArmSkeleton.Fingers.Point;
            return ArmSkeleton.Fingers.Native;
        }
        internal static MatrixD WristWorld(MyCharacter character,Controller hand)
        {
            Matrix tracking=CockpitHandPose.GripWrist(hand.GripTracking);
            MatrixD world=CockpitControls.HasTrackedSeat(character) ? CockpitControls.WristWorld(hand) : SpatialUi.DeviceWorld(tracking);
            world=Alignment.Apply(Alignment.HandKey(hand),world);
            var arm=hand==Player.HandL ? left : right;
            if(arm?.IndexTip!=null && FloatingKeyboard.TryAttachment(hand,out var keyboardWrist,out var keyboardPoint,out float keyboardBlend))
            {
                var point=CockpitHandPose.Contact(arm.Palm.Bone,arm.IndexTip,arm.ThumbTip,false,CockpitHandPose.Tip);
                return CockpitHandPose.Blend(world,CockpitHandPose.Attach(keyboardWrist,arm.PalmOffset,point,keyboardPoint),keyboardBlend);
            }
            if(arm?.IndexTip!=null && FloatingWindows.TryAttachment(hand,out var remoteWrist,out var remotePoint,out float remoteBlend))
            {
                var point=CockpitHandPose.Contact(arm.Palm.Bone,arm.IndexTip,arm.ThumbTip,false,CockpitHandPose.Tip);
                return CockpitHandPose.Blend(world,CockpitHandPose.Attach(remoteWrist,arm.PalmOffset,point,remotePoint),remoteBlend);
            }
            if(hand==Player.HandR && arm?.IndexTip!=null && SpatialUi.TryWristAttachment(out var wristPose,out var wristPoint,out float wristBlend))
            {
                bool pinch=SpatialUi.PinchingKnob;
                var point=CockpitHandPose.Contact(arm.Palm.Bone,arm.IndexTip,arm.ThumbTip,pinch,pinch ? -.025f:CockpitHandPose.Tip);
                return CockpitHandPose.Blend(world,CockpitHandPose.Attach(wristPose,arm.PalmOffset,point,wristPoint),wristBlend);
            }
            if(arm!=null && LadderClimb.TryPalm(hand,out var rungPalm,out float rungBlend))
                return CockpitHandPose.Blend(world,(MatrixD)Matrix.Invert(arm.PalmOffset)*rungPalm,rungBlend);
            if(!character.IsSitting && character.CurrentWeapon==null && arm?.IndexTip!=null)
            {
                if(HandInteraction.TryAttachment(hand,out var pressed,out var point,out float amount))
                {
                    pressed=CockpitHandPose.Attach(pressed,arm.PalmOffset,Vector3.Transform(new Vector3(CockpitHandPose.Tip,0,0),arm.PointFinger),point);
                    var rotation=Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(world),Quaternion.CreateFromRotationMatrix(pressed),amount);
                    var result=MatrixD.CreateFromQuaternion(rotation);
                    result.Translation=Vector3D.Lerp(world.Translation,pressed.Translation,amount);
                    return result;
                }
                return world;
            }
            if(arm!=null && CockpitTouch.TryBarPalm(hand,out var barPalm,out float barBlend))
                return CockpitHandPose.Blend(world,(MatrixD)Matrix.Invert(arm.PalmOffset)*barPalm,barBlend);
            if(arm?.IndexTip!=null && CockpitTouch.TryAttachment(hand,out var attached,out var contact,out float blend))
            {
                bool pinch=CockpitTouch.Pinching(hand);
                var point=CockpitHandPose.Contact(arm.Palm.Bone,arm.IndexTip,arm.ThumbTip,pinch,pinch ? -.025f : CockpitHandPose.Tip);
                attached=CockpitHandPose.Attach(attached,arm.PalmOffset,point,contact);
                var rotation=Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(world),Quaternion.CreateFromRotationMatrix(attached),blend);
                var result=MatrixD.CreateFromQuaternion(rotation);
                result.Translation=Vector3D.Lerp(world.Translation,attached.Translation,blend);
                return result;
            }
            if(arm!=null && WeaponHandling.TryPalm(character,hand,out var itemPalm)) return (MatrixD)Matrix.Invert(arm.PalmOffset)*itemPalm;
            return world;
        }
        internal static MatrixD FreeWristWorld(Controller hand)
        {
            return Alignment.Apply(Alignment.HandKey(hand),SpatialUi.DeviceWorld(CockpitHandPose.GripWrist(hand.GripTracking)));
        }
        internal static bool TryFreeFingertip(Controller hand,out Vector3D point)
            => TryPointContact(hand,false,out point);
        internal static bool TryPointPose(Controller hand,out MatrixD pose)
            => TryPointPose(hand,true,out pose);
        internal static bool TryFreePointPose(Controller hand,out MatrixD pose)
            => TryPointPose(hand,false,out pose);
        private static bool TryPointPose(Controller hand,bool attached,out MatrixD pose)
        {
            pose=MatrixD.Identity;
            var arm=hand==Player.HandL ? left : right;
            if(disabled || arm?.IndexTip==null || owner!=MySession.Static?.LocalCharacter || !hand.pose.isTracked) return false;
            pose=CockpitHandPose.PointPose(attached ? WristWorld(owner,hand) : FreeWristWorld(hand),arm.PalmOffset,arm.PointFinger);
            return pose.IsValid();
        }
        internal static bool TryPointContact(Controller hand,bool attached,out Vector3D point)
        {
            point=Vector3D.Zero;
            var arm=hand==Player.HandL ? left : right;
            if(disabled || arm?.IndexTip==null || owner!=MySession.Static?.LocalCharacter || !hand.pose.isTracked) return false;
            point=CockpitHandPose.PointContact(attached ? WristWorld(owner,hand) : FreeWristWorld(hand),arm.PalmOffset,arm.PointFinger);
            return point.IsValid();
        }
        internal static Matrix WristForPalm(Controller hand,Matrix palm)
        {
            var arm=hand==Player.HandL ? left : right;
            return arm==null ? palm : Matrix.Invert(arm.PalmOffset)*palm;
        }
        internal static readonly Vector3 BarPalmCenter=new Vector3(-.065f,-.015f,0);
        internal static readonly Vector3 BarFingerCavity=new Vector3(-.105f,-.035f,0);
        internal static bool TryFreeBarGrip(Controller hand,out CockpitProbe region)
        {
            var arm=hand==Player.HandL ? left:right;
            region=default;
            if(disabled || owner!=MySession.Static?.LocalCharacter || arm==null || !hand.pose.isTracked) return false;
            var palm=(MatrixD)arm.PalmOffset*FreeWristWorld(hand);
            region.Start=Vector3D.Transform(BarPalmCenter,palm);
            region.End=Vector3D.Transform(BarFingerCavity,palm);
            return region.Start.IsValid() && region.End.IsValid();
        }
        internal static bool TryPalmMirror(MyCharacter character,out Matrix mirror)
        {
            mirror=Matrix.Identity;
            if(owner!=character || left==null || right==null) return false;
            mirror=ArmSkeleton.PalmMirror(right,left); return true;
        }
        internal static bool TryFreePalm(MyCharacter character,Controller hand,out MatrixD world)
        {
            world=MatrixD.Identity;
            var arm=hand==Player.HandL ? left:right;
            if(owner!=character || arm==null || !hand.pose.isTracked) return false;
            world=(MatrixD)arm.PalmOffset*FreeWristWorld(hand); return world.IsValid();
        }
        internal static bool TryDesiredPalm(MyCharacter character,Controller hand,out MatrixD world)
        {
            world=MatrixD.Identity;
            var arm=hand==Player.HandL ? left : right;
            if(owner!=character || arm==null || !hand.pose.isTracked) return false;
            world=(MatrixD)arm.PalmOffset*WristWorld(character,hand);
            return world.IsValid();
        }
        internal static Matrix WristScreenLocal
        {
            get
            {
                Matrix local=Matrix.Identity;
                local.Right=new Vector3(.005888f,.999087f,.042033f);
                local.Up=new Vector3(-.995760f,.009721f,-.091583f);
                local.Backward=Vector3.Normalize(Vector3.Cross(local.Right,local.Up));
                local.Translation=new Vector3(-.107609f,-.013799f,.074502f)+local.Backward*.004f;
                return local;
            }
        }
        internal static bool TryWristScreen(out MatrixD world)
        {
            world=MatrixD.Identity;
            var character=MySession.Static?.LocalCharacter;
            if(character!=owner || disabled || left==null || !left.Applied || !Player.HandL.pose.isTracked) return false;
            var bone=character.AnimationController.FindBone("SE_RigLForearm2",out _);
            if(bone==null) return false;
            world=Alignment.Apply(Alignment.WristKey,(MatrixD)(WristScreenLocal*bone.AbsoluteTransform)*character.WorldMatrix);
            return world.IsValid();
        }
        internal static bool TryFingertip(out Vector3D point)
            => TryFingertip(Player.HandR,out point);
        internal static bool TryFingertip(Controller hand,out Vector3D point)
        {
            point=Vector3D.Zero;
            if(!Applied || owner!=MySession.Static?.LocalCharacter || !hand.pose.isTracked) return false;
            var bone=owner.AnimationController.FindBone(hand==Player.HandL ? "SE_RigL_Index_3" : "SE_RigR_Index_3",out _);
            if(bone==null) return false;
            point=Vector3D.Transform(new Vector3D(CockpitHandPose.Tip,0,0),(MatrixD)bone.AbsoluteTransform*owner.WorldMatrix);
            return point.IsValid();
        }
    }
}
