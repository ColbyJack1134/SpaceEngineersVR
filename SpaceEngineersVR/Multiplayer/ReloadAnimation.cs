using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class ReloadAnimation
    {
        internal sealed class Motion
        {
            private bool detached,returning;
            private double changed;
            internal MatrixD Pose(bool unplugged,double now,MatrixD dock,MatrixD hand)
            {
                if(unplugged!=detached) { returning=!unplugged; detached=unplugged; changed=now; }
                if(detached)
                {
                    double elapsed=now-changed;
                    if(elapsed>=.12) return hand;
                    var drop=dock; drop.Translation+=dock.Down*(.12*elapsed/.12); return drop;
                }
                if(!returning) return dock;
                float t=MathHelper.SmoothStep(0,1,(float)Math.Min(1,(now-changed)/.16));
                var result=MatrixD.CreateFromQuaternion(Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(hand),Quaternion.CreateFromRotationMatrix(dock),t));
                result.Translation=Vector3D.Lerp(hand.Translation,dock.Translation,t);
                if(t>=1) returning=false;
                return result;
            }
        }
        private sealed class State
        {
            internal MyAutomaticRifleGun Gun;
            internal MatrixD Relative;
            internal double At;
            internal bool SuppressHand,Hidden,WasVisible;
            internal readonly Motion Animation=new Motion();
        }
        private static readonly ConditionalWeakTable<MyCharacter,State> states=new ConditionalWeakTable<MyCharacter,State>();
        private static void Clear(MyCharacter character)
        {
            if(states.TryGetValue(character,out var state) && !state.Gun.Closed)
            { Visibility(state,false); state.Gun.ResetMagazinePosition(); }
            states.Remove(character);
        }
        private static void Visibility(State state,bool hide)
        {
            if(state.Hidden==hide || !state.Gun.Subparts.TryGetValue("magazine",out var magazine)) return;
            if(hide) state.WasVisible=magazine.Render.Visible;
            magazine.Render.Visible=hide ? false:state.WasVisible; state.Hidden=hide;
        }
        internal static void Capture(MyCharacter character)
        {
            if(character.CurrentWeapon?.IsReloading!=true || !(character.CurrentWeapon is MyAutomaticRifleGun gun) || !HeldItemPose.TryGet(character,out _,out _))
            { Clear(character); return; }
            var left=character.AnimationController.FindBone(character.Definition.LeftHandIKEndBone,out _);
            var dummy=character.GetLeftHandBone();
            if(left==null || dummy==null) return;
            if(states.TryGetValue(character,out var previous) && previous.Gun!=gun) Clear(character);
            var state=states.GetValue(character,_=>new State {Gun=gun});
            // Sample only the magazine's animated grip offset before native hand IK. Never retarget an arm.
            state.Relative=(MatrixD)dummy.AbsoluteTransform*MatrixD.Invert(left.AbsoluteTransform); state.At=MultiplayerRuntime.Now;
        }
        internal static void Apply(MyCharacter character)
        {
            if(!states.TryGetValue(character,out var state)) return;
            if(character.CurrentWeapon!=state.Gun || !state.Gun.IsReloading || MultiplayerRuntime.Now-state.At>.2 || !HeldItemPose.TryGet(character,out _,out _))
            { Clear(character); return; }
            var left=character.AnimationController.FindBone(character.Definition.LeftHandIKEndBone,out _);
            if(left==null || !state.Gun.Model.Dummies.TryGetValue("subpart_magazine",out var socket)) return;
            var dock=(MatrixD)Matrix.Normalize(socket.Matrix)*state.Gun.WorldMatrix;
            state.SuppressHand|=HeldItemPose.Supported(character);
            Visibility(state,state.SuppressHand && character.ShouldPositionMagazine);
            if(state.SuppressHand) { state.Gun.SetMagazinePosition(dock); return; }
            var hand=state.Relative*left.AbsoluteTransform*character.WorldMatrix;
            var pose=state.Animation.Pose(character.ShouldPositionMagazine,MultiplayerRuntime.Now,dock,hand);
            if(pose.IsValid()) state.Gun.SetMagazinePosition(pose);
        }
    }
    [HarmonyPatch(typeof(MyCharacter),"RealignMagazineWithHand")]
    internal static class HeldMagazineAlignmentPatch
    {
        private static void Postfix(MyCharacter __instance) => ReloadAnimation.Apply(__instance);
    }
    [HarmonyPatch(typeof(MyCharacter),"UpdateHandBones")]
    internal static class HeldReloadAnimationPatch
    {
        private static void Prefix(MyCharacter __instance) => ReloadAnimation.Capture(__instance);
    }
}
