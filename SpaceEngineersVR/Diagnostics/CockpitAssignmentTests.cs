using System;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Screens.Helpers;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using VRage.Game;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitAssignmentTests
    {
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }
        private static int Kind(CockpitRig rig,int slot)
        {
            if(rig.ButtonAt(slot)!=null) return 0;
            if(rig.LeverAt(slot) is CockpitRig.Lever lever) return lever.FingerSlide ? 2:1;
            if(rig.HandleAt(slot) is CockpitRig.Handle handle) return handle.Pinch ? 2:3;
            return 4;
        }
        private static MyToolbarItem Item(int slot) => MyToolbarItemFactory.CreateToolbarItem(new MyObjectBuilder_ToolbarItemTerminalBlock {
            BlockEntityId=900000+slot,_Action="OnOff",CustomIconTitle="Control "+slot });
        internal static void Run(Action<string> log)
        {
            var weaponId=new MyDefinitionId(typeof(MyObjectBuilder_SmallGatlingGun));
            var shared=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=new System.Collections.Generic.List<MyObjectBuilder_Toolbar.Slot> {
                new MyObjectBuilder_Toolbar.Slot {Index=2,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=42,_Action="OnOff"}},
                new MyObjectBuilder_Toolbar.Slot {Index=11,Data=new MyObjectBuilder_ToolbarItemWeapon {DefinitionId=weaponId}} }};
            string xml=CockpitMemory.Toolbar(shared);
            Require(CockpitMemory.ValidToolbar(xml,12),"Host rejected a cockpit with weapon selection and terminal actions");
            var saved=CockpitMemory.Decode(CockpitMemory.Encode(new CockpitMemory.Record {Toolbar=xml,Revision=1}));
            var restored=CockpitMemory.Toolbar(saved.Toolbar);
            Require(CockpitMemory.ValidToolbar(saved.Toolbar,12) && restored.Slots.Count==2 &&
                saved.Toolbar==xml && restored.Slots[1].Index==11 && restored.Slots[1].Data is MyObjectBuilder_ToolbarItemWeapon,
                "Weapon assignment was lost through shared cockpit persistence");
            Require(!CockpitMemory.ValidToolbar(xml,11),"Weapon validation bypassed physical-slot bounds");
            shared.Slots.Add(shared.Slots[1]);
            Require(!CockpitMemory.ValidToolbar(CockpitMemory.Toolbar(shared),12),"Weapon validation bypassed duplicate-slot rejection");
            shared.Slots.RemoveAt(2);
            shared.Slots[1]=new MyObjectBuilder_Toolbar.Slot {Index=11,Data=new MyObjectBuilder_ToolbarItemCubeBlock {DefinitionId=weaponId}};
            Require(!CockpitMemory.ValidToolbar(CockpitMemory.Toolbar(shared),12),"Weapon validation admitted an unrelated item type");
            log("PASS cockpit weapon shared-state validation and persistence; mixed terminal assignments, physical bounds, duplicate slots and unrelated-item rejection.");
            int total=0,pages=0;
            foreach(var rig in CockpitRig.All)
            {
                var layout=CockpitAssignmentLayout.Find(rig.Subtype);
                Require(layout.Pages.Sum(p=>p.Controls.Length)==rig.Count,"Assignment count differs: "+rig.Subtype);
                foreach(var page in layout.Pages)
                    Require(page.Controls.Length>0 && page.Controls.Length<=9 && page.Controls.Select(i=>Kind(rig,i)).Distinct().Count()==1,
                        "Assignment group mixes control types: "+rig.Subtype+"/"+page.Name);
                for(int slot=0;slot<rig.Count;slot++)
                {
                    int index=layout.DisplayIndex(slot);
                    Require(layout.Control(index)==slot,"Assignment cannot return to its physical control: "+rig.Subtype+"/"+slot);
                }
                Require(layout.DisplayIndex(-1)==-1 && layout.DisplayIndex(rig.Count)==-1 && layout.Control(-1)==-1 &&
                    layout.Control(layout.Pages.Length*9)==-1,"Assignment bounds accept a missing control");
                total+=rig.Count; pages+=layout.Pages.Length;
            }
            var seat=CockpitAssignmentLayout.Find(CockpitLayout.ControlSeat);
            Require(seat.Pages.Any(p=>p.Controls.SequenceEqual(new[] {65,58,64,63})) &&
                seat.Pages.Any(p=>p.Controls.SequenceEqual(new[] {69,70})) &&
                seat.Pages.Any(p=>p.Controls.SequenceEqual(new[] {72,71,73,74})),"Control Seat row, paired levers or slider-bank order changed");
            var longRow=new CockpitAssignmentLayout(12,new CockpitAssignmentLayout.Group("Row",Enumerable.Range(0,12).ToArray()));
            Require(longRow.Pages.Length==2 && longRow.Pages[0].Controls.SequenceEqual(Enumerable.Range(0,9)) &&
                longRow.Pages[1].Controls.SequenceEqual(Enumerable.Range(9,3)) && longRow.Control(12)==-1,"Long physical row lost continuation order");
            log("PASS grouped cockpit assignments: "+total+" controls / "+pages+" pages across all 26 variants, homogeneous groups, physical-ID round trips, bounds and row continuations.");
        }
        internal static void RunNative(Action<string> log)
        {
            var definition=Sandbox.Definitions.MyDefinitionManager.Static.GetAllDefinitions().First(d=>d.Public && d.AvailableInSurvival);
            var weapon=MyToolbarItemFactory.CreateToolbarItem(new MyObjectBuilder_ToolbarItemWeapon {DefinitionId=definition.Id});
            Require(weapon is MyToolbarItemWeapon,"Native weapon assignment fixture could not resolve its installed definition");
            var weaponLayout=CockpitAssignmentLayout.Find(CockpitLayout.ControlSeat);
            var weaponControls=new MyToolbar(MyToolbarType.ButtonPanel,9,9);
            int weaponIndex=weaponLayout.DisplayIndex(58);
            using(var editor=new CockpitAssignmentToolbar(weaponControls,weaponLayout)) editor.Toolbar.SetItemAtIndex(weaponIndex,weapon);
            string weaponXml=CockpitMemory.Toolbar(weaponControls.GetObjectBuilder());
            Require(CockpitMemory.ValidToolbar(weaponXml,CockpitLayout.Count(CockpitLayout.ControlSeat)),"Host rejects a native weapon assigned through a physical group");
            var record=CockpitMemory.Decode(CockpitMemory.Encode(new CockpitMemory.Record {Toolbar=weaponXml,Revision=1}));
            var reloaded=new MyToolbar(MyToolbarType.ButtonPanel,9,9);
            reloaded.Init(CockpitMemory.Toolbar(record.Toolbar),new VRage.Game.Entity.MyEntity());
            using(var editor=new CockpitAssignmentToolbar(reloaded,weaponLayout))
                Require(editor.Toolbar.GetItemAtIndex(weaponIndex) is MyToolbarItemWeapon reopened && reopened.Definition.Id==definition.Id &&
                    reloaded.GetItemAtIndex(58) is MyToolbarItemWeapon && record.Revision==1,
                    "Shared-state reload or grouped reopening deleted native weapon selection");
            weaponControls.Clear(); reloaded.Clear();
            log("PASS native weapon assignment through physical groups, host validation, shared record serialization, toolbar reload and grouped reopening.");
            foreach(var rig in CockpitRig.All)
            {
                if(rig.Count==0) continue;
                var layout=CockpitAssignmentLayout.Find(rig.Subtype);
                var controls=new MyToolbar(MyToolbarType.ButtonPanel,Math.Min(9,rig.Count),(rig.Count+8)/9);
                using(var editor=new CockpitAssignmentToolbar(controls,layout))
                {
                    for(int index=0;index<layout.Pages.Length*9;index++)
                    {
                        int slot=layout.Control(index);
                        Require(editor.Toolbar.GetItemAtIndex(index)==null,"Fresh cockpit has a preassigned action");
                        editor.Toolbar.SetItemAtIndex(index,Item(index));
                        if(slot<0)
                            Require(editor.Toolbar.GetItemAtIndex(index)==null,"Hidden assignment slot retained an action");
                        else
                        {
                            var assigned=controls.GetItemAtIndex(slot) as MyToolbarItemTerminalBlock;
                            Require(assigned?.BlockEntityId==900000+index && assigned.ActionId=="OnOff" &&
                                !ReferenceEquals(assigned,editor.Toolbar.GetItemAtIndex(index)),"Grouped assignment reached the wrong control or shared its mutable item");
                        }
                    }
                    var stored=CockpitMemory.Toolbar(CockpitMemory.Toolbar(controls.GetObjectBuilder()));
                    Require(stored.Slots.Count==rig.Count && stored.Slots.All(s=>s.Index<rig.Count &&
                        ((MyObjectBuilder_ToolbarItemTerminalBlock)s.Data).BlockEntityId==900000+layout.DisplayIndex(s.Index)),
                        "Grouped assignments saved display padding instead of physical IDs");
                    for(int slot=0;slot<rig.Count;slot++)
                    {
                        int index=layout.DisplayIndex(slot);
                        editor.Toolbar.SetItemAtIndex(index,null);
                        Require(controls.GetItemAtIndex(slot)==null,"Removing an assignment cleared the wrong control");
                        editor.Toolbar.SetItemAtIndex(index,Item(slot));
                    }
                    Require(!editor.Toolbar.SelectedSlot.HasValue && !editor.Toolbar.CanPlayerActivateItems,"Assignment activated a control");
                }
                using(var reopened=new CockpitAssignmentToolbar(controls,layout))
                    for(int slot=0;slot<rig.Count;slot++)
                        Require((reopened.Toolbar.GetItemAtIndex(layout.DisplayIndex(slot)) as MyToolbarItemTerminalBlock)?.BlockEntityId==900000+slot,
                            "Reopened group lost the saved physical assignment");
                controls.Clear();
            }
            log("PASS native grouped assignments: all cockpit controls, fresh assignment, hidden-slot rejection, removal, saved physical IDs, independent copies and reopen without action activation.");
        }
    }
}
