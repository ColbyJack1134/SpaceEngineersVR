using System;
using System.Threading;
using System.Diagnostics;
using SpaceEngineersVR.Player.Control;
using Sandbox.Engine.Utils;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SpectatorView
    {
        private static CameraRig.Frame frame;
        private static object camera;
        private static Matrix originInverse;
        private static readonly object sync=new object();
        private static readonly Diorama view=new Diorama();
        private static readonly ObserverFollow follow=new ObserverFollow();
        private static MyEntity followed;
        private static ObserverMode Mode => (ObserverMode)(Common.Config?.ThirdPersonMode ?? 0);
        private static MyEntity FollowTarget
        {
            get
            {
                var target=MyAPIGateway.SpectatorTools?.GetTarget() as MyEntity;
                return target!=null && !target.Closed && !target.MarkedForClose && target.Physics!=null ? target:null;
            }
        }
        internal static bool Following => Active && FollowTarget!=null;
        internal static bool RestoringTrackedView { get; private set; }
        private static MatrixD reference,lastApplied;
        private static long renderTime;
        private static bool consumed;
        internal static bool Manipulating => Active && consumed;
        internal static Vector3 Movement => Vector3.Zero;
        internal static Vector2 Rotation => Vector2.Zero;
        internal static float Roll => 0;
        private static int epoch=1000000;
        internal static MatrixD? CapturePose { get; private set; }
        internal static bool Active => Main.VrActive && MySession.Static?.CameraController is MySpectatorCameraController;
        internal static CameraRig.Frame Current => Active ? Volatile.Read(ref frame):null;
        internal static bool Available => MySession.Static?.LocalHumanPlayer!=null &&
            MySession.Static.HasPlayerSpectatorRights(Sync.MyId) && MyGuiScreenGamePlay.SpectatorEnabled;
        internal static bool CanTeleport => Active && MySession.Static.IsCameraUserControlledSpectator() && MySession.Static.IsUserSpaceMaster(Sync.MyId);
        internal static ActionChoice EnterAction=new ActionChoice("Spectator",Enter,enabled:()=>Available,searchTerms:"free camera F8 travel");
        internal static ActionChoice[] Actions() => new[] {
            GameActions.PauseAction,
            new ActionChoice("Move character here",Teleport,enabled:()=>CanTeleport,searchTerms:"Ctrl Space teleport"),
            new ActionChoice("Return to character",Exit,searchTerms:"F6 player control"),
            new ActionChoice("Bring view to character",Focus,searchTerms:"Ctrl F8 focus"),
            new ActionChoice("Faster",()=>Speed(1.5f)),new ActionChoice("Slower",()=>Speed(1/1.5f)),
            new ActionChoice("Lights",()=>MySpectatorCameraController.Static.SwitchLight()),
            new ActionChoice(()=>MyAPIGateway.SpectatorTools?.GetTarget()!=null ? "Unlock target":"Select follow target",()=> {
                if(MyAPIGateway.SpectatorTools?.GetTarget()!=null) MySession.Static.GetComponent<MySessionComponentSpectatorTools>()?.LockHitEntity();
                else GridSelection.Arm("Spectator lock");
            },historyKey:"Spectator target"),
            new ActionChoice(()=>ThirdPersonView.Label(Mode),ThirdPersonView.CycleMode,enabled:()=>Following,historyKey:"Camera mode"),
            new ActionChoice("Reset view",ResetView,enabled:()=>Following),
            new ActionChoice("Free spectator",()=>CameraMode(MyControlsSpace.SPECTATOR_FREE)),
            new ActionChoice("Follow character",()=>CameraMode(MyControlsSpace.SPECTATOR_DELTA)),
            new ActionChoice("Fixed spectator",()=>CameraMode(MyControlsSpace.SPECTATOR_STATIC)),
            new ActionChoice("Previous player",()=>NativeActions.Pulse(MyControlsSpace.SPECTATOR_PREVPLAYER)),
            new ActionChoice("Next player",()=>NativeActions.Pulse(MyControlsSpace.SPECTATOR_NEXTPLAYER)),
            new ActionChoice("Save tracked view 1",SaveTrackedView,enabled:()=>MyAPIGateway.SpectatorTools?.GetTarget()!=null),
            new ActionChoice("Recall tracked view 1",RecallTrackedView)
        };
        internal static void Enter()
        {
            if(!Available) return;
            ThirdPersonView.Reset(); GridSelection.Clear(); DampenerTargeting.Cancel();
            MyGuiScreenGamePlay.SetSpectatorFree();
            MySpectatorCameraController.Static.Velocity=Vector3D.Zero;
            Focus(); UpdateContext(); InputRouter.Update();
        }
        internal static void Exit()
        {
            if(!Active) return;
            GridSelection.Clear(); MyGuiScreenGamePlay.SetSpectatorNone(); Reset(); InputRouter.Update();
        }
        internal static void Focus()
        {
            if(!Active) return;
            var character=MySession.Static.LocalCharacter;
            var target=MySession.Static.ControlledEntity?.Entity ?? (MyEntity)(character?.Parent as MyCockpit) ?? character;
            if(target==null) return;
            if(Following) MySession.Static.GetComponent<MySessionComponentSpectatorTools>()?.LockHitEntity();
            MySpectatorCameraController.Static.Position=target.PositionComp.GetPosition()+MySpectatorCameraController.Static.ThirdPersonCameraDelta;
            MySpectatorCameraController.Static.SetTarget(target.PositionComp.GetPosition(),target.WorldMatrix.Up);
            ResetNavigation(); Publish();
        }
        private static void Speed(float factor)
        {
            if(!Active) return;
            lock(sync) view.ChangeScale(factor,Vector3D.Zero);
            Publish();
        }
        internal static void Teleport()
        {
            if(!CanTeleport || !Player.Headset.pose.isTracked) return;
            var eye=CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
            if(eye.IsValid()) MyMultiplayer.TeleportControlledEntity(eye.Translation);
        }
        internal static void Lock(MyEntity target)
        {
            if(!Active || !GridSelection.Eligible("Spectator lock",target)) return;
            // Native target/slot state captures the anchor without adding room/head movement again.
            CapturePose=Current?.Anchor;
            try { MyAPIGateway.SpectatorTools?.SetTarget(target); }
            finally { CapturePose=null; }
            Publish();
        }
        internal static void UpdateContext()
        {
            if(!Active) { if(camera!=null) Reset(); return; }
            if(!ReferenceEquals(camera,MySession.Static.CameraController))
            {
                VRMovementComponent.StopActive(); TrackedArms.Restore(MySession.Static.LocalCharacter);
                CameraRig.Reset(); ThirdPersonView.Reset();
                camera=MySession.Static.CameraController;
                originInverse=Matrix.Invert(Player.Headset.pose.deviceToAbsolute.matrix);
                ResetNavigation(); Controls.Static.BlockUntilRelease();
            }
            Publish();
        }
        private static MatrixD NativeAnchor()
        {
            var spectator=MySpectatorCameraController.Static;
            var anchor=spectator.Orientation; anchor.Translation=spectator.Position; return anchor;
        }
        private static void ResetNavigation()
        {
            lock(sync)
            {
                followed=null; reference=lastApplied=NativeAnchor(); view.Fit(.8,MatrixD.Identity,Vector3D.Zero);
                consumed=false; renderTime=0; epoch++;
            }
        }
        internal static MatrixD AdvanceReference(MatrixD basis,MatrixD applied,MatrixD native) => applied==native ? basis:basis*MatrixD.Invert(applied)*native;
        internal static void Publish()
        {
            if(!Active || camera==null) return;
            lock(sync)
            {
                var native=NativeAnchor();
                if(!native.IsValid()) return;
                var target=FollowTarget;
                if(target!=followed)
                {
                    followed=target;
                    if(target!=null)
                    {
                        var gravity=target.Physics.Gravity;
                        var vertical=gravity.LengthSquared()>.01 ? -(Vector3D)Vector3.Normalize(gravity) : target.WorldMatrix.Up;
                        follow.Reset(target.WorldMatrix,vertical);
                        view.SetAnchor(native,TargetCenter(target),follow.Reference(Mode));
                    }
                    else
                    {
                        reference=native;
                        view.SetAnchor(native,native.Translation,native.GetOrientation());
                    }
                    epoch++;
                }
                Vector3D center; MatrixD orientation;
                if(target!=null)
                {
                    follow.Advance(target.WorldMatrix);
                    center=TargetCenter(target); orientation=follow.Reference(Mode);
                }
                else
                {
                    // Free native camera changes advance the reference without reapplying our gesture offset.
                    reference=AdvanceReference(reference,lastApplied,native);
                    center=reference.Translation; orientation=reference.GetOrientation();
                }
                var anchor=view.Anchor(center,orientation);
                MySpectatorCameraController.Static.SetTarget(anchor.Translation+anchor.Forward,anchor.Up);
                MySpectatorCameraController.Static.Position=anchor.Translation;
                lastApplied=NativeAnchor();
                var scene=new Diorama.Scene(center,orientation,Stopwatch.GetTimestamp());
                Volatile.Write(ref frame,new CameraRig.Frame(anchor,originInverse,epoch,view.UnitsPerMeter,true,scene));
            }
        }
        internal static void UpdateGestures()
        {
            if(!Active) return;
            var c=Controls.Static;
            float left=c.LeftGripPressure.RawPosition.X,right=c.RightGripPressure.RawPosition.X;
            if(left<=.025f && right<=.025f) consumed=false;
            bool allowed=InputRouter.Gameplay && !Main.MenuOpen && !HelmetHud.ViewGestureHeld &&
                !PlacementControls.Adjusting && !FloatingWindows.OwnsInput && !SpatialUi.OwnsRight &&
                !CockpitTouch.Owns(Player.HandL) && !CockpitTouch.OwnsRight &&
                Player.Headset.pose.isTracked && Player.HandL.pose.isTracked && Player.HandR.pose.isTracked;
            bool started;
            lock(sync) { started=view.Input(allowed,left,right); if(view.Held) consumed=true; }
            if(started)
            {
                NativeActions.Reset();
                Player.HandL.Vibrate(0,.018f,100,.15f); Player.HandR.Vibrate(0,.018f,100,.15f);
            }
            if(consumed)
            {
                c.Primary.BlockUntilRelease(); c.Secondary.BlockUntilRelease(); c.ThrustUp.BlockUntilRelease(); c.ThrustDown.BlockUntilRelease();
                c.CrouchOrClimbDown.BlockUntilRelease(); c.ThrustRoll.BlockUntilRelease(); c.ThrustLRFB.BlockUntilRelease();
                c.ThrustLRUD.BlockUntilRelease(); c.ThrustRotate.BlockUntilRelease(); c.ThrustForward.BlockUntilRelease(); c.ThrustBackward.BlockUntilRelease();
            }
            Publish();
        }
        internal static CameraRig.Frame RenderFrame(CameraRig.Frame packet)
        {
            if(packet?.Observer==null) return packet;
            lock(sync)
            {
                if(packet.Epoch!=epoch || Current?.Epoch!=packet.Epoch) return packet;
                return view.RenderFrame(packet,Stopwatch.GetTimestamp(),ref renderTime);
            }
        }
        internal static void Move()
        {
            if(!Active || !InputRouter.Gameplay || Main.MenuOpen) return;
            var c=Controls.Static;
            if(c.Unequip.HasPressed && !GridSelection.Active) { c.Unequip.BlockUntilRelease();
                if(PlacementControls.ClipboardActive) PlacementControls.Cancel(); else Exit();
                return; }
            if(c.SpectatorMode.HasPressed) { Exit(); return; }
            if(c.Teleport.HasPressed) Teleport();
            if(c.Lights.HasPressed) MySpectatorCameraController.Static.SwitchLight();
            if(c.Pause.HasPressed) GameActions.Execute(GameActions.PauseAction);
            if(c.ToolbarConfig.HasPressed) GameActions.Execute(GameActions.ConfigureToolbarAction);
            if(c.CopyGrid.HasPressed) new Sandbox.Game.Screens.Helpers.RadialMenuActions.MyActionCopyGrid().ExecuteAction();
            if(c.CutGrid.HasPressed) new Sandbox.Game.Screens.Helpers.RadialMenuActions.MyActionCutGrid().ExecuteAction();
            if(c.PasteGrid.HasPressed) new Sandbox.Game.Screens.Helpers.RadialMenuActions.MyActionPasteGrid().ExecuteAction();
            Publish();
        }
        private static void CameraMode(VRage.Utils.MyStringId control)
        {
            if(Following) MySession.Static.GetComponent<MySessionComponentSpectatorTools>()?.LockHitEntity();
            NativeActions.Pulse(control);
        }
        private static void SaveTrackedView()
        {
            var target=FollowTarget;
            if(target==null) return;
            Lock(target);
            MyAPIGateway.SpectatorTools?.SaveTrackedSlot(0);
        }
        private static void RecallTrackedView()
        {
            var tools=MySession.Static.GetComponent<MySessionComponentSpectatorTools>();
            if(tools==null) return;
            tools.SelectTrackedSlot(0);
            if(FollowTarget==null) { Publish(); return; }
            var mode=tools.GetMode();
            try
            {
                RestoringTrackedView=true;
                tools.SetMode(VRage.Game.ModAPI.MyCameraMode.Follow);
                tools.UpdateAfterSimulation();
            }
            finally { tools.SetMode(mode); RestoringTrackedView=false; }
            lock(sync) followed=null;
            Publish();
        }
        private static Vector3D TargetCenter(MyEntity target) => Vector3D.Transform(target.PositionComp.LocalAABB.Center,target.WorldMatrix);
        internal static void ChangeMode(ObserverMode previous,ObserverMode next)
        {
            if(!Following) return;
            lock(sync) { view.ChangeReference(follow.Reference(previous),follow.Reference(next)); epoch++; }
            Publish();
        }
        internal static void ResetView()
        {
            if(!Following || !Player.Headset.pose.isTracked) return;
            Publish();
            lock(sync)
            {
                if(followed==null) return;
                var head=followed is Sandbox.Game.Entities.Character.MyCharacter character ? character.GetHeadMatrix(false) : followed.WorldMatrix;
                var gravity=followed.Physics.Gravity;
                var vertical=gravity.LengthSquared()>.01 ? -(Vector3D)Vector3.Normalize(gravity) : head.Up;
                follow.Reset(followed.WorldMatrix,vertical);
                view.FitTarget(followed.PositionComp.LocalAABB.Size.Length(),head,vertical,
                    Player.Headset.pose.deviceToAbsolute.matrix*originInverse,follow.Reference(Mode),Mode==ObserverMode.Ship);
                consumed=false; renderTime=0; epoch++;
            }
            Publish();
        }
        internal static void Recenter(Matrix newOrigin)
        {
            if(!Active || Current==null) return;
            if(Following)
            {
                originInverse=Matrix.Invert(newOrigin);
                ResetView(); return;
            }
            var eye=CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
            MySpectatorCameraController.Static.Position=eye.Translation;
            MySpectatorCameraController.Static.SetTarget(eye.Translation+eye.Forward,eye.Up);
            originInverse=Matrix.Invert(Player.Headset.pose.deviceToAbsolute.matrix); ResetNavigation(); Publish();
        }
        internal static void Reset()
        {
            lock(sync) { Volatile.Write(ref frame,null); camera=null; followed=null; view.Cancel(); consumed=false; renderTime=0; epoch++; }
        }
    }
}
