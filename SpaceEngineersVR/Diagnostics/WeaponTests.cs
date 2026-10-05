using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using VRageRender.Animations;
using VRageRender.Import;
using HarmonyLib;
using System.Xml.Linq;
using System.Runtime.Serialization.Json;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Multiplayer;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class WeaponTests
    {
        private static void Require(bool ok,string reason) { if(!ok) throw new Exception(reason); }
        internal static Matrix Palm(XElement item,string side)
        {
            var q=item.Element(side+"HandOrientation"); var p=item.Element(side+"HandPosition");
            var matrix=Matrix.CreateFromQuaternion(Quaternion.Normalize(new Quaternion((float)q.Element("X"),(float)q.Element("Y"),(float)q.Element("Z"),(float)q.Element("W"))));
            matrix.Translation=new Vector3((float)p.Element("X"),(float)p.Element("Y"),(float)p.Element("Z")); return matrix;
        }
        internal static void Run(Action<string> log)
        {
            var items=XDocument.Load(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","HandItems.sbc")).Descendants("HandItem").Where(x=>x.Element("PhysicalItemId")!=null).ToDictionary(x=>(string)x.Element("PhysicalItemId").Attribute("Subtype"));
            foreach(var p in WeaponProfile.All)
            {
                Require(WeaponProfile.Find(p.Item,p.Model)==p && WeaponProfile.Find(p.Item,"modded.mwm")==null,"Model matching bypassed");
                var hand=p.Palm(Palm(items[p.Item],"Right"),false);
                Require(Vector3D.Distance(hand.Translation,p.Primary)<.00001,"Primary grip differs from native corrected palm: "+p.Item);
                for(int i=0;i<20;i++)
                {
                    var world=MatrixD.CreateFromYawPitchRoll(i*.3,i*.17,-i*.12); world.Translation=new Vector3D(2e6,-3e6,4e6);
                    Require(WeaponPose.TryHandItem(hand,hand*world,out var restored) && Vector3D.Distance(restored.Translation,world.Translation)<.00001,"World-space palm inversion lost precision");
                    var current=WeaponPose.Current(p,restored,world,null,Quaternion.Identity,0);
                    Require(Vector3D.Distance(current.Translation,world.Translation)<1e-6 && Vector3D.Dot(current.Forward,world.Forward)>.99999,"Current one-hand pose used a stale world frame");
                    var moved=world; moved.Translation+=new Vector3D(.7,-.3,.4);
                    var live=WeaponPose.Current(p,moved,moved,null,Quaternion.Identity,0);
                    Require(Vector3D.Distance(live.Translation,moved.Translation)<1e-6,"Updated tracking or anchor did not move held item immediately");
                    var supporting=MatrixD.CreateTranslation(p.Support)*moved;
                    var braced=WeaponPose.Current(p,moved,moved,supporting,Quaternion.Identity,0);
                    Require(Vector3D.Distance(Vector3D.Transform(p.Primary,braced),Vector3D.Transform(p.Primary,moved))<1e-5,"Current support pose moved the tracked primary palm");
                    var work=HeldItemPose.Working(world,p);
                    Require(Vector3D.Distance(work.Translation,Vector3D.Transform(p.Muzzle,world))<1e-6 && Vector3D.Dot(work.Forward,Vector3D.TransformNormal(p.Direction,world))>.99999,"Working end transform diverged");
                    var one=(Matrix)world.GetOrientation(); var primary=Vector3.Transform(p.Primary,one);
                    Require(WeaponPose.TrySupport(p,one,primary,Vector3.Transform(p.Support,one),out var two) && Vector3.Distance(Vector3.Transform(p.Primary,two),primary)<.00001f,"Support moved the firing palm");
                    Require(!WeaponPose.TrySupport(p,one,primary,primary,out _) && !WeaponPose.TrySupport(p,one,primary,primary+Vector3.One,out _),"Impossible support accepted");
                    Require(WeaponPose.TrySupport(p,one,primary,primary+Vector3.One,out var retained,true) &&
                        Vector3.Distance(Vector3.Transform(p.Primary,retained),primary)<.00001f,"Held support released outside acquisition reach");
                    Require(WeaponPose.TrySupport(p,one,primary,primary,out retained,true) && retained.IsValid(),"Coincident held controllers invalidated grip");
                }
                var contact=p.SupportContact(Palm(items[p.Item],"Left"));
                Require(p.Grab(contact,contact)==1,"Support contact does not acquire support: "+p.Item);
                var geometry=ItemGrabVisual.Geometry(p.Model,contact);
                Require(geometry.Indices.Count>0 && geometry.Sections.Count>0,"No highlight geometry at support: "+p.Item);
                if(p.Tool)
                {
                    var volume=ToolVolume.For(p,MatrixD.Identity);
                    Require(volume.Contains(volume.Start) && volume.Contains(volume.End),"Contact volume lost its working end");
                    Require(!volume.Contains(volume.Start+Vector3D.One*3),"Contact volume includes distant target");
                    for(int n=0;n<7;n++) Require(volume.Probe(n).Length>0,"Degenerate volume probe");
                }
                Require(!p.ReloadAt(p.Support),"Support overlaps reload region: "+p.Item);
                if(p.Kind==ItemKind.Pistol) Require(p.ReloadAt(p.Magazine+Vector3.Down*.035f) && !p.ReloadAt(p.Magazine+Vector3.Left*.09f) && !p.ReloadAt(p.Magazine+Vector3.Up*.02f),"Pistol reload did not require underside contact");
            }
            foreach(var type in new[] {typeof(HeldItemPose),typeof(ToolContact),typeof(DrillContact),typeof(ToolProjectionPatch),typeof(HeldDrillPosePatch),typeof(ToolFeedbackPatch)})
                foreach(var field in type.GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static))
                    if(field.FieldType==typeof(System.Reflection.FieldInfo) || field.FieldType==typeof(System.Reflection.PropertyInfo))
                        Require(field.GetValue(null)!=null,"Installed engine member missing: "+type.Name+"."+field.Name);
            var capture=new GripCapture();
            capture.Update(true,false,true,true); Require(capture.Update(true,true,true,true),"Fresh support squeeze lost");
            Require(!capture.Update(true,true,false,true) && capture.Held,"Support changed owner while held");
            capture.Update(false,true,false,false); Require(capture.Consumed && !capture.Held,"Lost tracking released ownership before physical release");
            Require(!capture.Update(true,true,true,true),"Tracking recovery reacquired held squeeze");
            capture.Update(true,false,true,true); Require(capture.Update(true,true,true,true),"Fresh squeeze failed after recovery");
            var motion=new ReloadAnimation.Motion();
            var dock=MatrixD.CreateFromYawPitchRoll(.3,.2,.1); dock.Translation=new Vector3D(2e6,-3e6,4e6);
            var offhand=MatrixD.CreateFromYawPitchRoll(-.4,.2,-.3); offhand.Translation=dock.Translation+new Vector3D(-.3,-.2,.1);
            Require(motion.Pose(false,0,dock,offhand)==dock,"Idle magazine left its socket");
            motion.Pose(true,1,dock,offhand);
            Require(Vector3D.Distance(motion.Pose(true,1.06,dock,offhand).Translation,dock.Translation+dock.Down*.06)<1e-6,"Magazine ejection missed socket axis");
            Require(motion.Pose(true,1.3,dock,offhand)==offhand,"Detached magazine missed tracked hand");
            Require(motion.Pose(false,2,dock,offhand).Translation==offhand.Translation,"Insertion jumped from offhand");
            var inserted=motion.Pose(false,2.08,dock,offhand);
            Require(Vector3D.Distance(inserted.Translation,Vector3D.Lerp(offhand.Translation,dock.Translation,.5))<1e-5,"Magazine insertion lost precision");
            Require(Vector3D.Distance(motion.Pose(false,2.2,dock,offhand).Translation,dock.Translation)<1e-6,"Inserted magazine remained detached");
            var label=WeaponAmmo.Label(WeaponProfile.Rifle,MatrixD.Identity,18,20);
            var head=MatrixD.CreateWorld(new Vector3D(-.4,.1,0),Vector3D.Right,Vector3D.Up);
            Require(WeaponAmmo.Inspecting(MatrixD.Identity,head,label.Pose),"Side inspection label hidden");
            head=MatrixD.CreateWorld(new Vector3D(0,.05,.4),Vector3D.Forward,Vector3D.Up);
            Require(!WeaponAmmo.Inspecting(MatrixD.Identity,head,label.Pose),"Label obscures aiming posture");
            log("PASS held items: 22 installed attachment matches, 440 large-coordinate working-frame/support cases, impossible support rejection, pistol underside reload and release ownership, ammo visibility.");
        }
        internal static void Animate(MyCharacterBone[] bones,string name,double fraction)
        {
            var importer=new MyModelImporter();
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Models/Characters/Animations/"+name+".mwm"))))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {reader,new[] {"Animations"}});
            var clip=((ModelAnimations)importer.GetTagData()["Animations"]).Clips[0];
            foreach(var track in clip.Bones)
            {
                var bone=bones.FirstOrDefault(value=>value.Name==track.Name); if(bone==null || track.Keyframes.Length==0) continue;
                double time=clip.Duration*fraction;
                var a=track.Keyframes.LastOrDefault(k=>k.Time<=time); var b=track.Keyframes.FirstOrDefault(k=>k.Time>=time);
                float amount=b.Time<=a.Time ? 0:(float)((time-a.Time)/(b.Time-a.Time));
                var rotation=Quaternion.Slerp(a.Rotation,b.Rotation,amount); var translation=Vector3.Lerp(a.Translation,b.Translation,amount);
                bone.SetCompleteTransform(ref translation,ref rotation);
            }
            foreach(var bone in bones) bone.ComputeAbsoluteTransform();
        }
        public static void Export(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output); UiTests.Initialize(game,Path.Combine(output,"data")); Run(log);
            var items=XDocument.Load(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","HandItems.sbc")).Descendants("HandItem").Where(x=>x.Element("PhysicalItemId")!=null).ToDictionary(x=>(string)x.Element("PhysicalItemId").Attribute("Subtype"));
            foreach(var item in new[] {"SemiAutoPistolItem","WelderItem","HandDrillItem"}) foreach(bool rigid in new[] {true,false})
            {
                var profile=WeaponProfile.All.Single(p=>p.Item==item); var bones=ArmTests.InstalledBones();
                var arm=ArmSkeleton.Find(name=>bones.FirstOrDefault(b=>b.Name==name),"SE_RigLUpperarm","SE_RigLForearm1","SE_RigLPalm",-1);
                var model=Matrix.CreateTranslation(0,1.25f,-.45f);
                var palm=(Matrix)profile.Palm(Palm(items[item],"Left"),true)*model;
                var wrist=Matrix.Invert(arm.PalmOffset)*palm;
                Require(ArmSkeleton.Apply(arm,wrist,true,rigid,1,ArmSkeleton.Fingers.Stick,1,null,profile,true),"Support arm fixture failed");
                Require(Vector3.Distance(arm.Palm.Bone.AbsoluteTransform.Translation,palm.Translation)<.001f,"Articulated support left its anchor");
                WritePose(bones,Matrix.Invert(model),Path.Combine(output,item+"-support-"+(rigid ? "rigid":"flex")+".json"));
            }
            foreach(var profile in WeaponProfile.All) foreach(bool left in new[] {false,true}) foreach(float pressure in profile.Tool && !left ? new[] {-1f,0f,1f}:new[] {0f,1f})
            {
                var bones=ArmTests.InstalledBones(); string side=left ? "L":"R";
                if(profile.Kind==ItemKind.Pistol && !left) Animate(bones,"pistol_pose_fp_forward",0);
                var palm=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var frame=(Matrix)profile.Palm(Palm(items[profile.Item],left ? "Left":"Right"),left);
                palm.SetCompleteTransformFromAbsoluteMatrix(ref frame,false);
                foreach(var bone in bones.Where(b=>b.Name.StartsWith("SE_Rig"+side+"_"))) bone.Rotation=pressure<0 ? CockpitHandPose.Rotation(bone.Name,false):profile.FingerRotation(bone.Name,pressure,left,bone.Rotation);
                palm.ComputeAbsoluteTransform(true,true);
                WritePose(bones,Matrix.Identity,Path.Combine(output,profile.Item+"-"+side+"-"+pressure+".json"));
            }
            foreach(var profile in WeaponProfile.All)
            {
                var contact=profile.SupportContact(Palm(items[profile.Item],"Left"));
                var data=ItemGrabVisual.Geometry(profile.Model,contact);
                var values=new Dictionary<string,double[]> {
                    ["positions"]=data.Positions.SelectMany(v=>new[] {(double)v.X,v.Y,v.Z}).ToArray(),
                    ["indices"]=data.Indices.Select(v=>(double)v).ToArray(),
                    ["contact"]=new[] {(double)contact.X,contact.Y,contact.Z}};
                if(profile.Tool)
                {
                    var v=ToolVolume.For(profile,MatrixD.Identity);
                    values["volume"]=new[] {v.Start.X,v.Start.Y,v.Start.Z,v.End.X,v.End.Y,v.End.Z,v.Radius,v.HalfHeight,v.Disc ? 1d:0d};
                }
                using(var file=File.Create(Path.Combine(output,profile.Item+"-grab.json")))
                    new DataContractJsonSerializer(values.GetType(),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,values);
            }
            foreach(var family in new[] {new[] {"SemiAutoPistolItem","pistol"},new[] {"AutomaticRifleItem","rifle"},new[] {"BasicHandHeldLauncherItem","rocketlauncher"}})
            {
                var profile=WeaponProfile.All.Single(p=>p.Item==family[0]);
                var importer=new MyModelImporter();
                using(var reader=new BinaryReader(File.OpenRead(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,profile.Model))))
                    AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {reader,new[] {"Dummies"}});
                var socket=((Dictionary<string,MyModelDummy>)importer.GetTagData()["Dummies"])["subpart_magazine"];
                var motion=new ReloadAnimation.Motion();
                var times=new[] {0,.1,.16,.3,.6,.8,.88,1.0};
                for(int sample=0;sample<times.Length;sample++)
                {
                    var bones=ArmTests.InstalledBones(); Animate(bones,family[1]+"_fp_reload_forward",Math.Min(.95,sample*.14));
                    var left=bones.Single(b=>b.Name=="SE_RigLPalm"); var right=bones.Single(b=>b.Name=="SE_RigRPalm");
                    var grip=profile.Palm(Palm(items[profile.Item],"Right"),false);
                    var dummy=bones.Single(b=>b.Name=="MagazineDummy_01");
                    var relative=(MatrixD)dummy.AbsoluteTransform*MatrixD.Invert(left.AbsoluteTransform);
                    var leftPose=(Matrix)profile.Palm(Palm(items[profile.Item],"Left"),true); leftPose.Translation+=new Vector3(-.25f,-.2f,.1f);
                    var rightPose=(Matrix)grip;
                    left.SetCompleteTransformFromAbsoluteMatrix(ref leftPose,false); left.ComputeAbsoluteTransform(true,true);
                    right.SetCompleteTransformFromAbsoluteMatrix(ref rightPose,false); right.ComputeAbsoluteTransform(true,true);
                    var mag=(Matrix)motion.Pose(sample>=1 && sample<=4,times[sample],Matrix.Normalize(socket.Matrix),relative*leftPose);
                    dummy.SetCompleteTransformFromAbsoluteMatrix(ref mag,false); dummy.ComputeAbsoluteTransform(true,true);
                    WritePose(bones,Matrix.Identity,Path.Combine(output,profile.Item+"-reload-"+sample+".json"));
                }
            }
            AmmoPreviews(output);
            log("PASS production palm/finger exports for 22 installed profiles, six support arm poses, support highlight geometry, contact volumes, three installed reload clips and production ammo painter.");
        }
        public static void Ammo(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output); UiTests.Initialize(game,Path.Combine(output,"data"));
            AmmoPreviews(output); log("PASS production ammo previews: white, yellow, red, empty and single-round magazine.");
        }
        private static void AmmoPreviews(string output)
        {
            var anchors=WeaponProfile.All.Where(p=>!p.Tool).ToDictionary(p=>p.Model,p=> {
                var m=WeaponAmmo.Label(p,MatrixD.Identity,18,20).Pose;
                return new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}; });
            using(var file=File.Create(Path.Combine(output,"ammo-anchors.json")))
                new DataContractJsonSerializer(anchors.GetType(),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,anchors);
            using(var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))
            using(var canvas=new OverlayCanvas("Ammo fixture",512,192,1,false,device))
            {
                foreach(int remaining in new[] {18,6,5,3,2,0})
                {
                    UiTests.Render(canvas,()=>PhysicalSurface.Paint(canvas,WeaponAmmo.Label(WeaponProfile.Rifle,MatrixD.Identity,remaining,20)));
                    UiTests.Save(canvas.Texture,Path.Combine(output,"ammo-"+remaining+".png"));
                    if(remaining==18) UiTests.Save(canvas.Texture,Path.Combine(output,"ammo-label.png"));
                }
                UiTests.Render(canvas,()=>PhysicalSurface.Paint(canvas,WeaponAmmo.Label(WeaponProfile.Launcher,MatrixD.Identity,1,1)));
                UiTests.Save(canvas.Texture,Path.Combine(output,"ammo-single.png"));
            }
        }
        private static void WritePose(MyCharacterBone[] bones,Matrix frame,string path)
        {
            var pose=new CockpitHandTests.PoseExport();
            foreach(var bone in bones)
            {
                var m=bone.AbsoluteTransform*frame;
                pose.absolute[bone.Name]=new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
            }
            using(var file=File.Create(path))
                new DataContractJsonSerializer(typeof(CockpitHandTests.PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,pose);
        }
    }
}
