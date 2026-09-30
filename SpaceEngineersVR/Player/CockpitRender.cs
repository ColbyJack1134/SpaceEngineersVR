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
            public long ReadyMask;
            public volatile string Error;
            public Verification(uint interior) { Interior=interior; }
        }
        private const int ActorCount=34;
        private const long ReadyMask=(1L<<ActorCount)-1;
        private static readonly string[] materials={FighterProfile.Material,CockpitSwitchGeometry.Material,CockpitCoverGeometry.Material};
        private static Verification verification;
        private static CockpitGeometry geometry;
        private static MyCockpit owner;
        private static bool failed;
        private static Vector3? appliedColor;
        private static DateTime deadline;
        public static bool Ready => verification!=null && verification.NativeHidden && Volatile.Read(ref verification.ReadyMask)==ReadyMask && verification.Error==null;
        public static string Status => failed ? "Sticks unavailable: button flight" : Ready ? "Fighter sticks ready" : "Checking fighter stick renderer";
        private static readonly string[] names={"SEVR_Fighter_LeftBase","SEVR_Fighter_LeftHandle","SEVR_Fighter_LeftStem","SEVR_Fighter_RightHandle","SEVR_Fighter_RightStem","SEVR_Fighter_RightBase"};

        private static string Name(int i) => i<6 ? names[i] : i==6 ? "SEVR_Fighter_Chrome" : i<20 ? "SEVR_Fighter_Switch"+(i-7) : i==20 ? "SEVR_Fighter_Interior" : "SEVR_Fighter_Cover"+(i-21);
        private static string Material(int i) => i<6 ? FighterProfile.Material : i<20 ? CockpitSwitchGeometry.Material : CockpitCoverGeometry.Material;

        public static void Reset()
        {
            var previous=Interlocked.Exchange(ref verification,null);
            if (previous!=null)
            {
                // Remove replacements before making the native material visible again.
                foreach (uint id in previous.Actors) MyRenderProxy.RemoveRenderObject(id,MyRenderProxy.ObjectType.Entity);
                foreach(string material in materials)
                    MyRenderProxy.UpdateModelProperties(previous.Interior,material,RenderFlags.Visible,RenderFlags.Visible|Hidden,null,null);
            }
            owner=null; failed=false; appliedColor=null; feedback.Clear();
        }
        public static void Update(MyCockpit cockpit,Matrix left,Matrix right,bool leftHeld,bool rightHeld,Vector3 leftOffset=default(Vector3),Vector3 rightOffset=default(Vector3))
        {
            if (owner!=cockpit) { Reset(); owner=cockpit; }
            if (cockpit==null || failed) return;
            var render=cockpit.Render as MyRenderComponentCockpit;
            if (render==null || render.RenderObjectIDs.Length<2 || render.InteriorRenderId==uint.MaxValue) return;
            UpdateScene(render.InteriorRenderId,cockpit.WorldMatrix,left,right,leftHeld,rightHeld,leftOffset,rightOffset,colorMask:cockpit.SlimBlock.ColorMaskHSV);
        }
        internal static void UpdateScene(uint interior,MatrixD world,Matrix left,Matrix right,bool leftHeld,bool rightHeld,Vector3 leftOffset=default(Vector3),Vector3 rightOffset=default(Vector3),float? switchPreview=null,float? coverPreview=null,Vector3? colorMask=null,int previewHover=-1,int previewHeld=-1,bool previewCover=false)
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
                    if (geometry==null) geometry=CockpitGeometry.Load(MyFileSystem.ContentPath);
                    verification=new Verification(interior);
                    deadline=DateTime.UtcNow.AddSeconds(8);
                    // This also converts the instance to the engine's supported per-material pipeline.
                    foreach(string material in materials)
                        MyRenderProxy.UpdateModelProperties(interior,material,RenderFlags.Visible|Hidden,RenderFlags.Visible,null,null);
                    Logger.Info("FIGHTER STICKS waiting for native material visibility verification");
                }
                var check=verification;
                Vector3 paint=colorMask ?? Vector3.Zero;
                if (check.Error!=null) throw new InvalidOperationException(check.Error);
                if (!Ready && DateTime.UtcNow>deadline) throw new TimeoutException("Fighter stick renderer did not confirm hidden native geometry and replacement meshes.");
                if (check.NativeHidden && check.Actors.Length==0)
                {
                    var actors=new uint[ActorCount];
                    for (int i=0;i<actors.Length;i++)
                    {
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
                    for(int i=0;i<actors.Length;i++) MyRenderProxy.UpdateModelProperties(actors[i],Material(i),RenderFlags.Visible,RenderFlags.Visible,null,null);
                }
                if (check.Actors.Length!=ActorCount) return;
                if(appliedColor!=paint)
                {
                    foreach(uint actor in check.Actors) MyRenderProxy.UpdateRenderEntity(actor,null,paint);
                    appliedColor=paint;
                }
                var matrices=new[] { Matrix.CreateTranslation(leftOffset),left,left,right,right,Matrix.CreateTranslation(rightOffset) };
                for (int i=0;i<6;i++) MyRenderProxy.UpdateRenderObject(check.Actors[i],(MatrixD)matrices[i]*world);
                MyRenderProxy.UpdateRenderObject(check.Actors[6],world);
                for(int i=0;i<CockpitCoverGeometry.Count;i++) MyRenderProxy.UpdateRenderObject(check.Actors[7+i],(MatrixD)CockpitSwitchGeometry.Visual(i,switchPreview ?? CockpitButtons.SwitchPosition(i))*world);
                MyRenderProxy.UpdateRenderObject(check.Actors[20],world);
                for(int i=0;i<CockpitCoverGeometry.Count;i++) MyRenderProxy.UpdateRenderObject(check.Actors[21+i],
                    (MatrixD)CockpitCoverGeometry.Visual(i,coverPreview ?? CockpitButtons.CoverPosition(i))*world);
                SetFeedback(check.Actors[1],FighterProfile.Material,leftHeld ? 2 : 0);
                SetFeedback(check.Actors[3],FighterProfile.Material,rightHeld ? 2 : 0);
                for(int i=0;i<CockpitCoverGeometry.Count;i++)
                {
                    var lever=CockpitTouch.Read("CockpitControl"+i);
                    var cover=CockpitTouch.Read("CockpitCover"+i);
                    SetFeedback(check.Actors[7+i],CockpitSwitchGeometry.Material,
                        !previewCover && i==previewHeld || lever.Held>=0 ? 2 : !previewCover && i==previewHover || lever.Hover>=0 ? 1 : 0);
                    SetFeedback(check.Actors[21+i],CockpitCoverGeometry.Material,
                        previewCover && i==previewHeld || cover.Held>=0 ? 2 : previewCover && i==previewHover || cover.Hover>=0 ? 1 : 0);
                }
            }
            catch(Exception ex)
            {
                var previousOwner=owner; Reset(); owner=previousOwner; failed=true;
                Logger.Warning(ex,"FIGHTER STICKS disabled; native interior and button flight restored");
            }
        }
        private static readonly System.Collections.Generic.Dictionary<uint,int> feedback=new System.Collections.Generic.Dictionary<uint,int>();
        private static void SetFeedback(uint id,string material,int state)
        {
            if (feedback.TryGetValue(id,out int prior) && prior==state) return;
            feedback[id]=state;
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
                bool found=false,otherVisible=false; int nativeMaterials=0;
                foreach (object lod in lods)
                foreach (object proxy in (IEnumerable)Member(lod,"RenderableProxies"))
                {
                    string material=Member(Member(Member(proxy,"Material"),"Info"),"Name").ToString();
                    object flags=Member(proxy,"Flags");
                    long mask=Convert.ToInt64(Enum.Parse(flags.GetType(),"SkipInMainView, SkipInDepth, SkipInForward"));
                    long value=Convert.ToInt64(flags);
                    if (Array.IndexOf(materials,material)>=0)
                    {
                        bool hidden=(value&mask)==mask;
                        if (id==check.Interior && !hidden) return;
                        nativeMaterials|=1<<Array.IndexOf(materials,material);
                        if (replacement>=0 && (value&mask)!=0) throw new InvalidOperationException("Replacement stick proxy is hidden.");
                        found=true;
                    }
                    else if ((value&Convert.ToInt64(Enum.Parse(flags.GetType(),"SkipInMainView")))==0) otherVisible=true;
                }
                if (!found) return;
                if (id==check.Interior)
                {
                    if(nativeMaterials!=7) return;
                    if (!otherVisible) throw new InvalidOperationException("Native stick suppression hid unrelated cockpit geometry.");
                    if (!check.NativeHidden) Logger.Info("FIGHTER STICKS native material hidden in main/depth/forward passes; other interior materials visible");
                    check.NativeHidden=true;
                }
                else
                {
                    lock(check)
                    {
                        long old=check.ReadyMask;
                        check.ReadyMask=old|(1L<<replacement);
                        if (old!=ReadyMask && check.ReadyMask==ReadyMask) Logger.Info("FIGHTER STICKS native stick and switch meshes verified in the stereo scene pipeline");
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
