using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Renders;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRageMath;
using VRageRender;
using VRageRender.Import;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitRender
    {
        internal const RenderFlags Hidden=RenderFlags.SkipInMainView|RenderFlags.SkipInDepth|RenderFlags.SkipInForward;
        internal sealed class Verification
        {
            public readonly uint Interior;
            public uint[] Actors=new uint[0];
            public volatile bool NativeHidden;
            public readonly bool[] Verified;
            public readonly MatrixD?[] Poses;
            public readonly string[] Materials;
            public int ReadyCount;
            public volatile string Error;
            public Verification(uint interior,int count,string[] materials)
            { Interior=interior; Verified=new bool[count]; Poses=new MatrixD?[count]; Materials=materials; }
        }
        private const int CoverBase=7+CockpitSwitchGeometry.Count;
        private const int BarActor=CoverBase+1+CockpitCoverGeometry.Count;
        private const int ActorCount=BarActor+1;
        private static readonly string[] fighterMaterials={FighterProfile.Material,CockpitSwitchGeometry.Material,CockpitCoverGeometry.Material};
        private static Verification verification;
        private static CockpitGeometry geometry,fighterGeometry;
        private static CockpitRig activeRig;
        private static MyCockpit owner;
        private static bool failed;
        private static Vector3? appliedColor;
        private static DateTime deadline;
        public static bool Ready => verification!=null && verification.NativeHidden && Volatile.Read(ref verification.ReadyCount)==verification.Verified.Length && verification.Error==null;
        public static string Status => failed ? "Sticks unavailable: button flight" : Ready ? "Cockpit controls ready" : "Checking cockpit control renderer";
        private static readonly string[] names={"SEVR_Fighter_LeftBase","SEVR_Fighter_LeftHandle","SEVR_Fighter_LeftStem","SEVR_Fighter_RightHandle","SEVR_Fighter_RightStem","SEVR_Fighter_RightBase"};

        private static string Name(int i) => activeRig!=null ? "SEVR_Cockpit_"+activeRig.Subtype+"_"+i : i<6 ? names[i] : i==6 ? "SEVR_Fighter_Chrome" : i<CoverBase ? "SEVR_Fighter_Switch"+(i-7) : i==CoverBase ? "SEVR_Fighter_Interior" : i==BarActor ? "SEVR_Fighter_PullBar" : "SEVR_Fighter_Cover"+(i-CoverBase-1);

        public static void Reset()
        {
            var previous=Interlocked.Exchange(ref verification,null);
            if (previous!=null)
            {
                // Remove replacements before making the native material visible again.
                foreach (uint id in previous.Actors) if(id!=uint.MaxValue) MyRenderProxy.RemoveRenderObject(id,MyRenderProxy.ObjectType.Entity);
                foreach(string material in previous.Materials)
                    MyRenderProxy.UpdateModelProperties(previous.Interior,material,RenderFlags.Visible,RenderFlags.Visible|Hidden,null,null);
            }
            owner=null; activeRig=null; failed=false; appliedColor=null; feedback.Clear();
        }
        public static void Update(MyCockpit cockpit,Matrix left,Matrix right,bool leftHeld,bool rightHeld,Vector3 leftOffset=default(Vector3),Vector3 rightOffset=default(Vector3))
        {
            if (owner!=cockpit) { Reset(); owner=cockpit; }
            if (cockpit==null || failed) return;
            var render=cockpit.Render as MyRenderComponentCockpit;
            bool interior=cockpit.BlockDefinition.InteriorModel!=null;
            if (render==null || render.RenderObjectIDs.Length<(interior ? 2:1)) return;
            uint model=interior ? render.InteriorRenderId:render.ExteriorRenderId;
            if(model==uint.MaxValue) return;
            UpdateScene(model,cockpit.WorldMatrix,left,right,leftHeld,rightHeld,leftOffset,rightOffset,colorMask:cockpit.SlimBlock.ColorMaskHSV,rig:CockpitRig.Find(cockpit.BlockDefinition.Id.SubtypeName));
        }
        internal static void UpdateScene(uint interior,MatrixD world,Matrix left,Matrix right,bool leftHeld,bool rightHeld,Vector3 leftOffset=default(Vector3),Vector3 rightOffset=default(Vector3),float? switchPreview=null,float? coverPreview=null,Vector3? colorMask=null,int previewHover=-1,int previewHeld=-1,bool previewCover=false,bool nativeRest=false,float? barPreview=null,CockpitRig rig=null)
        {
            if (failed) return;
            try
            {
                if (verification!=null && verification.Interior!=interior)
                {
                    var previousOwner=owner; Reset(); owner=previousOwner;
                }
                if (verification==null)
                {
                    activeRig=rig;
                    geometry=rig!=null ? rig.Geometry(MyFileSystem.ContentPath) : fighterGeometry ?? (fighterGeometry=CockpitGeometry.Load(MyFileSystem.ContentPath));
                    var hidden=rig==null ? fighterMaterials : rig.Pieces.Select(p=>p.Material).Distinct().ToArray();
                    verification=new Verification(interior,geometry.Parts.Length,hidden);
                    deadline=DateTime.UtcNow.AddSeconds(8);
                    // This also converts the instance to the engine's supported per-material pipeline.
                    foreach(string material in verification.Materials)
                        MyRenderProxy.UpdateModelProperties(interior,material,RenderFlags.Visible|Hidden,RenderFlags.Visible,null,null);
                    Logger.Info("COCKPIT CONTROLS waiting for native material visibility verification");
                }
                var check=verification;
                Vector3 paint=colorMask ?? Vector3.Zero;
                if (check.Error!=null) throw new InvalidOperationException(check.Error);
                if (!Ready && DateTime.UtcNow>deadline) throw new TimeoutException("Cockpit control renderer did not confirm hidden native geometry and replacement meshes.");
                if (check.NativeHidden && check.Actors.Length==0)
                {
                    long started=FeatureTiming.Start();
                    try
                    {
                        var actors=Enumerable.Repeat(uint.MaxValue,check.Verified.Length).ToArray();
                        for (int i=0;i<actors.Length;i++)
                        {
                            if(geometry.Parts[i].Indices.Count==0)
                            { check.Verified[i]=true; Interlocked.Increment(ref check.ReadyCount); continue; }
                            var message=MyRenderProxy.PrepareAddRuntimeModel();
                            var data=message.ModelData; var source=geometry.Parts[i];
                            data.Positions.AddRange(source.Positions); data.Indices.AddRange(source.Indices);
                            data.Normals.AddRange(source.Normals); data.Tangents.AddRange(source.Tangents);
                            data.TexCoords.AddRange(source.TexCoords); data.Sections.AddRange(source.Sections); data.AABB=source.AABB;
                            message.ReplacedModel=null;
                            MyRenderProxy.AddRuntimeModel(Name(i),message);
                            actors[i]=MyRenderProxy.CreateRenderEntity(Name(i),Name(i),world,MyMeshDrawTechnique.MESH,
                                RenderFlags.Visible|RenderFlags.ForceOldPipeline|RenderFlags.CastShadows,(CullingOptions)0,Color.White,paint);
                            Volatile.Write(ref check.Actors,actors.Take(i+1).ToArray());
                        }
                        Volatile.Write(ref check.Actors,actors);
                        appliedColor=paint;
                        for(int i=0;i<actors.Length;i++) foreach(var section in geometry.Parts[i].Sections)
                            MyRenderProxy.UpdateModelProperties(actors[i],section.MaterialName,RenderFlags.Visible,RenderFlags.Visible,null,null);
                    }
                    finally { FeatureTiming.End(FeatureTiming.Area.CockpitActors,started); }
                }
                if (check.Actors.Length!=check.Verified.Length) return;
                if(appliedColor!=paint)
                {
                    foreach(uint actor in check.Actors) if(actor!=uint.MaxValue) MyRenderProxy.UpdateRenderEntity(actor,null,paint);
                    appliedColor=paint;
                }
                if(activeRig!=null)
                {
                    UpdatePose(check,0,world);
                    UpdateRigStick(check,activeRig.Left,left,leftOffset,leftHeld,world);
                    UpdateRigStick(check,activeRig.Right,right,rightOffset,rightHeld,world);
                    for(int i=0;i<activeRig.Levers.Length;i++)
                    {
                        var lever=activeRig.Levers[i]; if(lever==null) continue;
                        var touch=CockpitTouch.Read("CockpitControl"+i);
                        UpdatePose(check,lever.Actor,(MatrixD)(nativeRest ? Matrix.Identity : lever.Visual(switchPreview ?? CockpitButtons.SwitchPosition(i,activeRig.Subtype)))*world);
                        SetRigFeedback(check,lever.Actor,touch.Held>=0 ? 2 : touch.Hover>=0 ? 1 : 0);
                        if(lever.CoverActor<0) continue;
                        touch=CockpitTouch.Read("CockpitCover"+i);
                        UpdatePose(check,lever.CoverActor,(MatrixD)(nativeRest ? Matrix.Identity : lever.CoverVisual(coverPreview ?? CockpitButtons.CoverPosition(i,activeRig.Subtype)))*world);
                        SetRigFeedback(check,lever.CoverActor,touch.Held>=0 ? 2 : touch.Hover>=0 ? 1 : 0);
                    }
                    return;
                }
                var matrices=new[] { Matrix.CreateTranslation(leftOffset),left,left,right,right,Matrix.CreateTranslation(rightOffset) };
                for (int i=0;i<6;i++) UpdatePose(check,i,(MatrixD)matrices[i]*world);
                UpdatePose(check,6,world);
                for(int i=0;i<CockpitSwitchGeometry.Count;i++) UpdatePose(check,7+i,(MatrixD)(nativeRest ? Matrix.Identity : CockpitSwitchGeometry.Visual(i,switchPreview ?? CockpitButtons.SwitchPosition(i)))*world);
                UpdatePose(check,CoverBase,world);
                for(int i=0;i<CockpitCoverGeometry.Count;i++) UpdatePose(check,CoverBase+1+i,
                    (MatrixD)CockpitCoverGeometry.Visual(i,coverPreview ?? CockpitButtons.CoverPosition(CockpitCoverGeometry.Slot(i),FighterProfile.Subtype))*world);
                UpdatePose(check,BarActor,(MatrixD)CockpitBarGeometry.Visual(nativeRest ? 0 : barPreview ?? CockpitButtons.SwitchPosition(CockpitBarGeometry.Slot))*world);
                var bar=CockpitTouch.Read("CockpitControl"+CockpitBarGeometry.Slot);
                SetFeedback(check.Actors[BarActor],CockpitCoverGeometry.Material,
                    previewHeld==CockpitBarGeometry.Slot || bar.Held>=0 ? 2 : previewHover==CockpitBarGeometry.Slot || bar.Hover>=0 ? 1 : 0);
                SetFeedback(check.Actors[1],FighterProfile.Material,leftHeld ? 2 : 0);
                SetFeedback(check.Actors[3],FighterProfile.Material,rightHeld ? 2 : 0);
                for(int i=0;i<CockpitSwitchGeometry.Count;i++)
                {
                    var lever=CockpitTouch.Read("CockpitControl"+i);
                    var cover=CockpitTouch.Read("CockpitCover"+i);
                    SetFeedback(check.Actors[7+i],CockpitSwitchGeometry.Material,
                        !previewCover && i==previewHeld || lever.Held>=0 ? 2 : !previewCover && i==previewHover || lever.Hover>=0 ? 1 : 0);
                    if(CockpitSwitchGeometry.Covered(i)) SetFeedback(check.Actors[CoverBase+1+CockpitSwitchGeometry.CoverIndex(i)],CockpitCoverGeometry.Material,
                        previewCover && i==previewHeld || cover.Held>=0 ? 2 : previewCover && i==previewHover || cover.Hover>=0 ? 1 : 0);
                }
            }
            catch(Exception ex)
            {
                var previousOwner=owner; Reset(); owner=previousOwner; failed=true;
                Logger.Warning(ex,"COCKPIT CONTROLS disabled; native interior and button flight restored");
            }
        }
        private static void UpdateRigStick(Verification check,CockpitRig.Stick stick,Matrix visual,Vector3 offset,bool held,MatrixD world)
        {
            if(stick==null) return;
            UpdatePose(check,stick.Actor,(MatrixD)visual*world);
            SetRigFeedback(check,stick.Actor,held ? 2:0);
            if(stick.BaseActor>=0) UpdatePose(check,stick.BaseActor,MatrixD.CreateTranslation(offset)*world);
        }
        private static void SetRigFeedback(Verification check,int actor,int state)
        {
            uint id=check.Actors[actor];
            if(feedback.TryGetValue(id,out int previous) && previous==state) return;
            feedback[id]=state;
            foreach(var material in geometry.Parts[actor].Sections.Select(s=>s.MaterialName).Distinct()) ApplyFeedback(id,material,state);
        }
        private static void UpdatePose(Verification check,int index,MatrixD pose)
        {
            if(check.Actors[index]==uint.MaxValue) return;
            if(check.Poses[index].HasValue && check.Poses[index].Value==pose) return;
            check.Poses[index]=pose;
            MyRenderProxy.UpdateRenderObject(check.Actors[index],pose);
        }
        private static readonly System.Collections.Generic.Dictionary<uint,int> feedback=new System.Collections.Generic.Dictionary<uint,int>();
        private static void SetFeedback(uint id,string material,int state)
        {
            if (feedback.TryGetValue(id,out int prior) && prior==state) return;
            feedback[id]=state;
            ApplyFeedback(id,material,state);
        }
        private static void ApplyFeedback(uint id,string material,int state)
        {
            MyRenderProxy.UpdateModelProperties(id,material,RenderFlags.Visible,RenderFlags.Visible,
                state==2 ? new Color(160,255,190) : state==1 ? new Color(160,220,255) : Color.White,state==2 ? .10f : state==1 ? .05f : 0f);
        }

        internal static void InitializeRuntimeSections(object mesh)
        {
            // 1.210's runtime mesh factory leaves this null, but proxy construction enumerates it.
            var type=AccessTools.TypeByName("VRageRender.MyMeshes");
            object lod=AccessTools.Method(type,"GetLodMesh").Invoke(null,new[] {mesh,(object)0});
            int index=Convert.ToInt32(Member(lod,"Index"));
            var pool=AccessTools.Field(type,"LodMeshInfos").GetValue(null);
            var data=(Array)Member(pool,"Data");
            object info=data.GetValue(index);
            var sections=AccessTools.Field(info.GetType(),"SectionNames");
            if (sections.GetValue(info)==null)
            {
                sections.SetValue(info,new string[0]);
                data.SetValue(info,index);
            }
        }
        // Runs after actual engine proxy construction, before either eye draws the scene.
        internal static void Verify(object renderable)
        {
            var check=Volatile.Read(ref verification);
            if (check==null || check.Error!=null) return;
            try
            {
                uint id=Convert.ToUInt32(Member(Member(renderable,"Owner"),"ID"));
                int replacement=Array.IndexOf(check.Actors,id);
                if (id!=check.Interior && replacement<0) return;
                var lods=Member(renderable,"Lods") as IEnumerable;
                if (lods==null) return;
                bool found=false,otherVisible=false; var nativeMaterials=new System.Collections.Generic.HashSet<string>();
                foreach (object lod in lods)
                foreach (object proxy in (IEnumerable)Member(lod,"RenderableProxies"))
                {
                    string material=Member(Member(Member(proxy,"Material"),"Info"),"Name").ToString();
                    object flags=Member(proxy,"Flags");
                    long mask=Convert.ToInt64(Enum.Parse(flags.GetType(),"SkipInMainView, SkipInDepth, SkipInForward"));
                    long value=Convert.ToInt64(flags);
                    if (Array.IndexOf(check.Materials,material)>=0)
                    {
                        bool hidden=(value&mask)==mask;
                        if (id==check.Interior && !hidden) return;
                        nativeMaterials.Add(material);
                        if (replacement>=0 && (value&mask)!=0) throw new InvalidOperationException("Replacement cockpit proxy is hidden.");
                        found=true;
                    }
                    else if ((value&Convert.ToInt64(Enum.Parse(flags.GetType(),"SkipInMainView")))==0) otherVisible=true;
                }
                if (!found) return;
                if (id==check.Interior)
                {
                    if(nativeMaterials.Count!=check.Materials.Length) return;
                    if (!otherVisible) throw new InvalidOperationException("Native stick suppression hid unrelated cockpit geometry.");
                    if (!check.NativeHidden) Logger.Info("COCKPIT CONTROLS native material hidden in main/depth/forward passes; other interior materials visible");
                    check.NativeHidden=true;
                }
                else
                {
                    lock(check)
                    {
                        if(check.Verified[replacement]) return;
                        check.Verified[replacement]=true;
                        check.ReadyCount++;
                        if(check.ReadyCount==check.Verified.Length) Logger.Info("COCKPIT CONTROLS native stick and switch meshes verified in the stereo scene pipeline");
                    }
                }
            }
            catch(Exception ex) { check.Error=ex.ToString(); }
        }
        internal static object Member(object instance,string name)
        {
            if (instance==null) throw new InvalidOperationException("Missing renderer member "+name);
            var type=instance.GetType();
            var field=AccessTools.Field(type,name);
            if (field!=null) return field.GetValue(instance);
            var property=AccessTools.Property(type,name);
            if (property!=null) return property.GetValue(instance,null);
            throw new MissingMemberException(type.FullName,name);
        }
    }
}
