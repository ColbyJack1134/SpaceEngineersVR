using System;
using Sandbox.Game;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class WeaponHandling
    {
        private static readonly GripCapture support=new GripCapture(),reload=new GripCapture();
        private static MyEntity weapon;
        private static WeaponProfile profile;
        private static MatrixD worldPose;
        private static bool valid,wasSupported,wasReloading;
        private static int hover;
        private static Quaternion releaseRotation=Quaternion.Identity;
        private static float releaseBlend;
        public static bool ConsumesLeftGrip => support.Consumed || reload.Consumed;
        public static bool Supported => valid && support.Held;
        internal static WeaponProfile Profile => valid ? profile:null;
        internal static bool ToolEquipped => InputRouter.TrackedItems && MySession.Static?.ControlledEntity==MySession.Static?.LocalCharacter &&
            (MySession.Static?.LocalCharacter?.CurrentWeapon is MyEngineerToolBase || MySession.Static?.LocalCharacter?.CurrentWeapon is MyHandDrill);
        internal static bool ToolModifier => ToolEquipped && Controls.Static.RightGripPressure.CanPress && Controls.Static.RightGripPressure.RawPosition.X>.5f;

        internal static bool HideUseRay => MySession.Static?.LocalCharacter?.CurrentWeapon is MyAutomaticRifleGun || ToolEquipped && (Supported || ToolContactActive);
        internal static bool ToolContactActive => weapon is MyEngineerToolBase tool ? Multiplayer.ToolContact.Near(tool):
            weapon is MyHandDrill drill && Multiplayer.DrillContact.Near(drill);
        internal static bool Reloading => valid && !profile.Tool && MySession.Static?.LocalCharacter?.CurrentWeapon?.IsReloading==true;
        public static void Reset()
        {
            support.Release(); reload.Release(); valid=wasSupported=wasReloading=false; weapon=null; profile=null; releaseBlend=0;
            Multiplayer.HeldItemPose.Local=null; Multiplayer.HeldItemPose.LocalToolRay=null; Multiplayer.HeldItemPose.LocalSupported=null;
            hover=0; ItemGrabVisual.Reset();
        }
        public static void Update()
        {
            Multiplayer.HeldItemPose.Local=LocalPose;
            Multiplayer.HeldItemPose.LocalToolRay=LocalToolRay;
            Multiplayer.HeldItemPose.LocalSupported=c=>c==MySession.Static?.LocalCharacter ? Supported:(bool?)null;
            Multiplayer.HeldItemPose.Feedback=Haptic;
            var character=MySession.Static?.LocalCharacter;
            var current=character?.CurrentWeapon as MyEntity;
            var controls=Controls.Static;
            bool down=ConsumesLeftGrip ? controls.LeftGripPressure.RawPosition.X>.1f:
                InputRouter.Flying ? controls.ThrustDown.Position.X>.5f:controls.CrouchOrClimbDown.IsPressed;
            if(current!=weapon)
            {
                support.Release(); reload.Release(); valid=wasSupported=wasReloading=false; releaseBlend=0; weapon=current;
                profile=WeaponProfile.Find(character?.CurrentWeapon?.PhysicalObject?.SubtypeName,current?.Model?.AssetName);
                hover=0; ItemGrabVisual.Reset();
            }
            bool available=Main.VrActive && InputRouter.TrackedItems && character!=null && MySession.Static.ControlledEntity==character &&
                !character.IsDead && !character.IsSitting && !character.IsOnLadder && profile!=null && Player.HandR.pose.isTracked && Player.Headset.pose.isTracked;
            if(!available || !TrackedArms.TryFreePalm(character,Player.HandR,out var right) || character.HandItemDefinition==null ||
                !WeaponPose.TryHandItem(profile.Palm(character.HandItemDefinition.RightHand,false),right,out var oneWorld))
            { support.Update(false,down,false,false); reload.Update(false,down,false,false); valid=wasSupported=false; hover=0; ItemGrabVisual.Reset(); return; }
            oneWorld=Alignment.Apply(Alignment.ToolKey(character),oneWorld);
            var parent=character.WorldMatrix; var inverse=character.PositionComp.WorldMatrixNormalizedInv;
            var one=(Matrix)(oneWorld*inverse); var primary=Vector3.Transform(profile.Primary,one);
            bool offTracked=TrackedArms.TryFreePalm(character,Player.HandL,out var left);
            var off=offTracked ? (Vector3)Vector3D.Transform(left.Translation,inverse):Vector3.Zero;
            bool reachable=offTracked && WeaponPose.TrySupport(profile,one,primary,off,out _,support.Held);
            var local=offTracked ? (Vector3)Vector3D.Transform(Vector3D.Transform(WeaponPose.GrabOffset,left),MatrixD.Invert(valid && TryPose(character,out var displayed,out _) ? displayed:oneWorld)):Vector3.Zero;
            bool interaction=offTracked && InputRouter.Gameplay && !Main.MenuOpen && (support.Held ||
                !character.CurrentWeapon.IsReloading && !CockpitTouch.Owns(Player.HandL) && !HandInteraction.Owns(Player.HandL) &&
                !FloatingWindows.PointingFor(Player.HandL) && !TouchScreenBridge.PointingFor(Player.HandL) && !ArthurLcdBridge.PointingFor(Player.HandL) && !HelmetHud.Consumes(Player.HandL));
            var supportPoint=profile.SupportContact(character.HandItemDefinition.LeftHand);
            int candidate=interaction && !ConsumesLeftGrip ? profile.Grab(local,supportPoint):0;
            if(candidate==1 && !reachable) candidate=0;
            bool near=candidate==1,mag=candidate==2;
            if(candidate!=hover && candidate!=0) CockpitFeedback.Hover(Player.HandL);
            hover=candidate;
            bool reloadCaptured=reload.Update(interaction && (!support.Consumed || !down),down,mag,!support.Consumed || !down);
            if(reloadCaptured)
            {
                CockpitFeedback.Activate(Player.HandL);
                if(character.CurrentWeapon.CanReload()) NativeActions.Pulse(MyControlsSpace.RELOAD);
            }
            if(support.Update(interaction && (!reload.Consumed || !down),down,near,(support.Held || reachable) && (!reload.Consumed || !down))) Player.HandL.Vibrate(0,.04f,110,.35f);
            if(wasSupported && !support.Held)
            {
                var previous=(Matrix)(worldPose*inverse);
                releaseRotation=Quaternion.CreateFromRotationMatrix(previous.GetOrientation()*Matrix.Transpose(one.GetOrientation())); releaseBlend=1;
            }
            if(!support.Held) releaseBlend=Math.Max(0,releaseBlend-.16f);
            worldPose=WeaponPose.Current(profile,oneWorld,parent,support.Held ? left:(MatrixD?)null,releaseRotation,releaseBlend);
            wasSupported=support.Held; valid=true;
            bool reloading=character.CurrentWeapon.IsReloading;
            if(reloading && !wasReloading) Player.HandL.Vibrate(0,.055f,95,.4f);
            if(!reloading && wasReloading) Player.HandR.Vibrate(0,.035f,140,.25f);
            wasReloading=reloading;
            ItemGrabVisual.Update(current,profile,supportPoint,hover,Supported);

        }
        private static MatrixD? LocalToolRay(MyCharacter character) => Patches.MotionToolPatch.Eligible(character) && character==MySession.Static?.LocalCharacter &&
            ToolEquipped && TrackedArms.TryFreePointPose(Player.HandR,out var ray) ? ray:(MatrixD?)null;
        private static MatrixD? LocalPose(MyCharacter character) => Patches.MotionToolPatch.Eligible(character) && TryPose(character,out var pose,out _) ? pose:(MatrixD?)null;
        public static bool TryPose(MyCharacter character,out MatrixD model,out Vector3D muzzle)
        {
            model=MatrixD.Identity; muzzle=Vector3D.Zero;
            if(!InputRouter.TrackedItems || character==null || character!=MySession.Static?.LocalCharacter || MySession.Static.ControlledEntity!=character ||
                character.CurrentWeapon!=weapon || character.CurrentWeapon==null || !Player.HandR.pose.isTracked) return false;
            if(profile!=null)
            {
                if(!valid || character.HandItemDefinition==null || !TrackedArms.TryFreePalm(character,Player.HandR,out var right) ||
                    !WeaponPose.TryHandItem(profile.Palm(character.HandItemDefinition.RightHand,false),right,out var one)) return false;
                one=Alignment.Apply(Alignment.ToolKey(character),one);
                MatrixD? left=Supported && TrackedArms.TryFreePalm(character,Player.HandL,out var off) ? off:(MatrixD?)null;
                model=WeaponPose.Current(profile,one,character.WorldMatrix,left,releaseRotation,releaseBlend);
                muzzle=Vector3D.Transform(profile.Muzzle,model);
            }
            else
            {
                if(character.HandItemDefinition==null || !TrackedArms.TryFreePalm(character,Player.HandR,out var hand) ||
                    !WeaponPose.TryHandItem(character.HandItemDefinition.RightHand,hand,out model)) return false;
                model=Alignment.Apply(Alignment.ToolKey(character),model);
                muzzle=character.CurrentWeapon.GunBase?.GetMuzzleWorldPosition() ?? model.Translation;
            }
            return model.IsValid();
        }
        internal static bool TryPalm(MyCharacter character,Controller hand,out MatrixD palm)
        {
            palm=MatrixD.Identity;
            if(!valid || profile==null || hand==Player.HandL && !support.Held || !TryPose(character,out var model,out _)) return false;
            palm=profile.Palm(hand==Player.HandL ? character.HandItemDefinition.LeftHand:character.HandItemDefinition.RightHand,hand==Player.HandL)*model;
            return true;
        }
        private static double nextToolPulse;
        internal static void Haptic(MyCharacter owner,ItemKind kind)
        {
            if(owner!=MySession.Static?.LocalCharacter || !InputRouter.Gameplay || Main.MenuOpen || !Player.HandR.pose.isTracked) return;
            var now=Multiplayer.MultiplayerRuntime.Now;
            if(kind>=ItemKind.Welder && now<nextToolPulse) return;
            nextToolPulse=now+.08;
            float strength=kind==ItemKind.Welder ? .18f:kind==ItemKind.Pistol ? .35f:kind==ItemKind.Launcher ? .8f:.5f;
            Player.HandR.Vibrate(0,kind>=ItemKind.Welder ? .06f:.035f,kind==ItemKind.Welder ? 160:kind==ItemKind.Drill ? 65:110,strength);
            if(Supported) Player.HandL.Vibrate(0,.03f,100,strength*.45f);
        }
    }
}
