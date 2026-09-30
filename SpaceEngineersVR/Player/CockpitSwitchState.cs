using System;
using System.Collections.Generic;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.ModAPI.Interfaces;
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
                if(property==null) return false;
                properties.Add(property);
            }
            return true;
        }
        internal static bool Read(MyToolbarItem item,out float state)
        {
            state=0;
            if(!Resolve(item)) return false;
            int on=0;
            for(int i=0;i<blocks.Count;i++) if(properties[i].GetValue(blocks[i])) on++;
            state=on==0 ? 0 : on==blocks.Count ? 1 : .5f;
            return true;
        }
        internal static bool Set(MyToolbarItem item,bool on)
        {
            if(!Resolve(item)) return false;
            for(int i=0;i<blocks.Count;i++)
                if(properties[i].GetValue(blocks[i])!=on) properties[i].SetValue(blocks[i],on);
            return true;
        }
    }
}
