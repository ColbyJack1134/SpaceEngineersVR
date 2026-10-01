using System;
using System.Collections.Generic;
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
        private static bool Resolve(MyToolbarItem item)
        {
            blocks.Clear(); properties.Clear();
            if(!(item is MyToolbarItemActions action) || string.IsNullOrEmpty(action.ActionId)) return false;
            string id=action.ActionId;
            if(id.EndsWith("_On",StringComparison.Ordinal)) id=id.Substring(0,id.Length-3);
            else if(id.EndsWith("_Off",StringComparison.Ordinal)) id=id.Substring(0,id.Length-4);
            if(item is MyToolbarItemTerminalBlock block) block.FetchAllBlocks(blocks);
            else if(item is MyToolbarItemTerminalGroup group) group.FetchAllBlocks(blocks);
            else return false;
            if(blocks.Count==0) return false;
            foreach(var target in blocks)
            {
                if(target==null || !(target is Sandbox.ModAPI.IMyTerminalBlock accessible) ||
                    !accessible.HasPlayerAccess(MySession.Static.LocalPlayerId)) return false;
                var property=target.GetProperty(id) as ITerminalProperty<bool>;
                if(property==null && !(id=="SwitchLock" && target is IMyShipConnector)) return false;
                properties.Add(property);
            }
            return true;
        }
        internal static bool Read(MyToolbarItem item,out float state)
        {
            state=0;
            if(!Resolve(item)) return false;
            state=ReadValues(blocks,properties);
            return true;
        }
        internal static float ReadValues(IReadOnlyList<Block> targets,IReadOnlyList<ITerminalProperty<bool>> values)
        {
            float state=ReadValue(targets[0],values[0]);
            for(int i=1;i<targets.Count;i++)
            {
                float next=ReadValue(targets[i],values[i]);
                // A connector group is still locked if any member is connected; center means ready, never mixed.
                if(values[0]==null) state=Math.Max(state,next);
                else if(next!=state) return .5f;
            }
            return state;
        }
        internal static bool Set(MyToolbarItem item,bool on)
        {
            if(!Resolve(item)) return false;
            for(int i=0;i<blocks.Count;i++)
                SetValue(blocks[i],properties[i],on);
            return true;
        }
        internal static float ReadValue(Block block,ITerminalProperty<bool> property)
        {
            if(property!=null) return property.GetValue(block) ? 1 : 0;
            switch(((IMyShipConnector)block).Status)
            {
                case MyShipConnectorStatus.Connected: return 1;
                case MyShipConnectorStatus.Connectable: return .5f;
                default: return 0;
            }
        }
        internal static void SetValue(Block block,ITerminalProperty<bool> property,bool on)
        {
            if(property!=null)
            {
                if(property.GetValue(block)!=on) property.SetValue(block,on);
                return;
            }
            // SwitchLock is an action, not a Boolean property. Never synthesize its observed state.
            var connector=(IMyShipConnector)block;
            if(on) { if(connector.Status!=MyShipConnectorStatus.Connected) connector.Connect(); }
            else if(connector.Status!=MyShipConnectorStatus.Unconnected) connector.Disconnect();
        }
    }
}
