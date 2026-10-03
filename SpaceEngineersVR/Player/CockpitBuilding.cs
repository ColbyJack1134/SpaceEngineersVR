using System;
using HarmonyLib;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitBuilding
    {
        private static MyShipController requested;
        private static readonly Action<MyShipController> handle=AccessTools.MethodDelegate<Action<MyShipController>>(
            AccessTools.Method(typeof(MyShipController),"HandleBuildingMode"));
        internal static bool Active => Main.VrActive && !RemoteView.Active && MySession.Static?.ControlledEntity is MyCockpit cockpit && cockpit.BuildingMode;
        internal static string Label => Active ? "Exit building mode" : "Building mode";
        internal static bool Shortcut(bool native,MyShipController controller) => native || ReferenceEquals(requested,controller);
        internal static void Toggle()
        {
            if(!(MySession.Static?.ControlledEntity is MyCockpit cockpit) || RemoteView.Active) return;
            if(cockpit.BuildingMode) { Exit(cockpit); InputRouter.Update(); return; }
            if(!MySandboxGame.Config.ExperimentalMode) { EssentialHud.Notify("Cockpit building requires native Experimental mode"); return; }
            NativeActions.Reset(); Controls.Static.BlockUntilRelease(); Components.VRMovementComponent.StopActive();
            bool before=cockpit.BuildingMode;
            requested=cockpit;
            try { handle(cockpit); }
            finally { requested=null; }
            MyToolbarComponent.UpdateCurrentToolbar();
            InputRouter.Reset(); InputRouter.Update();
            if(cockpit.BuildingMode==before) EssentialHud.Notify("Building mode is unavailable in this cockpit");
        }
        internal static void Exit(MyCockpit cockpit)
        {
            if(!cockpit.BuildingMode) return;
            NativeActions.Reset();
            cockpit.BuildingMode=false;
            cockpit.Toolbar.Unselect();
            MyCubeBuilder.Static?.Deactivate();
            MyHud.Crosshair.ResetToDefault();
            MyToolbarComponent.UpdateCurrentToolbar();
            InputRouter.Reset();
        }
    }
}
