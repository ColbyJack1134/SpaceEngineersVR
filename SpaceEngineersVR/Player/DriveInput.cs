using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Every input keeps all of its native jobs, so ships and rovers need no separate mode:
    // rise also brakes (native jump key) and an empty-toolbar trigger drives the wheels.
    internal static class DriveInput
    {
        // Native wheel throttle is on/off, so a stick drifting forward while steering would be full throttle.
        internal const float StickThrottle=.5f;
        private static MyShipController ship;
        private static bool gas;
        public static bool Braking { get; private set; }

        public static void Update(MyShipController controller,Vector3 move)
        {
            ship=controller;
            Braking=move.Y>.5f;
        }
        public static void Throttle(bool trigger) => gas=CockpitControls.Throttle>.1f || trigger && ship?.Toolbar?.SelectedItem==null;
        public static void Stop() { ship=null; gas=Braking=false; }

        // Native wheels read X as steering and the sign of Z as throttle.
        internal static Vector3 Mix(Vector3 native,bool gas) =>
            new Vector3(native.X,native.Y,gas ? -1:Math.Abs(native.Z)>=StickThrottle ? native.Z:0);

        // Only the simulating host applies this; clients keep native wheel input rather than diverge from the server.
        public static Vector3 Wheels(Vector3 native,MyShipController controller) =>
            Main.VrActive && Sync.IsServer && ReferenceEquals(controller,ship) && controller.ControlWheels && controller.EnableShipControl &&
            (controller.IsMainCockpit || !controller.CubeGrid.HasMainCockpit()) ? Mix(native,gas):native;
    }
}
