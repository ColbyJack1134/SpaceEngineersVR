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
        private static Matrix palmMirror=Matrix.Identity;
        private static int hover;
        private static Quaternion releaseRotation=Quaternion.Identity;
        private static float releaseBlend;
        public static bool ConsumesSupportGrip => support.Consumed || reload.Consumed;
        internal static bool GripsItem(Controller hand) => hand==Handedness.SupportHand && ConsumesSupportGrip;
        public static bool Supported => valid && support.Held;
        public static bool Aiming { get; private set; }
        internal static bool Holding(Controller hand) => valid && (hand==Handedness.ActionHand || hand==Handedness.SupportHand && support.Held);
        // Support hand is at the held item's support grip or magazine, where a fist also pulls the trigger.
        internal static bool SupportAtItem => valid && hover!=0;
        internal static WeaponProfile Profile => valid ? profile:null;
        internal static bool ToolEquipped => InputRouter.TrackedItems && MySession.Static?.ControlledEntity==MySession.Static?.LocalCharacter &&
            (MySession.Static?.LocalCharacter?.CurrentWeapon is MyEngineerToolBase || MySession.Static?.LocalCharacter?.CurrentWeapon is MyHandDrill);
        internal static bool ToolModifier => ToolEquipped && Controls.Static.ActionGripPressure.CanPress && Controls.Static.ActionGripPressure.RawPosition.X>.5f;

        internal static bool HideUseRay => MySession.Static?.LocalCharacter?.CurrentWeapon is MyAutomaticRifleGun && !(VoxelTools.Current is DigiTool) || ToolEquipped && (Supported || ToolContactActive);
        internal static bool ToolContactActive => weapon is MyEngineerToolBase tool && Multiplayer.ToolContact.Near(tool);
        internal static bool Reloading => valid && !profile.Tool && MySession.Static?.LocalCharacter?.CurrentWeapon?.IsReloading==true;
        public static void Reset()
        {
            support.Release(); reload.Release(); valid=wasSupported=wasReloading=false; weapon=null; profile=null; releaseBlend=0;
            Multiplayer.HeldItemPose.Local=null; Multiplayer.HeldItemPose.LocalToolRay=null; Multiplayer.HeldItemPose.LocalSupported=null; Multiplayer.HeldItemPose.LocalMirrored=null;
            hover=0; ItemGrabVisual.Reset(); Aiming=false;
        }
        public static void Update()
        {
            Multiplayer.HeldItemPose.Local=LocalPose;
            Multiplayer.HeldItemPose.LocalToolRay=LocalToolRay;
            Multiplayer.HeldItemPose.LocalSupported=c=>c==MySession.Static?.LocalCharacter ? Supported:(bool?)null;
            Multiplayer.HeldItemPose.LocalMirrored=c=>c==MySession.Static?.LocalCharacter ? Handedness.Swapped:(bool?)null;
            Multiplayer.HeldItemPose.Feedback=Haptic;
            var character=MySession.Static?.LocalCharacter;
            var current=character?.CurrentWeapon as MyEntity;
            var controls=Controls.Static;
            Controller actionHand=Handedness.ActionHand,supportHand=Handedness.SupportHand;
            bool down=ConsumesSupportGrip ? controls.SupportGripPressure.RawPosition.X>.1f:
                InputRouter.Flying ? controls.ThrustDown.Position.X>.5f:controls.Sprint.IsPressed;
            if(current!=weapon || profile!=null && profile.Mirrored!=Handedness.Swapped)
            {
                support.Release(); reload.Release(); valid=wasSupported=wasReloading=false; releaseBlend=0; weapon=current; Aiming=false;
                profile=WeaponProfile.Find(character?.CurrentWeapon?.PhysicalObject?.SubtypeName,current?.Model?.AssetName);
                if(Handedness.Swapped) profile=profile?.Mirror;
                hover=0; ItemGrabVisual.Reset();
            }
            bool available=Main.VrActive && InputRouter.TrackedItems && character!=null && MySession.Static.ControlledEntity==character &&
                !character.IsDead && !character.IsSitting && !character.IsOnLadder && profile!=null && actionHand.pose.isTracked && Player.Headset.pose.isTracked &&
                (!profile.Mirrored || TrackedArms.TryPalmMirror(character,out palmMirror));
            if(!available || !TrackedArms.TryFreePalm(character,actionHand,out var actionPalm) || character.HandItemDefinition==null ||
                !WeaponPose.TryHandItem(Hold(character,false),actionPalm,out var oneWorld))
            { support.Update(false,down,false,false); reload.Update(false,down,false,false); valid=wasSupported=false; hover=0; ItemGrabVisual.Reset(); UpdateAim(character,false); return; }
            oneWorld=Alignment.Apply(Alignment.ToolKey(character),oneWorld);
            var parent=character.WorldMatrix; var inverse=character.PositionComp.WorldMatrixNormalizedInv;
            var one=(Matrix)(oneWorld*inverse); var primary=Vector3.Transform(profile.Primary,one);
            bool offTracked=TrackedArms.TryFreePalm(character,supportHand,out var supportPalm);
            var off=offTracked ? (Vector3)Vector3D.Transform(supportPalm.Translation,inverse):Vector3.Zero;
            bool reachable=offTracked && WeaponPose.TrySupport(profile,one,primary,off,out _,support.Held);
            var local=offTracked ? (Vector3)Vector3D.Transform(Vector3D.Transform(profile.GrabOffset(palmMirror),supportPalm),MatrixD.Invert(valid && TryPose(character,out var displayed,out _) ? displayed:oneWorld)):Vector3.Zero;
            bool interaction=offTracked && InputRouter.Gameplay && !Main.MenuOpen && (support.Held ||
                !character.CurrentWeapon.IsReloading && !CockpitTouch.Owns(supportHand) && !HandInteraction.Owns(supportHand) &&
                !FloatingWindows.PointingFor(supportHand) && !TouchScreenBridge.PointingFor(supportHand) && !ArthurLcdBridge.PointingFor(supportHand) && !HelmetHud.Consumes(supportHand));
            var supportPoint=profile.SupportContact(character.HandItemDefinition.LeftHand);
            int candidate=interaction && !ConsumesSupportGrip ? profile.Grab(local,supportPoint,hover):0;
            if(candidate==1 && !reachable) candidate=0;
            bool near=candidate==1,mag=candidate==2;
            if(candidate!=hover && candidate!=0) CockpitFeedback.Hover(supportHand);
            hover=candidate;
            bool reloadCaptured=reload.Update(interaction && (!support.Consumed || !down),down,mag,!support.Consumed || !down);
            if(reloadCaptured)
            {
                CockpitFeedback.Activate(supportHand);
                if(character.CurrentWeapon.CanReload()) NativeActions.Pulse(MyControlsSpace.RELOAD);
            }
            if(support.Update(interaction && (!reload.Consumed || !down),down,near,(support.Held || reachable) && (!reload.Consumed || !down))) supportHand.Vibrate(0,.04f,110,.35f);
            if(wasSupported && !support.Held)
            {
                var previous=(Matrix)(worldPose*inverse);
                releaseRotation=Quaternion.CreateFromRotationMatrix(previous.GetOrientation()*Matrix.Transpose(one.GetOrientation())); releaseBlend=1;
            }
            if(!support.Held) releaseBlend=Math.Max(0,releaseBlend-.16f);
            worldPose=WeaponPose.Current(profile,oneWorld,parent,support.Held ? supportPalm:(MatrixD?)null,releaseRotation,releaseBlend);
            wasSupported=support.Held; valid=true;
            bool reloading=character.CurrentWeapon.IsReloading;
            if(reloading && !wasReloading) supportHand.Vibrate(0,.055f,95,.4f);
            if(!reloading && wasReloading) actionHand.Vibrate(0,.035f,140,.25f);
            wasReloading=reloading;
            ItemGrabVisual.Update(current,profile,supportPoint,hover,Supported);
            var eye=CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
            var muzzle=Vector3D.Transform(profile.Muzzle,worldPose);
            UpdateAim(character,!profile.Tool && InputRouter.Gameplay && !Main.MenuOpen &&
                Sighted(eye,muzzle,profile.Sight.HasValue ? Vector3D.Transform(profile.Sight.Value,worldPose):muzzle,
                    Vector3D.Normalize(Vector3D.TransformNormal(profile.Sight.HasValue ? profile.SightDirection:profile.Direction,worldPose)),Aiming));
        }
        // Vanilla narrows the spread only in ironsight mode or while crouching; in VR, looking down the sights is aiming.
        internal static bool Sighted(Vector3D eye,Vector3D muzzle,Vector3D sight,Vector3D direction,bool was)
        {
            double along=Vector3D.Dot(eye-muzzle,direction);
            if(along>-.12 || along<-1.1) return false;
            var offset=eye-sight;
            return (offset-direction*Vector3D.Dot(offset,direction)).Length()<(was ? .14:.09);
        }
        private static void UpdateAim(MyCharacter character,bool sighted)
        {
            bool changed=sighted!=Aiming; Aiming=sighted;
            if(character?.CurrentWeapon==null || !Main.VrActive) return;
            if(changed) { if(sighted) Handedness.ActionHand.Vibrate(0,.03f,160,.45f); else Handedness.ActionHand.Vibrate(0,.02f,120,.25f); }
            // Vanilla drops ironsight mode on its own while reloading and on view changes, so the mode is re-asserted, not just toggled.
            if(sighted ? character.ZoomMode!=MyZoomModeEnum.IronSight && !character.CurrentWeapon.IsReloading:character.ZoomMode==MyZoomModeEnum.IronSight)
                character.EnableIronsight(sighted,true,false);
        }
        private static MatrixD? LocalToolRay(MyCharacter character) => Patches.MotionToolPatch.Eligible(character) && character==MySession.Static?.LocalCharacter &&
            ToolEquipped && TrackedArms.TryFreePointPose(Handedness.ActionHand,out var ray) ? ray:(MatrixD?)null;
        private static MatrixD? LocalPose(MyCharacter character) => Patches.MotionToolPatch.Eligible(character) && TryPose(character,out var pose,out _) ? pose:(MatrixD?)null;
        public static bool TryPose(MyCharacter character,out MatrixD model,out Vector3D muzzle)
        {
            model=MatrixD.Identity; muzzle=Vector3D.Zero;
            if(!InputRouter.TrackedItems || character==null || character!=MySession.Static?.LocalCharacter || MySession.Static.ControlledEntity!=character ||
                character.CurrentWeapon!=weapon || character.CurrentWeapon==null || !Handedness.ActionHand.pose.isTracked) return false;
            if(profile!=null)
            {
                if(!valid || character.HandItemDefinition==null || !TrackedArms.TryFreePalm(character,Handedness.ActionHand,out var actionPalm) ||
                    !WeaponPose.TryHandItem(Hold(character,false),actionPalm,out var one)) return false;
                one=Alignment.Apply(Alignment.ToolKey(character),one);
                MatrixD? supportPalm=Supported && TrackedArms.TryFreePalm(character,Handedness.SupportHand,out var off) ? off:(MatrixD?)null;
                model=Multiplayer.HeldItemPose.Kick(WeaponPose.Current(profile,one,character.WorldMatrix,supportPalm,releaseRotation,releaseBlend),profile,Multiplayer.HeldItemPose.Recoil(character));
                muzzle=Vector3D.Transform(profile.Muzzle,model);
            }
            else
            {
                var mirror=Matrix.Identity;
                if(character.HandItemDefinition==null || !TrackedArms.TryFreePalm(character,Handedness.ActionHand,out var hand) ||
                    Handedness.Swapped && !TrackedArms.TryPalmMirror(character,out mirror) ||
                    !WeaponPose.TryHandItem(Handedness.Swapped ? WeaponProfile.MirrorHold(character.HandItemDefinition.RightHand,mirror):(MatrixD)character.HandItemDefinition.RightHand,hand,out model)) return false;
                model=Alignment.Apply(Alignment.ToolKey(character),model);
                muzzle=character.CurrentWeapon.GunBase?.GetMuzzleWorldPosition() ?? model.Translation;
            }
            return model.IsValid();
        }
        internal static bool TryPalm(MyCharacter character,Controller hand,out MatrixD palm)
        {
            palm=MatrixD.Identity;
            if(!valid || profile==null || hand==Handedness.SupportHand && !support.Held || !TryPose(character,out var model,out _)) return false;
            palm=Hold(character,hand==Handedness.SupportHand)*model;
            return true;
        }
        private static MatrixD Hold(MyCharacter character,bool supporting) =>
            profile.Hold(character.HandItemDefinition.RightHand,character.HandItemDefinition.LeftHand,supporting,palmMirror);
        private static double nextToolPulse;
        internal static void Haptic(MyCharacter owner,ItemKind kind)
        {
            if(owner!=MySession.Static?.LocalCharacter || !InputRouter.Gameplay || Main.MenuOpen || !Handedness.ActionHand.pose.isTracked) return;
            var now=Multiplayer.MultiplayerRuntime.Now;
            if(kind>=ItemKind.Welder && now<nextToolPulse) return;
            nextToolPulse=now+.08;
            float strength=kind==ItemKind.Welder ? .18f:kind==ItemKind.Pistol ? .35f:kind==ItemKind.Launcher ? .8f:.5f;
            Handedness.ActionHand.Vibrate(0,kind>=ItemKind.Welder ? .06f:.035f,kind==ItemKind.Welder ? 160:kind==ItemKind.Drill ? 65:110,strength);
            if(Supported) Handedness.SupportHand.Vibrate(0,.03f,100,strength*.45f);
        }
    }
}
