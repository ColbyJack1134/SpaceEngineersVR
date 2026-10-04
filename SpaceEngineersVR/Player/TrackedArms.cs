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
        private sealed class ArmPair { public Arm Left, Right; }
        private static readonly Dictionary<MyCharacterBone[], ArmPair> buffers = new Dictionary<MyCharacterBone[], ArmPair>();
        private static Arm left,right;
        private static bool disabled,reportedPose;
        public static bool Applied => !disabled && left?.Applied==true && right?.Applied==true;
        public static void Reset()
        {
            latestPose=null;
            if (owner != null) Restore(owner);
            if(owner!=null && !owner.Closed) owner.AnimationController.UpdateTransformations();
            BodyFit.Reset();
            owner=null; buffers.Clear(); left=right=null; disabled=reportedPose=false;
        }
        public static void Restore(MyCharacter character)
        {
            if(owner!=character) return;
            // Animation workers own the inactive buffer; restore only the displayed bones.
            var bones=character.AnimationController.CharacterBones;
            if (bones!=null && buffers.TryGetValue(bones, out var pair))
            { pair.Left?.Restore(); pair.Right?.Restore(); }
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
                    buffers.Add(skeleton, pair);
                    Logger.Info("Tracked arm buffer initialized: left="+(pair.Left!=null)+"; right="+(pair.Right!=null));
                }
                left=pair.Left; right=pair.Right;
                Restore(character);
                bool seated=SeatFit.Eligible(SeatFit.Seat) && SeatFit.Seat.Pilot==character;
                if(disabled || !Main.VrActive || ThirdPersonView.Character ||
                    character!=MySession.Static?.LocalCharacter || (!seated && ((MySession.Static.ControlledEntity!=character && !RemoteView.CharacterAnchor) ||
                    character.IsSitting || !CameraRig.Owns(character))) || character.IsOnLadder || character.IsDead || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu && InputRouter.Mode!=InputMode.Radial) ||
                    !Player.Headset.pose.isTracked)
                { character.AnimationController.UpdateTransformations(); return; }
                BodyFit.Apply(character);
                bool applied=Common.Config.TrackedArms && (Apply(left,Player.HandL,character) | Apply(right,Player.HandR,character));
                character.AnimationController.UpdateTransformations();
                latestPose=CreatePose(); poseTime=Multiplayer.MultiplayerRuntime.Now;
                if(applied && !reportedPose) { reportedPose=true; Logger.Info("TRACKED ARMS applied after physics/vanilla hand animation (local cosmetic pose)"); }
            }
            catch(Exception ex)
            {
                left?.Restore(true); right?.Restore(true); BodyFit.Restore(character); disabled=true;
                Logger.Warning(ex,"Tracked arms disabled for this character; vanilla arms retained");
            }
        }
        private static bool Apply(Arm arm,Controller hand,MyCharacter character)
        {
            if(arm==null || !hand.pose.isTracked) return false;
            MatrixD world=WristWorld(character,hand);
            Matrix target=(Matrix)(world*character.PositionComp.WorldMatrixNormalizedInv);
            if(!target.IsValid()) return false;
            bool posed=ArmSkeleton.Apply(arm,target,Common.Config.AdaptiveArms,hand==Player.HandL && !CockpitControls.Held(hand),
                BodyFit.ScaleFor(character),FingerMode(character,hand),Trigger(hand));
            if(posed) Diagnostics.ArmPoseCapture.Record(character,hand,arm.Upper.Bone,arm.Lower.Bone,arm.Palm.Bone);
            return posed;
        }
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
                pose.LeftFingers=FingerMode(character,Player.HandL); pose.RightFingers=FingerMode(character,Player.HandR);
                pose.LeftTrigger=Trigger(Player.HandL); pose.RightTrigger=Trigger(Player.HandR);
                // Seated heads need the first-person seat frame; observer views would send a stale camera pose.
                bool headFrame=character.IsSitting ? SeatFit.Eligible(character.Parent as Sandbox.Game.Entities.MyCockpit) : !ThirdPersonView.Active && CameraRig.Owns(character);
                if(Player.Headset.pose.isTracked && headFrame)
                { pose.Tracked|=Multiplayer.PlayerPose.HeadTracked; pose.Head=(Matrix)(SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix)*character.PositionComp.WorldMatrixNormalizedInv); }
            }
            return pose;
        }
        internal static float Trigger(Controller hand) => (hand==Player.HandL ? Controls.Static.LeftTriggerPressure:Controls.Static.PointerPressure).RawPosition.X;
        internal static ArmSkeleton.Fingers FingerMode(MyCharacter character,Controller hand)
        {
            if(CockpitControls.Held(hand)) return ArmSkeleton.Fingers.Stick;
            if(character.CurrentWeapon==null || Main.MenuOpen || CockpitTouch.Attached(hand) ||
                TouchScreenBridge.PointingFor(hand) || RemoteView.PointingFor(hand) || HandInteraction.PointingFor(hand) ||
                (hand==Player.HandL ? CockpitTouch.LeftPointing : CockpitTouch.RightPointing || SpatialUi.Pointing || BlockInspection.Current!=null))
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
            if(arm?.IndexTip!=null && RemoteView.TryAttachment(hand,out var remoteWrist,out var remotePoint,out float remoteBlend))
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
