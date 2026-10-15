using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Manual-mode aim and painting. Manual weapons follow the head, the action hand's pointer or WeaponCore's own rule
    // (Mode). WeaponCore refreshes its aim point in a seat only while Alt is held, so the VR point is written before its
    // input step syncs it to the server and again after it computes its own.
    internal static class WeaponCoreAim
    {
        internal enum Mode { Head, Hand, Ship }
        private static readonly string[] names={"Head","Hand","Ship"};
        private static MethodBase patched,input;
        private static FieldInfo aimPositionField,aimDirectionField;
        internal static Vector3D? AimPoint { get; private set; }
        internal static DateTime AimTime { get; private set; }

        // True while a weapon the player controls is in Manual mode (WeaponCore stamps LastManualTick each tick for those).
        internal static bool ManualActive
        {
            get
            {
                uint last=ModReflection.Value(WeaponCoreInternals.TargetUi,0u,"LastManualTick");
                return last!=0 && WeaponCoreInternals.Tick-last<=2;
            }
        }
        internal static Mode Current => (Mode)Math.Max(0,Math.Min(2,Common.Config.WeaponCoreAim));
        internal static string ModeName => names[(int)Current];
        internal static void CycleMode() => Common.Config.WeaponCoreAim=((int)Current+1)%names.Length;

        internal static void Attach(Type targetUi)
        {
            if(patched!=null) return;
            if(targetUi==null) { Logger.Info("WeaponCore TargetUi not found; VR manual aim and painting disabled"); return; }
            var method=AccessTools.Method(targetUi,"SelectTarget",new[] {typeof(bool),typeof(bool),typeof(bool)});
            if(method==null) { Logger.Info("WeaponCore SelectTarget not found; VR manual aim disabled"); return; }
            Common.Plugin.Harmony.Patch(method,postfix:new HarmonyMethod(typeof(WeaponCoreAim),nameof(AfterSelect)));
            patched=method;
            aimPositionField=AccessTools.Field(targetUi,"AimPosition"); aimDirectionField=AccessTools.Field(targetUi,"AimDirection");
            var session=targetUi.Assembly.GetType("CoreSystems.Session",false);
            var handle=session==null ? null:AccessTools.Method(session,"HandleInput",Type.EmptyTypes);
            if(handle!=null) { Common.Plugin.Harmony.Patch(handle,prefix:new HarmonyMethod(typeof(WeaponCoreAim),nameof(BeforeInput))); input=handle; }
            else Logger.Info("WeaponCore HandleInput not found; VR aim only updates while WeaponCore updates its own");
            Logger.Info("WeaponCore VR manual aim attached");
        }

        internal static void Detach()
        {
            if(patched!=null) Common.Plugin.Harmony.Unpatch(patched,AccessTools.Method(typeof(WeaponCoreAim),nameof(AfterSelect)));
            if(input!=null) Common.Plugin.Harmony.Unpatch(input,AccessTools.Method(typeof(WeaponCoreAim),nameof(BeforeInput)));
            patched=input=null; aimPositionField=aimDirectionField=null; AimPoint=null; paintRequest=PaintRequest.None;
            paintArm.Disarm(); PaintPreview=null;
        }

        // manualSelect=false is WeaponCore's per-frame aim update; selection clicks and probes are left alone.
        private static void AfterSelect(object __instance,bool manualSelect,bool checkOnly)
        {
            if(manualSelect || checkOnly) return;
            Apply(__instance);
        }

        private static void BeforeInput()
        {
            if(WeaponCoreInternals.TrackingAi==null) { paintRequest=PaintRequest.None; return; }
            ApplyPaint();
            // Hand weapons aim with the hand itself; only seated (grid) weapons take the VR aim point.
            if(!WeaponCoreInternals.HandheldActive) Apply(WeaponCoreInternals.TargetUi);
        }

        // Painter: marks a point on a target for painter-mode / guided weapons. Requests are applied in WeaponCore's input
        // step so it syncs the mark to the server on that tick. Paints along the VR aim (or ahead in Ship mode).
        private enum PaintRequest { None, Paint, Clear }
        private static volatile PaintRequest paintRequest;
        internal static void RequestClearPaint() => paintRequest=PaintRequest.Clear;
        internal static bool Painted => WeaponCoreInternals.PaintedEntity!=0;

        // The Paint action only arms the painter: the hand that pressed a wrist or cockpit button is not aiming. The next
        // trigger pull marks along the aim instead of firing; the action again, or 10 s without a pull, cancels.
        private static readonly TriggerArm paintArm=new TriggerArm(10);
        internal static bool PaintArmed => paintArm.Armed;
        // Where a pull would mark now; null when the aim hits nothing.
        internal static Vector3D? PaintPreview { get; private set; }

        internal static void RequestPaint()
        {
            if(PaintArmed) { Disarm("Paint cancelled"); return; }
            if(TargetLock.ShooterGrid()==null) return;
            if(WeaponCoreInternals.PaintProhibited) { EssentialHud.Notify("Painting is disabled on this server"); return; }
            paintArm.Arm();
            EssentialHud.Notify("Paint armed: pull the trigger to mark");
            Handedness.ActionHand.Vibrate(0,.03f,120,.15f);
        }

        private static void Disarm(string notice)
        {
            paintArm.Disarm(); PaintPreview=null;
            if(notice!=null) EssentialHud.Notify(notice);
        }

        // Main thread, after controls are polled.
        internal static void UpdatePaintArm()
        {
            if(!PaintArmed) return;
            if(TargetLock.ShooterGrid()==null) { Disarm(null); return; }
            if(paintArm.Expired) { Disarm("Paint cancelled"); return; }
            PaintPreview=null;
            if(TryRay(out var origin,out var direction) || TargetLock.TryAheadRay(out origin,out direction))
            {
                if(Cast(new LineD(origin,origin+direction*WeaponCoreInternals.TargetingRange),out var point)!=null) PaintPreview=point;
            }
            if(!paintArm.Pulled()) return;
            PaintPreview=null;
            paintRequest=PaintRequest.Paint;
        }

        private static void ApplyPaint()
        {
            var request=paintRequest; paintRequest=PaintRequest.None;
            if(request==PaintRequest.None || !WeaponCoreBridge.ApiReady) return;
            try
            {
                if(request==PaintRequest.Clear)
                {
                    if(WeaponCoreInternals.UpdateFake("PaintedTarget",Vector3D.Zero,null)) { EssentialHud.Notify("Paint cleared"); WeaponCoreMarkers.Invalidate(); }
                    return;
                }
                // A missed pull stays armed so the player can simply aim again.
                if(!TryRay(out var origin,out var direction) && !TargetLock.TryAheadRay(out origin,out direction)) { paintArm.Arm(); EssentialHud.Notify("No aim direction; paint still armed"); return; }
                var hit=Cast(new LineD(origin,origin+direction*WeaponCoreInternals.TargetingRange),out var point);
                if(hit==null) { paintArm.Arm(); EssentialHud.Notify("Nothing to paint; still armed"); return; }
                if(WeaponCoreInternals.UpdateFake("PaintedTarget",point,hit)) { EssentialHud.Notify("Painted "+(hit.DisplayName ?? "target")); Handedness.ActionHand.Vibrate(0,.03f,120,.2f); WeaponCoreMarkers.Invalidate(); }
            }
            catch(Exception ex) { Logger.Warning(ex,"WeaponCore VR paint failed"); }
        }

        private static void Apply(object targetUi)
        {
            if(!Main.VrActive || Main.MenuOpen || !WeaponCoreBridge.ApiReady) return;
            try
            {
                if(!TryRay(out var origin,out var direction)) return;
                var end=origin+direction*WeaponCoreInternals.TargetingRange;
                var hit=Cast(new LineD(origin,end),out var point);
                if(!WeaponCoreInternals.UpdateFake("ManualTarget",hit!=null ? point:end,hit)) return;
                // Keep WeaponCore's own reticle state consistent with the aim it now uses.
                if(targetUi!=null) { aimPositionField?.SetValue(targetUi,origin); aimDirectionField?.SetValue(targetUi,direction); }
                AimPoint=hit!=null ? point:end; AimTime=DateTime.UtcNow;
            }
            catch(Exception ex) { Logger.Warning(ex,"WeaponCore VR manual aim failed; WeaponCore aim retained"); Detach(); }
        }

        private static bool TryRay(out Vector3D origin,out Vector3D direction)
        {
            origin=direction=Vector3D.Zero;
            var remote=RemoteView.Current;
            if(remote!=null)
            {
                // Through a camera, aim where the hand points on the image; otherwise keep WeaponCore's camera centre.
                return Current!=Mode.Ship && RemoteView.ImageUV.HasValue &&
                    FeedCamera.TryRay(remote.Source,RemoteView.ImageUV.Value,out origin,out direction);
            }
            if(Current==Mode.Ship) return false;
            if(Current==Mode.Hand)
            {
                if(!GridSelection.TryPointer(Handedness.ActionHand,out var pose)) return false;
                origin=pose.Translation; direction=pose.Forward;
            }
            else
            {
                if(!Player.Headset.pose.isTracked) return false;
                var head=CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
                origin=head.Translation; direction=head.Forward;
            }
            if(!direction.IsValid() || direction.LengthSquared()<.5) return false;
            direction.Normalize();
            return true;
        }

        // First hit that is not part of the player's own construct (grids joined by rotors, pistons or connectors).
        private static MyEntity Cast(LineD ray,out Vector3D point)
        {
            point=ray.To;
            var own=(MySession.Static?.ControlledEntity?.Entity as MyCubeBlock)?.CubeGrid;
            var start=ray.From;
            for(int i=0;i<4;i++)
            {
                var segment=new LineD(start,ray.To);
                var hit=GridSelection.Cast(segment,out var at);
                if(hit==null) return null;
                bool self=own!=null && hit is MyCubeGrid grid && (grid==own || MyCubeGridGroups.Static.Logical.HasSameGroup(grid,own));
                if(!self) { point=at; return hit; }
                start=at+ray.Direction*.5;
            }
            return null;
        }
    }
}
