#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class HandednessTests
    {
        private static void Require(bool ok,string reason) { if(!ok) throw new Exception(reason); }
        private static readonly MatrixD Mirror=MatrixD.CreateScale(-1,1,1);
        private static bool Near(MatrixD a,MatrixD b,double tolerance=1e-4)
        {
            for(int r=0;r<4;r++) for(int c=0;c<4;c++) if(Math.Abs(a[r,c]-b[r,c])>tolerance) return false;
            return true;
        }
        private static MatrixD Pose(int i)
        {
            var pose=MatrixD.CreateFromYawPitchRoll(i*.37-1.1,i*.29-.8,i*.21-.6);
            pose.Translation=new Vector3D(.3+i*.01,1.1-i*.02,-.2+i*.015); return pose;
        }
        internal static void Run(Action<string> log)
        {
            var items=WeaponTests.HandItems();
            // The installed astronaut's bind pose mirrors the palm across local Z; also cover a general reflection.
            var reflections=new[] { Matrix.CreateScale(1,1,-1),
                Matrix.CreateFromYawPitchRoll(.4f,-.3f,.2f)*Matrix.CreateScale(-1,1,1)*Matrix.Invert(Matrix.CreateFromYawPitchRoll(-.7f,.1f,.5f)) };
            foreach(var right in WeaponTests.Installed(items))
            {
                var item=items[right.Item]; var left=right.Mirror;
                MatrixD nativeRight=WeaponTests.Palm(item,"Right"),nativeLeft=WeaponTests.Palm(item,"Left");
                Require(left.Mirrored && left.Mirror==left && right.Mirror==left && !right.Mirrored,"Mirrored profile identity broken for "+right.Item);
                Require(Near(right.Hold(nativeRight,nativeLeft,false,Matrix.Identity),right.Palm(nativeRight,false),0) &&
                    Near(right.Hold(nativeRight,nativeLeft,true,Matrix.Identity),right.Palm(nativeLeft,true),0),"Right-handed hold changed for "+right.Item);
                foreach(var palm in reflections)
                    for(int i=0;i<12;i++)
                    {
                        var rightPalm=Pose(i); var leftPalm=MatrixD.Invert(palm)*rightPalm*Mirror;
                        MatrixD grip=WeaponProfile.Reflect(right.GripAxis,right.FlipUp),brace=WeaponProfile.Reflect(right.SupportAxis,right.FlipUp);
                        bool held=WeaponPose.TryHandItem(right.Hold(nativeRight,nativeLeft,false,Matrix.Identity),rightPalm,out var rightModel);
                        Require(WeaponPose.TryHandItem(left.Hold(nativeRight,nativeLeft,false,palm),leftPalm,out var leftModel) && held,"Hold rejected for "+right.Item);
                        Require(Near(leftModel,MatrixD.Invert(left.Tilt)*grip*rightModel*Mirror) && Math.Abs(leftModel.Determinant()-1)<1e-4,"Left hold is not the mirrored right hold for "+right.Item);
                        var supportLeft=Pose(i+20); var supportRight=(MatrixD)palm*supportLeft*Mirror;
                        held=WeaponPose.TryHandItem(right.Hold(nativeRight,nativeLeft,true,Matrix.Identity),supportLeft,out var rightSupported);
                        held&=WeaponPose.TryHandItem(left.Hold(nativeRight,nativeLeft,true,palm),supportRight,out var leftSupported);
                        Require(held && Near(leftSupported,brace*rightSupported*Mirror),"Right support is not the mirrored left support for "+right.Item);
                        var grab=Vector3D.Transform(left.GrabOffset(palm),left.Hold(nativeRight,nativeLeft,true,palm));
                        Require(Vector3D.Distance(grab,left.SupportContact(nativeLeft))<1e-4,"Mirrored grab offset misses the support contact for "+right.Item);
                    }
                var support=right.SupportContact(nativeLeft);
                for(int i=0;i<40;i++)
                {
                    var point=new Vector3((i%5-2)*.05f,(i/5%4-2)*.04f,(i/20-1)*.12f)+right.Magazine*(i%3==0 ? 1:0);
                    var flipped=WeaponProfile.Flip(point,right.SupportAxis,right.FlipUp);
                    Require(left.Grab(flipped,left.SupportContact(nativeLeft))==right.Grab(point,support) && left.ReloadAt(flipped)==right.ReloadAt(point),"Mirrored grab or reload target differs for "+right.Item);
                }
                Require(left.Muzzle==right.Muzzle && left.Magazine==right.Magazine,"Mirrored profile moved a mesh point for "+right.Item);
                if(right.GripAxis!=right.SupportAxis || right.FlipUp) continue;
                var one=(Matrix)Pose(3); var primary=Vector3.Transform(right.Primary,one);
                var reach=Vector3.Transform(right.Support,one)+new Vector3(.01f,-.02f,.015f);
                var oneLeft=(Matrix)(WeaponProfile.Reflect(right.GripAxis)*(MatrixD)one*Mirror);
                bool supported=WeaponPose.TrySupport(right,one,primary,reach,out var twoRight,true);
                supported&=WeaponPose.TrySupport(left,oneLeft,Vector3.Transform(left.Primary,oneLeft),new Vector3(-reach.X,reach.Y,reach.Z),out var twoLeft,true);
                Require(supported && Near(twoLeft,WeaponProfile.Reflect(right.GripAxis)*(MatrixD)twoRight*Mirror),"Two-hand support is not mirror symmetric for "+right.Item);
            }
            var rifle=WeaponProfile.All[0]; var aim=Pose(5); var recoil=new Vector2(6,-3);
            Require(Near(HeldItemPose.Kick(WeaponProfile.Reflect(0)*aim*Mirror,rifle.Mirror,recoil),WeaponProfile.Reflect(0)*HeldItemPose.Kick(aim,rifle,recoil)*Mirror),
                "Left-handed recoil is not the mirrored right-handed kick");
            var pistol=WeaponProfile.All.First(p=>p.Kind==ItemKind.Pistol); var native=Quaternion.CreateFromAxisAngle(Vector3.Up,.7f);
            Require(pistol.FingerRotation("SE_RigR_Index_2",.5f,false,native)!=pistol.Mirror.FingerRotation("SE_RigL_Index_2",.5f,false,native),
                "Left pistol hand used the right-hand native grip pose");
            var bones=ArmTests.InstalledBones();
            Func<string,VRageRender.Animations.MyCharacterBone> find=name=>bones.FirstOrDefault(b=>b.Name==name);
            var leftArm=ArmSkeleton.Find(find,"SE_RigLUpperarm","SE_RigLForearm1","SE_RigLPalm",-1);
            var rightArm=ArmSkeleton.Find(find,"SE_RigRUpperarm","SE_RigRForearm1","SE_RigRPalm",1);
            leftArm.Collar.Bone.Rotation=Quaternion.CreateFromYawPitchRoll(.1f,-.2f,.05f);
            rightArm.Collar.Bone.Rotation=Quaternion.CreateFromYawPitchRoll(-.3f,.25f,.15f); rightArm.Collar.Bone.Translation=new Vector3(.01f,.02f,-.015f);
            bones[0].ComputeAbsoluteTransform(true,true);
            MatrixD vanillaLeft=leftArm.Collar.Bone.AbsoluteTransform,vanillaRight=rightArm.Collar.Bone.AbsoluteTransform;
            MatrixD MirrorOf(VRageRender.Animations.MyCharacterBone from,VRageRender.Animations.MyCharacterBone to,MatrixD frame) =>
                MatrixD.Invert(to.GetAbsoluteRigTransform().GetOrientation()*Matrix.CreateScale(-1,1,1)*Matrix.Invert(from.GetAbsoluteRigTransform().GetOrientation()))*frame*Mirror;
            ArmSkeleton.MirrorCollars(leftArm,rightArm);
            Require(Near(leftArm.Collar.Bone.AbsoluteTransform,MirrorOf(rightArm.Collar.Bone,leftArm.Collar.Bone,vanillaRight)) &&
                Near(rightArm.Collar.Bone.AbsoluteTransform,MirrorOf(leftArm.Collar.Bone,rightArm.Collar.Bone,vanillaLeft)),"Left-handed collarbones are not the mirrored vanilla pose");
            leftArm.Restore(); rightArm.Restore();
            Require(Near(leftArm.Collar.Bone.AbsoluteTransform,vanillaLeft,1e-6) && Near(rightArm.Collar.Bone.AbsoluteTransform,vanillaRight,1e-6),"Collarbone restore lost the vanilla pose");
            log("PASS left-handed holds: mirrored grip, support, grab, reload and two-hand pose match the right-handed profiles.");

            var pull=new Analog(0,.55f); var click=new Button(0);
            foreach(var (x,down) in new[] { (0f,false),(.5f,false),(.6f,true),(.5f,true),(.46f,true),(.44f,false),(.54f,false),(.9f,true) })
            {
                pull.AcceptSample(new InputAnalogActionData_t { bActive=true,x=x }); click.UpdateFrom(pull,.55f,.45f);
                Require(click.RawPressed==down,"Derived grip click hysteresis wrong at "+x);
            }
            pull.AcceptSample(default(InputAnalogActionData_t)); click.UpdateFrom(pull,.55f,.45f);
            Require(!click.Active,"Derived grip click stayed active without its pull");
            var source=new Analog(0); var role=new Analog(0);
            source.AcceptSample(new InputAnalogActionData_t { bActive=true,x=.8f });
            role.Mirror(false,source); Require(!role.Active && role.Position.X==0,"Mirrored role ignored its inactive action set");
            source.AcceptSample(new InputAnalogActionData_t { bActive=true }); role.Mirror(true,source);
            source.AcceptSample(new InputAnalogActionData_t { bActive=true,x=.8f }); role.Mirror(true,source);
            Require(role.Position.X>.7f,"Mirrored role lost its source pull");
            var sourceButton=new Button(0); var roleButton=new Button(0);
            sourceButton.AcceptSample(new InputDigitalActionData_t { bActive=true,bState=true });
            roleButton.Mirror(false,sourceButton); Require(!roleButton.Active,"Mirrored button ignored its inactive action set");
            sourceButton.AcceptSample(new InputDigitalActionData_t { bActive=true }); roleButton.Mirror(true,sourceButton);
            sourceButton.AcceptSample(new InputDigitalActionData_t { bActive=true,bState=true }); roleButton.Mirror(true,sourceButton);
            Require(roleButton.IsPressed,"Mirrored button lost its source state");
            log("PASS left-handed input sources: derived grip click hysteresis and mirrored roles keep their own action set activity.");

            foreach(bool swapped in new[] { false,true }) foreach(bool right in new[] { false,true })
            {
                var c=LcdInputTests.ControlsForTest(); c.Swapped=swapped;
                SetRoles(c,false); SetRoles(c,true);
                var input=new LcdInput();
                input.Update(new LcdInput.Sample(true,.8f,true,.9f,true,true),true,false,DateTime.UtcNow);
                Require(input.ReserveTrigger && input.ReserveGrip,"LCD reservation fixture failed");
                input.Consume(c,right);
                SetRoles(c,true);
                bool support=right==swapped;
                string context=" (swapped="+swapped+", right="+right+")";
                Require((c.ThrustUp.Position.X==0)==support,"ThrustUp blocking followed the wrong trigger"+context);
                Require((c.ThrustDown.Position.X==0)==support && !c.Sprint.IsPressed==support,"Descent/sprint blocking followed the wrong grip"+context);
                Require(!c.ThrustRoll.IsPressed==!support,"Roll blocking followed the wrong grip"+context);
                Require(!(right ? c.Secondary:c.LeftGripClick).IsPressed,"Consumed grip click escaped"+context);
            }
            log("PASS left-handed LCD ownership: trigger and grip aliases are blocked on the hand that carries them in both layouts.");

            var pose=new PlayerPose { Character=7,Left=Matrix.Identity,Right=Matrix.Identity,Tracked=3|PlayerPose.ItemSupported|PlayerPose.ItemMirrored };
            var decoded=PlayerPose.Decode(pose.Encode());
            Require(decoded!=null && (decoded.Tracked&PlayerPose.ItemMirrored)!=0 && (decoded.Tracked&PlayerPose.ItemSupported)!=0,"Left-handed item flag lost in transit");
            pose.Tracked=3; Require((PlayerPose.Decode(pose.Encode()).Tracked&PlayerPose.ItemMirrored)==0,"Right-handed pose decoded as left-handed");
            log("PASS left-handed pose flag: round trip and right-handed default.");
        }
        private static void SetRoles(LcdInput.Bindings c,bool held)
        {
            var down=new InputDigitalActionData_t { bActive=true,bState=held };
            var pulled=new InputAnalogActionData_t { bActive=true,x=held ? 1:0 };
            foreach(var button in new[] { c.Primary,c.LeftClick,c.Secondary,c.LeftGripClick,c.ThrustRoll,c.Sprint,c.Interact,c.JumpOrClimbUp }) button.AcceptSample(down);
            foreach(var analog in new[] { c.PointerPressure,c.LeftTriggerPressure,c.RightGripPressure,c.LeftGripPressure,c.ThrustUp,c.ThrustDown,c.ThrustForward }) analog.AcceptSample(pulled);
        }
    }
}
#endif
