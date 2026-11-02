using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sandbox.Graphics.GUI;

namespace SpaceEngineersVR.GUI
{
    // Indexes the existing settings screens by building them offscreen, so results follow the pages without a separate list.
    internal static class SettingsSearch
    {
        internal sealed class Entry
        {
            internal string Label,Location,Terms;
            internal int Page=-1;
            internal Func<MyGuiScreenBase> Create;
        }
        private static readonly HashSet<string> Skipped=new HashSet<string> {"Undo","Done","Close","Reset group","−","+"};
        private static readonly Regex Value=new Regex(@"(:.*|\s{2,}(On|Off))$");

        internal static List<Entry> Build(string[] pages)
        {
            var entries=new List<Entry>();
            var links=new Queue<(string location,Func<MyGuiScreenBase> create)>();
            var seen=new HashSet<string>();
            for(int i=0;i<pages.Length;i++) Harvest(entries,links,new MyPluginConfigDialog(i),pages[i],i,null);
            while(links.Count>0)
            {
                var link=links.Dequeue();
                var screen=link.create();
                // Several links can open the same screen; the first one names it.
                if(seen.Add(screen.GetFriendlyName())) Harvest(entries,links,screen,link.location,-1,link.create);
                else screen.CloseScreenNow();
            }
            return entries;
        }

        internal static IEnumerable<Entry> Find(List<Entry> entries,string query)
        {
            var words=Player.ActionCatalog.QueryWords(query);
            if(words.Length==0) return Enumerable.Empty<Entry>();
            return entries.Where(e=>Player.ActionCatalog.Matches(e.Terms,words))
                .OrderBy(e=>e.Label.IndexOf(words[0],StringComparison.OrdinalIgnoreCase)<0);
        }

        private static void Harvest(List<Entry> entries,Queue<(string location,Func<MyGuiScreenBase> create)> links,MyGuiScreenBase screen,string location,int page,Func<MyGuiScreenBase> create)
        {
            try
            {
                screen.RecreateControls(true);
                var labels=screen.Controls.OfType<MyGuiControlLabel>().ToArray();
                foreach(var control in screen.Controls)
                {
                    // The main dialog's left column is the page list and shared buttons.
                    if(!control.Visible || page>=0 && control.Position.X<-.2f) continue;
                    string text=null;
                    if(control is MyGuiControlButton button)
                    {
                        text=button.Text?.ToString();
                        if(button.UserData is Func<MyGuiScreenBase> opens) links.Enqueue((text,opens));
                    }
                    else if(control is MyGuiControlCheckbox || control is MyGuiControlSlider)
                        // Toggles share the label's row; slider labels sit up to .05 above the bar.
                        text=labels.Where(l=>l.Position.X<control.Position.X && l.Position.Y>control.Position.Y-.065f && l.Position.Y<control.Position.Y+.01f)
                            .OrderBy(l=>Math.Abs(l.Position.Y-control.Position.Y)).FirstOrDefault()?.Text;
                    if(string.IsNullOrWhiteSpace(text)) continue;
                    text=Value.Replace(text,"").Trim();
                    if(text.Length==0 || Skipped.Contains(text) || char.IsDigit(text[0]) || entries.Any(e=>e.Label==text && e.Location==location)) continue;
                    entries.Add(new Entry {Label=text,Location=location,Page=page,Create=create,Terms=location+" "+text+" "+SettingsTooltips.Get(text)});
                }
            }
            finally { screen.CloseScreenNow(); }
        }
    }
}
