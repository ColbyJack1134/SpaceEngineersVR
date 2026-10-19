using System;
using System.Text;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // WeaponCore lock actions for ship controller toolbars and cockpit buttons, acting for the player controlling that grid.
    // Terminal actions cannot be removed, so later worlds in the session without WeaponCore show them disabled.
    internal static class TargetLockToolbar
    {
        private static bool registered;

        internal static void Register()
        {
            if(registered || !WeaponCoreBridge.ApiReady || MyAPIGateway.TerminalControls==null) return;
            registered=true;
            Add("SEVR_WcLockTargetAhead","Lock target ahead","Lock",TargetLock.LockAhead);
            Add("SEVR_WcNextTarget","Next target","Next",()=>TargetLock.Cycle(1));
            Add("SEVR_WcPreviousTarget","Previous target","Prev",()=>TargetLock.Cycle(-1));
            Add("SEVR_WcReleaseTarget","Release target","Free",TargetLock.Release);
            Add("SEVR_WcPaintTarget","Paint target","Paint",WeaponCoreAim.RequestPaint);
        }

        private static void Add(string id,string name,string label,Action run)
        {
            var action=MyAPIGateway.TerminalControls.CreateAction<IMyShipController>(id);
            action.Name=new StringBuilder(name);
            action.Icon=GUI.CockpitAssignment.MarkerIcon;
            action.ValidForGroups=false;
            action.Enabled=block=>block!=null && WeaponCoreBridge.ApiReady;
            action.Action=block =>
            {
                if(!(MyAPIGateway.Session?.ControlledObject?.Entity is IMyShipController controlled) || controlled.CubeGrid!=block.CubeGrid) return;
                // Runs inside the game's toolbar code, outside the WeaponCore Integration.
                try { run(); }
                catch(Exception ex) { Logger.Warning(ex,name+" failed"); }
            };
            action.Writer=(block,text)=>text.Append(label);
            MyAPIGateway.TerminalControls.AddAction<IMyShipController>(action);
        }
    }
}
