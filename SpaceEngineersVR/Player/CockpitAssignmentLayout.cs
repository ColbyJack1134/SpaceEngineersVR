using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceEngineersVR.Player
{
    internal sealed partial class CockpitAssignmentLayout
    {
        internal const int SlotsPerPage=9;
        internal sealed class Group
        {
            internal readonly string Name;
            internal readonly int[] Controls;
            internal Group(string name,params int[] controls) { Name=name; Controls=controls; }
        }
        internal readonly Group[] Pages;
        private readonly int[] displayIndices;
        internal CockpitAssignmentLayout(int count,params Group[] groups)
        {
            displayIndices=Enumerable.Repeat(-1,count).ToArray();
            var pages=new List<Group>();
            foreach(var group in groups)
            {
                if(group.Controls.Length==0) throw new ArgumentException("Empty cockpit assignment group: "+group.Name);
                int parts=(group.Controls.Length+SlotsPerPage-1)/SlotsPerPage;
                for(int part=0;part<parts;part++)
                {
                    var controls=group.Controls.Skip(part*SlotsPerPage).Take(SlotsPerPage).ToArray();
                    for(int slot=0;slot<controls.Length;slot++)
                    {
                        int control=controls[slot];
                        if(control<0 || control>=count || displayIndices[control]>=0) throw new ArgumentException("Invalid cockpit assignment control: "+control);
                        displayIndices[control]=pages.Count*SlotsPerPage+slot;
                    }
                    pages.Add(new Group(group.Name,controls));
                }
            }
            if(displayIndices.Any(i=>i<0)) throw new ArgumentException("Cockpit assignment groups omit a control");
            Pages=pages.ToArray();
        }
        internal int DisplayIndex(int control) => control>=0 && control<displayIndices.Length ? displayIndices[control]:-1;
        internal int Control(int index)
        {
            if(index<0) return -1;
            int page=index/SlotsPerPage,slot=index%SlotsPerPage;
            return page<Pages.Length && slot<Pages[page].Controls.Length ? Pages[page].Controls[slot]:-1;
        }
        private static readonly Dictionary<string,CockpitAssignmentLayout> layouts=Create();
        internal static CockpitAssignmentLayout Find(string subtype) => layouts[subtype];
    }
}
