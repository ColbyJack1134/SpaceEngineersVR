using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SpaceEngineersVR.Player
{
    internal static class RenderFrameBridge
    {
        private sealed class Packet { public CameraRig.Frame Rig; public SurfaceView[] Surfaces; public WorldMarkers.View Markers; }
        private static readonly ConditionalWeakTable<object, Packet> packets = new ConditionalWeakTable<object, Packet>();
        private static readonly List<Packet> pending=new List<Packet>();
        private static WorldMarkers.View latestMarkers;
        public static CameraRig.Frame Current { get; private set; }
        public static SurfaceView[] Surfaces { get; private set; }
        public static WorldMarkers.View Markers { get; private set; }
        public static CameraRig.Frame ForCurrentOwner(CameraRig.Frame current)
        {
            // During respawn/ejection, keep validated eye height until the new batch arrives.
            return Current?.Epoch == current?.Epoch ? Current : current;
        }

        public static void Capture(object message, CameraRig.Frame rig)
        {
            // Render messages are pooled; replace the previous use before enqueueing.
            packets.Remove(message);
            var packet=new Packet { Rig=rig,Surfaces=SpatialUi.Current };
            packets.Add(message,packet);
            pending.Add(packet);
        }

        public static void CaptureMarkers(WorldMarkers.View markers) => latestMarkers=markers;
        public static void Commit()
        {
            // HUD draw follows camera enqueue. Finalize before AfterUpdate publishes the native batch.
            // Camera-only updates do not mean the native HUD removed its markers.
            foreach(var packet in pending) packet.Markers=latestMarkers;
            pending.Clear();
        }

        public static void Consume(object message)
        {
            Current = packets.TryGetValue(message, out var packet) ? packet.Rig : null;
            Surfaces=packet?.Surfaces;
            Markers=packet?.Markers;
        }
    }
}
