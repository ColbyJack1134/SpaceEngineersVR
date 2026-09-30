using System;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class WeaponHandling
    {
        private static readonly GripCapture support=new GripCapture();
        private static MyEntity weapon;
        private static WeaponProfile profile;
        private static Matrix trackingPose;
        private static bool valid, wasSupported;
        private static Quaternion releaseRotation=Quaternion.Identity;
        private static float releaseBlend;
        public static bool ConsumesLeftGrip => support.Consumed;
        public static bool Supported => valid && support.Held;

        public static void Reset()
        {
            support.Release(); valid=wasSupported=false; weapon=null; profile=null; releaseBlend=0;
        }
        public static void Update()
        {
            var character=MySession.Static?.LocalCharacter;
            var current=character?.CurrentWeapon as MyEntity;
            var controls=Controls.Static;
            bool down=InputRouter.Flying ? support.AnalogDown(controls.ThrustDown.Position.X,controls.ThrustDown.RawPosition.X) :
                support.Consumed ? controls.CrouchOrClimbDown.RawPressed : controls.CrouchOrClimbDown.IsPressed;
            if (current!=weapon)
            {
                Reset(); weapon=current;
                profile=WeaponProfile.Find(character?.CurrentWeapon?.PhysicalObject?.SubtypeName,current?.Model?.AssetName);
            }
            bool available=Main.VrActive && InputRouter.TrackedItems &&
                character!=null && MySession.Static.ControlledEntity==character && !character.IsDead &&
                !character.IsSitting && !character.IsOnLadder &&
                profile!=null && Player.HandR.pose.isTracked && Player.HandL.pose.isTracked && Player.Headset.pose.isTracked;
            if (!available)
            {
                support.Update(false,down,false,false); valid=wasSupported=false; releaseBlend=0; return;
            }
            Vector3 primary=WeaponPose.Palm(Player.HandR.GripTracking);
            Vector3 offhand=WeaponPose.Palm(Player.HandL.GripTracking);
            Matrix oneHand=WeaponPose.Anchor(profile,Player.HandR.AimTracking,primary);
            if (!oneHand.IsValid() || System.Math.Abs(oneHand.Determinant()-1)>0.01f)
            { support.Update(false,down,false,false); valid=wasSupported=false; return; }
            bool reachable=WeaponPose.TrySupport(profile,oneHand,primary,offhand,out Matrix twoHand);
            Vector3 contact=Vector3.Transform(profile.Support,valid ? trackingPose : oneHand);
            bool near=Vector3.DistanceSquared(offhand,contact)<0.13f*0.13f;
            if (support.Update(true,down,near,reachable)) Player.HandL.Vibrate(0,0.045f,110,0.45f);
            if (wasSupported && !support.Held)
            {
                releaseRotation=Quaternion.CreateFromRotationMatrix(trackingPose.GetOrientation()*Matrix.Transpose(oneHand.GetOrientation()));
                releaseBlend=1;
            }
            if (support.Held) trackingPose=twoHand;
            else
            {
                releaseBlend=System.Math.Max(0,releaseBlend-0.16f);
                Matrix correction=Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity,releaseRotation,releaseBlend));
                trackingPose=WeaponPose.Anchor(profile,correction*oneHand.GetOrientation(),primary);
            }
            wasSupported=support.Held; valid=true;
        }
        public static bool TryPose(MyCharacter character, out MatrixD model, out Vector3D muzzle)
        {
            model=MatrixD.Identity; muzzle=Vector3D.Zero;
            if (!InputRouter.TrackedItems || character==null || character!=MySession.Static?.LocalCharacter ||
                MySession.Static.ControlledEntity!=character || character.CurrentWeapon!=weapon ||
                character.CurrentWeapon==null || !Player.HandR.pose.isTracked) return false;
            string key=Alignment.ToolKey(character);
            if(key==null) return false;
            if(profile!=null)
            {
                if(!valid || !Player.HandL.pose.isTracked) return false;
                model=Alignment.Apply(key,CameraRig.DeviceWorld(trackingPose));
                muzzle=Vector3D.Transform(profile.Muzzle,model);
            }
            else
            {
                if(character.HandItemDefinition==null || !TrackedArms.TryDesiredPalm(character,Player.HandR,out var hand)) return false;
                var attachment=(MatrixD)character.HandItemDefinition.RightHand;
                if(!attachment.IsValid() || Math.Abs(attachment.Determinant())<1e-6) return false;
                model=Alignment.Apply(key,MatrixD.Invert(attachment)*hand);
                muzzle=character.CurrentWeapon.GunBase is Sandbox.Game.Weapons.MyGunBase gun && gun.HasDummies
                    ? Vector3D.Transform(gun.GetMuzzleLocalPosition(),model) : model.Translation;
            }
            return model.IsValid();
        }
        public static void Draw()
        {
            if (!Common.Config.DeveloperTools || !valid || !InputRouter.Gameplay) return;
            MatrixD model=CameraRig.DeviceWorld(trackingPose);
            Vector3D contact=Vector3D.Transform(profile.Support,model);
            var color=(support.Held ? new Color(80,255,130) : new Color(255,180,60)).ToVector4();
            VRage.Game.MySimpleObjectDraw.DrawLine(contact-model.Right*0.022,contact+model.Right*0.022,
                VRage.Utils.MyStringId.GetOrCompute("Square"),ref color,0.009f);
        }
    }
}
