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
                if(!buffers.TryGetValue(bones,out var pair))
                {
                    if(buffers.Count>=2) buffers.Clear();
                    var d=character.Definition;
                    pair=new Pair {Left=ArmSkeleton.Find(character,d.LeftHandIKStartBone,d.LeftForearmBone,d.LeftHandIKEndBone,-1),
                        Right=ArmSkeleton.Find(character,d.RightHandIKStartBone,d.RightForearmBone,d.RightHandIKEndBone,1)};
                    var head=character.AnimationController.FindBone(d.HeadBone,out _);
                    if(head!=null) pair.Head=new ArmSkeleton.SavedBone {Bone=head};
                    buffers.Add(bones,pair);
                }
                if((pose.Tracked&1)!=0) ArmSkeleton.Apply(pair.Left,Stream.Hand(true,now),true,false,1,pose.LeftFingers,pose.LeftTrigger);
                if((pose.Tracked&2)!=0) ArmSkeleton.Apply(pair.Right,Stream.Hand(false,now),true,false,1,pose.RightFingers,pose.RightTrigger);
                if((pose.Tracked&PlayerPose.HeadTracked)!=0) ArmSkeleton.Look(pair.Head,Stream.Head(now),HeadLimit);
                character.AnimationController.UpdateTransformations();
                // Native weapon placement ran before this pose; move the held item onto the tracked palm.
                if((pose.Tracked&2)!=0 && pair.Right?.Applied==true && character.CurrentWeapon is VRage.Game.Entity.MyEntity weapon && character.HandItemDefinition!=null &&
                    WeaponPose.TryHandItem(character.HandItemDefinition.RightHand,(MatrixD)pair.Right.Palm.Bone.AbsoluteTransform*character.WorldMatrix,out var held))
                    weapon.WorldMatrix=held;
            }
            catch(Exception error)
            {
                Restore(); failed=true;
                MultiplayerRuntime.Log("Remote arm animation disabled for "+character.EntityId+": "+error.Message);
            }
        }
    }
}
