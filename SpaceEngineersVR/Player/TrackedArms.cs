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
            public Matrix PalmOffset;
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
            owner=null; buffers.Clear(); left=right=null; disabled=reportedPose=false;
        }
        public static void Restore(MyCharacter character)
        {
            if(owner!=character) return;
            // Animation workers own the inactive buffer; restore only the displayed bones.
            var bones=character.AnimationController.CharacterBones;
            if (bones!=null && buffers.TryGetValue(bones, out var pair))
            { pair.Left?.Restore(); pair.Right?.Restore(); }
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
            return new Arm {
                Fingers=fingers.ToArray(),Twists=twists.ToArray(),
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
                if(disabled || !Common.Config.TrackedArms || !Main.VrActive ||
                    character!=MySession.Static?.LocalCharacter || (!seated && (MySession.Static.ControlledEntity!=character ||
                    character.IsSitting || !CameraRig.Owns(character))) || character.IsOnLadder || character.IsDead || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu && InputRouter.Mode!=InputMode.Radial) ||
                    !Player.Headset.pose.isTracked)
                { character.AnimationController.UpdateTransformations(); return; }
                bool applied=Apply(left,Player.HandL,character) | Apply(right,Player.HandR,character);
                character.AnimationController.UpdateTransformations();
                if(applied && !reportedPose) { reportedPose=true; Logger.Info("TRACKED ARMS applied after physics/vanilla hand animation (local cosmetic pose)"); }
            }
            catch(Exception ex)
            {
                left?.Restore(true); right?.Restore(true); disabled=true;
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
            if(!ArmMath.ApplyPose(arm.Upper.Bone,arm.Lower.Bone,arm.Palm.Bone,target,arm.PalmOffset,arm.Hint,Common.Config.AdaptiveArms))
            { arm.Restore(true); return false; }
            if(CockpitControls.Held(hand) || (hand==Player.HandL ? CockpitTouch.LeftPointing : CockpitTouch.RightPointing || SpatialUi.Pointing || TouchScreenBridge.Pointing))
                foreach(var finger in arm.Fingers)
                {
                    bool thumb=finger.Bone.Name.Contains("Thumb");
                    // Both hands' phalanges extend along local -X; +Z curls toward the palm (-Y).
                    float curl=thumb ? .35f : .85f;
                    if(!CockpitControls.Held(hand) && finger.Bone.Name.Contains("Index")) curl=0;
                    finger.Bone.Rotation=Quaternion.CreateFromAxisAngle(Vector3.Backward,curl);
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
            Matrix tracking=Matrix.CreateTranslation(0,.02f,.04f)*hand.GripTracking;
            MatrixD world=CockpitControls.HasTrackedSeat(character) ? CockpitControls.WristWorld(hand) : SpatialUi.DeviceWorld(tracking);
            return Alignment.Apply(Alignment.HandKey(hand),world);
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
        internal static bool TryWristScreen(out MatrixD world)
        {
            world=MatrixD.Identity;
            var character=MySession.Static?.LocalCharacter;
            if(character!=owner || disabled || left==null || !left.Applied || !Player.HandL.pose.isTracked) return false;
            var bone=character.AnimationController.FindBone("SE_RigLForearm2",out _);
            if(bone==null) return false;
            // Measured default astronaut display, in its skinned forearm's bind space.
            Matrix local=Matrix.Identity;
            local.Right=new Vector3(.005888f,.999087f,.042033f);
            local.Up=new Vector3(-.995760f,.009721f,-.091583f);
            local.Backward=Vector3.Normalize(Vector3.Cross(local.Right,local.Up));
            local.Translation=new Vector3(-.107609f,-.013799f,.074502f)+local.Backward*.004f;
            world=Alignment.Apply(Alignment.WristKey,(MatrixD)(local*bone.AbsoluteTransform)*character.WorldMatrix);
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
            point=Vector3D.Transform(new Vector3D(-.025,0,0),(MatrixD)bone.AbsoluteTransform*owner.WorldMatrix);
            return point.IsValid();
        }
    }
}
