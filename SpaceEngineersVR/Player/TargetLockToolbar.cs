using System;
using System.Text;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // Adds the VR WeaponCore actions to ship controller toolbars (cockpits, seats, remote controls), so they can sit on a
    // toolbar slot or a cockpit button like any vanilla action. They act only for the player controlling that grid.
    // Registered only after WeaponCore answers; terminal actions cannot be removed, so later worlds without WC see them disabled.
    internal static class TargetLockToolbar
    {
        private static bool registered;

        internal static void Register()
        {
            if(registered || !WeaponCoreBridge.ApiReady || MyAPIGateway.TerminalControls==null) return;
            registered=true;
            Add("SEVR_WcLockTargetAhead","Lock target ahead (VR)","Lock",@"Textures\GUI\Icons\Actions\Start.dds",TargetLock.LockAhead);
            Add("SEVR_WcNextTarget","Next target (VR)","Next",@"Textures\GUI\Icons\Actions\Increase.dds",()=>TargetLock.Cycle(1));
            Add("SEVR_WcPreviousTarget","Previous target (VR)","Prev",@"Textures\GUI\Icons\Actions\Decrease.dds",()=>TargetLock.Cycle(-1));
            Add("SEVR_WcReleaseTarget","Release target (VR)","Free",@"Textures\GUI\Icons\Actions\Reset.dds",TargetLock.Release);
            Add("SEVR_WcPaintTarget","Paint target (VR)","Paint",@"Textures\GUI\Icons\Actions\Start.dds",WeaponCoreAim.RequestPaint);
            Logger.Info("Registered WeaponCore VR toolbar actions");
        }

        private static void Add(string id,string name,string label,string icon,Action run)
        {
            var action=MyAPIGateway.TerminalControls.CreateAction<IMyShipController>(id);
            action.Name=new StringBuilder(name);
            action.Icon=icon;
            action.ValidForGroups=false;
            action.Enabled=block=>block!=null && WeaponCoreBridge.ApiReady;
            action.Action=block =>
            {
                // Toolbar activation runs on the activating client; only act for the local controller of this block's grid.
                if(MyAPIGateway.Session?.ControlledObject?.Entity is IMyShipController controlled &&
                    controlled.CubeGrid==block.CubeGrid) run();
            };
            action.Writer=(block,text)=>text.Append(label);
            MyAPIGateway.TerminalControls.AddAction<IMyShipController>(action);
        }
    }
}
