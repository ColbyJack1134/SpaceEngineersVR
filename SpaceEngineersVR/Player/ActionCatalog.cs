using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HarmonyLib;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.Screens.Helpers.RadialMenuActions;
using Sandbox.Game.World;
using VRage;
using VRage.FileSystem;
using VRage.Game;
using VRage.Input;
using VRage.Utils;

namespace SpaceEngineersVR.Player
{
    internal static class ActionCatalog
    {
        private static ActionChoice[] native;
        private static ActionChoice[] current;
        private static object owner;
        private static MyToolbar toolbar;
        private static DateTime refresh;
        internal static ActionChoice[] Search(string query)
        {
            var active=MySession.Static?.ControlledEntity;
            var bar=MyToolbarComponent.CurrentToolbar;
            if(current==null || !ReferenceEquals(owner,active) || !ReferenceEquals(toolbar,bar) || DateTime.UtcNow>=refresh)
            {
                owner=active; toolbar=bar; refresh=DateTime.UtcNow.AddSeconds(.5);
                current=Entries().ToArray();
            }
            var words=(query ?? "").Split(new[] {' '},StringSplitOptions.RemoveEmptyEntries);
            return current.Where(a=>words.All(word=>(a.Label+" "+a.SearchTerms).IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
        }
        private static IEnumerable<ActionChoice> Entries()
        {
            foreach(var action in GameActions.Quick.Concat(GameActions.Building).Concat(GameActions.Developer)
                .Concat(new[] {GameActions.HudOptions,GameActions.UnequipAction,GameActions.RecenterAction,GameActions.DesktopFloating,GameActions.DesktopWrist})
                .GroupBy(a=>a.Label).Select(g=>g.First())) yield return action;
            foreach(string category in new[] {"Character","Flight","Third person","HUD & Interface","Rendering","Controls"})
            {
                string title=category;
                yield return new ActionChoice("VR: "+title,()=>Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(GUI.MyPluginConfigDialog.CreatePage(title)),true,searchTerms:"settings options controls");
            }
            if(MySession.Static==null) yield break;
            // Native suicide asks for confirmation and honors campaign respawn rules.
            yield return new ActionChoice("Respawn",()=>NativeActions.Pulse(Sandbox.Game.MyControlsSpace.SUICIDE),searchTerms:"suicide kill die stuck");
            if(native==null) native=NativeEntries();
            foreach(var action in native) yield return action;
            var ids=new HashSet<MyStringId>();
            if(MyInput.Static!=null)
                foreach(var control in MyInput.Static.GetGameControlsList())
                {
                    var id=control.GetGameControlEnum(); ids.Add(id);
                    string name=MyTexts.GetString(control.GetControlName());
                    if(string.IsNullOrWhiteSpace(name)) name=Words(id.String);
                    string binding=control.ToString();
                    string help=control.GetControlDescription().HasValue ? MyTexts.GetString(control.GetControlDescription().Value) : "";
                    yield return new ActionChoice("Control: "+name,()=>NativeActions.Pulse(id),
                        searchTerms:"hotkey keyboard "+id.String+" "+binding+" "+help);
                }
            foreach(var field in typeof(Sandbox.Game.MyControlsSpace).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
                if(field.FieldType==typeof(MyStringId))
                {
                    var id=(MyStringId)field.GetValue(null);
                    if(!ids.Add(id)) continue;
                    yield return new ActionChoice("Control: "+Words(id.String),()=>NativeActions.Pulse(id),searchTerms:"hotkey "+field.Name);
                }
            var bar=MyToolbarComponent.CurrentToolbar;
            if(bar==null) yield break;
            for(int p=0;p<bar.PageCount;p++)
            {
                int page=p;
                yield return new ActionChoice("Toolbar page "+(page+1),()=> { if(Current(bar)) bar.SwitchToPage(page); },enabled:()=>Current(bar));
                for(int s=0;s<bar.SlotCount;s++)
                {
                    int slot=s,index=page*bar.SlotCount+slot;
                    var item=bar.GetItemAtIndex(index);
                    if(item==null) continue;
                    yield return new ActionChoice("Toolbar "+(page+1)+" / "+(slot+1)+": "+item.DisplayName,()=> {
                        if(!Current(bar) || bar.GetItemAtIndex(index)!=item || !item.Enabled) return;
                        bar.SwitchToPage(page);
                        ToolbarWheel.SelectSlot(bar,slot,false,GameActions.AssignToolbarSlot);
                    },icon:item.Icons?.FirstOrDefault(),enabled:()=>Current(bar) && bar.GetItemAtIndex(index)==item && item.Enabled,
                        searchTerms:"ship character toolbar hotbar action");
                }
            }
        }
        private static bool Current(MyToolbar bar) => ReferenceEquals(bar,MyToolbarComponent.CurrentToolbar);
        private static string Words(string name) => Regex.Replace(name,"([a-z])([A-Z])","$1 $2").Replace('_',' ');
        internal static ActionChoice[] NativeEntries()
        {
            var definitions=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,"Data","RadialMenu.sbc"))
                .Descendants("Item").Where(e=>e.Element("SystemAction")!=null)
                .GroupBy(e=>(int)e.Element("SystemAction")).ToDictionary(g=>g.Key,g=>g.First());
            var factory=AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Screens.Helpers.MyRadialMenuItemFactory"),"GetSystemMenuAction");
            var result=new List<ActionChoice>();
            foreach(MySystemAction key in Enum.GetValues(typeof(MySystemAction)))
            {
                var action=factory.Invoke(null,new object[] {key}) as MyActionBase;
                if(action==null) continue;
                definitions.TryGetValue((int)key,out var definition);
                string label=(string)definition?.Element("LabelName");
                label=string.IsNullOrEmpty(label) ? Words(key.ToString()) : MyTexts.GetString(MyStringId.GetOrCompute(label));
                string icon=(string)definition?.Element("Icons")?.Elements("string").FirstOrDefault();
                result.Add(new ActionChoice("Game: "+label,()=> { if(MySession.Static!=null && action.IsEnabled()) action.ExecuteAction(); },
                    icon:icon,enabled:()=>MySession.Static!=null && action.IsEnabled(),searchTerms:"native action ship "+Words(key.ToString())));
            }
            return result.ToArray();
        }
    }
}
