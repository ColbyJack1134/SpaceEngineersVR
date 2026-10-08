using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using SpaceEngineersVR.Player;
using VRage.Game;

namespace SpaceEngineersVR.Diagnostics
{
    public static class BuildPlannerTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        internal static VRage.Game.Entity.UseObject.IMyUseObject InventoryFixture()
        {
            var type=typeof(SpaceEngineers.Game.Entities.UseObjects.MyUseObjectPanelButton).Assembly.GetType("SpaceEngineers.Game.Entities.UseObjects.MyUseObjectInventory",true);
            return (VRage.Game.Entity.UseObject.IMyUseObject)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        }
        public static void Run(Action<string> log)
        {
            var inventoryTarget=InventoryFixture();
            Require(BuildPlannerActions.InventoryTarget(inventoryTarget),"Installed inventory access point does not expose planner actions");
            Require(!BuildPlannerActions.InventoryTarget(null),"Empty hand target exposes inventory operations");
            bool available=true;
            var dispatched=new List<BuildPlannerActions.Operation>();
            var choices=BuildPlannerActions.InventoryChoices(()=>available,operation=>dispatched.Add(operation));
            foreach(var choice in choices) choice.Run();
            Require(dispatched.SequenceEqual(Enum.GetValues(typeof(BuildPlannerActions.Operation)).Cast<BuildPlannerActions.Operation>()),"Inventory wheel lost or duplicated a planner operation");
            available=false;
            foreach(var choice in choices) { Require(!choice.Enabled,"Lost inventory target stayed enabled"); choice.Run(); }
            Require(dispatched.Count==6,"Unavailable inventory target executed a planner operation");
            var pages=BlockActions.Pages(BlockActions.Choices(VRage.Game.Entity.UseObject.UseActionEnum.Manipulate|
                VRage.Game.Entity.UseObject.UseActionEnum.OpenTerminal|VRage.Game.Entity.UseObject.UseActionEnum.OpenInventory,false,_=>true,_=>{}).Concat(choices).ToArray(),
                BlockVariants.Pages(Array.Empty<ActionChoice>(),GameActions.WheelActions(false,false,false)));
            Require(pages[0].Count(c=>c!=null)==9 && pages.Skip(1).SelectMany(p=>p).Any(c=>c==GameActions.PauseAction),"Inventory operations displaced personal wheel pages");
            foreach(string field in new[] {"withdraw","production","deposit"})
            {
                var native=(Delegate)AccessTools.Field(typeof(BuildPlannerActions),field).GetValue(null);
                Require(native.Method.DeclaringType.Assembly==typeof(MyWelder).Assembly,"Planner operation no longer delegates to the installed game");
            }
            var welderCalls=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MyWelder),"AddMissingComponentsToBuildPlanner"));
            Require(welderCalls.Any(i=>i.operand is System.Reflection.MethodInfo method && method.Name=="GetMissingComponents"),"Native welder no longer calculates missing components");
            var steel=new MyComponentDefinition {Id=new MyDefinitionId(typeof(MyObjectBuilder_Component),"SteelPlate"),MaxIntegrity=100};
            var motor=new MyComponentDefinition {Id=new MyDefinitionId(typeof(MyObjectBuilder_Component),"Motor"),MaxIntegrity=100};
            var definition=new MyCubeBlockDefinition {MaxIntegrity=1500,Components=new[] {
                new MyCubeBlockDefinition.Component {Definition=steel,Count=10},new MyCubeBlockDefinition.Component {Definition=motor,Count=5}}};
            var stack=new MyComponentStack(definition,.2f,.2f);
            var stockpile=new MyConstructionStockpile();
            stockpile.Init(new MyObjectBuilder_ConstructionStockpile {Items=new[] {
                new MyObjectBuilder_StockpileItem {Amount=2,PhysicalContent=new MyObjectBuilder_Component {SubtypeName="SteelPlate"}},
                new MyObjectBuilder_StockpileItem {Amount=1,PhysicalContent=new MyObjectBuilder_Component {SubtypeName="Motor"}}}});
            var missing=new Dictionary<string,int>();
            stack.GetMissingComponents(missing,stockpile);
            Require(missing["SteelPlate"]==5 && missing["Motor"]==4,"Installed component stack included mounted or stockpiled components");
            var completed=new MyComponentStack(definition,1,1);
            missing.Clear(); completed.GetMissingComponents(missing);
            Require(missing.Count==0,"Completed block still needs components");
            stockpile.Init(new MyObjectBuilder_ConstructionStockpile {Items=new[] {
                new MyObjectBuilder_StockpileItem {Amount=10,PhysicalContent=new MyObjectBuilder_Component {SubtypeName="SteelPlate"}},
                new MyObjectBuilder_StockpileItem {Amount=5,PhysicalContent=new MyObjectBuilder_Component {SubtypeName="Motor"}}}});
            missing.Clear(); stack.GetMissingComponents(missing,stockpile);
            Require(missing.Count==0,"Stockpile-complete unfinished block still needs components");
            log("PASS build planner: six captured inventory operations, access-loss rejection, native delegate signatures, preserved wheel pages and installed missing-component calculation with mounted/stockpiled/complete blocks. No world modified.");
        }
    }
}
