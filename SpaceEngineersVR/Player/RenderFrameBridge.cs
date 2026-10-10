using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SpaceEngineersVR.Player
{
    internal static class RenderFrameBridge
    {
        private sealed class Packet { public CameraRig.Frame Rig; public RemoteView.View Remote; public SurfaceView[] Surfaces; public WorldMarkers.View Markers; public ShipCrosshair.View Crosshair; public TargetFeedback.View Selection; public SymmetryPlanes.View[] Symmetry; public VRageRender.Messages.MyRenderMessageDebugDrawSphere[] Planets; }
        private static readonly ConditionalWeakTable<object, Packet> packets = new ConditionalWeakTable<object, Packet>();
        private static readonly List<Packet> pending=new List<Packet>();
        private static WorldMarkers.View latestMarkers;
        public static CameraRig.Frame Current { get; private set; }
        public static SurfaceView[] Surfaces { get; private set; }
        public static RemoteView.View Remote { get; private set; }
        public static WorldMarkers.View Markers { get; private set; }
        public static ShipCrosshair.View Crosshair { get; private set; }
        internal static TargetFeedback.View Selection { get; private set; }
        internal static SymmetryPlanes.View[] Symmetry { get; private set; }
        internal static VRageRender.Messages.MyRenderMessageDebugDrawSphere[] Planets { get; private set; }
        public static CameraRig.Frame ForCurrentOwner(CameraRig.Frame current)
        {
            // During respawn/ejection, keep validated eye height until the new batch arrives.
            return Current?.Epoch == current?.Epoch ? Current : current;
        }

        public static void Capture(object message, CameraRig.Frame rig,ShipCrosshair.View crosshair=null)
        {
            // Render messages are pooled; replace the previous use before enqueueing.
            packets.Remove(message);
            var packet=new Packet { Rig=rig,Remote=RemoteView.Current,Surfaces=SpatialUi.Current,Crosshair=crosshair };
            packets.Add(message,packet);
            pending.Add(packet);
        }

        public static void CaptureMarkers(WorldMarkers.View markers) => latestMarkers=markers;
        public static void Commit() => Commit(SpatialUi.Current);
        internal static void Commit(SurfaceView[] surfaces)
        {
            // HUD draw follows camera enqueue. Finalize before AfterUpdate publishes the native batch.
            // Camera-only updates do not mean the native HUD removed its markers.
            foreach(var packet in pending)
            {
                packet.Markers=latestMarkers;
                packet.Surfaces=surfaces;
                packet.Selection=GridSelection.Feedback;
                packet.Planets=PlacementControls.ClipboardActive ? PlanetPreview.Current:null;
                packet.Symmetry=SymmetryPlanes.Current.Length>0 && Sandbox.Game.Entities.MyCubeBuilder.Static?.IsActivated==true ? SymmetryPlanes.Current:null;
            }
            pending.Clear();
        }

        public static void Consume(object message)
        {
            Current = packets.TryGetValue(message, out var packet) ? packet.Rig : null;
            Remote=packet?.Remote;
            Surfaces=packet?.Surfaces;
            Markers=packet?.Markers;
            Crosshair=packet?.Crosshair;
            Selection=packet?.Selection;
            Symmetry=packet?.Symmetry;
            Planets=packet?.Planets;
        }
    }
}
