using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Multiplayer
{
    internal sealed class RemoteArms
    {
        private sealed class Pair { internal ArmSkeleton.Arm Left,Right; internal ArmSkeleton.SavedBone Head; }
        private readonly Dictionary<MyCharacterBone[],Pair> buffers=new Dictionary<MyCharacterBone[],Pair>();
        internal readonly PoseStream Stream=new PoseStream();
        internal MyCharacter Character;
        private bool failed;
        internal void Restore()
        {
            var bones=Character?.AnimationController?.CharacterBones;
            if(bones!=null && buffers.TryGetValue(bones,out var pair))
            {
                pair.Left?.Restore(); pair.Right?.Restore();
                if(pair.Head!=null) { pair.Head.Restore(false); pair.Head.Bone.ComputeAbsoluteTransform(true,true); }
            }
        }
        internal void Clear()
        {
            Restore();
            if(Character!=null && !Character.Closed) Character.AnimationController.UpdateTransformations();
            buffers.Clear(); Character=null; failed=false;
        }
        internal bool TryItemPose(double now,out MatrixD model)
        {
            model=MatrixD.Identity;
            if(Character?.HandItemDefinition==null || !Stream.Sample(now,out var pose) || pose.Seat!=0) return false;
            bool left=(pose.Tracked&PlayerPose.ItemMirrored)!=0;
            var bones=Character.AnimationController.CharacterBones;
            if(bones==null || (pose.Tracked&(left ? 1:2))==0) return false;
            var pair=PairFor(Character,bones);
            var arm=left ? pair.Left:pair.Right;
            var profile=HeldItemPose.Profile(Character);
            return arm!=null && profile!=null && TryHold(pair,profile,Character.HandItemDefinition,left,out var hold) &&
                WeaponPose.TryHandItem(hold,(MatrixD)arm.PalmOffset*Stream.Hand(left,now)*Character.WorldMatrix,out model);
        }
        private static bool TryHold(Pair pair,WeaponProfile profile,Sandbox.Definitions.MyHandItemDefinition item,bool mirrored,out MatrixD hold)
        {
            hold=item.RightHand;
            if(!mirrored) { if(profile!=null) hold=profile.Hold(item.RightHand,item.LeftHand,false,Matrix.Identity); return true; }
            if(pair.Left==null || pair.Right==null) return false;
            var palm=ArmSkeleton.PalmMirror(pair.Right,pair.Left);
            hold=profile!=null ? profile.Hold(item.RightHand,item.LeftHand,false,palm):WeaponProfile.MirrorHold(item.RightHand,palm);
            return true;
        }
        internal bool Mirrored(double now) => Character!=null && Stream.Sample(now,out var pose) && pose.Character==Character.EntityId &&
            pose.Seat==0 && (pose.Tracked&PlayerPose.ItemMirrored)!=0;
        internal bool Supported(double now) => Character!=null && Stream.Sample(now,out var pose) && pose.Character==Character.EntityId &&
            pose.Seat==0 && (pose.Tracked&3)==3 && (pose.Tracked&PlayerPose.ItemSupported)!=0;
        internal bool TryToolRay(double now,out MatrixD ray)
        {
            ray=MatrixD.Identity;
            if(Character==null || !Stream.Sample(now,out var pose) || pose.Seat!=0 || (pose.Tracked&PlayerPose.ToolRayTracked)==0) return false;
            ray=Stream.ToolRay(now)*Character.WorldMatrix; return ray.IsValid();
        }
        private Pair PairFor(MyCharacter character,MyCharacterBone[] bones)
        {
            if(!buffers.TryGetValue(bones,out var pair))
            {
                if(buffers.Count>=2) buffers.Clear();
                var d=character.Definition;
                pair=new Pair {Left=ArmSkeleton.Find(character,d.LeftHandIKStartBone,d.LeftForearmBone,d.LeftHandIKEndBone,-1),
                    Right=ArmSkeleton.Find(character,d.RightHandIKStartBone,d.RightForearmBone,d.RightHandIKEndBone,1)};
                var head=ArmSkeleton.Head(name=>character.AnimationController.FindBone(name,out _),d.HeadBone);
                if(head!=null) pair.Head=new ArmSkeleton.SavedBone {Bone=head};
                buffers.Add(bones,pair);
            }
            return pair;
        }
        internal void Apply(double now)
        {
            var character=Character;
            if(character==null || character.Closed || character==MySession.Static?.LocalCharacter) return;
            Restore();
            if(failed || character.IsDead || character.IsOnLadder || !Stream.Sample(now,out var pose) ||
                pose.Character!=character.EntityId || (character.Parent?.EntityId ?? 0)!=pose.Seat)
            { character.AnimationController.UpdateTransformations(); return; }
            try
            {
                var bones=character.AnimationController.CharacterBones;
                if(bones==null) return;
                var pair=PairFor(character,bones);
                var item=character.IsSitting ? null:HeldItemPose.Profile(character);
                bool mirrored=(pose.Tracked&PlayerPose.ItemMirrored)!=0;
                if(mirrored && pair.Left!=null && pair.Right!=null) ArmSkeleton.MirrorCollars(pair.Left,pair.Right);
                if((pose.Tracked&1)!=0) ArmSkeleton.Apply(pair.Left,Stream.Hand(true,now),true,0,1,pose.LeftFingers,pose.LeftTrigger,null,item,!mirrored);
                if((pose.Tracked&2)!=0) ArmSkeleton.Apply(pair.Right,Stream.Hand(false,now),true,0,1,pose.RightFingers,pose.RightTrigger,null,item,mirrored);
                if((pose.Tracked&PlayerPose.HeadTracked)!=0) ArmSkeleton.Look(pair.Head,Stream.Head(now),ArmSkeleton.HeadLimit);
                character.AnimationController.UpdateTransformations();
                // Native weapon placement ran before this pose; move the held item onto the tracked palm.
                var holding=mirrored ? pair.Left:pair.Right;
                if((pose.Tracked&(mirrored ? 1:2))!=0 && holding?.Applied==true && character.CurrentWeapon is VRage.Game.Entity.MyEntity weapon && character.HandItemDefinition!=null &&
                    TryHold(pair,item,character.HandItemDefinition,mirrored,out var hold) &&
                    WeaponPose.TryHandItem(hold,(MatrixD)holding.Palm.Bone.AbsoluteTransform*character.WorldMatrix,out var held))
                    weapon.WorldMatrix=held;
                ReloadAnimation.Apply(character);
            }
            catch(Exception error)
            {
                Restore(); failed=true;
                MultiplayerRuntime.Log("Remote arm animation disabled for "+character.EntityId+": "+error.Message);
            }
        }
    }
}
