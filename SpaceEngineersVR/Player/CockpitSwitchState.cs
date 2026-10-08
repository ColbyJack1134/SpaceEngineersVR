using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Block=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitSwitchState
    {
        private static readonly List<Block> blocks=new List<Block>();
        private static readonly List<ITerminalProperty<bool>> properties=new List<ITerminalProperty<bool>>();
        internal static bool ViewBlock(object block) => block is MyCameraBlock || RemoteView.ExitControlled(block);
        internal static bool ViewAction(string action,object block) => action=="View" && block is MyCameraBlock || action=="Control" && RemoteView.ExitControlled(block);
        internal static bool ViewState(MyToolbarItem item,out bool active)
        {
            active=false;
            if(!(item is MyToolbarItemTerminalBlock block) || !ViewAction(block.ActionId,block.Block)) return false;
            active=ViewActive(block.Block,MySession.Static?.ControlledEntity,MySession.Static?.CameraController);
            return true;
        }
        internal static bool ViewActive(object block,object controlled,object camera) => block!=null &&
            (ReferenceEquals(block,camera) || ReferenceEquals(block,controlled));
        internal static bool WeaponState(MyToolbarItem item,MyShipController controller,out bool active)
        {
            active=false;
            if(!(item is MyToolbarItemWeapon weapon) || weapon.Definition==null) return false;
            active=controller?.GridSelectionSystem?.GetGunId()==weapon.Definition.Id;
            return true;
        }
        private static string StateId(string action) => action.EndsWith("_On",StringComparison.Ordinal) ? action.Substring(0,action.Length-3) :
            action.EndsWith("_Off",StringComparison.Ordinal) ? action.Substring(0,action.Length-4) : action;
        // Connector lock is the one switch state with a middle position (Ready to lock).
        internal static bool ShowsReady(Block block,string action) => action=="SwitchLock" && block is IMyShipConnector;
        internal static bool BatteryAction(Block block,string action) => block is IMyBatteryBlock &&
            !string.IsNullOrEmpty(action) && (StateId(action)=="Recharge" || StateId(action)=="Discharge");
        private static bool Resolve(MyToolbarItem item,out string id)
        {
            blocks.Clear(); properties.Clear(); id=null;
            if(!(item is MyToolbarItemActions action) || string.IsNullOrEmpty(action.ActionId)) return false;
            id=StateId(action.ActionId);
            if(item is MyToolbarItemTerminalBlock block) block.FetchAllBlocks(blocks);
            else if(item is MyToolbarItemTerminalGroup group) group.FetchAllBlocks(blocks);
            else return false;
            if(blocks.Count==0) return false;
            foreach(var target in blocks)
            {
                if(target==null || !(target is Sandbox.ModAPI.IMyTerminalBlock accessible) ||
                    !accessible.HasPlayerAccess(MySession.Static.LocalPlayerId)) return false;
                var property=target.GetProperty(id) as ITerminalProperty<bool>;
                if(property==null && !(id=="SwitchLock" && target is IMyShipConnector) && !BatteryAction(target,id)) return false;
                properties.Add(property);
            }
            return true;
        }
        internal static bool Read(MyToolbarItem item,out float state)
        {
            state=0;
            if(ViewState(item,out bool active)) { state=active ? 1:0; return true; }
            if(WeaponState(item,MySession.Static?.ControlledEntity as MyShipController,out active)) { state=active ? 1:0; return true; }
            if(!Resolve(item,out string id)) return false;
            state=ReadValues(blocks,properties,id);
            return true;
        }
        internal static float ReadValues(IReadOnlyList<Block> targets,IReadOnlyList<ITerminalProperty<bool>> values,string action=null)
        {
            float state=ReadValue(targets[0],values[0],action);
            for(int i=1;i<targets.Count;i++)
            {
                float next=ReadValue(targets[i],values[i],action);
                // A connector group is still locked if any member is connected; center means ready, never mixed.
                if(values[0]==null && targets[0] is IMyShipConnector) state=Math.Max(state,next);
                else if(next!=state) return .5f;
            }
            return state;
        }
        internal static bool Set(MyToolbarItem item,bool on)
        {
            if(!Resolve(item,out string id)) return false;
            for(int i=0;i<blocks.Count;i++)
                SetValue(blocks[i],properties[i],on,id);
            return true;
        }
        internal static float ReadValue(Block block,ITerminalProperty<bool> property,string action=null)
        {
            if(property!=null) return property.GetValue(block) ? 1 : 0;
            if(BatteryAction(block,action)) return ((IMyBatteryBlock)block).ChargeMode==BatteryMode(action) ? 1:0;
            switch(((IMyShipConnector)block).Status)
            {
                case MyShipConnectorStatus.Connected: return 1;
                case MyShipConnectorStatus.Connectable: return .5f;
                default: return 0;
            }
        }
        private static ChargeMode BatteryMode(string action) => StateId(action)=="Recharge" ? ChargeMode.Recharge:ChargeMode.Discharge;
        internal static void SetValue(Block block,ITerminalProperty<bool> property,bool on,string action=null)
        {
            if(property!=null)
            {
                if(property.GetValue(block)!=on) property.SetValue(block,on);
                return;
            }
            if(BatteryAction(block,action))
            {
                var battery=(IMyBatteryBlock)block;
                var mode=BatteryMode(action);
                if(on) { if(battery.ChargeMode!=mode) battery.ChargeMode=mode; }
                // An inactive switch must not cancel the other battery mode.
                else if(battery.ChargeMode==mode) battery.ChargeMode=ChargeMode.Auto;
                return;
            }
            // SwitchLock is an action, not a Boolean property. Never synthesize its observed state.
            var connector=(IMyShipConnector)block;
            if(on) { if(connector.Status!=MyShipConnectorStatus.Connected) connector.Connect(); }
            else if(connector.Status!=MyShipConnectorStatus.Unconnected) connector.Disconnect();
        }
    }
}
