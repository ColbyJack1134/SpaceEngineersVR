#if SEVR_DIAGNOSTICS
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRageMath;
using VRageRender.Messages;

namespace SpaceEngineersVR.Diagnostics
{
    public static class SmoothFramesTests
    {
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static T State<T>(string name) => (T)AccessTools.Field(typeof(SmoothFramesBridge),name).GetValue(null);
        private static SmoothFramesBridge.Tick SampledTick =>
            (SmoothFramesBridge.Tick)AccessTools.Field(typeof(SmoothFrames),"to").GetValue(null);
        private static VRage.Render.Scene.MyScene Scene() =>
            (VRage.Render.Scene.MyScene)Activator.CreateInstance(AccessTools.TypeByName("VRage.Render11.Scene.MyScene11"),true);
        public static void Upstream(Assembly upstream,Action<string> log)
        {
            var harmony=new Harmony("SEVR.SmoothFrames.Upstream.Tests");
            var main=typeof(SpaceEngineersVR.Plugin.Main);
            var failed=AccessTools.Field(main,"failed"); var wasFailed=failed.GetValue(null);
            bool world=SpaceEngineersVR.Plugin.Main.WorldAvailable;
            var frame=AccessTools.Field(typeof(CameraRig),"frame"); var oldFrame=frame.GetValue(null);
            var cameraProperty=AccessTools.Property(typeof(Sandbox.Game.World.MySector),"MainCamera");
            var oldCamera=cameraProperty.GetValue(null);
            var capture=upstream.GetType("SmoothFrames.CameraCapture");
            var render=upstream.GetType("SmoothFrames.RenderFrameSmoothing");
            var history=upstream.GetType("SmoothFrames.CameraHistory");
            var enabled=AccessTools.Field(upstream.GetType("SmoothFrames.Plugin"),"InterpolationEnabled");
            var wasEnabled=enabled.GetValue(null);
            var oldConfig=Sandbox.MySandboxGame.Config;
            var game=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.MySandboxGame));
            var afterDraw=AccessTools.Method(typeof(Sandbox.MySandboxGame),"AfterDraw");
            var proxyField=AccessTools.Field(typeof(VRageRender.MyRenderProxy),"m_render"); var originalProxy=proxyField.GetValue(null);
            var sharedField=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyRender11"),"SharedData"); var originalShared=sharedField.GetValue(null);
            try
            {
                Sandbox.MySandboxGame.Config=new Sandbox.Engine.Utils.MyConfig("sevr-smoothing-test.cfg");
                SmoothFramesBridge.Install(harmony);
                harmony.CreateClassProcessor(typeof(SpaceEngineersVR.Patches.CameraFramePatch)).Patch();
                harmony.CreateClassProcessor(typeof(SpaceEngineersVR.Patches.SmoothFramesCommitPatch)).Patch();
                SpaceEngineersVR.Patches.FrameInjections.InstallCamera(harmony);
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"SetupCameraMatrices"),
                    prefix:new HarmonyMethod(typeof(SmoothFramesTests),nameof(NoCamera)));
                harmony.Patch(AccessTools.Method(render,"TryResolveSyncPath"),prefix:new HarmonyMethod(typeof(SmoothFramesTests),nameof(NoScene)));
                AccessTools.Method(history,"Reset").Invoke(null,null);
                failed.SetValue(null,false); SpaceEngineersVR.Plugin.Main.WorldAvailable=true; enabled.SetValue(null,true);
                var camera=new VRage.Game.Utils.MyCamera(1,new VRageRender.MyViewport(0,0,640,480));
                cameraProperty.SetValue(null,camera);
                var shared=new VRageRender.MySharedData();
                sharedField.SetValue(null,shared);
                proxyField.SetValue(null,Activator.CreateInstance(AccessTools.TypeByName("VRageRender.MyDX11Render"),new object[] {null}));
                for(int i=1;i<=3;i++)
                {
                    var rig=new CameraRig.Frame(MatrixD.CreateTranslation(i,2,3),Matrix.Identity);
                    frame.SetValue(null,rig); camera.SetViewMatrix(MatrixD.Invert(rig.Anchor));
                    var message=NativeCamera();
                    shared.CurrentUpdateFrame.Enqueue(message);
                    AccessTools.Method(capture,"SnapAndDefer").Invoke(null,new object[] {camera});
                    Require(!(bool)AccessTools.Field(capture,"_hasPendingSnap").GetValue(null),"Early upstream capture was not deferred");
                    afterDraw.Invoke(game,null);
                    var tick=State<SmoothFramesBridge.Tick>("captured");
                    Require(tick.Batch>0 && tick.Snapshot>0,"Late entity capture did not precede native batch publication");
                    if(i==3)
                    {
                        RenderFrameBridge.Consume(message);
                        RunUpstream(render);
                        Require(SampledTick==null,"Older published pair applied to a newer native batch");
                    }
                    AccessTools.Method(capture,"PublishPendingSnap").Invoke(null,null);
                    if(i==2)
                    {
                        RunUpstream(render);
                        Require(SampledTick==null,"Future pair applied before its native batch arrived");
                    }
                    RenderFrameBridge.Consume(message);
                    RunUpstream(render);
                    Require(SmoothFramesBridge.Ready,"Upstream compatibility failed: "+SmoothFramesBridge.Failure);
                    if(i==1) continue;
                    Require(SampledTick==tick,"Real upstream synthetic camera did not retain the native batch");
                    Require(ReferenceEquals(RenderFrameBridge.Current,rig),"Real upstream synthetic camera cleared the VR packet");
                    double alpha=(double)AccessTools.Property(render,"LastSmoothingAlpha").GetValue(null);
                    var blended=SmoothFrames.RenderFrame(rig);
                    Require(Math.Abs(blended.TrackingToWorld.Translation.X-(i-1+alpha))<1e-6,"Camera used a different pair or alpha from upstream");
                    SmoothFrames.EndFrame();
                }
                enabled.SetValue(null,false); RunUpstream(render);
                Require(SampledTick==null && ReferenceEquals(SmoothFrames.RenderFrame((CameraRig.Frame)frame.GetValue(null)),frame.GetValue(null)),"Disabled interpolation retained a smoothed camera");
                enabled.SetValue(null,true);
                AccessTools.Method(history,"Reset").Invoke(null,null); RunUpstream(render);
                Require(SampledTick==null,"Reset history retained a sample");
                NativeBones(log);
                log("PASS installed SmoothFrames active snapshots: late capture, native commit, both publication race directions, exact pair/alpha, packet retention, disabled/reset fallback");
            }
            finally
            {
                SmoothFrames.EndFrame(); SmoothFramesBridge.Dispose();
                failed.SetValue(null,wasFailed); SpaceEngineersVR.Plugin.Main.WorldAvailable=world;
                frame.SetValue(null,oldFrame); cameraProperty.SetValue(null,oldCamera); enabled.SetValue(null,wasEnabled);
                proxyField.SetValue(null,originalProxy); sharedField.SetValue(null,originalShared); Sandbox.MySandboxGame.Config=oldConfig;
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                RenderFrameBridge.Consume(new object());
            }
        }
        private static void RunUpstream(Type render)
        {
            AccessTools.Method(typeof(SmoothFramesBridge),"QueueStart").Invoke(null,new object[] {true});
            AccessTools.Method(render,"RunFrame").Invoke(null,null);
        }
        private static bool NoCamera() => false;
        private static bool NoScene(ref bool __result) { __result=false; return false; }
        private static Harmony cockpitFixture;
        internal static void BeginNativeCockpit()
        {
            if(cockpitFixture!=null) return;
            cockpitFixture=new Harmony("SEVR.SmoothFrames.NativeCockpitFixture");
            SmoothFramesBridge.Attach(cockpitFixture,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
            cockpitFixture.CreateClassProcessor(typeof(SpaceEngineersVR.Patches.CockpitTransformPatch)).Patch();
        }
        internal static void EndNativeCockpit()
        {
            if(cockpitFixture==null) return;
            foreach(var method in cockpitFixture.GetPatchedMethods().ToArray()) cockpitFixture.Unpatch(method,HarmonyPatchType.All,cockpitFixture.Id);
            cockpitFixture=null; SmoothFramesBridge.Stop();
        }
        internal static void NativeCockpit(Action<string> log)
        {
            var check=(CockpitRender.Verification)AccessTools.Field(typeof(CockpitRender),"verification").GetValue(null);
            var parent=VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>.FindByID(check.Interior);
            var actors=check.Actors.Where(id=>id!=uint.MaxValue).Select(VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>.FindByID).ToArray();
            Require(actors.Length>0 && actors.All(a=>a!=null && ReferenceEquals(a.Parent,parent)),"Cockpit replacements are not attached to the native interior");
            var grid=parent.Parent as VRage.Render.Scene.MyActor;
            Require(grid!=null,"Native cockpit fixture has no grid parent");
            MatrixD original=grid.WorldMatrix;
            var local=actors.Select(a=>a.WorldMatrix*MatrixD.Invert(original)).ToArray();
            var update=AccessTools.Method(parent.Scene.GetType(),"Update");
            try
            {
                foreach(double alpha in new[] {.16,.82,.92,1d,1d})
                {
                    var moved=MatrixD.CreateRotationY(alpha*.7)*MatrixD.CreateTranslation(1e8+alpha*.08,-2e8,3e8);
                    grid.SetMatrix(ref moved);
                    update.Invoke(parent.Scene,null);
                    for(int read=0;read<2;read++) for(int i=0;i<actors.Length;i++)
                    {
                        var expected=local[i]*moved; var actual=actors[i].WorldMatrix;
                        Require(Vector3D.Distance(actual.Translation,expected.Translation)<1e-5 && Vector3D.Distance(actual.Forward,expected.Forward)<1e-5,
                            "Cockpit replacement did not follow repeated grid movement during pose read "+read);
                    }
                }
            }
            finally
            {
                grid.SetMatrix(ref original); update.Invoke(parent.Scene,null);
            }
            log("PASS native cockpit hierarchy: "+actors.Length+" replacement actors follow repeated grid rotation/translation before repeated native pose reads at large coordinates");
        }
        private static void NativeBones(Action<string> log)
        {
            var type=AccessTools.TypeByName("VRage.Render11.Scene.Components.MySkinningComponent");
            var skin=Activator.CreateInstance(type,true);
            var hierarchyField=AccessTools.Field(type,"m_skeletonHierarchy");
            var boneType=hierarchyField.FieldType.GetElementType();
            var hierarchy=Array.CreateInstance(boneType,2);
            for(int i=0;i<2;i++)
            {
                var bone=Activator.CreateInstance(boneType);
                AccessTools.Field(boneType,"SkinTransform").SetValue(bone,Matrix.Identity); hierarchy.SetValue(bone,i);
            }
            hierarchyField.SetValue(skin,hierarchy);
            AccessTools.Field(type,"m_skeletonIndices").SetValue(skin,new[] {0,1});
            AccessTools.Field(type,"m_absoluteTransforms").SetValue(skin,new Matrix[2]);
            AccessTools.Field(type,"m_skinTransforms").SetValue(skin,new Matrix[2]);
            var current=new[] {Matrix.Identity,Matrix.CreateTranslation(2,3,4)};
            var blended=new[] {Matrix.Identity,(Matrix)SmoothFrames.Blend(Matrix.CreateTranslation(0,3,4),current[1],.25)};
            SmoothFramesBridge.SetRenderBones(skin,blended);
            var absolute=(Matrix[])AccessTools.Field(type,"m_absoluteTransforms").GetValue(skin);
            var gpu=(Matrix[])AccessTools.Field(type,"m_skinTransforms").GetValue(skin);
            Require(Math.Abs(absolute[1].Translation.X-.5)<1e-6 && Matrix.Transpose(gpu[1])==absolute[1],"Native skin and absolute bones disagree");
            SmoothFramesBridge.SetRenderBones(skin,current);
            Require(absolute[1]==current[1] && Matrix.Transpose(gpu[1])==current[1],"Native skeleton restoration failed");
            MovingWrist(skin,current,log);
            log("PASS installed renderer skeleton: interpolated absolute bones update GPU skin matrices and restore together");
        }
        private static void MovingWrist(object skin,Matrix[] current,Action<string> log)
        {
            var actor=new VRage.Render.Scene.MyActor(Scene());
            var tracker=new VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>();
            const uint id=uint.MaxValue-10;
            Require(VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>.FindByID(id)==null,"Fixture actor ID already used");
            AccessTools.Field(actor.GetType(),"m_ID").SetValue(actor,tracker); actor.SetID(id);
            actor.AddComponent(skin.GetType(),(VRage.Render.Scene.Components.MyActorComponent)skin);
            var surfaces=AccessTools.Property(typeof(RenderFrameBridge),"Surfaces"); var oldSurfaces=surfaces.GetValue(null);
            try
            {
                foreach(double alpha in new[] {0d,.25,.5,.75,1}) foreach(float fold in new[] {0f,1f})
                {
                    var parentA=MatrixD.CreateTranslation(1e8,-2e8,3e8);
                    var parentB=MatrixD.CreateRotationY(.1)*MatrixD.CreateTranslation(1e8+.1,-2e8,3e8+.03);
                    var rendered=SmoothFrames.Blend(parentA,parentB,alpha); actor.LastWorldMatrix=rendered;
                    var before=new[] {Matrix.Identity,Matrix.CreateRotationX(.2f)*Matrix.CreateTranslation(2.1f,3,4)};
                    var cameraLocal=MatrixD.CreateTranslation(0,1.7,0);
                    var a=new SmoothFramesBridge.Tick {CharacterId=23,CharacterActor=id,Character=parentA,Forearm=1,
                        Rig=new CameraRig.Frame(cameraLocal*parentA,Matrix.Identity),Bones=new SmoothFrames.Bones {Actor=id,Poses=before}};
                    var b=new SmoothFramesBridge.Tick {CharacterId=23,CharacterActor=id,Character=parentB,Forearm=1,
                        Rig=new CameraRig.Frame(cameraLocal*parentB,Matrix.Identity),Bones=new SmoothFrames.Bones {Actor=id,Poses=current}};
                    var publishedBone=(MatrixD)current[1]*parentB;
                    var panels=SpatialUi.WristViews((MatrixD)TrackedArms.WristScreenLocal*publishedBone,fold,1,null,new SurfaceKey[0]);
                    foreach(var panel in panels) panel.WristLocal=panel.Pose*MatrixD.Invert(publishedBone);
                    surfaces.SetValue(null,panels); SmoothFrames.Sample(a,b,alpha);
                    var frame=SmoothFrames.RenderFrame(b.Rig);
                    Require(Vector3D.Distance(frame.TrackingToWorld.Translation,(cameraLocal*rendered).Translation)<1e-5,"Walking camera drifted from the rendered character");
                    var actualBone=SmoothFramesBridge.RenderBones(actor)[1];
                    var expected=SpatialUi.WristViews((MatrixD)TrackedArms.WristScreenLocal*actualBone*rendered,fold,1,null,new SurfaceKey[0]);
                    var actual=SmoothFrames.Surfaces(panels);
                    for(int i=0;i<panels.Length;i++)
                        Require(Vector3D.Distance(actual[i].Pose.Translation,expected[i].Pose.Translation)<1e-5 &&
                            Vector3D.Distance(actual[i].Pose.Forward,expected[i].Pose.Forward)<1e-5,"Wrist drifted from the native rendered forearm during body/hand movement");
                    SmoothFrames.EndFrame();
                    Require(SmoothFramesBridge.RenderBones(actor)[1]==current[1],"Moving wrist test left blended bones behind");
                }
                log("PASS moving body/wrist: production camera and compact/expanded wrist poses follow native actor/bones at five alphas and large coordinates despite later simulation bone changes");
            }
            finally
            {
                SmoothFrames.EndFrame(); SmoothFrames.Clear(); surfaces.SetValue(null,oldSurfaces);
                AccessTools.Method(tracker.GetType(),"Deregister").Invoke(tracker,null);
            }
        }
        public static void Run(Action<string> log)
        {
            NativeHandoff(log);
            NestedCockpit(log);
            Faults(log);
            var harmony=new Harmony("SEVR.SmoothFramesBridge.Tests");
            var camera=new MyRenderMessageSetCameraViewMatrix {ViewMatrix=MatrixD.Identity,Smooth=true};
            var lateCamera=new MyRenderMessageSetCameraViewMatrix {ViewMatrix=MatrixD.Identity,Smooth=true};
            var rig=new CameraRig.Frame(MatrixD.Identity,Matrix.Identity);
            try
            {
                History.Previous=History.Current=default(Snapshot);
                SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                for(int i=1;i<=3;i++)
                {
                    RenderFrameBridge.Capture(camera,rig);
                    Capture.Next=new Snapshot {TimestampTicks=i*100-1,Smooth=true};
                    Capture.SnapAndDefer();
                    RenderFrameBridge.Capture(lateCamera,rig);
                    Capture.Next.TimestampTicks=i*100;
                    Capture.SnapAndDefer();
                    RenderFrameBridge.Commit(new SurfaceView[0]);
                    Capture.PublishPendingSnap();
                    RenderFrameBridge.Consume(camera);
                    RenderFrameBridge.Consume(lateCamera);
                    Require(SmoothFramesBridge.Ready,"SmoothFrames capture failed: "+SmoothFramesBridge.Failure);
                    Require(State<SmoothFramesBridge.Tick>("current")?.Snapshot==i*100,"Repeated camera upload lost the latest native snapshot");
                    Require(ReferenceEquals(RenderFrameBridge.Current,rig),"Compatibility changed native camera consumption");
                }
                History.TryGetPair(out var outsidePrevious,out var outsideCurrent);
                Require(outsidePrevious.TimestampTicks==200 && outsideCurrent.TimestampTicks==300,"Compatibility changed out values outside RunFrame");
                Task.Run(()=>History.TryGetPair(out _,out _)).GetAwaiter().GetResult();
                Require(State<SmoothFramesBridge.Tick>("pairCurrent")==null,"Non-render history reads entered the smoothing sample");
                RunUpstream(typeof(Render));
                Require(State<SmoothFramesBridge.Tick>("pairPrevious")?.Snapshot==200 && State<SmoothFramesBridge.Tick>("pairCurrent")?.Snapshot==300,
                    "Boxed out snapshots lost their exact source tick");
                Require(History.Current.TimestampTicks==300 && History.Previous.TimestampTicks==200 && Render.LastSmoothingAlpha==.25 &&
                    Render.LastPrevious==200 && Render.LastCurrent==300,
                    "Compatibility changed upstream output");
                Require(ReferenceEquals(RenderFrameBridge.Current,rig),"Synthetic camera cleared the native VR packet");
                Require(SampledTick==State<SmoothFramesBridge.Tick>("current") && SampledTick?.Batch>0,"Published smoothing pair lost the native batch");

                Plugin.InterpolationEnabled=false;
                RunUpstream(typeof(Render));
                Require(State<SmoothFramesBridge.Tick>("pairCurrent")==null && SampledTick==null,"Disabled interpolation acquired or retained a sample");
                Plugin.InterpolationEnabled=true; History.Current=new Snapshot {TimestampTicks=400,Smooth=false};
                RunUpstream(typeof(Render));
                Require(SampledTick==null,"Camera cut was reported as applied smoothing");
                Render.Throw=true;
                bool threw=false;
                try { Render.RunFrame(); } catch(InvalidOperationException ex) when(ex.Message=="upstream failure") { threw=true; }
                Require(threw,"Compatibility swallowed an upstream exception outside VR");
                Require(!State<bool>("inSmoothing"),"Exception retained the render sample scope");
                Coherence();
                log("PASS SmoothFrames compatibility: pooled camera reuse, exact boxed snapshot pairs, publication identity, other-thread exclusion, disabled/cut paths and flatscreen exception/result preservation");
            }
            finally
            {
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                SmoothFramesBridge.Stop(); Render.Throw=false; Plugin.InterpolationEnabled=true;
                RenderFrameBridge.Consume(new object());
            }
        }
        private static void Faults(Action<string> log)
        {
            var mainFailed=AccessTools.Field(typeof(SpaceEngineersVR.Plugin.Main),"failed"); var oldFailed=mainFailed.GetValue(null);
            bool oldWorld=SpaceEngineersVR.Plugin.Main.WorldAvailable;
            var harmony=new Harmony("SEVR.SmoothFrames.FaultTests");
            var getter=AccessTools.Field(typeof(SmoothFramesBridge),"getSkinning"); var oldGetter=getter.GetValue(null);
            var setter=AccessTools.Field(typeof(SmoothFramesBridge),"setBones"); var oldSetter=setter.GetValue(null);
            var surfaceField=AccessTools.Property(typeof(RenderFrameBridge),"Surfaces"); var oldSurfaces=surfaceField.GetValue(null);
            var actor=new VRage.Render.Scene.MyActor(Scene());
            var tracker=new VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>();
            const uint id=uint.MaxValue-13;
            Require(VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>.FindByID(id)==null,"Fault fixture actor already exists");
            AccessTools.Field(actor.GetType(),"m_ID").SetValue(actor,tracker); actor.SetID(id); actor.LastWorldMatrix=MatrixD.Identity;
            var fallback=new CameraRig.Frame(MatrixD.Identity,Matrix.Identity);
            var a=new SmoothFramesBridge.Tick {Rig=fallback,Bones=new SmoothFrames.Bones {Actor=id,Poses=new[] {Matrix.Identity}}};
            var b=new SmoothFramesBridge.Tick {Rig=fallback,Bones=new SmoothFrames.Bones {Actor=id,Poses=new[] {Matrix.CreateTranslation(1,0,0)}}};
            try
            {
                mainFailed.SetValue(null,false); SpaceEngineersVR.Plugin.Main.WorldAvailable=true;
                surfaceField.SetValue(null,null);
                for(int stage=0;stage<3;stage++)
                {
                    SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                    getter.SetValue(null,AccessTools.Method(typeof(SmoothFramesTests),nameof(FaultSkin)));
                    setter.SetValue(null,AccessTools.Method(typeof(FaultBones),nameof(FaultBones.Set)));
                    faultBones=new FaultBones {ThrowAt=stage==0 ? 0:stage}; faultGetter=stage==0;
                    SmoothFrames.Sample(a,b,.5);
                    var frame=SmoothFrames.RenderFrame(fallback);
                    if(stage<2) Require(ReferenceEquals(frame,fallback),"Bone getter/application failure lost native camera fallback");
                    SmoothFrames.EndFrame();
                    Require(!SmoothFramesBridge.Ready && !(bool)mainFailed.GetValue(null),"Bone failure stopped VR or left compatibility active");
                    Require(AccessTools.Field(typeof(SmoothFrames),"skin").GetValue(null)==null &&
                        AccessTools.Field(typeof(SmoothFrames),"restoreBones").GetValue(null)==null,"Bone failure retained restoration state");
                    Require(ReferenceEquals(SmoothFrames.RenderFrame(fallback),fallback),"Failed compatibility altered the fallback frame");
                    if(stage==1) Require(faultBones.Current==b.Bones.Poses,"Failed bone application did not restore the native pose");
                    foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                }
                SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                AccessTools.Field(typeof(SmoothFramesBridge),"synthetic").SetValue(null,AccessTools.Field(typeof(FaultBones),nameof(FaultBones.Current)));
                AccessTools.Field(typeof(SmoothFramesBridge),"enabled").SetValue(null,AccessTools.Field(typeof(FaultBones),nameof(FaultBones.Current)));
                Require(!SmoothFramesBridge.IsSynthetic(new object()) && !SmoothFramesBridge.Ready && !(bool)mainFailed.GetValue(null),
                    "Synthetic identity or failure-reporting reflection escaped compatibility");
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);

                SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                Plugin.InterpolationEnabled=true; Render.LastCurrent=-1;
                History.Previous=new Snapshot {TimestampTicks=1,Smooth=true}; History.Current=new Snapshot {TimestampTicks=2,Smooth=true};
                AccessTools.Field(typeof(SmoothFramesBridge),"stamp").SetValue(null,AccessTools.Field(typeof(FaultBones),nameof(FaultBones.Current)));
                Render.RunFrame();
                Require(Render.LastCurrent==-1 && !SmoothFramesBridge.Ready && !(bool)mainFailed.GetValue(null),"Pair lookup failure allowed the in-flight interpolation");
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);

                SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                Render.Throw=true;
                try { Render.RunFrame(); }
                finally { Render.Throw=false; }
                Require(!SmoothFramesBridge.Ready && !(bool)mainFailed.GetValue(null),"Upstream interpolation exception escaped the VR fallback");
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);

                for(int stage=0;stage<2;stage++)
                {
                    int failStage=stage;
                    var group=new SpaceEngineersVR.Plugin.PatchGroup("SmoothFrames fault fixture","SmoothFramesFaultFixture",h=> {
                        SmoothFramesBridge.Protect(typeof(Render),typeof(Plugin));
                        if(failStage==0) throw new MissingFieldException("injected pre-attach failure");
                        SmoothFramesBridge.Attach(h,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                        throw new MissingMethodException("injected post-attach failure");
                    },off:()=>SmoothFramesBridge.Fail(new InvalidOperationException("injected attach rollback")));
                    try
                    {
                        SmoothFramesBridge.Stop();
                        AccessTools.Field(typeof(SmoothFramesBridge),"failed").SetValue(null,false);
                        Require(!group.Apply() && !group.Harmony.GetPatchedMethods().Any(),"Failed attachment retained compatibility hooks");
                        Plugin.InterpolationEnabled=true; Render.LastCurrent=-1; Render.RunFrame();
                        Require(Render.LastCurrent==-1 && !SmoothFramesBridge.Ready && !(bool)mainFailed.GetValue(null),"Attachment failure left interpolation active or stopped VR");
                    }
                    finally { group.Remove(); SmoothFramesBridge.Dispose(); }
                }
                InterpolationRestore(harmony,mainFailed,log);
                SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                SmoothFrames.Sample(a,b,.5); SmoothFramesBridge.Reset();
                Require(ReferenceEquals(SmoothFrames.RenderFrame(fallback),fallback) && State<SmoothFramesBridge.Tick>("current")==null,
                    "World reset retained a sampled frame");
                SmoothFramesBridge.Stop();
                var surfaces=new[] {new SurfaceView()};
                Require(ReferenceEquals(SmoothFrames.RenderFrame(fallback),fallback) && ReferenceEquals(SmoothFrames.Surfaces(surfaces),surfaces),
                    "Inactive compatibility changed the native frame or surfaces");
                log("PASS compatibility faults: bone get/apply/restore, synthetic identity, failure reporting, pair lookup, upstream interpolation, early/late attachment rollback, upstream guard, world reset and inactive passthrough; VR failure flag unchanged");
            }
            finally
            {
                SmoothFrames.EndFrame(); SmoothFramesBridge.Dispose();
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                getter.SetValue(null,oldGetter); setter.SetValue(null,oldSetter); surfaceField.SetValue(null,oldSurfaces);
                mainFailed.SetValue(null,oldFailed); SpaceEngineersVR.Plugin.Main.WorldAvailable=oldWorld; Plugin.InterpolationEnabled=true;
                AccessTools.Method(tracker.GetType(),"Deregister").Invoke(tracker,null);
            }
        }
        private static void InterpolationRestore(Harmony harmony,FieldInfo mainFailed,Action<string> log)
        {
            var cleanup=AccessTools.Field(typeof(SpaceEngineersVR.Plugin.Main),"cleanupRequested");
            var oldCleanup=cleanup.GetValue(null);
            var main=new SpaceEngineersVR.Plugin.Main();
            try
            {
                SmoothFramesBridge.Protect(typeof(Render),typeof(Plugin));
                foreach(bool enabled in new[] { true,false })
                {
                    foreach(bool dispose in new[] { false,true })
                    foreach(bool changeSetting in new[] { false,true })
                    {
                        mainFailed.SetValue(null,false);
                        SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                        Plugin.SetInterpolationEnabled(enabled);
                        var error=new InvalidOperationException("injected bridge failure");
                        SmoothFramesBridge.Fail(error);
                        SmoothFramesBridge.Fail(error);
                        Require(!Plugin.InterpolationEnabled,"Bridge failure left interpolation enabled in VR");
                        if(changeSetting)
                        {
                            Plugin.SetInterpolationEnabled(true);
                            Plugin.SetInterpolationEnabled(!enabled);
                        }
                        if(dispose) SmoothFramesBridge.Dispose();
                        else
                        {
                            mainFailed.SetValue(null,true);
                            cleanup.SetValue(null,true);
                            main.Update();
                        }
                        Require(Plugin.InterpolationEnabled==(changeSetting ? !enabled:enabled),"VR exit lost the latest SmoothFrames preference");
                    }
                }
                log("PASS SmoothFrames interpolation setting on desktop fallback and disposal, initially enabled and disabled, including settings changed during failure");
            }
            finally
            {
                cleanup.SetValue(null,oldCleanup);
                mainFailed.SetValue(null,false);
                SmoothFramesBridge.Dispose();
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
            }
        }
        private static FaultBones faultBones;
        private static bool faultGetter;
        private static object FaultSkin(VRage.Render.Scene.MyActor actor)
        {
            if(faultGetter) throw new InvalidOperationException("injected bone getter failure");
            return faultBones;
        }
        private sealed class FaultBones
        {
            public int Calls,ThrowAt;
            public Matrix[] Current;
            public void Set(Matrix[] bones,MyBoneDecalUpdate[] decals)
            {
                if(++Calls==ThrowAt) throw new InvalidOperationException("injected bone write failure");
                Current=bones;
            }
        }
        private static void NestedCockpit(Action<string> log)
        {
            if(ParallelTasks.Parallel.Scheduler==null)
                ParallelTasks.Parallel.Scheduler=new ParallelTasks.WorkStealingScheduler(1,System.Threading.ThreadPriority.Normal);
            var scene=Scene();
            var grid=new VRage.Render.Scene.MyActor(scene);
            var interior=new VRage.Render.Scene.MyActor(scene);
            var control=new VRage.Render.Scene.MyActor(scene);
            grid.Construct("SEVR test grid"); interior.Construct("SEVR test interior"); control.Construct("SEVR test control");
            var root=MatrixD.CreateTranslation(1e8,-2e8,3e8);
            var mount=Matrix.CreateRotationY(.2f)*Matrix.CreateTranslation(2,3,4);
            var local=Matrix.CreateRotationX(.3f)*Matrix.CreateTranslation(.1f,.2f,.3f);
            const uint interiorId=uint.MaxValue-21,controlId=uint.MaxValue-22;
            Require(VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>.FindByID(interiorId)==null &&
                VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>.FindByID(controlId)==null,"Fixture cockpit IDs already used");
            interior.SetID(interiorId); control.SetID(controlId);
            var field=AccessTools.Field(typeof(CockpitRender),"verification"); var previous=field.GetValue(null);
            var check=(CockpitRender.Verification)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(CockpitRender.Verification));
            AccessTools.Field(typeof(CockpitRender.Verification),"Interior").SetValue(check,interiorId);
            check.Parented=true; check.Actors=new[] {controlId,uint.MaxValue}; field.SetValue(null,check);
            var harmony=new Harmony("SEVR.CockpitTransforms.Tests");
            try
            {
                grid.SetMatrix(ref root);
                interior.SetParent(grid); interior.SetRelativeTransform(ref mount); interior.UpdateWorldMatrix();
                control.SetParent(interior); control.SetRelativeTransform(ref local); control.UpdateWorldMatrix();
                var update=AccessTools.Method(scene.GetType(),"Update");
                update.Invoke(scene,null);
                harmony.CreateClassProcessor(typeof(SpaceEngineersVR.Patches.CockpitTransformPatch)).Patch();
                foreach(double alpha in new[] {.16,.82,.92,1d,1d,0d})
                {
                    var moved=MatrixD.CreateRotationY(alpha*.1)*root;
                    moved.Translation+=new Vector3D(alpha*.08,0,0);
                    grid.SetMatrix(ref moved);
                    update.Invoke(scene,null);
                    var expected=(MatrixD)local*mount*moved;
                    for(int read=0;read<2;read++)
                    {
                        Require(Vector3D.Distance(control.WorldMatrix.Translation,expected.Translation)<1e-5 &&
                            Vector3D.Distance(control.WorldMatrix.Forward,expected.Forward)<1e-5,
                            "Nested cockpit control retained its previous pose on a repeated render, alpha="+alpha);
                        Require(control.RelativeTransform==local && interior.RelativeTransform==mount,"Render refresh changed cockpit placement");
                    }
                }
                log("PASS nested cockpit: native scene update refreshes grid/interior/control hierarchy before repeated native pose reads on repeated renders and return to native pose");
            }
            finally
            {
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                field.SetValue(null,previous); control.Destruct(); interior.Destruct(); grid.Destruct();
            }
        }
        private static void NativeHandoff(Action<string> log)
        {
            Assembly.Load("VRage.Render11");
            var harmony=new Harmony("SEVR.NativeCameraHandoff.Tests");
            var frameField=AccessTools.Field(typeof(CameraRig),"frame"); var original=frameField.GetValue(null);
            var failedField=AccessTools.Field(typeof(SpaceEngineersVR.Plugin.Main),"failed"); var originalFailed=failedField.GetValue(null);
            var surfaceField=AccessTools.Field(typeof(SpatialUi),"current"); var originalSurfaces=surfaceField.GetValue(null);
            var proxyField=AccessTools.Field(typeof(VRageRender.MyRenderProxy),"m_render"); var originalProxy=proxyField.GetValue(null);
            var renderer=AccessTools.TypeByName("VRageRender.MyRender11");
            var sharedField=AccessTools.Field(renderer,"SharedData"); var originalShared=sharedField.GetValue(null);
            var process=AccessTools.Method(renderer,"ProcessRenderFrame");
            var preframe=AccessTools.Method(renderer,"ProcessPreFrame");
            var shared=new VRageRender.MySharedData();
            var oldConfig=Sandbox.MySandboxGame.Config;
            var game=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.MySandboxGame));
            var afterDraw=AccessTools.Method(typeof(Sandbox.MySandboxGame),"AfterDraw");
            try
            {
                Sandbox.MySandboxGame.Config=new Sandbox.Engine.Utils.MyConfig("sevr-batch-test.cfg");
                sharedField.SetValue(null,shared);
                proxyField.SetValue(null,Activator.CreateInstance(AccessTools.TypeByName("VRageRender.MyDX11Render"),new object[] {null}));
                failedField.SetValue(null,false);
                var camera=new VRage.Game.Utils.MyCamera(1,new VRageRender.MyViewport(0,0,640,480));
                SmoothFramesBridge.Attach(harmony,typeof(Render),typeof(Capture),typeof(History),typeof(Plugin));
                // Prepare the real producer before attaching the publication hook.
                camera.UploadViewMatrixToRender(); afterDraw.Invoke(game,null);
                harmony.CreateClassProcessor(typeof(SpaceEngineersVR.Patches.SmoothFramesCommitPatch)).Patch();
                SpaceEngineersVR.Patches.FrameInjections.InstallCamera(harmony);
                harmony.Patch(AccessTools.Method(renderer,"SetupCameraMatrices"),prefix:new HarmonyMethod(typeof(SmoothFramesTests),nameof(NoCamera)));
                for(int i=0;i<3;i++)
                {
                    var frame=new CameraRig.Frame(MatrixD.CreateTranslation(100+i,20,30),Matrix.Identity);
                    var views=new[] {new SurfaceView {Id="Wrist",Style=SurfaceStyle.WristStatus,Pose=frame.Anchor}};
                    frameField.SetValue(null,frame);
                    if(i==1) VRageRender.MyRenderProxy.BeginRecordingDeferredMessages();
                    camera.SetViewMatrix(MatrixD.Invert(frame.Anchor)); camera.UploadViewMatrixToRender();
                    if(i==1) VRageRender.MyRenderProxy.ExecuteCommands(VRageRender.MyRenderProxy.FinishRecordingDeferredMessages());
                    surfaceField.SetValue(null,views);
                    afterDraw.Invoke(game,null);
                    frameField.SetValue(null,new CameraRig.Frame(MatrixD.CreateTranslation(1000,0,0),Matrix.Identity));
                    surfaceField.SetValue(null,new SurfaceView[0]);
                    VRageRender.MyUpdateFrame batch;
                    bool previous;
                    while((batch=shared.GetRenderFrame(out previous))!=null)
                    {
                        if(previous) preframe.Invoke(null,new object[] {batch,true});
                        else { process.Invoke(null,new object[] {batch,true}); break; }
                    }
                    Require(ReferenceEquals(RenderFrameBridge.Current,frame),"Real native render queue lost its camera batch");
                    Require(ReferenceEquals(RenderFrameBridge.Surfaces,views),"Real native render queue mixed newer wrist surfaces into an older batch");
                    AccessTools.Method(renderer,"SetupCameraMatrices").Invoke(null,new object[] {new MyRenderMessageSetCameraViewMatrix()});
                    Require(ReferenceEquals(RenderFrameBridge.Current,frame) && ReferenceEquals(RenderFrameBridge.Surfaces,views),
                        "Auxiliary camera setup erased the queued world packet");
                }
                log("PASS native camera pipeline: prepared MyCamera producer, direct/deferred commands, MyRenderProxy publication and actual renderer queue retain matching camera/wrist packets");
            }
            finally
            {
                SmoothFramesBridge.Stop();
                frameField.SetValue(null,original); failedField.SetValue(null,originalFailed); surfaceField.SetValue(null,originalSurfaces);
                proxyField.SetValue(null,originalProxy); sharedField.SetValue(null,originalShared); Sandbox.MySandboxGame.Config=oldConfig;
                foreach(var method in harmony.GetPatchedMethods().ToArray()) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                RenderFrameBridge.Consume(new object());
            }
        }
        private static void Coherence()
        {
            var origin=Matrix.CreateTranslation(-.3f,0,.2f);
            var basis=MatrixD.CreateRotationY(.4); basis.Translation=new Vector3D(1e8,2e8,-3e8);
            var a=new SmoothFramesBridge.Tick {Batch=1,Owner=23,Smooth=true,Rig=new CameraRig.Frame(basis,Matrix.Identity)};
            var b=new SmoothFramesBridge.Tick {Batch=2,Owner=23,Smooth=true,Rig=new CameraRig.Frame(MatrixD.Invert(origin)*basis,origin)};
            Require(SmoothFrames.Accept(a,b,b),"Compensating roomscale rebase rejected");
            var mixed=SmoothFrames.Blend(SmoothFrames.Basis(a),SmoothFrames.Basis(b),.35);
            Require(Vector3D.Distance(mixed.Translation,basis.Translation)<1e-5 && Vector3D.Distance(mixed.Forward,basis.Forward)<1e-5,
                "Compensating origin/anchor rebase moved the rendered camera");
            Require(!SmoothFrames.Accept(a,b,a),"Future history pair applied to an older native batch");
            b.Owner=24; Require(!SmoothFrames.Accept(a,b,b),"Owner transition interpolated"); b.Owner=23;
            b.Generation=1; Require(!SmoothFrames.Accept(a,b,b),"World transition interpolated"); b.Generation=0;
            b.Smooth=false; Require(!SmoothFrames.Accept(a,b,b),"Camera cut interpolated"); b.Smooth=true;
            a.Rig=b.Rig=null; a.Camera=basis; b.Camera=MatrixD.CreateTranslation(1,0,0)*basis; a.Origin=b.Origin=Matrix.Identity;
            Require(SmoothFrames.Accept(a,b,b),"Native cockpit pair incorrectly required a character rig");
            var local=MatrixD.CreateRotationX(.7)*MatrixD.CreateTranslation(.03,.02,.01);
            var moved=MatrixD.CreateRotationY(.1)*basis;
            var tick=local*basis;
            var attached=tick*MatrixD.Invert(basis)*moved;
            Require(Vector3D.Distance(attached.Translation,(local*moved).Translation)<1e-5,"Render-parent attachment lost precision");
            var next=MatrixD.CreateRotationY(.8)*basis;
            var actual=MatrixD.CreateRotationY(.3)*MatrixD.CreateTranslation(3,5,7)*basis;
            var parented=SmoothFrames.AttachedBasis(local*basis,local*next,basis,next,actual,.35);
            Require(Vector3D.Distance(parented.Translation,(local*actual).Translation)<1e-5 && Vector3D.Distance(parented.Forward,(local*actual).Forward)<1e-5,
                "Camera drifted from the actual parent when its interpolation pivot differed");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static MyRenderMessageSetCameraViewMatrix NativeCamera()
        {
            VRageRender.MyRenderProxy.BeginRecordingDeferredMessages();
            VRageRender.MyRenderProxy.SetCameraViewMatrix(MatrixD.Identity,Matrix.Identity,Matrix.Identity,1,1,.1f,1000,10000,Vector3D.Zero);
            var commands=VRageRender.MyRenderProxy.FinishRecordingDeferredMessages();
            return commands.Messages.OfType<MyRenderMessageSetCameraViewMatrix>().Single();
        }
        // Internal value-type out arguments match the upstream assembly's Harmony boundary.
        private struct Snapshot
        {
            public long TimestampTicks;
            public bool Smooth;
        }
        private sealed class PluginConfig { public bool InterpolationEnabled { get; set; } = true; }
        private static class Plugin
        {
            public static bool InterpolationEnabled=true;
            public static PluginConfig Config { get; }=new PluginConfig();
            public static void SetInterpolationEnabled(bool enabled) { InterpolationEnabled=Config.InterpolationEnabled=enabled; }
        }
        private static class Capture
        {
            public static Snapshot Next;
            private static Snapshot _pendingSnap;
            private static bool _hasPendingSnap;
            [MethodImpl(MethodImplOptions.NoInlining)] public static void SnapAndDefer()
            {
                if(_hasPendingSnap) { _pendingSnap.TimestampTicks=Next.TimestampTicks; return; }
                _pendingSnap=Next; _hasPendingSnap=true;
            }
            [MethodImpl(MethodImplOptions.NoInlining)] public static void PublishPendingSnap()
            {
                if(!_hasPendingSnap) return;
                History.Previous=History.Current; History.Current=_pendingSnap; _hasPendingSnap=false;
            }
        }
        private static class History
        {
            public static Snapshot Previous,Current;
            [MethodImpl(MethodImplOptions.NoInlining)] public static bool TryGetPair(out Snapshot previous,out Snapshot current)
            { previous=Previous; current=Current; return previous.TimestampTicks>0 && current.Smooth; }
        }
        private static class Render
        {
            private static readonly MyRenderMessageSetCameraViewMatrix _synthMessage=new MyRenderMessageSetCameraViewMatrix {ViewMatrix=MatrixD.Identity};
            public static double LastSmoothingAlpha {get; private set;}
            public static long LastPrevious,LastCurrent;
            public static bool Throw;
            [MethodImpl(MethodImplOptions.NoInlining)] public static void RunFrame()
            {
                if(Throw) throw new InvalidOperationException("upstream failure");
                if(!Plugin.InterpolationEnabled || !History.TryGetPair(out var previous,out var current)) return;
                LastPrevious=previous.TimestampTicks; LastCurrent=current.TimestampTicks;
                LastSmoothingAlpha=.25;
                RenderFrameBridge.Consume(_synthMessage);
            }
        }
    }
}
#endif
