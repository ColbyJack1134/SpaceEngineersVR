#if !LINUX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VRage.Render.Scene;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Moving render actors as boxes with one reprojection per rigid motion, so motion vectors follow moving grids and characters; render thread.
    internal static class DlssObjects
    {
        internal const int MaxBoxes=255;
        // Covers depth error at the surface of the box.
        private const float Margin=.03f;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Box
        {
            // ToLocal takes clip space at the scene depth straight to the object's space.
            public Matrix ToClip,ToLocal;
            public Vector3 Min;
            public uint Index;
            public Vector3 Max;
            public float Flip;
        }

        internal struct Candidate
        {
            public MatrixD Current,Previous;
            public BoundingBox Bounds;
            public double Distance;
        }

        // Last two world matrices per actor; a matrix is only a previous one when it was seen the frame before.
        internal sealed class Tracker
        {
            private struct Track { public MatrixD Current,Previous; public int Frame; public bool Known,Moved; }
            private readonly Dictionary<uint,Track> tracks=new Dictionary<uint,Track>();
            private readonly List<uint> stale=new List<uint>();
#if SEVR_DIAGNOSTICS
            internal int Count => tracks.Count;
#endif

            // False when the actor has no usable previous matrix this frame.
            internal bool Observe(uint id,in MatrixD world,int frame,out MatrixD previous,out bool moved)
            {
                Track track;
                bool seen=tracks.TryGetValue(id,out track);
                if(!seen || track.Frame!=frame)
                {
                    track.Known=seen && track.Frame==frame-1 && !Jump(track.Current,world);
                    track.Previous=track.Current; track.Current=world; track.Frame=frame;
                    track.Moved=track.Known && !Same(track.Previous,world);
                    tracks[id]=track;
                }
                previous=track.Previous; moved=track.Moved;
                return track.Known;
            }
            internal void Prune(int frame)
            {
                stale.Clear();
                foreach(var pair in tracks) if(pair.Value.Frame<frame-1) stale.Add(pair.Key);
                foreach(uint id in stale) tracks.Remove(id);
            }
            internal void Clear() => tracks.Clear();
            // A jump drive or a reused actor id is a cut for that object, not motion.
            private static bool Jump(in MatrixD from,in MatrixD to) => Vector3D.DistanceSquared(from.Translation,to.Translation)>2500;
        }

        private sealed class MotionComparer : IEqualityComparer<Candidate>
        {
            public bool Equals(Candidate a,Candidate b) => Same(a.Current,b.Current) && Same(a.Previous,b.Previous);
            public int GetHashCode(Candidate c) => c.Current.Translation.GetHashCode()*397^c.Previous.Translation.GetHashCode();
        }

        private static readonly Tracker worlds=new Tracker();
        private static readonly List<Candidate> candidates=new List<Candidate>();
        private static readonly Dictionary<Candidate,int> indices=new Dictionary<Candidate,int>(new MotionComparer());
        private static readonly List<Candidate> merged=new List<Candidate>();
        private static readonly IComparer<Candidate> nearest=Comparer<Candidate>.Create((a,b)=>a.Distance.CompareTo(b.Distance));
        private static readonly IComparer<Box> largest=Comparer<Box>.Create((a,b)=>Volume(b).CompareTo(Volume(a)));
        internal static readonly System.Reflection.FieldInfo Extension=HarmonyLib.AccessTools.Field(typeof(MyDynamicAABBTree),"m_extension");
        private static Vector3? extension;
        internal static readonly Box[] Boxes=new Box[MaxBoxes];
        internal static readonly Matrix[] Transforms=new Matrix[MaxBoxes+1];
        internal static int BoxCount { get; private set; }
        internal static int TransformCount => BoxCount+1;

        internal static bool Same(in MatrixD a,in MatrixD b) =>
            a.M41==b.M41 && a.M42==b.M42 && a.M43==b.M43 && a.M11==b.M11 && a.M12==b.M12 && a.M13==b.M13 &&
            a.M21==b.M21 && a.M22==b.M22 && a.M23==b.M23 && a.M31==b.M31 && a.M32==b.M32 && a.M33==b.M33;

        // Camera-relative position this frame to camera-relative position last frame, both at this frame's origin.
        internal static Matrix Reproject(in MatrixD current,in MatrixD previous,Vector3D camera) =>
            (Matrix)(MatrixD.CreateTranslation(camera)*MatrixD.Invert(current)*previous*MatrixD.CreateTranslation(-camera));
        internal static Matrix ToLocal(in MatrixD current,Vector3D camera) => (Matrix)(MatrixD.CreateTranslation(camera)*MatrixD.Invert(current));
        private static bool Mirrored(in MatrixD m) =>
            m.M11*(m.M22*m.M33-m.M23*m.M32)-m.M12*(m.M21*m.M33-m.M23*m.M31)+m.M13*(m.M21*m.M32-m.M22*m.M31)<0;
        private static float Volume(Box box) { var size=box.Max-box.Min; return size.X*size.Y*size.Z; }

        internal static void Clear() { BoxCount=0; }
        internal static void Forget() { Clear(); worlds.Clear(); }

        // Groups are the manual cull objects in view: one per grid render cell or character.
        internal static void Collect(List<MyManualCullTreeData> groups,Vector3D camera,Matrix inverseViewProjection,int frame)
        {
            candidates.Clear();
            foreach(var group in groups)
            {
                var actor=group.Actor;
                int root=group.Children.GetRoot();
                if(actor==null || actor.IsDestroyed || !actor.IsVisible || root==-1) continue;
                MatrixD world=actor.WorldMatrix,previous;
                bool moved;
                if(!worlds.Observe(actor.ID,world,frame,out previous,out moved) || !moved) continue;
                // Tree leaves are padded by the extension every group tree is constructed with.
                var bounds=group.Children.GetAabb(root);
                var padding=(extension ?? (extension=(Vector3)Extension.GetValue(group.Children)).Value)-Margin;
                Vector3 min=bounds.Min+padding,max=bounds.Max-padding,centre=bounds.Center;
                if(!min.IsValid() || !max.IsValid()) continue;
                bounds=new BoundingBox(Vector3.Min(min,centre),Vector3.Max(max,centre));
                candidates.Add(new Candidate { Current=world,Previous=previous,Bounds=bounds,Distance=Vector3D.DistanceSquared(Vector3D.Transform((Vector3D)bounds.Center,world),camera) });
            }
            Emit(candidates,camera,Matrix.Invert(inverseViewProjection),inverseViewProjection);
            if((frame&511)==0) worlds.Prune(frame);
        }

        // One box per rigid motion, nearest kept at the limit; larger boxes draw first so a grid inside another's box wins.
        internal static void Emit(List<Candidate> list,Vector3D camera,Matrix viewProjection,Matrix inverseViewProjection)
        {
            Clear();
            if(list.Count==0) return;
            list.Sort(nearest);
            indices.Clear(); merged.Clear();
            foreach(var candidate in list)
            {
                int slot;
                if(indices.TryGetValue(candidate,out slot))
                {
                    var whole=merged[slot];
                    whole.Bounds=whole.Bounds.Include(candidate.Bounds);
                    merged[slot]=whole;
                }
                else if(merged.Count<MaxBoxes) { indices[candidate]=merged.Count; merged.Add(candidate); }
            }
            Transforms[0]=Matrix.Identity;
            foreach(var motion in merged)
            {
                Transforms[BoxCount+1]=Reproject(motion.Current,motion.Previous,camera);
                Boxes[BoxCount]=new Box
                {
                    ToClip=(Matrix)(motion.Current*MatrixD.CreateTranslation(-camera))*viewProjection,ToLocal=inverseViewProjection*ToLocal(motion.Current,camera),
                    Min=motion.Bounds.Min,Max=motion.Bounds.Max,Index=(uint)++BoxCount,Flip=Mirrored(motion.Current) ? -1:1
                };
            }
            Array.Sort(Boxes,0,BoxCount,largest);
        }
    }
}
#endif
