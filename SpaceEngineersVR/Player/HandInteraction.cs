using System;
using HarmonyLib;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public static class HandInteraction
    {
        private static bool disabled;
        private static bool rayVisible;
        internal static bool ShowRay(float pressure,bool wasVisible) => pressure>=(wasVisible ? .025f : .06f);
        private static readonly Action<MyCharacterDetectorComponent, bool> detect =
            AccessTools.MethodDelegate<Action<MyCharacterDetectorComponent, bool>>(
                AccessTools.Method(typeof(MyCharacterDetectorComponent), "DoDetection"));
        public static bool TryWorldPose(Controller controller, out MatrixD world)
        {
            world = MatrixD.Identity;
            if (!Main.VrActive || Main.MenuOpen || !controller.pose.isTracked || MySector.MainCamera == null || MySession.Static?.LocalCharacter == null)
                return false;
            world = CameraRig.DeviceWorld(controller.AimTracking);
            return world.IsValid();
        }

        public static bool TryInteractionRay(out LineD ray)
        {
            ray = default(LineD);
            var character = MySession.Static?.LocalCharacter;
            if (!InputRouter.Gameplay || character == null || character.IsDead || character.IsSitting ||
                MySession.Static.ControlledEntity != character || !TryWorldPose(Player.HandR, out MatrixD pose)) return false;
            if (WeaponHandling.TryPose(character, out MatrixD model, out Vector3D muzzle))
            { pose = model; pose.Translation = muzzle; }
            ray = RayForPose(pose);
            return true;
        }

        internal static LineD RayForPose(MatrixD pose) =>
            new LineD(pose.Translation, pose.Translation + pose.Forward * MyConstants.DEFAULT_INTERACTIVE_DISTANCE);

        public static void RefreshTarget()
        {
            // Native hover updates run every ten ticks. Refresh on an action so a
            // moved controller cannot activate the object highlighted on an old ray.
            if (!TryInteractionRay(out _)) return;
            var detector = MySession.Static.LocalCharacter.GetDetectorComponent();
            if (detector == null) return;
            detect(detector, true);
            if(Common.Config.DeveloperTools) Logger.Info("INTERACTION detector=" + detector.GetType().Name + "; origin=" + detector.StartPosition +
                "; use=" + (detector.UseObject?.GetType().Name ?? "none") +
                "; target=" + (detector.UseObject?.Owner?.DisplayName ?? "none"));
        }
        public static void Update()
        {
            if (disabled) return;
            rayVisible=InputRouter.Gameplay && Player.HandR.pose.isTracked &&
                (ShowRay(Controls.Static.PointerPressure.RawPosition.X,rayVisible) || Controls.Static.Primary.RawPressed);
            try
            {
                foreach (var hand in new[] { Player.HandL, Player.HandR })
                {
                    if (!TryWorldPose(hand,out MatrixD world)) continue;
                    var color = hand == Player.HandR ? new Color(60,220,255) : new Color(255,180,60);
                    var lineColor = color.ToVector4();
                    // Grip marker belongs at the raw tracked controller origin, not at
                    // its tip attachment. Only the aiming ray/tool uses the tip pose.
                    MatrixD grip=CameraRig.DeviceWorld(hand.pose.deviceToAbsolute.matrix);
                    if(Common.Config.DeveloperTools) MySimpleObjectDraw.DrawLine(grip.Translation-grip.Up*0.035,grip.Translation+grip.Up*0.035,
                        MyStringId.GetOrCompute("Square"),ref lineColor,0.025f);
                    if (hand == Player.HandR && rayVisible && TryInteractionRay(out LineD ray))
                    {
                        MySimpleObjectDraw.DrawLine(ray.From, ray.To,
                            MyStringId.GetOrCompute("Square"), ref lineColor, 0.002f);
                    }
                }
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Hand indicators disabled"); }
        }
        public static bool TryInteract()
        {
            var block = TargetBlock();
            if (block is IMyDoor door) { if (door.OpenRatio > 0) door.CloseDoor(); else door.OpenDoor(); }
            else if (block is IMyLightingBlock light) light.Enabled = !light.Enabled;
            else return false;
            Player.HandR.Vibrate(0,0.06f,120,0.4f);
            return true;
        }
        private static IMyTerminalBlock TargetBlock()
        {
            if (!TryInteractionRay(out LineD ray)) return null;
            // Door/light convenience actions must not override a native cockpit,
            // button panel or other use-object selected along the same ray.
            if (MySession.Static.LocalCharacter.GetDetectorComponent()?.UseObject != null) return null;
            Vector3D from = ray.From;
            Vector3D to = ray.To;
            if (MyAPIGateway.Physics == null || !MyAPIGateway.Physics.CastRay(from,to,out IHitInfo hit)) return null;
            var grid = hit.HitEntity as IMyCubeGrid;
            if (grid == null) return null;
            var cell = grid.RayCastBlocks(from,to);
            var block = cell.HasValue ? grid.GetCubeBlock(cell.Value)?.FatBlock as IMyTerminalBlock : null;
            if (block == null || MyAPIGateway.Session?.Player == null || !block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)) return null;
            return block;
        }
    }
}
