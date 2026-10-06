using System;
using System.Collections;
using System.Collections.Concurrent;
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
        private static Verification verification;
        private static CockpitGeometry geometry;
        private static CockpitRig activeRig;
        private static MyCockpit owner;
        private static readonly RenderRecovery recovery=new RenderRecovery("Cockpit controls");
        private static bool failed => recovery.Failed;
        private static Vector3? appliedColor;
        private static DateTime deadline;
        public static bool Ready => verification!=null && verification.NativeHidden && Volatile.Read(ref verification.ReadyCount)==verification.Verified.Length && verification.Error==null;
        public static string Status => failed ? "Sticks unavailable: button flight" : Ready ? "Cockpit controls ready" : "Checking cockpit control renderer";
        private static string Name(int i) => "SEVR_Cockpit_"+activeRig.Subtype+"_"+i;

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
            owner=null; activeRig=null; recovery.Clear(); appliedColor=null; feedback.Clear();
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
            UpdateScene(CockpitRig.Find(cockpit.BlockDefinition.Id.SubtypeName),model,cockpit.WorldMatrix,left,right,leftHeld,rightHeld,leftOffset,rightOffset,colorMask:cockpit.SlimBlock.ColorMaskHSV);
        }
        internal static void UpdateScene(CockpitRig rig,uint interior,MatrixD world,Matrix left,Matrix right,bool leftHeld,bool rightHeld,Vector3 leftOffset=default(Vector3),Vector3 rightOffset=default(Vector3),float? switchPreview=null,float? coverPreview=null,Vector3? colorMask=null,int previewHover=-1,int previewHeld=-1,bool previewCover=false,bool nativeRest=false,float? barPreview=null,bool? buttonPreview=null)
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
                    geometry=activeRig.Geometry(MyFileSystem.ContentPath);
                    verification=new Verification(interior,geometry.Parts.Length,activeRig.Pieces.Select(p=>p.Material).Distinct().ToArray());
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
                foreach(int actor in activeRig.StaticActors) UpdatePose(check,actor,world);
                UpdateRigStick(check,activeRig.Left,left,leftOffset,leftHeld,world);
                UpdateRigStick(check,activeRig.Right,right,rightOffset,rightHeld,world);
                string subtype=activeRig.Subtype;
                int State(int slot,bool cover)
                {
                    var touch=CockpitTouch.Read((cover ? "CockpitCover":"CockpitControl")+slot);
                    bool preview=previewCover==cover;
                    return preview && slot==previewHeld || touch.Held>=0 ? 2 : preview && slot==previewHover || touch.Hover>=0 ? 1 : 0;
                }
                for(int slot=0;slot<activeRig.Count;slot++)
                {
                    if(activeRig.ButtonAt(slot) is CockpitRig.Button button && button.Actor>=0)
                    {
                        int state=State(slot,false);
                        UpdatePose(check,button.Actor,(MatrixD)(nativeRest ? Matrix.Identity:button.Visual(buttonPreview ?? state==2))*world);
                        SetRigFeedback(check,button.Actor,state);
                    }
                    else if(activeRig.LeverAt(slot) is CockpitRig.Lever lever)
                    {
                        UpdatePose(check,lever.Actor,(MatrixD)(nativeRest ? Matrix.Identity : lever.Visual(switchPreview ?? CockpitButtons.SwitchPosition(slot,subtype)))*world);
                        SetRigFeedback(check,lever.Actor,State(slot,false));
                        if(lever.CoverActor<0) continue;
                        UpdatePose(check,lever.CoverActor,(MatrixD)(nativeRest ? Matrix.Identity : lever.CoverVisual(coverPreview ?? CockpitButtons.CoverPosition(slot,subtype)))*world);
                        SetRigFeedback(check,lever.CoverActor,State(slot,true));
                    }
                    else if(activeRig.HandleAt(slot) is CockpitRig.Handle handle)
                    {
                        UpdatePose(check,handle.Actor,(MatrixD)(nativeRest ? Matrix.Identity : handle.Visual(switchPreview ?? CockpitButtons.SwitchPosition(slot,subtype)))*world);
                        SetRigFeedback(check,handle.Actor,State(slot,false));
                    }
                    else if(activeRig.BarAt(slot) is CockpitRig.Bar bar)
                    {
                        UpdatePose(check,bar.Actor,(MatrixD)(nativeRest ? Matrix.Identity : bar.Visual(barPreview ?? CockpitButtons.SwitchPosition(slot,subtype)))*world);
                        SetRigFeedback(check,bar.Actor,State(slot,false));
                    }
                }
            }
            catch(Exception ex)
            {
                var previousOwner=owner; Reset(); owner=previousOwner;
                recovery.Fail(ex,"COCKPIT CONTROLS disabled; native interior and button flight restored");
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
        internal static void ApplyFeedback(uint id,string material,int state)
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
                    // Proxies can carry MyMeshMaterialId.NULL, whose Info getter indexes the table with -1.
                    object materialId=Member(proxy,"Material");
                    if(Convert.ToInt32(Member(materialId,"Index"))<0) continue;
                    string material=Member(Member(materialId,"Info"),"Name").ToString();
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
        private static readonly ConcurrentDictionary<(Type,string),MemberInfo> members=new ConcurrentDictionary<(Type,string),MemberInfo>();
        internal static MemberInfo Find(Type type,string name) =>
            members.GetOrAdd((type,name),key=>(MemberInfo)AccessTools.Field(key.Item1,key.Item2) ?? AccessTools.Property(key.Item1,key.Item2));
        internal static object Member(object instance,string name)
        {
            if (instance==null) throw new InvalidOperationException("Missing renderer member "+name);
            var type=instance.GetType();
            switch (Find(type,name))
            {
                case FieldInfo field: return field.GetValue(instance);
                case PropertyInfo property: return property.GetValue(instance,null);
            }
            throw new MissingMemberException(type.FullName,name);
        }
    }
}
