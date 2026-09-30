using System.Runtime.CompilerServices;

namespace SpaceEngineersVR.Player
{
    internal static class RenderFrameBridge
    {
        private sealed class Packet { public CameraRig.Frame Rig; public SurfaceView[] Surfaces; }
        private static readonly ConditionalWeakTable<object, Packet> packets = new ConditionalWeakTable<object, Packet>();
        public static CameraRig.Frame Current { get; private set; }
        public static SurfaceView[] Surfaces { get; private set; }
        public static CameraRig.Frame ForCurrentOwner(CameraRig.Frame current)
        {
            // During respawn/ejection, keep validated eye height until the new batch arrives.
            return Current?.Epoch == current?.Epoch ? Current : current;
        }

        public static void Capture(object message, CameraRig.Frame rig)
        {
            // Render messages are pooled; replace the previous use before enqueueing.
            packets.Remove(message);
            packets.Add(message, new Packet { Rig = rig,Surfaces=SpatialUi.Current });
        }

        public static void Consume(object message)
        {
            Current = packets.TryGetValue(message, out var packet) ? packet.Rig : null;
            Surfaces=packet?.Surfaces;
        }
    }
}
