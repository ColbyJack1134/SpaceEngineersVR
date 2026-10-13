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
            var items=XDocument.Load(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","HandItems.sbc")).Descendants("HandItem")
                .Where(x=>x.Element("PhysicalItemId")!=null).ToDictionary(x=>(string)x.Element("PhysicalItemId").Attribute("Subtype"));
            int written=0;
            foreach(string id in Items)
            {
                var right=WeaponProfile.All.First(p=>p.Item==id);
                Matrix nativeRight=WeaponTests.Palm(items[id],"Right"),nativeLeft=WeaponTests.Palm(items[id],"Left");
                var aim=Quaternion.CreateFromRotationMatrix(ArmMath.AimBone(Matrix.Identity,right.Direction,Vector3.Forward));
                var rightModel=Matrix.CreateFromQuaternion(aim);
                var rightPalm=(Matrix)right.Hold(nativeRight,nativeLeft,false,Matrix.Identity)*rightModel;
                rightModel.Translation+=new Vector3(.2f,1.22f,-.38f)-rightPalm.Translation;
                foreach(bool left in new[] {false,true}) foreach(bool two in new[] {false,true})
                {
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
                    ArmSkeleton.Apply(holding,Matrix.Invert(holding.PalmOffset)*primaryPalm,true,0,1,ArmSkeleton.Fingers.Stick,0,null,profile,false);
                    if(two)
                    {
                        var supportPalm=(Matrix)profile.Hold(nativeRight,nativeLeft,true,palmMirror)*model;
                        ArmSkeleton.Apply(supporting,Matrix.Invert(supporting.PalmOffset)*supportPalm,true,0,1,ArmSkeleton.Fingers.Stick,0,null,profile,true);
                    }
                    bones[0].ComputeAbsoluteTransform(true,true);
                    string model3d=profile.Model.Substring("Models/Weapons/".Length).Replace(".mwm","");
                    var json=new StringBuilder("{\"item\":\""+model3d+"\",\"model\":"+Values(model)+",\"roots\":[\""+(left ? "SE_RigLUpperarm":"SE_RigRUpperarm")+"\""+
                        (two ? ",\""+(left ? "SE_RigRUpperarm":"SE_RigLUpperarm")+"\"":"")+"],\"absolute\":{");
                    json.Append(string.Join(",",bones.Select(b=>"\""+b.Name+"\":"+Values(b.AbsoluteTransform))));
                    json.Append("}}");
                    File.WriteAllText(Path.Combine(output,model3d+"-"+(left ? "left":"right")+"-"+(two ? "two":"one")+".json"),json.ToString());
                    written++;
                }
            }
            log("hold preview: "+written+" poses written to "+output);
        }
        private static string Values(Matrix m) => "["+string.Join(",",Enumerable.Range(0,16).Select(i=>m[i/4,i%4].ToString("R",CultureInfo.InvariantCulture)))+"]";
    }
}
#endif
