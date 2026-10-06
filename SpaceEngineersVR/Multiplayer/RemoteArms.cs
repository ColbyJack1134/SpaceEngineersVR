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
        private const float HeadLimit=1.4f;
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
            if(Character?.HandItemDefinition==null || !Stream.Sample(now,out var pose) || (pose.Tracked&2)==0 || pose.Seat!=0) return false;
            var bones=Character.AnimationController.CharacterBones;
            if(bones==null) return false;
            var arm=PairFor(Character,bones).Right;
            var profile=HeldItemPose.Profile(Character);
            return arm!=null && profile!=null && WeaponPose.TryHandItem(profile.Palm(Character.HandItemDefinition.RightHand,false),
                (MatrixD)arm.PalmOffset*Stream.Hand(false,now)*Character.WorldMatrix,out model);
        }
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
                if((pose.Tracked&1)!=0) ArmSkeleton.Apply(pair.Left,Stream.Hand(true,now),true,0,1,pose.LeftFingers,pose.LeftTrigger,null,item,true);
                if((pose.Tracked&2)!=0) ArmSkeleton.Apply(pair.Right,Stream.Hand(false,now),true,0,1,pose.RightFingers,pose.RightTrigger,null,item,false);
                if((pose.Tracked&PlayerPose.HeadTracked)!=0) ArmSkeleton.Look(pair.Head,Stream.Head(now),HeadLimit);
                character.AnimationController.UpdateTransformations();
                // Native weapon placement ran before this pose; move the held item onto the tracked palm.
                if((pose.Tracked&2)!=0 && pair.Right?.Applied==true && character.CurrentWeapon is VRage.Game.Entity.MyEntity weapon && character.HandItemDefinition!=null &&
                    WeaponPose.TryHandItem(item?.Palm(character.HandItemDefinition.RightHand,false) ?? character.HandItemDefinition.RightHand,(MatrixD)pair.Right.Palm.Bone.AbsoluteTransform*character.WorldMatrix,out var held))
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
