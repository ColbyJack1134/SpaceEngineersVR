using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
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
            internal long Batch,Snapshot,Owner,CharacterId,GridId;
            internal int Generation,Forearm=-1;
            internal CameraRig.Frame Rig;
            internal MatrixD Camera,Character,Grid;
            internal Matrix Origin;
            internal SmoothFrames.Bones Bones;
            internal uint CharacterActor=uint.MaxValue,GridActor=uint.MaxValue;
            internal bool Smooth;
        }
        private static readonly object gate=new object();
        private static readonly Dictionary<long,Tick> snapshots=new Dictionary<long,Tick>();
        private static readonly Queue<long> snapshotOrder=new Queue<long>();
        private static FieldInfo pending,hasPending,stamp,enabled,synthetic;
        private static FieldInfo absoluteBones,suspendedInterpolation;
        private static Harmony guard;
        private static MethodInfo getSkinning,setBones,captureMethod;
        private static Func<double> alpha;
        private static Tick captured,current,pairPrevious,pairCurrent;
        private static long nextBatch;
        private static int generation;
        private static volatile bool ready,failed;
        private static bool pairValid,pairRead;
        [ThreadStatic] private static bool inSmoothing;
        [ThreadStatic] private static bool lateCapture;
        internal static bool Ready => ready && !failed;
        internal static Exception Failure { get; private set; }
        internal static bool? Available() => AccessTools.TypeByName("SmoothFrames.RenderFrameSmoothing")!=null;
        internal static void Reset()
        {
            Interlocked.Increment(ref generation); SmoothFrames.Reset(); SmoothFrames.Clear();
            lock(gate) { snapshots.Clear(); snapshotOrder.Clear(); }
            captured=current=pairPrevious=pairCurrent=null;
            pairValid=pairRead=false;
        }
        internal static void Stop() { ready=false; inSmoothing=false; RestoreInterpolation(); Reset(); }
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
        private static bool AllowSmoothing() => !Main.VrPresent || Ready;
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
            pending=Required(capture,"_pendingSnap"); hasPending=Required(capture,"_hasPendingSnap",typeof(bool));
            stamp=Required(pending.FieldType,"TimestampTicks",typeof(long)); enabled=Required(plugin,"InterpolationEnabled",typeof(bool));
            synthetic=Required(render,"_synthMessage",typeof(MyRenderMessageSetCameraViewMatrix));
            alpha=(Func<double>)Delegate.CreateDelegate(typeof(Func<double>),PatchGroup.Getter(render,"LastSmoothingAlpha"));
            captureMethod=PatchGroup.Method(capture,"SnapAndDefer");
            harmony.Patch(captureMethod,prefix:Hook(nameof(Snapping)),postfix:Hook(nameof(Snapped)));
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
            bool vrPresent;
            lock(gate)
            {
                if(failed) return;
                failed=true; Failure=ex; ready=false;
                vrPresent=Main.VrPresent;
                try
                {
                    if(vrPresent && enabled!=null && (bool)enabled.GetValue(null))
                    {
                        enabled.SetValue(null,false);
                        suspendedInterpolation=enabled;
                    }
                }
                catch(Exception disableError) { ex=new AggregateException(ex,disableError); }
            }
            SmoothFrames.Clear();
            try { Logger.Warning(ex,vrPresent ? "SmoothFrames compatibility failed; interpolation suspended while VR is active" : "SmoothFrames compatibility failed"); }
            catch { }
        }
        internal static void RestoreInterpolation()
        {
            lock(gate)
            {
                if(suspendedInterpolation==null) return;
                try
                {
                    // SmoothFrames keeps the player's preference separately from its render-thread flag.
                    var config=AccessTools.Property(suspendedInterpolation.DeclaringType,"Config")?.GetValue(null);
                    var preference=config==null ? null:AccessTools.Property(config.GetType(),"InterpolationEnabled");
                    bool resume=preference?.GetValue(config) as bool? ?? true;
                    suspendedInterpolation.SetValue(null,resume);
                }
                catch(Exception ex)
                {
                    try { Logger.Warning(ex,"Could not restore SmoothFrames interpolation"); } catch { }
                }
                suspendedInterpolation=null;
            }
        }
        internal static Tick Capture(object message,CameraRig.Frame rig)
        {
            if(!Ready || !(message is MyRenderMessageSetCameraViewMatrix camera)) return null;
            try
            {
                var tick=new Tick {Generation=Volatile.Read(ref generation),
                    Rig=rig,Camera=MatrixD.Invert(camera.ViewMatrix),Origin=rig?.OriginInverse ?? (Main.VrActive ? Player.PlayerToAbsolute.inverted : VRageMath.Matrix.Identity),Smooth=camera.Smooth};
                Volatile.Write(ref captured,tick);
                return tick;
            }
            catch(Exception ex) { Fail(ex); return null; }
        }
        internal static long BeginCommit() => Ready ? Interlocked.Increment(ref nextBatch):0;
        internal static void Commit(Tick tick,long batch)
        {
            if(!Ready || tick==null) return;
            try
            {
                tick.Batch=batch;
                var session=MySession.Static; var character=session?.LocalCharacter;
                tick.Owner=session?.ControlledEntity?.Entity?.EntityId ?? 0;
                tick.CharacterId=character?.EntityId ?? 0;
                var grid=session?.ControlledEntity?.Entity?.GetTopMostParent() as Sandbox.Game.Entities.MyCubeGrid;
                tick.GridId=grid?.EntityId ?? 0; tick.Grid=grid?.WorldMatrix ?? MatrixD.Identity;
                tick.GridActor=grid?.Render?.GetRenderObjectID() ?? uint.MaxValue;
                tick.Character=character?.WorldMatrix ?? MatrixD.Identity;
                tick.CharacterActor=character?.Render?.GetRenderObjectID() ?? uint.MaxValue;
                tick.Bones=SmoothFrames.CapturedBones(tick.CharacterActor);
                var forearm=character?.AnimationController?.FindBone("SE_RigLForearm2",out _);
                if(forearm!=null) tick.Forearm=forearm.Index;
                if(tick.Snapshot!=0) lock(gate)
                {
                    if(!snapshots.ContainsKey(tick.Snapshot)) snapshotOrder.Enqueue(tick.Snapshot);
                    snapshots[tick.Snapshot]=tick;
                    while(snapshotOrder.Count>16) snapshots.Remove(snapshotOrder.Dequeue());
                }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static bool Snapping() => !Ready || !Main.VrActive || !Main.WorldAvailable || lateCapture;
        private static void Snapped()
        {
            if(!Ready) return;
            try
            {
                var tick=Volatile.Read(ref captured);
                if(tick!=null && tick.Batch==0 && (bool)hasPending.GetValue(null))
                    tick.Snapshot=(long)stamp.GetValue(pending.GetValue(null));
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static Tick Find(long timestamp) { lock(gate) return snapshots.TryGetValue(timestamp,out var tick) ? tick:null; }
        internal static void Consume(object message,Tick tick)
        {
            if(!Ready) return;
            try
            {
                if(tick!=null) current=tick;
                else if(ReferenceEquals(message,synthetic.GetValue(null)))
                {
                    if(pairValid && AcceptPair(pairPrevious,pairCurrent)) SmoothFrames.Sample(pairPrevious,pairCurrent,alpha());
                }
                else { current=null; SmoothFrames.Clear(); }
            }
            catch(Exception ex) { Fail(ex); }
        }
        private static void QueueStart(bool draw)
        {
            if(!Ready || !draw) return;
            SmoothFrames.Clear();
            pairValid=pairRead=false; pairPrevious=pairCurrent=null;
        }
        private static void SmoothingStart()
        {
            if(!Ready) return;
            inSmoothing=true; pairValid=pairRead=false; pairPrevious=pairCurrent=null;
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
            return __exception;
        }
    }
}
