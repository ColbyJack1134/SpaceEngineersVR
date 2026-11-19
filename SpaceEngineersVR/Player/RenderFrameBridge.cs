using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VRageMath;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class RenderFrameBridge
    {
        private sealed class Packet { public CameraRig.Frame Rig; public RemoteView.View Remote; public SurfaceView[] Surfaces; public WorldMarkers.View Markers; public ShipCrosshair.View Crosshair; public TargetFeedback.View Selection; public (Controller Hand,Vector3D Hit)? ObserverUse; public HoloSight.View Holo; public SymmetryPlanes.View[] Symmetry; public VRageRender.Messages.MyRenderMessageDebugDrawSphere[] Planets; public VoiceMarkers.Talker[] Talking; public LcdCameras.View[] Lcds; public SmoothFramesBridge.Tick Smoothing; }
        private static readonly ConditionalWeakTable<object, Packet> packets = new ConditionalWeakTable<object, Packet>();
        private static readonly List<Packet> pending=new List<Packet>();
        private static WorldMarkers.View latestMarkers;
        public static CameraRig.Frame Current { get; private set; }
        public static SurfaceView[] Surfaces { get; private set; }
        public static RemoteView.View Remote { get; private set; }
        public static WorldMarkers.View Markers { get; private set; }
        public static ShipCrosshair.View Crosshair { get; private set; }
        internal static TargetFeedback.View Selection { get; private set; }
        internal static (Controller Hand,Vector3D Hit)? ObserverUse { get; private set; }
        internal static HoloSight.View Holo { get; private set; }
        internal static SymmetryPlanes.View[] Symmetry { get; private set; }
        internal static VRageRender.Messages.MyRenderMessageDebugDrawSphere[] Planets { get; private set; }
        internal static VoiceMarkers.Talker[] Talking { get; private set; }
        internal static LcdCameras.View[] Lcds { get; private set; }
        public static CameraRig.Frame ForCurrentOwner(CameraRig.Frame current)
        {
            // During respawn/ejection, keep validated eye height until the new batch arrives.
            return Current?.Epoch == current?.Epoch ? Current : current;
        }

        public static void Capture(object message, CameraRig.Frame rig,ShipCrosshair.View crosshair=null)
        {
            // Render messages are pooled; replace the previous use before publishing the batch.
            packets.Remove(message);
            var packet=new Packet { Rig=rig,Remote=RemoteView.Current,Surfaces=SpatialUi.Current,Crosshair=crosshair,Smoothing=SmoothFramesBridge.Capture(message,rig) };
            packets.Add(message,packet);
            pending.Add(packet);
        }

        internal static void CaptureBatch(IEnumerable<MyRenderMessageBase> messages,CameraRig.Frame rig)
        {
            // Inspect the messages the renderer will receive, including deferred worker commands.
            foreach(var message in messages)
            {
                if(message is MyRenderMessageDrawCommands commands) CaptureBatch(commands.Messages,rig);
                else if(message is MyRenderMessageSetCameraViewMatrix) Capture(message,rig,ShipCrosshair.Capture());
                else if(message is MyRenderMessageSetCharacterTransforms bones) SmoothFrames.CaptureBones(bones);
            }
        }

        public static void CaptureMarkers(WorldMarkers.View markers) => latestMarkers=markers;
        public static void Commit() => Commit(SpatialUi.Current);
        internal static void Commit(SurfaceView[] surfaces)
        {
            // HUD draw follows camera enqueue. Finalize before AfterUpdate publishes the native batch.
            // Camera-only updates do not mean the native HUD removed its markers.
            var markers=pending.Count>0 ? WorldMarkers.WithWeaponCore(WorldMarkers.Live(latestMarkers)) : null;
            var talking=pending.Count>0 ? VoiceMarkers.Capture() : null;
            var lcds=pending.Count>0 ? LcdCameras.Capture() : null;
            long smoothingBatch=SmoothFramesBridge.BeginCommit();
            foreach(var packet in pending)
            {
                packet.Markers=markers;
                packet.Talking=talking;
                packet.Lcds=lcds;
                packet.Surfaces=surfaces;
                packet.Selection=GridSelection.Feedback ?? TargetLock.Feedback;
                if(ThirdPersonView.Character && InputRouter.Gameplay) packet.ObserverUse=HandInteraction.ObserverUse;
                packet.Holo=HoloSight.Capture();
                SmoothFramesBridge.Commit(packet.Smoothing,smoothingBatch);
                packet.Planets=PlacementControls.ClipboardActive ? PlanetPreview.Current:null;
                packet.Symmetry=SymmetryPlanes.Current.Length>0 && Sandbox.Game.Entities.MyCubeBuilder.Static?.IsActivated==true ? SymmetryPlanes.Current:null;
            }
            pending.Clear();
        }

        public static void Consume(object message)
        {
            if(SmoothFramesBridge.IsSynthetic(message)) { SmoothFramesBridge.Consume(message,null); return; }
            Current = packets.TryGetValue(message, out var packet) ? packet.Rig : null;
            SmoothFramesBridge.Consume(message,packet?.Smoothing);
            Remote=packet?.Remote;
            Surfaces=packet?.Surfaces;
            Markers=packet?.Markers;
            Crosshair=packet?.Crosshair;
            Selection=packet?.Selection;
            ObserverUse=packet?.ObserverUse;
            Holo=packet?.Holo;
            Symmetry=packet?.Symmetry;
            Planets=packet?.Planets;
            Talking=packet?.Talking;
            Lcds=packet?.Lcds;
        }
    }
}
