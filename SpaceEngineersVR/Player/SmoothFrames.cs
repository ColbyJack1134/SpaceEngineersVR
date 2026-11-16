using System;
using System.Threading;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Render.Scene;
using VRageMath;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class SmoothFrames
    {
        internal sealed class Bones
        {
            internal uint Actor;
            internal Matrix[] Poses;
        }
        private static Bones queuedBones;
        private static object skin;
        private static Matrix[] restoreBones,blendedBones;
        private static SurfaceView[] surfaces;
        private static SmoothFramesBridge.Tick from,to;
        private static double amount;
        internal static void CaptureBones(MyRenderMessageSetCharacterTransforms message)
        {
            var character=MySession.Static?.LocalCharacter;
            if(!SmoothFramesBridge.Ready || message.CharacterID!=character?.Render?.GetRenderObjectID()) return;
            int count=character.AnimationController.CharacterBones?.Length ?? 0;
            if(count==0 || message.BoneAbsoluteTransforms.Length<count) return;
            // A pooled message can retain capacity from a larger skeleton.
            var poses=new Matrix[count]; Array.Copy(message.BoneAbsoluteTransforms,poses,count);
            Volatile.Write(ref queuedBones,new Bones {Actor=message.CharacterID,Poses=poses});
        }
        internal static Bones CapturedBones(uint actor)
        {
            var value=Volatile.Read(ref queuedBones);
            return value?.Actor==actor ? value:null;
        }
        internal static bool Accept(SmoothFramesBridge.Tick previous,SmoothFramesBridge.Tick current,SmoothFramesBridge.Tick native)
        {
            if(previous==null || current==null || native==null || current.Batch!=native.Batch || current.Generation!=previous.Generation ||
                current.Owner!=previous.Owner || !previous.Smooth || !current.Smooth) return false;
            var a=previous.Rig; var b=current.Rig;
            if((a==null)!=(b==null) || a?.ThirdPerson==true || b?.ThirdPerson==true || a?.Observer!=null || b?.Observer!=null) return false;
            return a==null || a.Epoch==b.Epoch && a.UnitsPerMeter==b.UnitsPerMeter;
        }
        internal static void Sample(SmoothFramesBridge.Tick previous,SmoothFramesBridge.Tick current,double alpha)
        { from=previous; to=current; amount=MathHelper.Clamp(alpha,0,1); }
        internal static void Clear() { from=to=null; surfaces=null; }
        internal static void Reset() => Volatile.Write(ref queuedBones,null);
        internal static MatrixD Blend(MatrixD a,MatrixD b,double alpha)
        {
            var qa=Quaternion.CreateFromRotationMatrix(VrMath.Rigid(a));
            var qb=Quaternion.CreateFromRotationMatrix(VrMath.Rigid(b));
            var result=MatrixD.CreateFromQuaternion(Quaternion.Slerp(qa,qb,(float)alpha));
            result.Right*=MathHelper.Lerp(a.Right.Length(),b.Right.Length(),alpha);
            result.Up*=MathHelper.Lerp(a.Up.Length(),b.Up.Length(),alpha);
            result.Backward*=MathHelper.Lerp(a.Backward.Length(),b.Backward.Length(),alpha);
            result.Translation=Vector3D.Lerp(a.Translation,b.Translation,alpha);
            return result;
        }
        internal static MatrixD Basis(SmoothFramesBridge.Tick tick) => tick.Rig?.TrackingToWorld ?? (MatrixD)tick.Origin*tick.Camera;
        internal static MatrixD AttachedBasis(MatrixD a,MatrixD b,MatrixD parentA,MatrixD parentB,MatrixD rendered,double alpha) =>
            Blend(a*MatrixD.Invert(parentA),b*MatrixD.Invert(parentB),alpha)*rendered;
        internal static CameraRig.Frame RenderFrame(CameraRig.Frame fallback)
        {
            surfaces=null;
            try { return RenderSample(fallback); }
            catch(Exception ex)
            {
                EndFrame();
                SmoothFramesBridge.Fail(ex);
                return fallback;
            }
        }
        private static CameraRig.Frame RenderSample(CameraRig.Frame fallback)
        {
            if(!SmoothFramesBridge.Ready || from==null || to==null || !Main.WorldAvailable) return fallback;
            if(to.Rig!=null && to.Rig.Epoch!=fallback?.Epoch || to.Rig==null && fallback!=null) return fallback;
            var basis=Blend(Basis(from),Basis(to),amount);
            bool grid=to.GridId!=0 && to.GridId==from.GridId;
            uint parent=grid ? to.GridActor:to.CharacterActor;
            var actor=parent==uint.MaxValue ? null:MyIDTracker<MyActor>.FindByID(parent);
            if(actor!=null && (grid || to.CharacterId!=0 && to.CharacterId==from.CharacterId))
                basis=AttachedBasis(Basis(from),Basis(to),grid ? from.Grid:from.Character,grid ? to.Grid:to.Character,actor.WorldMatrix,amount);
            double scale=to.Rig?.UnitsPerMeter ?? 1;
            var origin=to.Rig?.OriginInverse ?? to.Origin;
            var anchor=MatrixD.CreateScale(1/scale)*MatrixD.Invert((MatrixD)origin)*basis;
            var frame=new CameraRig.Frame(anchor,origin,to.Rig?.Epoch ?? to.Generation,scale);
            BlendBones();
            var native=RenderFrameBridge.Surfaces;
            if(native!=null)
            {
                surfaces=new SurfaceView[native.Length];
                var character=MyIDTracker<MyActor>.FindByID(to.CharacterActor);
                var bones=character==null ? null:SmoothFramesBridge.RenderBones(character);
                MatrixD? wrist=bones!=null && to.Forearm>=0 && to.Forearm<bones.Length ?
                    (MatrixD)bones[to.Forearm]*character.WorldMatrix : (MatrixD?)null;
                for(int i=0;i<native.Length;i++)
                {
                    var s=native[i]; surfaces[i]=s;
                    if(s.TrackingSpace || s.WindowPose.HasValue || s.RenderParent!=uint.MaxValue) continue;
                    if(wrist.HasValue && s.WristLocal.HasValue) surfaces[i]=s.At(s.WristLocal.Value*wrist.Value);
                }
            }
            return frame;
        }
        private static void BlendBones()
        {
            var a=from.Bones; var b=to.Bones;
            if(a==null || b==null || a.Actor!=b.Actor || a.Poses.Length!=b.Poses.Length) return;
            var actor=MyIDTracker<MyActor>.FindByID(b.Actor);
            if(actor==null) return;
            skin=SmoothFramesBridge.RenderSkin(actor);
            if(skin==null) return;
            if(blendedBones==null || blendedBones.Length!=b.Poses.Length) blendedBones=new Matrix[b.Poses.Length];
            for(int i=0;i<blendedBones.Length;i++) blendedBones[i]=(Matrix)Blend(a.Poses[i],b.Poses[i],amount);
            restoreBones=b.Poses;
            SmoothFramesBridge.SetRenderBones(skin,blendedBones);
        }
        internal static SurfaceView[] Surfaces(SurfaceView[] original) => surfaces ?? original;
        internal static void EndFrame()
        {
            try { if(skin!=null && restoreBones!=null) SmoothFramesBridge.SetRenderBones(skin,restoreBones); }
            catch(Exception ex) { SmoothFramesBridge.Fail(ex); }
            finally { skin=null; restoreBones=null; surfaces=null; }
        }
    }
}
