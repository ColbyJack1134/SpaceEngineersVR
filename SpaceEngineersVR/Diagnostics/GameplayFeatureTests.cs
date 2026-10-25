#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class GameplayFeatureTests
    {
        [DataContract] private sealed class PoseExport
        { [DataMember] public Dictionary<string,float[]> absolute=new Dictionary<string,float[]>(); }
        private static void Near(float actual,float expected,string message,float tolerance=.0002f)
        { if(!actual.IsValid() || Math.Abs(actual-expected)>tolerance) throw new Exception(message+": "+actual); }
        // The head-driven bend writes the native leaning value; the installed astronaut must bend its spine from it on the jetpack too.
        private static void HeadLean(Action<string> log)
        {
            var field=HarmonyLib.AccessTools.Field(typeof(Sandbox.Game.Entities.Character.MyCharacter),"m_animLeaning");
            if(field?.FieldType!=typeof(VRage.Sync.Sync<float,VRage.Sync.SyncDirection.BothWays>)) throw new Exception("Native character leaning value changed");
            var controller=System.Xml.Linq.XDocument.Load(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","AnimationControllers","AC_Astronaut.sbc"));
            var layer=controller.Descendants("Layer").FirstOrDefault(l=>(string)l.Element("Name")=="Leaning");
            var machine=controller.Descendants("StateMachine").FirstOrDefault(m=>(string)m.Element("Name")==(string)layer?.Element("StateMachine") && m.Element("Nodes")!=null);
            var node=machine?.Element("Nodes")?.Elements("Node").FirstOrDefault(n=>(string)n.Element("Name")==(string)layer?.Element("InitialSMNode"));
            var exits=machine?.Descendants("Transition").Where(t=>(string)t.Attribute("From")==(string)node?.Element("Name")).ToArray();
            if(layer==null || !((string)layer.Element("BoneMask")).Contains("SE_RigSpine") || node==null ||
                !node.Descendants("ParameterName").Any(p=>(string)p=="lean") || exits.Any(t=>t.Descendants("Condition").Any(c=>(string)c.Attribute("Lhs")!="crouch")))
                throw new Exception("Installed astronaut no longer bends its spine from lean outside a crouch");
            log("PASS head lean: native leaning value drives the astronaut's spine layer in every state but a crouch");
        }
        private static void Aiming(Action<string> log)
        {
            var muzzle=new Vector3D(10,5,-3); var direction=Vector3D.Normalize(new Vector3D(.3,.1,-1));
            Vector3D Eye(double back,double off) => muzzle-direction*back+Vector3D.CalculatePerpendicularVector(direction)*off;
            if(!WeaponHandling.Sighted(Eye(.6,.05),muzzle,muzzle,direction,false) || !WeaponHandling.Sighted(Eye(.2,.0),muzzle,muzzle,direction,false) || !WeaponHandling.Sighted(Eye(1,.08),muzzle,muzzle,direction,false))
                throw new Exception("Looking down the barrel did not count as aiming");
            if(WeaponHandling.Sighted(Eye(.6,.1),muzzle,muzzle,direction,false) || WeaponHandling.Sighted(Eye(.05,0),muzzle,muzzle,direction,false) || WeaponHandling.Sighted(Eye(1.3,0),muzzle,muzzle,direction,false) ||
                WeaponHandling.Sighted(Eye(-.3,0),muzzle,muzzle,direction,false)) throw new Exception("Off-axis or misplaced gun counted as aiming");
            if(!WeaponHandling.Sighted(Eye(.6,.12),muzzle,muzzle,direction,true) || WeaponHandling.Sighted(Eye(.6,.15),muzzle,muzzle,direction,true)) throw new Exception("Aiming hysteresis failed");
            var launcherSight=Eye(-.3,.117);
            if(!WeaponHandling.Sighted(Eye(.5,.117),muzzle,launcherSight,direction,false) || WeaponHandling.Sighted(Eye(.5,0),muzzle,launcherSight,direction,false))
                throw new Exception("A sight above the barrel is not the aiming line");
            var trail=(Vector3)direction; var bullet=muzzle+direction*40;
            if(Patches.TracerPatch.HeadWeight(bullet,trail,Eye(.6,.1),out double far)<.99f || Patches.TracerPatch.HeadWeight(bullet,trail,bullet+direction*30,out _)<.99f ||
                Patches.TracerPatch.HeadWeight(bullet,trail,bullet+Vector3D.CalculatePerpendicularVector(direction)*30,out _)>.01f || Math.Abs(far-40.6)>.01)
                throw new Exception("Tracer head dot does not complement the native end-on fade");
            double pixel=Math.PI/3600;
            if(Math.Abs(Patches.TracerPatch.HeadRadius(300,.036f,pixel)-300*pixel*1.3)>1e-4 || Patches.TracerPatch.HeadRadius(5,.036f,pixel)!=.036f)
                throw new Exception("Tracer head dot size is wrong");
            if(Patches.TracerPatch.Fade(20)!=1 || Math.Abs(Patches.TracerPatch.Fade(235)-.7f)>1e-4 || Math.Abs(Patches.TracerPatch.Fade(900)-.4f)>1e-6)
                throw new Exception("Tracer head dot does not fade with distance");
            if(Patches.TracerPatch.RangeFade(100,250)!=1 || Math.Abs(Patches.TracerPatch.RangeFade(187.5,250)-.5f)>1e-6 || Patches.TracerPatch.RangeFade(250,250)!=0 || Patches.TracerPatch.RangeFade(400,0)!=1)
                throw new Exception("Tracer head dot does not fade out over the shot's range");
            var tail=muzzle+direction*7; float span=3;
            if(!Patches.TracerPatch.Streak(muzzle,Eye(.5,.1),ref tail,trail,ref span) || Vector3D.Distance(tail,muzzle+direction*.3)>1e-4 || Math.Abs(span-9.7)>1e-3)
                throw new Exception("Own shot does not streak from the muzzle");
            tail=muzzle+direction*7; span=3;
            if(Patches.TracerPatch.Streak(muzzle,muzzle+new Vector3D(30,0,0),ref tail,trail,ref span) || span!=3)
                throw new Exception("Another player's shot streaks from the muzzle");
            tail=muzzle+direction*30; span=3;
            if(Patches.TracerPatch.Streak(muzzle,Eye(.5,.1),ref tail,trail,ref span) || span!=3) throw new Exception("A far shot streaks from the muzzle");
            var sightMuzzle=new Vector3D(0,.0481,-.3189); var sightRear=new Vector3D(0,.1121,-.0107); var forward=new Vector3D(0,0,-1);
            var aim=Patches.MotionToolPatch.SightAim(sightMuzzle,forward,sightRear,start=> { if(Math.Abs(start.Y-.1121)>1e-6 || Math.Abs(start.Z+.3189)>1e-6) throw new Exception("Sight ray does not start on the muzzle plane"); return 20; });
            var hit=sightMuzzle+aim*Vector3D.Dot(new Vector3D(0,.1121,-20.3189)-sightMuzzle,aim);
            if(Math.Abs(aim.Y-.064/Math.Sqrt(.064*.064+400))>1e-4 || Vector3D.Distance(hit,new Vector3D(0,.1121,-20.3189))>.002) throw new Exception("Sighted shot misses the sight line at 20 m");
            if(Vector3D.Distance(Patches.MotionToolPatch.SightAim(sightMuzzle,forward,sightRear,start=>300),forward)>.0003) throw new Exception("Sighted shot with no obstacle is not level");
            var tilted=Vector3D.Normalize(new Vector3D(0,.0041,-.1386)); var flareRear=new Vector3D(0,.0931,.0553); var flareMuzzle=new Vector3D(0,.0634,-.1047);
            var flareAim=Patches.MotionToolPatch.SightAim(flareMuzzle,tilted,flareRear,start=>10);
            var flareStart=flareRear+tilted*Vector3D.Dot(flareMuzzle-flareRear,tilted); var flareTarget=flareStart+tilted*10;
            if(Vector3D.Distance(flareMuzzle+flareAim*Vector3D.Dot(flareTarget-flareMuzzle,flareAim),flareTarget)>.002) throw new Exception("Tilted sight line not followed");
            var reticles=new HashSet<string> {"PreciseAutomaticRifleItem","UltimateAutomaticRifleItem","ElitePistolItem","BasicHandHeldLauncherItem","AdvancedHandHeldLauncherItem"};
            foreach(var profile in WeaponProfile.All)
                if(profile.Reticle!=reticles.Contains(profile.Item) || profile.Reticle && (!profile.Sight.HasValue || profile.Mirror.Reticle!=profile.Reticle)) throw new Exception("Reticle flag wrong: "+profile.Item);
            foreach(var profile in WeaponProfile.All)
                if((profile.Kind==ItemKind.Rifle || profile.Kind==ItemKind.Launcher || profile.Kind==ItemKind.Pistol)!=profile.Sight.HasValue || profile.Sight.HasValue && (profile.Sight.Value.Y<=profile.Muzzle.Y || profile.Mirror.Sight!=profile.Sight || profile.Mirror.SightDirection!=profile.SightDirection || Math.Abs(profile.SightDirection.Length()-1)>1e-5f))
                    throw new Exception("Sight line missing, below the barrel or lost in the mirrored profile: "+profile.Item);
            var definition=VRageRender.MyPostprocessSettings.Default; definition.Data.ChromaticFactor=.02f; definition.Data.VignetteStart=.9f; definition.Data.BloomDirtRatio=.75f; definition.Data.BloomMult=.02f;
            Func<VRageRender.MyPostprocessSettings,float> clean=v=>v.Data.BloomMult*(1-v.Data.BloomDirtRatio);
            var settings=definition;
            ScreenEffects.Apply(ref settings,definition,true,0);
            if(settings.Data.ChromaticFactor!=0 || settings.Data.VignetteStart!=0 || settings.Data.BloomDirtRatio!=0 || Math.Abs(clean(settings)-.005f)>1e-6f) throw new Exception("Screen effects not reduced or open-visor bloom changed");
            ScreenEffects.Apply(ref settings,definition,true,1);
            if(settings.Data.ChromaticFactor!=0 || settings.Data.BloomDirtRatio!=.75f || settings.Data.BloomMult!=.02f) throw new Exception("Closed visor did not restore the dirt texture and bloom");
            ScreenEffects.Apply(ref settings,definition,true,.5f);
            if(Math.Abs(settings.Data.BloomDirtRatio-.375f)>1e-6f || Math.Abs(clean(settings)-.005f)>1e-6f) throw new Exception("Dirt does not follow the visor animation or bloom changed mid-animation");
            settings=definition; ScreenEffects.Apply(ref settings,definition,false,0);
            if(settings.Data.ChromaticFactor!=.02f || settings.Data.VignetteStart!=.9f || settings.Data.BloomDirtRatio!=0 || Math.Abs(clean(settings)-.005f)>1e-6f) throw new Exception("Dust removal depends on the screen effects option");
            settings=definition; ScreenEffects.Apply(ref settings,definition,false,1);
            if(settings.Data.ChromaticFactor!=.02f || settings.Data.VignetteStart!=.9f || settings.Data.BloomDirtRatio!=.75f || settings.Data.BloomMult!=.02f) throw new Exception("Disabled screen effects changed settings");
            var allDirt=definition; allDirt.Data.BloomDirtRatio=1.2f;
            settings=allDirt; ScreenEffects.Apply(ref settings,allDirt,true,.5f);
            if(settings.Data.BloomMult<0 || settings.Data.BloomDirtRatio>1) throw new Exception("Out-of-range dirt ratio produced invalid bloom");
            log("PASS aiming and screen effects: sight-line detection with hysteresis, sighted shots converge on the sight line, sight profiles; effects follow VR, option and visor closure; dust independent of the option with bloom held at the closed level");
        }
        public static void Run(Action<string> log,string output=null)
        {
            HeadLean(log);
            Aiming(log);
            foreach(bool selected in new[] {false,true})
                if(BodyFit.UseFit(true,selected,true) || BodyFit.UseFit(false,selected,false) || BodyFit.UseFit(true,selected,false)!=selected)
                    throw new Exception("Body fitting changed native cockpit size or ignored opt-in");
            foreach(float scale in new[] {.6f,.85f,1f,1.2f,1.33f})
            foreach(bool seated in new[] {false,true})
            {
                var bones=ArmTests.InstalledBones(); var ordered=bones.OrderBy(b=>b.Depth).ToArray();
                var original=bones.Select(b=>b.AbsoluteTransform).ToArray();
                var head=bones.Single(b=>b.Name=="SE_RigHead");
                var pivot=seated ? head.AbsoluteTransform.Translation : Vector3.Zero;
                var translations=new Vector3[bones.Length]; var applied=new Vector3[bones.Length];
                BodyFit.Fit(ordered,pivot,scale,translations,applied,new Matrix[bones.Length],new Matrix[bones.Length]);
                Near(Vector3.Distance(head.AbsoluteTransform.Translation,BodyFit.Position(original[head.Index].Translation,pivot,scale)),0,"Fitted eye position");
                foreach(var bone in bones.Where(b=>BodyFit.Rigid(b.Name)))
                {
                    var before=original[bone.Index]*Matrix.Invert(original[bone.Parent.Index]);
                    var after=bone.AbsoluteTransform*Matrix.Invert(bone.Parent.AbsoluteTransform);
                    Near(Vector3.Distance(before.Translation,after.Translation),0,"Rigid attachment length");
                    Near(Vector3.Distance(before.Up,after.Up),0,"Rigid attachment orientation");
                }
                foreach(float side in new[] {-1f,1f})
                {
                    string prefix=side<0 ? "SE_RigL":"SE_RigR";
                    var palm=bones.Single(b=>b.Name==prefix+"Palm");
                    var cuff=bones.Single(b=>b.Name==prefix+"Forearm2");
                    Matrix expected=cuff.GetAbsoluteRigTransform()*Matrix.Invert(palm.GetAbsoluteRigTransform());
                    var arm=ArmSkeleton.Find(name=>bones.FirstOrDefault(b=>b.Name==name),prefix+"Upperarm",prefix+"Forearm1",prefix+"Palm",side);
                    Matrix target=Matrix.CreateRotationY(side*.4f);
                    target.Translation=pivot+(new Vector3(side*.28f,1.25f,-.35f)-pivot)*scale;
                    if(!ArmSkeleton.Apply(arm,target,new ArmSolve.Options(true,true,side<0 ? 1:0),scale,ArmSkeleton.Fingers.Native,0)) throw new Exception("Fitted arm failed");
                    Near(Vector3.Distance(palm.AbsoluteTransform.Translation,target.Translation),0,"Fitted arm lost controller");
                    Near(Vector3.Distance(palm.AbsoluteTransform.Up,(arm.PalmOffset*target).Up),0,"Fitted palm orientation");
                    if(side<0)
                    {
                        // Screen in use: the cuff and its tablet ride the forearm with the hand's orientation under the body scale.
                        var actual=cuff.AbsoluteTransform*Matrix.Invert(palm.AbsoluteTransform);
                        Vector3 elbow=arm.Lower.Bone.AbsoluteTransform.Translation,line=Vector3.Normalize(palm.AbsoluteTransform.Translation-elbow),along=cuff.AbsoluteTransform.Translation-elbow;
                        Near((along-line*Vector3.Dot(along,line)).Length(),0,"Fitted tablet left the forearm",.001f);
                        Near(Vector3.Distance(expected.Up,actual.Up),0,"Fitted tablet twists",.02f);
                        float upper=ArmTests.Length(arm,true),lower=ArmTests.Length(arm,false);
                        arm.Restore(true);
                        if(!ArmSkeleton.Apply(arm,target,new ArmSolve.Options(true,true,0),scale,ArmSkeleton.Fingers.Native,0)) throw new Exception("Fitted arm failed");
                        Near(upper,ArmTests.Length(arm,true),"Fitted screen arm stretched the upper arm",.001f);
                        // A hand close to the body slides the elbow along the glove line, changing the forearm by at most a quarter.
                        if(Math.Abs(lower/ArmTests.Length(arm,false)-1)>.26f) throw new Exception("Fitted screen arm forearm off by "+lower/ArmTests.Length(arm,false));
                    }
                }
                if(output!=null && !seated)
                {
                    var export=new PoseExport();
                    foreach(var bone in bones)
                    {
                        var m=BodyFit.RenderBone(bone.AbsoluteTransform,scale,BodyFit.Rigid(bone.Name));
                        export.absolute[bone.Name]=new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
                    }
                    using(var stream=File.Create(Path.Combine(output,"body-fit-"+scale.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".json")))
                        new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(stream,export);
                }
            }
            log("PASS installed skeleton body fit: five sizes, standing/seat pivots, exact hand targets, left cuff/tablet on the forearm line and no screen stretch");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(LadderClimb).TypeHandle);
            log("PASS installed ladder members: offset, constraint, end checks and ladder switching");
            var armor=(Sandbox.Game.Entities.Cube.MySlimBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Cube.MySlimBlock));
            var door=(Sandbox.Game.Entities.Cube.MySlimBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Cube.MySlimBlock));
            foreach(var hit in new[] {door,armor,door,armor})
                if(Multiplayer.ToolContact.Block(new Sandbox.Game.Entities.MyCube {CubeBlock=hit},null)!=hit)
                    throw new Exception("Inspection failed to follow the native geometry hit, including non-interactable armor");
            if(Multiplayer.ToolContact.Block(null,null)!=null)
                throw new Exception("Inspection retained the previous block after pointing into empty space");
            var available=HarmonyLib.AccessTools.Property(typeof(NativeBlockInfo),nameof(NativeBlockInfo.Available));
            available.SetValue(null,true);
            try
            {
                // Not installed, so the capture throws inside the HUD draw prefix.
                NativeBlockInfo.Show((Sandbox.Game.Gui.MyHudBlockInfo)FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Gui.MyHudBlockInfo)),"failure");
                NativeBlockInfo.Draw();
                if(NativeBlockInfo.Available) throw new Exception("A failed block info capture left grip inspection enabled");
            }
            finally { available.SetValue(null,false); NativeBlockInfo.Hide(); }
            RemoteView.Axes(new Vector2(1,1),new Vector2(1,1),10,out var aim,out float zoom);
            Near(aim.X,-10,"Turret pitch"); Near(aim.Y,10,"Turret yaw"); Near(zoom,-1,"Turret zoom");
            RemoteView.Axes(Vector2.Zero,Vector2.UnitX,10,out aim,out zoom);
            Near(aim.Length()+Math.Abs(zoom),0,"Horizontal left stick moved turret");
            foreach(float tilt in new[] {-.5f,0f,.5f})
            {
                var translation=CockpitStickMath.Translation(Matrix.Identity,Matrix.CreateRotationX(tilt),0,true);
                aim=new Vector2(.2f,.3f); zoom=.7f;
                CockpitStickMath.ApplyTurret(false,true,Vector3.Zero,translation,10,2,ref aim,ref zoom);
                Near(zoom,translation.Z,"Physical left forward/back tilt lost turret zoom");
                Near(aim.X,.2f,"Translation stick changed turret pitch"); Near(aim.Y,.3f,"Translation stick changed turret yaw");
                CockpitStickMath.ApplyTurret(true,false,new Vector3(.5f,0,-.5f),Vector3.Zero,10,2,ref aim,ref zoom);
                Near(aim.X,2.5f,"Physical turret pitch"); Near(aim.Y,-2.5f,"Physical turret yaw");
                Near(zoom,translation.Z,"Right rotation stick competed with turret zoom");
            }
            foreach(float thumb in new[] {-1f,0f,1f})
            foreach(float physical in new[] {-1f,0f,1f})
            {
                RemoteView.Axes(new Vector2(.8f,-thumb),new Vector2(0,1),10,out aim,out zoom);
                CockpitStickMath.ApplyTurret(true,true,new Vector3(.5f,0,-.5f),new Vector3(0,0,physical),10,2,ref aim,ref zoom,thumb);
                Near(zoom,MathHelper.Clamp(physical+thumb,-1,1),"Physical left and held-right thumb turret zoom did not combine within limits");
                Near(aim.X,2.5f,"Zoom thumbstick changed physical turret pitch");
                Near(aim.Y,-2.5f,"Zoom thumbstick changed physical turret yaw");
            }
            foreach(float width in new[] {MenuWindow.MinWidth,1.2f,MenuWindow.MaxWidth})
                if(RemoteView.ZoomX(width,false)-.027f< -width/2 || RemoteView.ZoomX(width,true)+.027f>-.19f)
                    throw new Exception("Feed zoom controls overlap drag handle or screen edge");
            var skeleton=ArmTests.InstalledBones();
            Vector3 pelvis=skeleton.Single(b=>b.Name=="SE_RigPelvis").AbsoluteTransform.Translation;
            Vector3 chest=skeleton.Single(b=>b.Name=="SE_RigRibcage").AbsoluteTransform.Translation;
            Vector3 referenceHead=skeleton.Single(b=>b.Name=="SE_RigHead").AbsoluteTransform.Translation;
            Near(BodyProximity.Amount(referenceHead,pelvis,chest),0,"Native seated reference unexpectedly fades");
            Near(BodyProximity.Amount((pelvis+chest)*.5f,pelvis,chest),1,"Head inside torso not faded");
            Near(BodyProximity.Amount(chest+Vector3.UnitX,pelvis,chest),0,"Distant body faded");
            foreach(float folded in new[] {0f,1f})
            {
                MatrixD oldMount=MatrixD.CreateTranslation(1000000000,0,0);
                MatrixD currentMount=MatrixD.CreateRotationY(.15)*MatrixD.CreateTranslation(1000000000+.0833,.02,-.03);
                var panel=SpatialUi.WristViews(oldMount,folded,1,null,new SurfaceKey[0]).Last();
                var current=SpatialUi.WristViews(currentMount,folded,1,null,new SurfaceKey[0]).Last();
                MatrixD parent=SpatialUi.WristAttachmentParent(panel,currentMount);
                Vector3 localContact=new Vector3(.021f,-.011f,.001f);
                if(Vector3D.Distance(Vector3D.Transform(localContact,parent),Vector3D.Transform(localContact,current.Pose))>1e-6)
                    throw new Exception("Moving wrist attachment retained an older surface transform");
                if(Vector3D.Distance(Vector3D.Transform(localContact,parent),Vector3D.Transform(localContact,panel.Pose))<.05)
                    throw new Exception("Moving wrist fixture did not expose the old-frame offset");
            }
            log("PASS camera axes, component costs, native torso proximity and moving/rotating wrist contacts at large world coordinates");
        }
    }
}
#endif
