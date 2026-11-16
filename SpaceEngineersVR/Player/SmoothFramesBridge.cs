using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRage.Render.Scene;
using VRageMath;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    // Pair identity comes from the upstream render call, never a second history lookup.
    internal static class SmoothFramesBridge
    {
        internal sealed class Tick
        {
            internal long Id,Batch,Captured,Committed,Snapshot,Published,EntityStart,EntityEnd,ToolUpdated,Owner,CharacterId,GunId,GridId;
            internal int Generation,Forearm=-1;
            internal CameraRig.Frame Rig;
            internal MatrixD Camera,Character,Gun,Grid,Cockpit,ForearmWorld;
            internal Matrix Origin;
            internal SmoothFrames.Bones Bones;
            internal uint CharacterActor=uint.MaxValue,GunActor=uint.MaxValue,GridActor=uint.MaxValue,CockpitActor=uint.MaxValue;
            internal bool Smooth;
            internal HoloSight.View Holo;
            internal SurfaceView Wrist;
        }
        private static readonly object gate=new object();
        private static readonly Dictionary<long,Tick> snapshots=new Dictionary<long,Tick>();
        private static readonly Queue<long> snapshotOrder=new Queue<long>();
        private static readonly List<string> rows=new List<string>();
        private static FieldInfo pending,hasPending,stamp,enabled,synthetic;
        private static FieldInfo absoluteBones;
        private static Harmony guard;
        private static MethodInfo getSkinning,setBones,captureMethod;
        private static Func<double> alpha;
        private static Tick captured,current,previous,pairPrevious,pairCurrent;
        private static long nextId,nextBatch,renderFrame,queueTime,smoothingTime,smoothingEnd,publishedStamp,toolUpdated,entityStart,entityEnd;
        private static int generation,request,remaining,tracedFrames,syntheticCalls,pairs;
        private static bool ready,failed,interpolation,pairValid,pairRead,applied;
        [ThreadStatic] private static bool inSmoothing;
        [ThreadStatic] private static bool lateCapture;
        internal static bool Ready => ready && !failed;
        internal static Exception Failure { get; private set; }
        internal static bool Tracing => Ready && remaining>0;
        internal static bool? Available() => AccessTools.TypeByName("SmoothFrames.RenderFrameSmoothing")!=null;
        internal static void Request() => Interlocked.Exchange(ref request,1);
        internal static void Reset()
        {
            Interlocked.Increment(ref generation); SmoothFrames.Reset(); SmoothFrames.Clear();
            lock(gate) { snapshots.Clear(); snapshotOrder.Clear(); }
            captured=current=previous=pairPrevious=pairCurrent=null;
            pairValid=pairRead=applied=false; remaining=0; rows.Clear();
        }
        internal static void ToolUpdated() { if(Ready) Interlocked.Exchange(ref toolUpdated,Stopwatch.GetTimestamp()); }
        internal static void Stop() { ready=false; inSmoothing=false; Reset(); }
        internal static void Dispose()
        {
            Stop();
            try
            {
                if(guard!=null) foreach(var method in guard.GetPatchedMethods().ToArray()) guard.Unpatch(method,HarmonyPatchType.All,guard.Id);
            }
            catch(Exception ex)
            {
                try { Logger.Warning(ex,"Could not remove SmoothFrames guard"); } catch { }
            }
            finally { guard=null; }
        }
        internal static void Disable()
        {
            try { Protect(PatchGroup.Type("SmoothFrames.RenderFrameSmoothing"),PatchGroup.Type("SmoothFrames.Plugin")); }
            catch(Exception ex) { Fail(ex); return; }
            Fail(new InvalidOperationException("SmoothFrames hooks unavailable"));
        }
        internal static void Protect(Type render,Type plugin)
        {
            enabled=AccessTools.Field(plugin,"InterpolationEnabled");
            if(enabled?.FieldType!=typeof(bool)) enabled=null;
            if(guard==null)
            {
                var protection=new Harmony("SpaceEngineersVR.SmoothFramesGuard");
                protection.Patch(PatchGroup.Method(render,"RunFrame"),prefix:Hook(nameof(AllowSmoothing)));
                guard=protection;
            }
            enabled=Required(plugin,"InterpolationEnabled",typeof(bool));
        }
        private static bool AllowSmoothing() => !Main.VrActive || Ready;
        internal static bool IsSynthetic(object message)
        {
            if(!Ready) return false;
            try { return ReferenceEquals(message,synthetic.GetValue(null)); }
            catch(Exception ex) { Fail(ex); return false; }
        }
        internal static object RenderSkin(MyActor actor) => getSkinning?.Invoke(null,new object[] {actor});
        internal static VRageMath.Matrix[] RenderBones(MyActor actor)
        {
            var value=RenderSkin(actor);
            return value==null ? null:(VRageMath.Matrix[])absoluteBones.GetValue(value);
        }
        private static readonly MyBoneDecalUpdate[] noDecals=new MyBoneDecalUpdate[0];
        internal static void SetRenderBones(object skin,VRageMath.Matrix[] bones) => setBones.Invoke(skin,new object[] {bones,noDecals});
        internal static void CaptureLate()
        {
            if(!Ready || !Main.VrActive || !Main.WorldAvailable || MySector.MainCamera==null) return;
            var tick=Volatile.Read(ref captured);
            if(tick==null || tick.Batch!=0) return;
            try { lateCapture=true; captureMethod.Invoke(null,new object[] {MySector.MainCamera}); }
            catch(Exception ex) { Fail(ex); }
            finally { lateCapture=false; }
        }

        internal static void Install(Harmony harmony)
        {
            var render=PatchGroup.Type("SmoothFrames.RenderFrameSmoothing");
            Protect(render,PatchGroup.Type("SmoothFrames.Plugin"));
            getSkinning=PatchGroup.Method(PatchGroup.Type("VRage.Render11.Scene.ActorExtensions"),"GetSkinning");
            absoluteBones=Required(getSkinning.ReturnType,"m_absoluteTransforms",typeof(VRageMath.Matrix[]));
            setBones=PatchGroup.Method(getSkinning.ReturnType,"SetAnimationBones");
            var queue=PatchGroup.Method(PatchGroup.Type("VRageRender.MyRender11"),"ProcessMessageQueue",new[] {typeof(bool)});
            harmony.Patch(queue,prefix:new HarmonyMethod(typeof(SmoothFramesBridge),nameof(QueueStart)) {priority=Priority.First});
            Patches.FrameInjections.InstallCamera(harmony);
            Attach(harmony,render,PatchGroup.Type("SmoothFrames.CameraCapture"),
                PatchGroup.Type("SmoothFrames.CameraHistory"),PatchGroup.Type("SmoothFrames.Plugin"));
            Logger.Info("SmoothFrames compatibility; SEVR module="+typeof(Main).Module.ModuleVersionId+
                "; upstream="+pending.DeclaringType.Assembly.FullName+"; module="+pending.Module.ModuleVersionId);
        }
        internal static void Attach(Harmony harmony,Type render,Type capture,Type history,Type plugin)
        {
            Stop(); failed=false; Failure=null;
            request=tracedFrames=syntheticCalls=pairs=0;
            publishedStamp=toolUpdated=entityStart=entityEnd=0;
            pending=Required(capture,"_pendingSnap"); hasPending=Required(capture,"_hasPendingSnap",typeof(bool));
            stamp=Required(pending.FieldType,"TimestampTicks",typeof(long)); enabled=Required(plugin,"InterpolationEnabled",typeof(bool));
            synthetic=Required(render,"_synthMessage",typeof(MyRenderMessageSetCameraViewMatrix));
            alpha=(Func<double>)Delegate.CreateDelegate(typeof(Func<double>),PatchGroup.Getter(render,"LastSmoothingAlpha"));
            captureMethod=PatchGroup.Method(capture,"SnapAndDefer");
            harmony.Patch(captureMethod,prefix:Hook(nameof(Snapping)),postfix:Hook(nameof(Snapped)));
            harmony.Patch(PatchGroup.Method(capture,"PublishPendingSnap"),prefix:Hook(nameof(Publishing)),postfix:Hook(nameof(Published)));
            harmony.Patch(PatchGroup.Method(render,"RunFrame"),prefix:Hook(nameof(SmoothingStart)),finalizer:Hook(nameof(SmoothingEnd)));
            harmony.Patch(PatchGroup.Method(history,"TryGetPair"),postfix:Hook(nameof(Pair)));
            ready=true;
        }
        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(SmoothFramesBridge),name);
        private static FieldInfo Required(Type type,string name,Type expected=null)
        {
            var field=AccessTools.Field(type,name);
            if(field==null || expected!=null && field.FieldType!=expected) throw new MissingFieldException(type.FullName,name);
            return field;
        }
        internal static void Fail(Exception ex)
        {
            if(failed) return;
            failed=true; Failure=ex; ready=false;
            SmoothFrames.Clear();
            try { if(Main.VrActive && enabled!=null) enabled.SetValue(null,false); }
            catch(Exception disableError) { ex=new AggregateException(ex,disableError); }
            try { Logger.Warning(ex,"SmoothFrames compatibility failed; interpolation suspended while VR is active"); }
            catch { }
        }
        internal static Tick Capture(object message,CameraRig.Frame rig)
        {
            if(!Ready || !(message is MyRenderMessageSetCameraViewMatrix camera)) return null;
            try
            {
                var tick=new Tick {Id=Interlocked.Increment(ref nextId),Captured=Stopwatch.GetTimestamp(),Generation=Volatile.Read(ref generation),
                    Rig=rig,Camera=MatrixD.Invert(camera.ViewMatrix),Origin=rig?.OriginInverse ?? (Main.VrActive ? Player.PlayerToAbsolute.inverted : VRageMath.Matrix.Identity),Smooth=camera.Smooth};
                Volatile.Write(ref captured,tick);
                return tick;
            }
            catch(Exception ex) { Fail(ex); return null; }
        }
        internal static long BeginCommit() => Ready ? Interlocked.Increment(ref nextBatch):0;
        internal static void Commit(Tick tick,long batch,SurfaceView[] surfaces,HoloSight.View holo)
        {
            if(!Ready || tick==null) return;
            try
            {
                tick.Batch=batch; tick.Committed=Stopwatch.GetTimestamp(); tick.Holo=holo;
                var session=MySession.Static; var character=session?.LocalCharacter;
                var gun=character?.CurrentWeapon as VRage.Game.Entity.MyEntity;
                tick.Owner=session?.ControlledEntity?.Entity?.EntityId ?? 0;
                tick.CharacterId=character?.EntityId ?? 0; tick.GunId=gun?.EntityId ?? 0;
                var grid=session?.ControlledEntity?.Entity?.GetTopMostParent() as Sandbox.Game.Entities.MyCubeGrid;
                tick.GridId=grid?.EntityId ?? 0; tick.Grid=grid?.WorldMatrix ?? MatrixD.Identity;
                tick.GridActor=grid?.Render?.GetRenderObjectID() ?? uint.MaxValue;
                tick.ToolUpdated=Interlocked.Read(ref toolUpdated);
                tick.Character=character?.WorldMatrix ?? MatrixD.Identity;
                tick.CharacterActor=character?.Render?.GetRenderObjectID() ?? uint.MaxValue;
                tick.Bones=SmoothFrames.CapturedBones(tick.CharacterActor);
                tick.Gun=gun?.WorldMatrix ?? MatrixD.Identity; tick.GunActor=gun?.Render?.GetRenderObjectID() ?? uint.MaxValue;
                CockpitRender.TracePose(out tick.CockpitActor,out tick.Cockpit);
                tick.Wrist=Array.Find(surfaces ?? new SurfaceView[0],s=>s.Style==SurfaceStyle.WristStatus);
                var forearm=character?.AnimationController?.FindBone("SE_RigLForearm2",out _);
                if(forearm!=null) { tick.Forearm=forearm.Index; tick.ForearmWorld=(MatrixD)forearm.AbsoluteTransform*tick.Character; }
                if(tick.Snapshot!=0) lock(gate)
                {
                    if(!snapshots.ContainsKey(tick.Snapshot)) snapshotOrder.Enqueue(tick.Snapshot);
                    snapshots[tick.Snapshot]=tick;
                    while(snapshotOrder.Count>16) snapshots.Remove(snapshotOrder.Dequeue());
                }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static bool Snapping(out long __state)
        {
            __state=0;
            if(!Ready) return true;
            if(Main.VrActive && Main.WorldAvailable && !lateCapture) return false;
            try { if(!(bool)hasPending.GetValue(null)) __state=Stopwatch.GetTimestamp(); }
            catch(Exception ex) { Fail(ex); }
            return true;
        }
        private static void Snapped(long __state)
        {
            if(!Ready) return;
            try
            {
                if(__state!=0) { entityStart=__state; entityEnd=Stopwatch.GetTimestamp(); }
                var tick=Volatile.Read(ref captured);
                if(tick!=null && tick.Batch==0 && (bool)hasPending.GetValue(null))
                {
                    tick.Snapshot=(long)stamp.GetValue(pending.GetValue(null));
                    tick.EntityStart=entityStart; tick.EntityEnd=entityEnd;
                }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static Tick Find(long timestamp) { lock(gate) return snapshots.TryGetValue(timestamp,out var tick) ? tick:null; }
        private static void Publishing(out Tick __state)
        {
            __state=null;
            if(!Ready) return;
            try { if((bool)hasPending.GetValue(null)) __state=Find((long)stamp.GetValue(pending.GetValue(null))); }
            catch(Exception ex) { Fail(ex); }
        }
        private static void Published(Tick __state)
        {
            if(!Ready || __state==null) return;
            Interlocked.Exchange(ref __state.Published,Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref publishedStamp,__state.Snapshot);
        }
        internal static void Consume(object message,Tick tick)
        {
            if(!Ready) return;
            try
            {
                if(tick!=null)
                {
                    if(current!=tick)
                    {
                        if(current==null || current.Batch!=tick.Batch) previous=current;
                        current=tick;
                    }
                    if(Tracing) Record("native",tick.Id,tick.Batch,tick.Snapshot,tick.Generation,tick.Rig==null ? 0:1,tick.Smooth ? 1:0,
                        Interlocked.Read(ref publishedStamp),Ms(Stopwatch.GetTimestamp()-tick.Committed));
                }
                else if(ReferenceEquals(message,synthetic.GetValue(null)))
                {
                    syntheticCalls++; applied=true;
                    if(pairValid && AcceptPair(pairPrevious,pairCurrent)) SmoothFrames.Sample(pairPrevious,pairCurrent,alpha());
                    if(Tracing) Record("synthetic",alpha(),current?.Batch ?? 0,pairCurrent?.Batch ?? 0);
                    if(Tracing) Matrix("camera_synthetic",MatrixD.Invert(((MyRenderMessageSetCameraViewMatrix)message).ViewMatrix));
                }
                else { current=previous=null; SmoothFrames.Clear(); Record("unknown_camera"); }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static void QueueStart(bool draw)
        {
            if(!Ready || !draw) return;
            try
            {
                SmoothFrames.Clear();
                renderFrame++; queueTime=Stopwatch.GetTimestamp(); smoothingTime=smoothingEnd=0; pairValid=pairRead=applied=false; pairPrevious=pairCurrent=null;
                interpolation=(bool)enabled.GetValue(null);
                if(Main.VrActive && Main.WorldAvailable && Interlocked.Exchange(ref request,0)!=0)
                {
                    if(remaining>0) Save();
                    remaining=240; tracedFrames=0; rows.Clear();
                    rows.Add("frame,event,stopwatch_ticks,values");
                    rows.Add("0,frequency,0,"+Stopwatch.Frequency);
                }
                if(Tracing) Record("queue",interpolation ? 1:0,current?.Batch ?? 0,Interlocked.Read(ref publishedStamp));
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static void SmoothingStart()
        {
            if(!Ready) return;
            inSmoothing=true; pairValid=pairRead=applied=false; pairPrevious=pairCurrent=null;
            smoothingTime=Stopwatch.GetTimestamp();
        }
        private static bool AcceptPair(Tick a,Tick b) => current?.Generation==Volatile.Read(ref generation) && SmoothFrames.Accept(a,b,current);
        private static void Pair(ref bool __result,object __0,object __1)
        {
            if(!Ready || !inSmoothing || pairRead) return;
            pairRead=true;
            try
            {
                var a=__0; var b=__1;
                long ta=(long)stamp.GetValue(a),tb=(long)stamp.GetValue(b);
                pairPrevious=Find(ta); pairCurrent=Find(tb);
                if(__result && Main.VrActive && Main.WorldAvailable && !AcceptPair(pairPrevious,pairCurrent)) __result=false;
                pairValid=__result;
                if(__result) pairs++;
                if(Tracing) Record("pair",__result ? 1:0,ta,tb,pairPrevious?.Batch ?? 0,pairCurrent?.Batch ?? 0,current?.Batch ?? 0);
            }
            catch(Exception ex)
            {
                if(Main.VrActive) __result=false;
                Fail(ex);
            }
        }
        private static Exception SmoothingEnd(Exception __exception)
        {
            inSmoothing=false;
            if(__exception!=null && Main.VrActive) { Fail(__exception); return null; }
            if(!Ready) return __exception;
            try
            {
                smoothingEnd=Stopwatch.GetTimestamp();
                if(Tracing) Record("smoothing_end",pairValid ? 1:0,applied ? 1:0,Ms(smoothingEnd-smoothingTime),__exception==null ? 0:1);
            }
            catch(Exception ex) { Fail(ex); }
            return __exception;
        }
        internal static void Eyes(CameraRig.Frame rig)
        {
            if(!Tracing) return;
            try
            {
                Record("eyes",current?.Batch ?? 0,pairCurrent?.Batch ?? 0,RenderFrameBridge.Current==null ? 0:1,rig==null ? 0:1,
                    RenderFrameBridge.Holo==null ? 0:1,RenderPerformance.PoseWait,smoothingEnd==0 ? -1:Ms(Stopwatch.GetTimestamp()-smoothingEnd));
                if(current==null) return;
                Timing("native_timing",current);
                if(pairCurrent!=null) Timing("pair_timing",pairCurrent);
                Record("owners",current.Owner,current.CharacterId,current.GunId,current.GridId,current.Generation);
                Matrix("camera_tick",current.Camera);
                Actor("character",current.CharacterActor,current.Character);
                Actor("gun",current.GunActor,current.Gun);
                Actor("grid",current.GridActor,current.Grid);
                Actor("cockpit",current.CockpitActor,current.Cockpit);
                if(current.Wrist!=null) Matrix("wrist_tick",current.Wrist.Pose);
                var actor=MyIDTracker<MyActor>.FindByID(current.CharacterActor);
                if(actor!=null && current.Forearm>=0 && getSkinning!=null)
                {
                    var skin=getSkinning.Invoke(null,new object[] {actor});
                    var bones=skin==null ? null:(VRageMath.Matrix[])absoluteBones.GetValue(skin);
                    if(bones!=null && current.Forearm<bones.Length)
                    {
                        var world=(MatrixD)bones[current.Forearm]*actor.WorldMatrix;
                        Matrix("forearm_tick",current.ForearmWorld); Matrix("forearm_render",world);
                        if(current.Wrist?.WristLocal!=null) Matrix("wrist_attached",current.Wrist.WristLocal.Value*world);
                    }
                }
                if(rig!=null)
                {
                    Record("rig",rig.Epoch,rig.UnitsPerMeter,rig.ThirdPerson ? 1:0,rig.Observer==null ? 0:1);
                    Matrix("tracking_base",rig.TrackingToWorld); Matrix("rig_anchor",rig.Anchor); Matrix("rig_origin_inverse",rig.OriginInverse);
                }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static void Timing(string name,Tick tick) => Record(name,tick.Id,tick.Batch,tick.Captured,tick.Snapshot,tick.EntityStart,tick.EntityEnd,
            tick.ToolUpdated,tick.Committed,Interlocked.Read(ref tick.Published),queueTime,smoothingTime,smoothingEnd);
        internal static void Eye()
        {
            if(!Tracing || current==null) return;
            try
            {
                string prefix=StereoRenderState.View==0 ? "left_":"right_";
                Actor(prefix+"grid",current.GridActor,current.Grid);
                Actor(prefix+"cockpit",current.CockpitActor,current.Cockpit);
                var control=MyIDTracker<MyActor>.FindByID(current.CockpitActor);
                if(control?.Parent!=null) Matrix(prefix+"interior",control.Parent.WorldMatrix);
                var surfaces=RenderFrameBridge.Surfaces;
                if(surfaces==null) return;
                for(int i=0;i<surfaces.Length;i++)
                {
                    var surface=surfaces[i];
                    if(surface.RenderParent==uint.MaxValue || surface.RenderParent!=current.GridActor) continue;
                    var parent=MyIDTracker<MyActor>.FindByID(surface.RenderParent);
                    if(parent!=null) Matrix(prefix+"panel_"+i,surface.ParentLocal*parent.WorldMatrix);
                }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static void Actor(string name,uint id,MatrixD tick)
        {
            var actor=id==uint.MaxValue ? null:MyIDTracker<MyActor>.FindByID(id);
            Record(name+"_actor",id,actor==null ? 0:1);
            if(actor==null) return;
            Matrix(name+"_tick",tick); Matrix(name+"_render",actor.WorldMatrix);
        }
        internal static void Holo(HoloSight.View state,bool failedSight,MatrixD view)
        {
            if(!Tracing) return;
            try
            {
                var original=state ?? current?.Holo;
                Record("holo",StereoRenderState.View,state==null ? 0:1,failedSight ? 1:0,original==null ? 0:1);
                if(original==null) return;
                var eye=MatrixD.Invert(view).Translation; var r=original.Reticle;
                Window("holo_tick",original.Gun,eye,r);
                var actor=current==null || current.GunActor==uint.MaxValue ? null:MyIDTracker<MyActor>.FindByID(current.GunActor);
                if(actor!=null) Window("holo_render",actor.WorldMatrix,eye,r);
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static void Window(string name,MatrixD gun,Vector3D eye,HoloSight.Reticle r)
        {
            var center=Vector3D.Transform(r.Center,gun); var offset=eye-center;
            var forward=Vector3D.Normalize(Vector3D.TransformNormal(r.Forward,gun));
            var right=Vector3D.Normalize(Vector3D.TransformNormal(r.Right,gun)); var up=Vector3D.Normalize(Vector3D.TransformNormal(r.Up,gun));
            Record(name,StereoRenderState.View,HoloSight.Along(eye,center,forward,right,up,r.Window).HasValue ? 1:0,
                Vector3D.Dot(offset,right),Vector3D.Dot(offset,up),-Vector3D.Dot(offset,forward));
        }
        private static void Matrix(string name,MatrixD m) => Record(name,m.M11,m.M12,m.M13,m.M21,m.M22,m.M23,m.M31,m.M32,m.M33,m.M41,m.M42,m.M43);
        private static double Ms(long ticks) => ticks*1000.0/Stopwatch.Frequency;
        private static void Record(string name,params object[] values)
        {
            if(!Tracing) return;
            rows.Add(renderFrame+","+name+","+Stopwatch.GetTimestamp()+","+string.Join(",",Array.ConvertAll(values,Value)));
        }
        private static string Value(object value) => value is double number ? number.ToString("R",CultureInfo.InvariantCulture) :
            value is float single ? single.ToString("R",CultureInfo.InvariantCulture) : Convert.ToString(value,CultureInfo.InvariantCulture);
        internal static void EndFrame()
        {
            if(!Ready) return;
            try
            {
                if(remaining>0) { tracedFrames++; if(--remaining==0) Save(); }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static void Save()
        {
            if(tracedFrames==0) return;
            var lines=rows.ToArray();
            string file=Path.Combine(MyFileSystem.UserDataPath,"SEVR-smoothframes-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+".csv");
            Task.Run(()=> {
                try { File.WriteAllLines(file,lines); Logger.Info("SmoothFrames trace saved: "+file); }
                catch(Exception ex) { Logger.Warning(ex,"Could not save SmoothFrames trace"); }
            });
        }
    }
}
