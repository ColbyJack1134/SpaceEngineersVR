#if SEVR_DIAGNOSTICS
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SpaceEngineersVR.Player;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Diagnostics
{
    // Poses the installed astronaut arms with production hold, palm and IK code and exports bone and item matrices for offline rendering.
    public static class HoldPreview
    {
        private static readonly string[] Items={"AutomaticRifleItem","SemiAutoPistolItem","BasicHandHeldLauncherItem","WelderItem","AngleGrinderItem","HandDrillItem"};
        public static void Export(string game,string output,Action<string> log)
        {
            // Controller frames of the left and right Touch render models are mirror images across their local X.
            Matrix controllerMirror=Matrix.CreateScale(-1,1,1),worldMirror=Matrix.CreateScale(-1,1,1);
            Directory.CreateDirectory(output); UiTests.Initialize(game,Path.Combine(output,"data"));
            var items=WeaponTests.HandItems();
            int written=0;
            // Left-handed poses use the moved-side-part copies, rendered from <model>-left exports.
            var hook=WeaponProfile.SideSwapped; WeaponProfile.SideSwapped=p=>true;
            foreach(string id in Items.Concat(items.ContainsKey("PhysicalPaintGun") ? new[] {"PhysicalPaintGun"}:new string[0]))
            {
                var right=WeaponProfile.All.First(p=>p.Item==id);
                Matrix nativeRight=WeaponTests.Palm(items[id],"Right"),nativeLeft=WeaponTests.Palm(items[id],"Left");
                var aim=Quaternion.CreateFromRotationMatrix(ArmMath.AimBone(Matrix.Identity,right.Direction,Vector3.Forward));
                var rightModel=Matrix.CreateFromQuaternion(aim);
                var rightPalm=(Matrix)right.Hold(nativeRight,nativeLeft,false,Matrix.Identity)*rightModel;
                rightModel.Translation+=new Vector3(.2f,1.22f,-.38f)-rightPalm.Translation;
                if(right.Tool && !right.OneHanded) { Vanilla(output,right,items[id]); written++; }
                foreach(bool left in new[] {false,true}) foreach(bool two in new[] {false,true})
                {
                    if(two && right.OneHanded) continue;
                    var bones=ArmTests.InstalledBones();
                    Func<string,MyCharacterBone> find=name=>bones.FirstOrDefault(b=>b.Name==name);
                    var leftArm=ArmSkeleton.Find(find,"SE_RigLUpperarm","SE_RigLForearm1","SE_RigLPalm",-1);
                    var rightArm=ArmSkeleton.Find(find,"SE_RigRUpperarm","SE_RigRForearm1","SE_RigRPalm",1);
                    var palmMirror=ArmSkeleton.PalmMirror(rightArm,leftArm);
                    var profile=left ? right.Mirror:right;
                    Matrix model=rightModel,primaryPalm=(Matrix)right.Hold(nativeRight,nativeLeft,false,Matrix.Identity)*rightModel;
                    if(left)
                    {
                        var grip=Matrix.Invert(CockpitHandPose.GripWrist(Matrix.Identity))*Matrix.Invert(rightArm.PalmOffset)*primaryPalm;
                        primaryPalm=leftArm.PalmOffset*CockpitHandPose.GripWrist(controllerMirror*grip*worldMirror);
                        model=(Matrix)Matrix.Invert((Matrix)profile.Hold(nativeRight,nativeLeft,false,palmMirror))*primaryPalm;
                    }
                    var holding=left ? leftArm:rightArm; var supporting=left ? rightArm:leftArm;
                    ArmSkeleton.Apply(holding,Matrix.Invert(holding.PalmOffset)*primaryPalm,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Stick,0,null,profile,false);
                    if(two)
                    {
                        var supportPalm=(Matrix)profile.Hold(nativeRight,nativeLeft,true,palmMirror)*model;
                        ArmSkeleton.Apply(supporting,Matrix.Invert(supporting.PalmOffset)*supportPalm,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Stick,0,null,profile,true);
                    }
                    bones[0].ComputeAbsoluteTransform(true,true);
                    Write(output,profile,(left ? "left":"right")+"-"+(two ? "two":"one"),model,bones,two,left);
                    written++;
                }
            }
            WeaponProfile.SideSwapped=hook;
            log("hold preview: "+written+" poses written to "+output);
        }
        // Our arm solver stands in for CalculateHandIK, so elbows are approximate.
        private static void Vanilla(string output,WeaponProfile profile,XElement item)
        {
            var bones=ArmTests.InstalledBones();
            Func<string,MyCharacterBone> find=name=>bones.FirstOrDefault(b=>b.Name==name);
            string clip=profile.Kind==ItemKind.Welder ? "welder_first_aim_straight":profile.Kind==ItemKind.Drill ? "drill_first_aim_straight":"grinder_aim_straight";
            WeaponTests.Animate(bones,clip,0);
            WeaponTests.Animate(bones,"holding_tool",0,name=>name.StartsWith("SE_RigR_") || name.StartsWith("SE_RigL_"));
            Matrix right=WeaponTests.Palm(item,"Right"),left=WeaponTests.Palm(item,"Left"),model;
            var head=find("HeadDummy").AbsoluteTransform;
            if((string)item.Element("ItemPositioning")=="TransformFromData")
            {
                var location=Matrix.CreateFromQuaternion(Quaternion.Normalize(Quat(item.Element("ItemOrientation")))); location.Translation=Vec(item.Element("ItemPosition"));
                model=location*Matrix.CreateTranslation(head.Translation);
            }
            else model=Matrix.Invert(right)*find("SE_RigR_Weapon_pin").AbsoluteTransform;
            foreach(bool side in new[] {false,true})
            {
                if(side && (string)item.Element("SimulateLeftHand")=="false") continue;
                var arm=side ? ArmSkeleton.Find(find,"SE_RigLUpperarm","SE_RigLForearm1","SE_RigLPalm",-1):ArmSkeleton.Find(find,"SE_RigRUpperarm","SE_RigRForearm1","SE_RigRPalm",1);
                ArmSkeleton.Apply(arm,Matrix.Invert(arm.PalmOffset)*(side ? left:right)*model,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Native,0);
            }
            bones[0].ComputeAbsoluteTransform(true,true);
            Write(output,profile,"vanilla-first",model,bones,true,false,head.Translation);
        }
        private static Quaternion Quat(XElement e) => new Quaternion((float)e.Element("X"),(float)e.Element("Y"),(float)e.Element("Z"),(float)e.Element("W"));
        private static Vector3 Vec(XElement e) => new Vector3((float)e.Element("X"),(float)e.Element("Y"),(float)e.Element("Z"));
        private static void Write(string output,WeaponProfile profile,string name,Matrix model,MyCharacterBone[] bones,bool two,bool left=false,Vector3? eye=null)
        {
            string model3d=Path.GetFileNameWithoutExtension(profile.Model)+(profile.Mirrored && profile.SideAxis.HasValue ? "-left":"");
            var json=new StringBuilder("{\"item\":\""+model3d+"\",\"model\":"+Values(model)+(eye.HasValue ? ",\"eye\":["+eye.Value.X.ToString("R",CultureInfo.InvariantCulture)+","+eye.Value.Y.ToString("R",CultureInfo.InvariantCulture)+","+eye.Value.Z.ToString("R",CultureInfo.InvariantCulture)+"]":"")+",\"roots\":[\""+(left ? "SE_RigLUpperarm":"SE_RigRUpperarm")+"\""+
                (two ? ",\""+(left ? "SE_RigRUpperarm":"SE_RigLUpperarm")+"\"":"")+"],\"absolute\":{");
            json.Append(string.Join(",",bones.Select(b=>"\""+b.Name+"\":"+Values(b.AbsoluteTransform))));
            json.Append("}}");
            File.WriteAllText(Path.Combine(output,model3d+"-"+name+".json"),json.ToString());
        }
        private static string Values(Matrix m) => "["+string.Join(",",Enumerable.Range(0,16).Select(i=>m[i/4,i%4].ToString("R",CultureInfo.InvariantCulture)))+"]";
    }
}
#endif
