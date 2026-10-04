using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender.Animations;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class BodyFit
    {
        private sealed class Pose
        {
            public MyCharacterBone[] Bones;
            public Vector3[] Original,Applied;
            public Matrix[] Targets,Local;
            public float Scale;
            public bool Active;
        }
        private static readonly Dictionary<MyCharacterBone[],Pose> poses=new Dictionary<MyCharacterBone[],Pose>();
        private static MyCharacter owner;
        public static bool Enabled => Common.Config.BodyCalibrated;
        internal static float Scale(float height,float reference) =>
            height.IsValid() && reference.IsValid() && reference>.5f ? MathHelper.Clamp(height/reference,.5f,1.5f) : 1;
        internal static bool UseFit(bool calibrated,bool selected,bool sitting) => calibrated && selected && !sitting;
        public static bool Fitting(MyCharacter character) => character!=null && UseFit(Enabled,Common.Config.FitBodyOnFoot,character.IsSitting);
        public static float ScaleFor(MyCharacter character) => Fitting(character) ? Scale(Common.Config.PlayerHeight,character.Definition.CharacterCollisionHeight) : 1;
        public static float EyeReference(MyCharacter character)
        {
            var bone=character?.AnimationController.FindBone(character.Definition.HeadBone,out _);
            return bone?.GetAbsoluteRigTransform().Translation.Y ?? 1.69f;
        }
        public static float DesiredEye(MyCharacter character) => EyeReference(character)*ScaleFor(character);
        public static float CrouchDepth(MyCharacter character) =>
            Math.Max(0,character.Definition.CharacterCollisionHeight-character.Definition.CharacterCollisionCrouchHeight)*ScaleFor(character);
        public static float StandingReference()
        {
            var config=Common.Config;
            if(config.MeasuredEyeHeight>.3f) return config.MeasuredEyeHeight;
            var character=MySession.Static?.LocalCharacter;
            return character==null ? config.PlayerHeight*(1.69f/1.8f) : EyeReference(character)*Scale(config.PlayerHeight,character.Definition.CharacterCollisionHeight);
        }
        internal static bool Rigid(string name) => name.Contains("Palm") || name.Contains("Forearm2") || name.Contains("Forearm3") ||
            name.StartsWith("SE_RigL_") || name.StartsWith("SE_RigR_");
        internal static Vector3 Position(Vector3 point,Vector3 pivot,float scale) => pivot+(point-pivot)*scale;
        public static void Reset()
        {
            if(owner!=null) Restore(owner);
            poses.Clear(); owner=null;
        }
        public static void Restore(MyCharacter character)
        {
            if(owner==character && character.AnimationController.CharacterBones!=null && poses.TryGetValue(character.AnimationController.CharacterBones,out var pose)) Restore(pose);
        }
        private static void Restore(Pose pose)
        {
            if(!pose.Active) return;
            foreach(var bone in pose.Bones)
                if(bone.Translation==pose.Applied[bone.Index]) bone.Translation=pose.Original[bone.Index];
            foreach(var bone in pose.Bones.Where(b=>b.Parent==null)) bone.ComputeAbsoluteTransform(true,true);
            pose.Active=false;
        }
        public static void Apply(MyCharacter character)
        {
            if(owner!=character) { Reset(); owner=character; }
            if(!Fitting(character)) return;
            var bones=character.AnimationController.CharacterBones;
            if(bones==null) return;
            if(!poses.TryGetValue(bones,out var pose))
            {
                if(poses.Count>=2) poses.Clear();
                pose=new Pose { Bones=bones.OrderBy(b=>b.Depth).ToArray(),Original=new Vector3[bones.Length],Applied=new Vector3[bones.Length],
                    Targets=new Matrix[bones.Length],Local=new Matrix[bones.Length] };
                poses.Add(bones,pose);
            }
            Restore(pose);
            pose.Scale=ScaleFor(character);
            Fit(pose.Bones,Vector3.Zero,pose.Scale,pose.Original,pose.Applied,pose.Targets,pose.Local);
            pose.Active=true;
        }
        internal static void Fit(MyCharacterBone[] ordered,Vector3 pivot,float scale,Vector3[] original,Vector3[] applied,Matrix[] targets,Matrix[] local)
        {
            foreach(var bone in ordered)
            {
                original[bone.Index]=bone.Translation;
                var target=bone.AbsoluteTransform;
                local[bone.Index]=bone.Parent==null ? target : target*Matrix.Invert(bone.Parent.AbsoluteTransform);
                target.Translation=Position(target.Translation,pivot,scale);
                targets[bone.Index]=target;
            }
            foreach(var bone in ordered)
            {
                var target=targets[bone.Index];
                if(Rigid(bone.Name) && bone.Parent!=null)
                {
                    // Rigid cuffs and fingers retain the measured attachment geometry.
                    target=local[bone.Index]*bone.Parent.AbsoluteTransform;
                }
                bone.SetCompleteTransformFromAbsoluteMatrix(ref target,false);
                bone.ComputeAbsoluteTransform(false,true);
                applied[bone.Index]=bone.Translation;
            }
        }
        internal static Matrix RenderBone(Matrix bone,float scale,bool rigid)
        {
            if(!rigid) { bone.Right*=scale; bone.Up*=scale; bone.Backward*=scale; }
            return bone;
        }
        public static void Render(MyRenderMessageSetCharacterTransforms message)
        {
            var character=MySession.Static?.LocalCharacter;
            if(!Main.VrActive || !Fitting(character) || character!=owner || character.Render.GetRenderObjectID()!=message.CharacterID ||
                !poses.TryGetValue(character.AnimationController.CharacterBones,out var pose) || !pose.Active) return;
            foreach(var bone in pose.Bones)
                message.BoneAbsoluteTransforms[bone.Index]=RenderBone(message.BoneAbsoluteTransforms[bone.Index],pose.Scale,Rigid(bone.Name));
        }
        public static void SetHeight(float height)
        {
            Common.Config.PlayerHeight=MathHelper.Clamp(height,1.0f,2.4f); Common.Config.BodyCalibrated=true; Common.Config.MeasuredEyeHeight=0;
            if(Main.VrActive) Player.ApplyCalibrationOrigin();
        }
        public static void SetSeated(bool value)
        {
            if(value && Main.VrActive && Player.Headset.pose.isTracked) Common.Config.SeatedReference=Player.Headset.pose.deviceToAbsolute.matrix.Translation.Y;
            Common.Config.SeatedPlay=value; Common.Config.BodyCalibrated=true;
            if(Main.VrActive)
            {
                if(MySession.Static?.LocalCharacter?.IsSitting==true) Player.SeatOrigin(true);
                else Player.ApplyCalibrationOrigin();
            }
        }
        public static void CaptureSeat()
        {
            if(!Main.VrActive || !Player.Headset.pose.isTracked) return;
            Common.Config.SeatedReference=Player.Headset.pose.deviceToAbsolute.matrix.Translation.Y;
            if(Main.VrActive) Player.ApplyCalibrationOrigin();
        }
        public static void ResetCalibration()
        {
            var config=Common.Config;
            config.BodyCalibrated=false; config.FitBodyOnFoot=false; config.SeatedPlay=false; config.MeasuredEyeHeight=0;
            if(Main.VrActive) Player.ApplyCalibrationOrigin();
        }
    }
}
