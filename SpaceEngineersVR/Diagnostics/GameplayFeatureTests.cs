using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Sandbox.Game.Gui;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class GameplayFeatureTests
    {
        [DataContract] private sealed class PoseExport
        { [DataMember] public Dictionary<string,float[]> absolute=new Dictionary<string,float[]>(); }
        private static void Near(float actual,float expected,string message,float tolerance=.0002f)
        { if(!actual.IsValid() || Math.Abs(actual-expected)>tolerance) throw new Exception(message+": "+actual); }
        public static void Run(Action<string> log,string output=null)
        {
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
                    var upper=bones.Single(b=>b.Name==prefix+"Upperarm");
                    var lower=bones.Single(b=>b.Name==prefix+"Forearm1");
                    var palm=bones.Single(b=>b.Name==prefix+"Palm");
                    var cuff=bones.Single(b=>b.Name==prefix+"Forearm2");
                    var expected=cuff.GetAbsoluteRigTransform()*Matrix.Invert(palm.GetAbsoluteRigTransform());
                    var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side);
                    Matrix target=Matrix.CreateRotationY(side*.4f);
                    target.Translation=pivot+(new Vector3(side*.28f,1.25f,-.35f)-pivot)*scale;
                    if(!ArmMath.ApplyPose(upper,lower,palm,target,correction,new Vector3(side*.55f,-1,.3f),rigidWrist:side<0,bodyScale:scale))
                        throw new Exception("Fitted arm failed");
                    Near(Vector3.Distance(palm.AbsoluteTransform.Translation,target.Translation),0,"Fitted arm lost controller");
                    Near(Vector3.Distance(palm.AbsoluteTransform.Up,(correction*target).Up),0,"Fitted palm orientation");
                    if(side<0)
                    {
                        var actual=cuff.AbsoluteTransform*Matrix.Invert(palm.AbsoluteTransform);
                        Near(Vector3.Distance(expected.Translation,actual.Translation),0,"Fitted tablet slides");
                        Near(Vector3.Distance(expected.Up,actual.Up),0,"Fitted tablet twists");
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
            log("PASS installed skeleton body fit: five sizes, standing/seat pivots, exact hand targets and rigid left cuff/tablet");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(LadderClimb).TypeHandle);
            log("PASS installed ladder members: offset, constraint, end checks and ladder switching");
            var id=new MyDefinitionId(typeof(MyObjectBuilder_Component),"SteelPlate");
            var rows=new[] {
                new MyHudBlockInfo.ComponentInfo {DefinitionId=id,ComponentName="Steel Plate",TotalCount=10,MountedCount=3,StockpileCount=2,AvailableAmount=4},
                new MyHudBlockInfo.ComponentInfo {DefinitionId=id,ComponentName="Steel Plate",TotalCount=5,MountedCount=1,StockpileCount=1,AvailableAmount=4} };
            string info=BlockInspection.Describe(.5f,.3f,false,25,rows);
            var armor=(Sandbox.Game.Entities.Cube.MySlimBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Cube.MySlimBlock));
            var door=(Sandbox.Game.Entities.Cube.MySlimBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Cube.MySlimBlock));
            foreach(var hit in new[] {door,armor,door,armor})
                if(BlockInspection.BlockForHit(new Sandbox.Game.Entities.MyCube {CubeBlock=hit},null)!=hit)
                    throw new Exception("Inspection failed to follow the native geometry hit, including non-interactable armor");
            if(BlockInspection.BlockForHit(null,null)!=null)
                throw new Exception("Inspection retained the previous block after pointing into empty space");
            if(!info.Contains("7/15  need 8  have 4") || !info.Contains("Damaged") || info.Split('\n').Length!=3)
                throw new Exception("Component stack grouping, stockpile or damage lost: "+info);
            if(BlockInspection.Describe(.5f,.5f,false,25,rows).Contains("Damaged") || !BlockInspection.Describe(0,0,true,25,rows).StartsWith("Component cost"))
                throw new Exception("Unfinished construction reported as damage");
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
