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
        internal static void ApplyObserverCamera(VRage.Game.Utils.MyCamera camera,Frame observer)
        {
            camera.CameraSpring.Enabled=false;
            camera.CameraShake.ShakeEnabled=false;
            camera.SetViewMatrix(VrMath.EyeView(MatrixD.Invert(observer.Anchor),Player.Headset.pose.deviceToAbsolute.matrix,
                observer.OriginInverse,Matrix.Identity,observer.UnitsPerMeter),smooth:false);
        }
        private static Frame frame;
        private static int epoch;
        private static MyCharacter owner;
        private static MatrixD anchor, lastBody;
        private static Vector3D eyeOffset;
        private static double standingEye;
        private static readonly EyeHeightCalibration eyeHeight=new EyeHeightCalibration();
        private static bool onLadder;
        // Boots turn the body to each voxel triangle underfoot; the view's up springs after it, ignoring small tilts on voxels only (grids are smoothed natively).
        internal const double BootsSmoothing=.2;
        internal static readonly double BootsDeadband=MathHelper.ToRadians(3),BootsMaxLag=MathHelper.ToRadians(60);
        private static Vector3D bootsUp,bootsSpin,bootsTarget;
        private static long bootsTime;
        private static object activeDefinition;
        private static bool restoreBag;
        private static readonly System.Reflection.FieldInfo bagField=AccessTools.Field(typeof(MyCharacter),"m_enableBag");
        private static readonly Action<MyCharacter> refreshDepth=AccessTools.MethodDelegate<Action<MyCharacter>>(AccessTools.Method(typeof(MyCharacter),"UpdateNearFlag"));
        internal static bool Detached => ThirdPersonView.Active || SpectatorView.Active;
        public static Frame Current => SpectatorView.Current ?? ThirdPersonView.Current ?? RemoteView.SeatedRig ?? Volatile.Read(ref frame);
        public static void Reset(bool forgetHeight=false)
        {
            epoch++;
            var previous=owner;
            // Only first person hides the head, and vanilla hides it again when it takes the camera.
            if(previous!=null && !previous.Closed) { previous.EnableBag(restoreBag); previous.EnableHead(true); }
            owner=null; activeDefinition=null; Volatile.Write(ref frame,null); crouchDrop=0; crouchTime=0;
            onLadder=false; ResetBoots();
            if(previous!=null && !previous.Closed && MySession.Static!=null) refreshDepth(previous);
            if(forgetHeight) eyeHeight.Clear();
        }
        public static void Recenter(Matrix oldOrigin, Matrix newOrigin)
        {
            HandInteraction.ResetTouch();
            if(owner==null) return;
            anchor=VrMath.RecenterAnchor(anchor,oldOrigin,newOrigin);
            Place(owner,BodyFrame(owner));
        }
        public static MatrixD Anchor => anchor;
        public static bool Owns(MyCharacter character) => !SpectatorView.Active && owner==character && Current!=null;
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
            bool ladder=character.IsOnLadder;
            bool thrusts=((IMyCharacter)character).EnabledThrusts;
            Vector3D gravity=character.Physics?.Gravity ?? Vector3.Zero;
            bool gravityUp=!thrusts && gravity.LengthSquared()>0.01;
            Vector3D up=gravityUp ? -Vector3D.Normalize(gravity) : character.IsMagneticBootsEnabled ? BootsUp(character,body.Up) : body.Up;
            if(gravityUp || !character.IsMagneticBootsEnabled) ResetBoots();
            if (owner!=character || !ReferenceEquals(activeDefinition,character.Definition))
            {
                Reset();
                restoreBag=(bool)(bagField?.GetValue(character) ?? true);
                MatrixD head=character.GetHeadMatrix(true,true,forceHeadBone:true);
                // Avoid animation/vanilla camera roll. Retain the character's up frame (including flight).
                anchor=VrMath.Level(head,body.Up);
                double measured=Vector3D.Dot(head.Translation-body.Translation,body.Up);
                var headBone=character.AnimationController.FindBone(character.Definition.HeadBone,out _);
                double rest=headBone?.GetAbsoluteRigTransform().Translation.Y ?? double.NaN;
                double height=eyeHeight.Get(character,character.Definition,measured,rest,character.Definition.CharacterCollisionHeight);
                // Remain centered over the physics capsule and preserve the previously
                // validated standing height when ejection animation is still settling.
                standingEye=height;
                eyeOffset=new Vector3D(0,height,0);
                activeDefinition=character.Definition;
                owner=character;
                lastBody=body;
                Logger.Info("Independent VR camera rig initialized; eye height="+height.ToString("F3")+
                    "; animated="+measured.ToString("F3")+"; bind="+rest.ToString("F3"));
            }
            else if (ladder==onLadder)
            {
                // Only changes since END of last tick are game/joystick rotation.
                // Physical body-follow yaw is already in lastBody and never enters the camera.
                // Mounting or leaving a ladder turns the body to face it; the view keeps its heading.
                anchor=gravityUp ? VrMath.AdvanceYaw(anchor,lastBody,body,up) : VrMath.AdvanceAnchor(anchor,lastBody,body);
            }
            if (!thrusts) anchor=VrMath.Level(anchor,up);
            character.EnableHead(ThirdPersonView.Character);
            Patches.CharacterDepthPatch.ApplyWorldDepth(character);
            character.EnableBag(ThirdPersonView.Character);
            eyeOffset.Y=(BodyFit.Fitting(character) ? BodyFit.DesiredEye(character) : standingEye)-CrouchDrop(character);
            onLadder=ladder;
            Place(character,body);
        }
        private static void Place(MyCharacter character,MatrixD body)
        {
            // Installed ladder clips lean the head toward the ladder; the eye follows the animated head.
            anchor.Translation=VrMath.EyePosition(body,anchor,eyeOffset,character.Physics?.Center.Y ?? 0)+body.Forward*LadderClimb.ViewLead(character);
        }
        private static Vector3D BootsUp(MyCharacter character,Vector3D bodyUp)
        {
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            double seconds=bootsTime==0 ? 0 : (now-bootsTime)/(double)System.Diagnostics.Stopwatch.Frequency;
            bootsTime=now;
            bootsTarget=character.SoundComp?.StandingOnVoxel!=null ? VrMath.Deadband(bootsTarget,bodyUp,BootsDeadband) : bodyUp;
            bootsUp=VrMath.SpringUp(bootsUp,ref bootsSpin,bootsTarget,Math.Min(seconds,1),BootsSmoothing,BootsMaxLag);
            return bootsUp;
        }
        private static void ResetBoots() { bootsUp=bootsSpin=bootsTarget=Vector3D.Zero; bootsTime=0; }
        private static double crouchDrop;
        private static long crouchTime;
        private static double CrouchDrop(MyCharacter character)
        {
            double target=ThirdPersonView.Character ? 0 : Control.CrouchControl.ViewDrop(character.IsCrouching,Components.VRMovementComponent.Crouch.Automatic,
                Components.VRMovementComponent.HeadDrop,BodyFit.CrouchDepth(character));
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            double seconds=crouchTime==0 ? 1 : (now-crouchTime)/(double)System.Diagnostics.Stopwatch.Frequency;
            crouchTime=now;
            // Ease the stick-crouch height change over about a quarter second.
            crouchDrop+=(target-crouchDrop)*(1-Math.Exp(-Math.Min(seconds,1)/.08));
            return crouchDrop;
        }
        public static void End(MyCharacter character)
        {
            lastBody=BodyFrame(character);
            Place(character,lastBody);
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
            if(SpectatorView.Active) { SpectatorView.Publish(); return; }
            var character=MySession.Static?.LocalCharacter;
            // Vanilla swings a third-person camera around the dead body; keep the last living frame until the respawn.
            if(character!=null && character.IsDead && owner==character && Current!=null) return;
            if(character==null || (MySession.Static.ControlledEntity!=character && !RemoteView.CharacterAnchor) || character.IsSitting || character.IsDead)
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
