using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    internal static class TrackedArms
    {
        private sealed class SavedBone
        {
            public MyCharacterBone Bone;
            public Quaternion Original,Applied;
            public Vector3 Translation,AppliedTranslation;
            public void Save() { Original=Bone.Rotation; Translation=Bone.Translation; }
            public void Restore(bool force)
            {
                // Do not overwrite a new pose that vanilla animation has already written.
                if(force || (Bone.Rotation==Applied && Bone.Translation==AppliedTranslation))
                { Bone.Rotation=Original; Bone.Translation=Translation; }
            }
        }
        private sealed class Arm
        {
            public SavedBone Upper,Lower,Palm;
            public SavedBone[] Fingers,Twists;
            public MyCharacterBone IndexTip,ThumbTip;
            public Matrix PalmOffset,PointFinger;
            public Vector3 Hint;
            public bool Applied;
            public void Restore(bool force=false)
            {
                if(!Applied) return;
                Upper.Restore(force); Lower.Restore(force); Palm.Restore(force);
                foreach(var twist in Twists) twist.Restore(force);
                if(Fingers!=null) foreach(var finger in Fingers) finger.Restore(force);
                Upper.Bone.ComputeAbsoluteTransform(true,true);
                Applied=false;
            }
        }
        private static MyCharacter owner;
        private sealed class ArmPair { public Arm Left, Right; }
        private static readonly Dictionary<MyCharacterBone[], ArmPair> buffers = new Dictionary<MyCharacterBone[], ArmPair>();
        private static Arm left,right;
        private static bool disabled,reportedPose;
        public static bool Applied => !disabled && left?.Applied==true && right?.Applied==true;
        public static void Reset()
        {
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
        private static Arm Find(MyCharacter character,string upperName,string lowerName,string palmName,float side)
        {
            if(string.IsNullOrEmpty(upperName) || string.IsNullOrEmpty(lowerName) || string.IsNullOrEmpty(palmName)) return null;
            var animation=character.AnimationController;
            var upper=animation.FindBone(upperName,out _);
            var lower=animation.FindBone(lowerName,out _);
            var palm=animation.FindBone(palmName,out _);
            if(upper==null || lower==null || palm==null || !Descends(lower,upper) || !Descends(palm,lower)) return null;
            var fingers=new List<SavedBone>();
            var twists=new List<SavedBone>();
            for(var bone=palm.Parent;bone!=lower;bone=bone.Parent) twists.Add(new SavedBone { Bone=bone });
            string prefix=side<0 ? "SE_RigL_" : "SE_RigR_";
            foreach(string digit in new[] { "Thumb","Index","Middle","Ring","Little" })
                for(int i=1;i<=3;i++)
                {
                    var bone=animation.FindBone(prefix+digit+"_"+i,out _);
                    if(bone!=null) fingers.Add(new SavedBone { Bone=bone });
                }
            var index=animation.FindBone(prefix+"Index_3",out _);
            return new Arm {
                PointFinger=index==null ? Matrix.Identity : CockpitHandPose.FingerPose(palm,index,false),
                Fingers=fingers.ToArray(),Twists=twists.ToArray(),
                IndexTip=animation.FindBone(prefix+"Index_3",out _),ThumbTip=animation.FindBone(prefix+"Thumb_3",out _),
                Upper=new SavedBone { Bone=upper },Lower=new SavedBone { Bone=lower },Palm=new SavedBone { Bone=palm },
                PalmOffset=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side),
                Hint=new Vector3(side*0.55f,-1,0.3f) };
        }
        private static bool Descends(MyCharacterBone child,MyCharacterBone parent)
        {
            for(var node=child.Parent;node!=null;node=node.Parent) if(node==parent) return true;
            return false;
        }
        public static void Update(MyCharacter character)
        {
            long started=FeatureTiming.Start();
            try { UpdateCore(character); }
            finally { FeatureTiming.End(FeatureTiming.Area.Arms,started); }
        }
        private static void UpdateCore(MyCharacter character)
        {
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
                        Left=Find(character,definition.LeftHandIKStartBone,definition.LeftForearmBone,definition.LeftHandIKEndBone,-1),
                        Right=Find(character,definition.RightHandIKStartBone,definition.RightForearmBone,definition.RightHandIKEndBone,1) };
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
            arm.Upper.Save(); arm.Lower.Save(); arm.Palm.Save();
            foreach(var twist in arm.Twists) twist.Save();
            foreach(var finger in arm.Fingers) finger.Save();
            arm.Applied=true;
            if(!ArmMath.ApplyPose(arm.Upper.Bone,arm.Lower.Bone,arm.Palm.Bone,target,arm.PalmOffset,arm.Hint,Common.Config.AdaptiveArms,hand==Player.HandL && !CockpitControls.Held(hand),BodyFit.ScaleFor(character)))
            { arm.Restore(true); return false; }
            if(character.CurrentWeapon==null || (Main.MenuOpen && hand==Player.HandR) || CockpitControls.Held(hand) || CockpitTouch.Attached(hand) || (hand==Player.HandL ? CockpitTouch.LeftPointing || HandInteraction.PointingFor(hand) : CockpitTouch.RightPointing || RemoteView.Pointing || SpatialUi.Pointing || TouchScreenBridge.Pointing || HandInteraction.PointingFor(hand) || BlockInspection.Current!=null))
                foreach(var finger in arm.Fingers)
                {
                    finger.Bone.Rotation=CockpitControls.Held(hand)
                        ? CockpitHandPose.StickRotation(finger.Bone.Name,(hand==Player.HandL ? Controls.Static.LeftTriggerPressure:Controls.Static.PointerPressure).RawPosition.X)
                        : CockpitHandPose.Rotation(finger.Bone.Name,CockpitTouch.Pinching(hand));
                    finger.Bone.ComputeAbsoluteTransform(true,true);
                }
            Diagnostics.ArmPoseCapture.Record(character,hand,arm.Upper.Bone,arm.Lower.Bone,arm.Palm.Bone);
            foreach(var saved in new[] { arm.Upper,arm.Lower,arm.Palm })
            { saved.Applied=saved.Bone.Rotation; saved.AppliedTranslation=saved.Bone.Translation; }
            foreach(var saved in arm.Fingers) { saved.Applied=saved.Bone.Rotation; saved.AppliedTranslation=saved.Bone.Translation; }
            foreach(var saved in arm.Twists) { saved.Applied=saved.Bone.Rotation; saved.AppliedTranslation=saved.Bone.Translation; }
            return true;
        }
        internal static MatrixD WristWorld(MyCharacter character,Controller hand)
        {
            Matrix tracking=CockpitHandPose.GripWrist(hand.GripTracking);
            MatrixD world=CockpitControls.HasTrackedSeat(character) ? CockpitControls.WristWorld(hand) : SpatialUi.DeviceWorld(tracking);
            world=Alignment.Apply(Alignment.HandKey(hand),world);
            var arm=hand==Player.HandL ? left : right;
            if(hand==Player.HandR && arm?.IndexTip!=null && FloatingKeyboard.TryAttachment(out var keyboardWrist,out var keyboardPoint,out float keyboardBlend))
            {
                var point=CockpitHandPose.Contact(arm.Palm.Bone,arm.IndexTip,arm.ThumbTip,false,CockpitHandPose.Tip);
                return CockpitHandPose.Blend(world,CockpitHandPose.Attach(keyboardWrist,arm.PalmOffset,point,keyboardPoint),keyboardBlend);
            }
            if(hand==Player.HandR && arm?.IndexTip!=null && RemoteView.TryAttachment(out var remoteWrist,out var remotePoint,out float remoteBlend))
            {
                var point=CockpitHandPose.Contact(arm.Palm.Bone,arm.IndexTip,arm.ThumbTip,false,CockpitHandPose.Tip);
                return CockpitHandPose.Blend(world,CockpitHandPose.Attach(remoteWrist,arm.PalmOffset,point,remotePoint),remoteBlend);
            }
            if(hand==Player.HandR && arm?.IndexTip!=null && SpatialUi.TryWristAttachment(out var wristPose,out var wristPoint,out float wristBlend))
            {
                var point=CockpitHandPose.Contact(arm.Palm.Bone,arm.IndexTip,arm.ThumbTip,false,CockpitHandPose.Tip);
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
