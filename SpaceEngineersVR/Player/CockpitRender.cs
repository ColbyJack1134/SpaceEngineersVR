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
using VRage.Utils;
using VRage.Render.Scene;
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
            public volatile uint Exterior=uint.MaxValue;
            public uint[] Actors=new uint[0];
            public volatile bool NativeHidden;
            public readonly bool[] Verified;
            public readonly MatrixD?[] Poses;
            public readonly string[] Materials;
            public readonly string[][] ActorMaterials;
            public int ReadyCount;
            public volatile string Error;
            public volatile string NativeStatus="No native proxy verification";
            public Verification(uint interior,CockpitGeometry geometry,string[] materials,string subtype=null)
            { Interior=interior; Verified=new bool[geometry.Parts.Length]; Poses=new MatrixD?[geometry.Parts.Length]; Materials=materials;
                ActorMaterials=geometry.Parts.Select(p=>p.Sections.Select(s=>subtype==null ? s.MaterialName:CockpitMaterials.Name(subtype,s.MaterialName)).Distinct().ToArray()).ToArray(); }
        }
        private static Verification verification;
        private static CockpitGeometry geometry;
        private static CockpitRig activeRig;
        private static MyCockpit owner;
        private static readonly RenderRecovery recovery=new RenderRecovery("Cockpit controls");
        private static bool failed => recovery.Failed;
        private static Vector3? appliedColor;
        private static MyStringHash? appliedSkin;
        private static DateTime deadline;
        private static volatile bool remoteInterior;
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
            owner=null; activeRig=null; remoteInterior=false; recovery.Clear(); appliedColor=null; appliedSkin=null; feedback.Clear(); screenTextures.Clear();
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
            bool remote=RemoteView.UsesSeat(cockpit) && !ThirdPersonView.Active;
            if(remote!=remoteInterior) { remoteInterior=remote; cockpit.UpdateCockpitModel(); }
            UpdateScene(CockpitRig.Find(cockpit.BlockDefinition.Id.SubtypeName),model,cockpit.WorldMatrix,left,right,leftHeld,rightHeld,leftOffset,rightOffset,colorMask:cockpit.SlimBlock.ColorMaskHSV);
            if(verification!=null) verification.Exterior=render.ExteriorRenderId;
            SyncSkin(cockpit);
            SyncScreens(cockpit);
        }
        internal static void RemoteVisibility(System.Collections.Generic.List<Action> restore)
        {
            var check=verification;
            if(check==null || !remoteInterior) return;
            RemoteVisibility(check,restore);
        }
        internal static void RemoteVisibility(Verification check,System.Collections.Generic.List<Action> restore)
        {
            if(check.Exterior==uint.MaxValue || check.Exterior==check.Interior) return;
            void Set(uint id,bool visible)
            {
                var actor=MyIDTracker<MyActor>.FindByID(id);
                if(actor==null || actor.IsVisible==visible) return;
                bool saved=actor.IsVisible;
                restore.Add(()=> { actor.SetVisibility(saved); actor.UpdateBeforeDraw(); });
                actor.SetVisibility(visible); actor.UpdateBeforeDraw();
            }
            Set(check.Interior,false);
            foreach(uint id in check.Actors) if(id!=uint.MaxValue) Set(id,false);
            Set(check.Exterior,true);
        }
        internal static void UpdateScene(CockpitRig rig,uint interior,MatrixD world,Matrix left,Matrix right,bool leftHeld,bool rightHeld,Vector3 leftOffset=default(Vector3),Vector3 rightOffset=default(Vector3),float? switchPreview=null,float? coverPreview=null,Vector3? colorMask=null,int previewHover=-1,int previewHeld=-1,bool previewCover=false,bool nativeRest=false,float? barPreview=null,bool? buttonPreview=null,float? throttlePreview=null)
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
                    foreach(string templateSubtype in activeRig.Buttons.Select(b=>b.TemplateSubtype).Where(s=>s!=null).Distinct())
                        MyRenderProxy.PreloadModel(CockpitRig.Find(templateSubtype).Model,forceOldPipeline:true);
                    geometry=activeRig.Geometry(MyFileSystem.ContentPath);
                    verification=new Verification(interior,geometry,activeRig.Pieces.Select(p=>p.Material).Distinct().ToArray(),activeRig.Subtype);
                    deadline=DateTime.UtcNow.AddSeconds(8);
                    // This also converts the instance to the engine's supported per-material pipeline.
                    foreach(string material in verification.Materials)
                        MyRenderProxy.UpdateModelProperties(interior,material,RenderFlags.Visible|Hidden,RenderFlags.Visible,null,null);
                    Logger.Info("COCKPIT CONTROLS waiting for native material visibility verification");
                }
                var check=verification;
                Vector3 paint=colorMask ?? Vector3.Zero;
                if (check.Error!=null) throw new InvalidOperationException(check.Error);
                if (!Ready && DateTime.UtcNow>deadline) throw new TimeoutException("Cockpit control renderer did not confirm hidden native geometry and replacement meshes: "+check.NativeStatus);
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
                            data.TexCoords.AddRange(source.TexCoords);
                            foreach(var section in source.Sections)
                            {
                                var replacement=section; replacement.MaterialName=CockpitMaterials.Name(activeRig.Subtype,section.MaterialName);
                                data.Sections.Add(replacement);
                            }
                            data.AABB=source.AABB;
                            message.ReplacedModel=null;
                            CockpitMaterials.Prepare(Name(i),activeRig.Subtype,geometry);
                            MyRenderProxy.AddRuntimeModel(Name(i),message);
                            actors[i]=MyRenderProxy.CreateRenderEntity(Name(i),Name(i),world,MyMeshDrawTechnique.MESH,
                                RenderFlags.Visible|RenderFlags.ForceOldPipeline|RenderFlags.CastShadows,(CullingOptions)0,Color.White,paint);
                            Volatile.Write(ref check.Actors,actors.Take(i+1).ToArray());
                        }
                        Volatile.Write(ref check.Actors,actors);
                        appliedColor=paint;
                        for(int i=0;i<actors.Length;i++) foreach(var section in geometry.Parts[i].Sections)
                            MyRenderProxy.UpdateModelProperties(actors[i],CockpitMaterials.Name(activeRig.Subtype,section.MaterialName),RenderFlags.Visible,RenderFlags.Visible,null,null);
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
                if(activeRig.Wheel!=null)
                {
                    UpdatePose(check,activeRig.Wheel.Actor,(MatrixD)(nativeRest ? Matrix.Identity:left)*world);
                    SetRigFeedback(check,activeRig.Wheel.Actor,leftHeld || rightHeld ? 2:0);
                    if(activeRig.Wheel.ThrottleActor>=0)
                    {
                        Matrix twist=nativeRest ? Matrix.Identity:activeRig.Wheel.ThrottleVisual(throttlePreview ?? CockpitControls.ThrottleVisual);
                        UpdatePose(check,activeRig.Wheel.ThrottleActor,(MatrixD)(twist*(nativeRest ? Matrix.Identity:left))*world);
                        SetRigFeedback(check,activeRig.Wheel.ThrottleActor,rightHeld ? 2:0);
                    }
                }
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
        private static readonly System.Collections.Generic.Dictionary<string,string> screenTextures=new System.Collections.Generic.Dictionary<string,string>();
        private static void SyncSkin(MyCockpit cockpit) => SyncSkin(cockpit.SlimBlock.SkinSubtypeId,cockpit.Render.TextureChanges,cockpit.Render.MetalnessColorable);
        internal static void SyncSkin(MyStringHash skin,System.Collections.Generic.Dictionary<MyStringId,VRageRender.Messages.MyTextureChange> textures,bool metalnessColorable)
        {
            var check=verification;
            if(check==null || check.Actors.Length!=check.Verified.Length || appliedSkin==skin) return;
            var changes=CockpitMaterials.Skin(activeRig.Subtype,geometry.Parts.SelectMany(p=>p.Sections).Select(s=>s.MaterialName),textures);
            for(int i=0;i<check.Actors.Length;i++)
            {
                uint actor=check.Actors[i]; if(actor==uint.MaxValue) continue;
                MyRenderProxy.ChangeMaterialTexture(actor,(System.Collections.Generic.Dictionary<MyStringId,VRageRender.Messages.MyTextureChange>)null);
                if(changes.Count!=0) MyRenderProxy.ChangeMaterialTexture(actor,changes);
                foreach(string material in check.ActorMaterials[i])
                    MyRenderProxy.UpdateModelProperties(actor,material,metalnessColorable ? RenderFlags.MetalnessColorable:0,
                        metalnessColorable ? 0:RenderFlags.MetalnessColorable,null,null);
            }
            appliedSkin=skin;
            screenTextures.Clear();
        }
        internal static int MovingScreenActor(CockpitRig rig,string material) => rig?.Wheel==null ? -1 :
            rig.Pieces.Where(p=>p.Actor==rig.Wheel.Actor && p.Material==material).Select(p=>p.Actor).DefaultIfEmpty(-1).First();
        internal static void PreserveScreenVisibility(uint id,string material,ref RenderFlags add,ref RenderFlags remove)
        {
            if(verification==null || id!=verification.Interior || string.IsNullOrEmpty(material) || !material.StartsWith("CockpitScreen_",StringComparison.Ordinal) ||
                MovingScreenActor(activeRig,material)<0 || add==0 && remove==0) return;
            // The engine replaces this override on live LCD updates rather than merging it.
            // Without Visible, the engine translates remove flags into main/forward skip removal.
            add|=RenderFlags.Visible|Hidden; remove=(remove&~Hidden)|RenderFlags.Visible;
        }
        internal static void ScreenTexture(string material,string path)
        {
            if(!Ready) return;
            int actor=MovingScreenActor(activeRig,material);
            if(actor<0 || screenTextures.TryGetValue(material,out string previous) && previous==path) return;
            MyRenderProxy.ChangeMaterialTexture(verification.Actors[actor],CockpitMaterials.Name(activeRig.Subtype,material),path);
            screenTextures[material]=path;
        }
        private static void SyncScreens(MyCockpit cockpit)
        {
            if(!Ready || activeRig.Wheel==null || cockpit.BlockDefinition.ScreenAreas==null) return;
            var provider=(Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider)cockpit;
            for(int i=0;i<cockpit.BlockDefinition.ScreenAreas.Count;i++)
            {
                string material=cockpit.BlockDefinition.ScreenAreas[i].Name;
                if(MovingScreenActor(activeRig,material)<0) continue;
                var surface=provider.GetSurface(i);
                if(surface!=null) ScreenTexture(material,(string)Member(surface,"m_previousTextureID"));
            }
        }
        internal static MatrixD SurfaceWorld(Sandbox.ModAPI.IMyTerminalBlock block,int index)
        {
            if(block==owner && Ready && index>=0 && index<owner.BlockDefinition.ScreenAreas.Count &&
                MovingScreenActor(activeRig,owner.BlockDefinition.ScreenAreas[index].Name)>=0)
                return (MatrixD)CockpitControls.SteeringVisual*block.WorldMatrix;
            return block.WorldMatrix;
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
            foreach(var material in geometry.Parts[actor].Sections.Select(s=>s.MaterialName).Distinct()) ApplyFeedback(id,CockpitMaterials.Name(activeRig.Subtype,material),state);
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
                var expected=replacement<0 ? check.Materials:check.ActorMaterials[replacement];
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
                    if (Array.IndexOf(expected,material)>=0)
                    {
                        bool hidden=(value&mask)==mask;
                        if (id==check.Interior && !hidden) { check.NativeStatus="Visible native material "+material+": "+flags; return; }
                        nativeMaterials.Add(material);
                        if (replacement>=0 && (value&mask)!=0) throw new InvalidOperationException("Replacement cockpit proxy is hidden.");
                        found=true;
                    }
                    else if ((value&Convert.ToInt64(Enum.Parse(flags.GetType(),"SkipInMainView")))==0) otherVisible=true;
                }
                if (!found) return;
                if (id==check.Interior)
                {
                    if(nativeMaterials.Count!=check.Materials.Length) { check.NativeStatus="Missing native materials: "+string.Join(", ",check.Materials.Except(nativeMaterials)); return; }
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
        internal static MemberInfo Find(Type type,string name) => members.GetOrAdd((type,name),key=>
        {
            var field=AccessTools.Field(key.Item1,key.Item2);
            if(field!=null) return field;
            // A derived property can hide a base property with a different return type.
            for(var current=key.Item1;current!=null;current=current.BaseType)
            {
                var property=current.GetProperty(key.Item2,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(property!=null) return property;
            }
            return null;
        });
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
