using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Pose is raw tracking space.
    internal static class DesktopWindow
    {
        internal sealed class View
        {
            public MatrixD Pose;
            public float Width,Height;
            public int Hover,Monitor;
            public Vector3D? RayStart,RayEnd;
            public bool LeftHand;
        }
        internal const int ImageHover=7;
        private const string Key="Desktop";
        private static readonly MenuWindow window=new MenuWindow();
        private static readonly WindowInteraction interaction=new WindowInteraction(window);
        private static readonly RenderRecovery recovery=new RenderRecovery("Desktop window");
        private static bool open,left,placementDirty;
        private static long placedFor=-1;
        private static DateTime last;
        private static int lastHover;
        private static Vector3D? rayStart,rayEnd;
        private static volatile View current;
        internal static View Current => current;
        internal static bool IsOpen => open;
        internal static bool OwnsInput { get; private set; }
        private static Controller Hand => left ? Player.HandL:Player.HandR;
        private static bool Pointed => open && (lastHover!=0 || interaction.Active || rayStart.HasValue);
        internal static bool PointingFor(Controller hand) => Pointed && hand==Hand;
        internal static bool TryAttachment(Controller hand,out MatrixD wrist,out Vector3D point,out float blend,bool tracking=false) =>
            interaction.TryAttachment(hand,TrackingToWorld,tracking,out wrist,out point,out blend) & open;
        private static MyCockpit Seat => SeatFit.Eligible(SeatFit.Seat) ? SeatFit.Seat : null;
        private static MatrixD TrackingToWorld => SpatialUi.DeviceWorld(Matrix.Identity);

        internal static void Open()
        {
            if(!Main.WorldAvailable) return;
            // Reopening applies the saved spot again, so a mirror left behind the player comes back in front.
            if(!open) placedFor=-1;
            open=true; Publish();
        }
        internal static void Close()
        {
            if(!open) return;
            if(placementDirty) Save();
            open=false; current=null; interaction.Reset(); OwnsInput=false; lastHover=0; rayStart=rayEnd=null;
            Controls.Static.BlockUntilRelease();
        }
        internal static void Reset()
        {
            open=false; current=null; interaction.Reset(true); OwnsInput=false; lastHover=0; rayStart=rayEnd=null;
            placedFor=-1; placementDirty=false;
        }
        internal static void Follow(Matrix oldOrigin,Matrix newOrigin)
        { if(open && Seat!=null) { window.Cancel(); window.Pose=WindowSpots.Follow(window.Pose,oldOrigin,newOrigin); } }
        internal static void ResetPlacement() { if(open) placedFor=-1; }
        internal static bool CloseIfPointed()
        {
            if(!Pointed) return false;
            Close(); return true;
        }
        private static MatrixD Aim(Controller hand) => WindowInteraction.Aim(hand,TrackingToWorld);
        // The window draws over the world, so it ignores world obstacles but yields to controls the hand is touching.
        private static bool Free(Controller hand) => hand.pose.isTracked && !CockpitControls.Held(hand) && !CockpitControls.NearGrip(hand) &&
            !CockpitTouch.Owns(hand) && !CockpitTouch.Hovering(hand) && !RemoteView.OwnsInput && !TouchScreenBridge.OwnsInput &&
            (hand==Player.HandL || !SpatialUi.OwnsRight && !SpatialUi.RayTargeted);

        internal static void Update()
        {
            OwnsInput=false; rayStart=rayEnd=null;
            if(!open) return;
            if(!Main.WorldAvailable) { Reset(); return; }
            DesktopCapture.Request();
            window.Aspect=DesktopCapture.Aspect;
            var seat=Seat;
            long seatId=seat?.EntityId ?? 0;
            if(seatId!=placedFor)
            {
                placedFor=seatId; interaction.Reset(true); placementDirty=false;
                if(!Restore(seat)) Place();
            }
            var now=DateTime.UtcNow; float seconds=(float)Math.Min(.05,Math.Max(0,(now-last).TotalSeconds)); last=now;
            if(InputRouter.Gameplay && !Main.MenuOpen) left=interaction.PickHand(Hand,Free,Aim)==Player.HandL;
            var hand=Hand;
            bool available=InputRouter.Gameplay && !Main.MenuOpen && !ThirdPersonView.Manipulating && Free(hand);
            if(!available) { interaction.Reset(); if(placementDirty) Save(); lastHover=0; Publish(); return; }
            MatrixD aim=Aim(hand);
            int ExtraHit(Vector3 point,bool near)
            {
                int control=WindowFrame.Control(point,window.Width,window.Height,window.BarOffset,false,true,DesktopCapture.Monitors.Length>1);
                if(control!=0) return control;
                if(Math.Abs(point.X)>=window.Width/2 || Math.Abs(point.Y)>=window.Height/2) return 0;
                return near ? 1 : ImageHover;
            }
            interaction.Update(hand,(Matrix)aim,seconds,ExtraHit);
            int hover=interaction.Hover;
            OwnsInput=interaction.Active || interaction.Released;
            if(interaction.Changed) placementDirty=true;
            if(interaction.Released && placementDirty) Save();
            if(interaction.Ray(aim,_=>true,out var start,out var end)) { rayStart=start; rayEnd=end; }
            if(interaction.Captured)
            {
                interaction.Grab(hand,TrackingToWorld);
                if(interaction.HeldAction==WindowFrame.CloseHover) { Close(); return; }
                if(interaction.HeldAction==WindowFrame.MonitorHover) DesktopCapture.NextMonitor();
                if(interaction.HeldAction==ImageHover) DesktopCapture.PlayPause();
            }
            if(hover!=0 || interaction.Active || rayStart.HasValue) WindowInteraction.Claim(hand);
            lastHover=hover;
            Publish(window.Drag!=0 ? window.Drag:hover);
        }
        private static void Place()
        {
            window.Stop(); window.Width=MathHelper.Clamp(MenuWindow.DefaultHeight/window.Aspect,MenuWindow.MinWidth,MenuWindow.MaxWidth);
            window.Pose=MenuWindow.Ahead(Player.Headset.pose.deviceToAbsolute.matrix,MathHelper.ToRadians(30));
        }
        private static MatrixD ToSeat(MyCockpit seat) => TrackingToWorld*seat.PositionComp.WorldMatrixNormalizedInv;
        private static string Spot(MyCockpit seat) => seat!=null ? "Mirror/"+seat.BlockDefinition.Id.SubtypeName:"Mirror";
        private static void Save() => Save(Seat);
        private static void Save(MyCockpit seat)
        {
            placementDirty=false;
            if(placedFor<0 || (seat?.EntityId ?? 0)!=placedFor) return;
            var local=(Matrix)((MatrixD)window.Pose*(seat!=null ? ToSeat(seat):MatrixD.Identity));
            WindowSpots.Store(Spot(seat),local,window.Width,window.Height);
        }
        private static bool Restore(MyCockpit seat)
        {
            if(!WindowSpots.Find(Spot(seat),out var local,out var saved) || !saved.Width.IsValid() || saved.Width<MenuWindow.MinWidth || saved.Width>MenuWindow.MaxWidth) return false;
            var pose=seat!=null ? (Matrix)((MatrixD)local*MatrixD.Invert(ToSeat(seat))):local;
            if(seat==null && !WindowSpots.Keep(pose,Player.Headset.pose.deviceToAbsolute.matrix)) return false;
            window.Stop(); window.Width=saved.Width; window.Pose=pose;
            return true;
        }
        private static void Publish(int hover=-1)
        {
            if(!open) { current=null; return; }
            current=new View {Pose=(MatrixD)window.Pose,Width=window.Width,Height=window.Height,Hover=hover<0 ? lastHover:hover,
                Monitor=DesktopCapture.DisplayedNumber,RayStart=rayStart,RayEnd=rayEnd,LeftHand=left};
        }
        internal static void Draw(Texture2D target,MatrixD view,MatrixD projection)
        {
            var v=current;
            if(v==null || recovery.Failed) return;
            try
            {
                DrawPanel(target,DesktopCapture.View,v,view,projection,DesktopCapture.BadgeAlpha(DateTime.UtcNow),NativeHandLayer.Depth);
                if(v.RayStart.HasValue && v.RayEnd.HasValue)
                {
                    var delta=v.RayEnd.Value-v.RayStart.Value;
                    if(delta.LengthSquared()>.0001)
                        PhysicalSurface.Draw(target,new[] {new SurfaceView {Id="DesktopRay",Style=SurfaceStyle.Pointer,Width=.002f,Height=(float)delta.Length(),LeftHand=v.LeftHand,
                            Pose=MatrixD.CreateWorld((v.RayStart.Value+v.RayEnd.Value)*.5,Vector3D.Normalize(delta),v.Pose.Up)}},view,projection,null);
                }
                recovery.Succeeded();
            }
            catch(Exception ex) { recovery.Fail(ex,"Desktop window hidden"); }
        }
        internal static void DrawPanel(Texture2D target,ShaderResourceView picture,View v,MatrixD view,MatrixD projection,float badgeAlpha,ShaderResourceView hands=null)
        {
            WindowFrame.Draw(target,Key,new WindowFrame.Snapshot {Pose=(Matrix)v.Pose,Width=v.Width,Height=v.Height,Hover=v.Hover,Close=true,Monitor=v.Monitor},
                view,projection,hands);
            var sprites=new List<NativeSprite>();
            if(picture!=null) sprites.Add(DesktopCapture.Picture(picture,v.Pose,v.Width,v.Height,view,projection,target.Description.Format,0));
            if(DesktopCapture.Badge(target.Device,v.Pose,.14f,view,projection,.002f,badgeAlpha,out var badge)) sprites.Add(badge);
            if(sprites.Count>0) NativeSprites.Draw(target,sprites,handDepth:hands);
        }
    }

    internal static class FloatingWindows
    {
        internal static bool OwnsInput => RemoteView.OwnsInput || DesktopWindow.OwnsInput;
        internal static bool PointingFor(Controller hand) => RemoteView.PointingFor(hand) || DesktopWindow.PointingFor(hand);
        internal static bool TryAttachment(Controller hand,out MatrixD wrist,out Vector3D point,out float blend,bool tracking=false) =>
            RemoteView.TryAttachment(hand,out wrist,out point,out blend,tracking) || DesktopWindow.TryAttachment(hand,out wrist,out point,out blend,tracking);
    }
}
