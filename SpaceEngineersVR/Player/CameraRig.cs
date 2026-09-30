using System;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Camera state is owned by the rig, not by the animated/physics body camera.
    // Publish anchor AND origin in one immutable packet; the renderer never mixes ticks.
    internal static class CameraRig
    {
        internal sealed class Frame
        {
            public readonly MatrixD Anchor;
            public readonly Matrix OriginInverse;
            public readonly int Epoch;
            public readonly double UnitsPerMeter;
            public readonly bool ThirdPerson;
            public readonly Control.Diorama.Scene Observer;
            public Frame(MatrixD anchor,Matrix originInverse, int epoch = 0,double unitsPerMeter=1,bool thirdPerson=false,Control.Diorama.Scene observer=null)
            { Anchor=anchor; OriginInverse=originInverse; Epoch=epoch; UnitsPerMeter=unitsPerMeter; ThirdPerson=thirdPerson; Observer=observer; }
            public MatrixD TrackingToWorld => (MatrixD)OriginInverse*MatrixD.CreateScale(UnitsPerMeter)*Anchor;
        }
        private static Frame frame;
        private static int epoch;
        private static MyCharacter owner;
        private static MatrixD anchor, lastBody;
        private static Vector3D eyeOffset;
        private static readonly EyeHeightCalibration eyeHeight=new EyeHeightCalibration();
        private static object activeDefinition;
        private static bool restoreBag, restoreHead;
        private static readonly System.Reflection.FieldInfo bagField=AccessTools.Field(typeof(MyCharacter),"m_enableBag");
        private static readonly System.Reflection.FieldInfo headField=AccessTools.Field(typeof(MyCharacter),"m_headRenderingEnabled");
        private static readonly Action<MyCharacter> refreshDepth=AccessTools.MethodDelegate<Action<MyCharacter>>(AccessTools.Method(typeof(MyCharacter),"UpdateNearFlag"));
        public static Frame Current => ThirdPersonView.Current ?? Volatile.Read(ref frame);
        public static void Reset(bool forgetHeight=false)
        {
            epoch++;
            var previous=owner;
            if(previous!=null && !previous.Closed) { previous.EnableBag(restoreBag); previous.EnableHead(restoreHead); }
            owner=null; activeDefinition=null; Volatile.Write(ref frame,null);
            if(previous!=null && !previous.Closed && MySession.Static!=null) refreshDepth(previous);
            if(forgetHeight) eyeHeight.Clear();
        }
        public static void Recenter(Matrix oldOrigin, Matrix newOrigin)
        {
            if(owner==null) return;
            anchor=VrMath.RecenterAnchor(anchor,oldOrigin,newOrigin);
            anchor.Translation=BodyFrame(owner).Translation+Vector3D.TransformNormal(eyeOffset,anchor);
        }
        public static MatrixD Anchor => anchor;
        public static bool Owns(MyCharacter character) => owner==character && Current!=null;
        private static MatrixD BodyFrame(MyCharacter character)
        {
            // WorldMatrix includes the engine's changing foot-IK offset. Roomscale
            // writes physics coordinates, so using that visual translation made the
            // eye height jump whenever the body-follow threshold was crossed.
            return VrMath.PhysicsAnchoredBody(character.WorldMatrix,
                character.Physics?.GetWorldMatrix().Translation ?? character.WorldMatrix.Translation);
        }
        public static void Begin(MyCharacter character)
        {
            MatrixD body=BodyFrame(character);
            if (owner!=character || !ReferenceEquals(activeDefinition,character.Definition))
            {
                Reset();
                restoreBag=(bool)(bagField?.GetValue(character) ?? true);
                restoreHead=(bool)(headField?.GetValue(character) ?? true);
                MatrixD head=character.GetHeadMatrix(true,true,forceHeadBone:true);
                // Avoid animation/vanilla camera roll. Retain the character's up frame (including flight).
                anchor=VrMath.Level(head,body.Up);
                double measured=Vector3D.Dot(head.Translation-body.Translation,body.Up);
                var headBone=character.AnimationController.FindBone(character.Definition.HeadBone,out _);
                double rest=headBone?.GetAbsoluteRigTransform().Translation.Y ?? double.NaN;
                double height=eyeHeight.Get(character,character.Definition,measured,rest,character.Definition.CharacterCollisionHeight);
                // Remain centered over the physics capsule and preserve the previously
                // validated standing height when ejection animation is still settling.
                eyeOffset=new Vector3D(0,height,0);
                activeDefinition=character.Definition;
                owner=character;
                lastBody=body;
                Logger.Info("Independent VR camera rig initialized; eye height="+height.ToString("F3")+
                    "; animated="+measured.ToString("F3")+"; bind="+rest.ToString("F3"));
            }
            else
            {
                // Only changes since END of last tick are game/joystick rotation.
                // Physical body-follow yaw is already in lastBody and never enters the camera.
                anchor=VrMath.AdvanceAnchor(anchor,lastBody,body);
            }
            if (!((IMyCharacter)character).EnabledThrusts)
            {
                Vector3D up=character.Physics.Gravity;
                up=up.LengthSquared()>0.01 ? -Vector3D.Normalize(up) : body.Up;
                anchor=VrMath.Level(anchor,up);
            }
            character.EnableHead(false);
            character.Render.NearFlag=false;
            if ((bool)(bagField?.GetValue(character) ?? true)) character.EnableBag(false);
            anchor.Translation=body.Translation+Vector3D.TransformNormal(eyeOffset,anchor);
        }
        public static void End(MyCharacter character)
        {
            lastBody=BodyFrame(character);
            anchor.Translation=lastBody.Translation+Vector3D.TransformNormal(eyeOffset,anchor);
            Publish();
        }
        public static void RefreshAfterSimulation(MyCharacter character)
        {
            // Advance game-applied rotation once, and capture the body's CURRENT
            // physics position. Do not run roomscale or body-follow input a second time.
            Begin(character);
            End(character);
        }
        public static void Publish()
        {
            var character=MySession.Static?.LocalCharacter;
            if(character==null || MySession.Static.ControlledEntity!=character || character.IsSitting || character.IsDead)
            { Reset(); return; }
            if(owner==character) Volatile.Write(ref frame,new Frame(anchor,Player.PlayerToAbsolute.inverted,epoch));
        }
        public static MatrixD DeviceWorld(Matrix device)
        {
            var state=Current;
            if(state==null) return (MatrixD)VrMath.Affine(device*Player.PlayerToAbsolute.inverted)*MySector.MainCamera.WorldMatrix;
            return MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(state.Anchor),device,state.OriginInverse,Matrix.Identity,state.UnitsPerMeter));
        }
    }
}
