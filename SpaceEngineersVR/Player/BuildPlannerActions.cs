using System;
using System.Collections.Generic;
using Sandbox.Game;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class BuildPlannerActions
    {
        internal enum Operation { Withdraw, WithdrawKeep, WithdrawTen, Production, ProductionTen, Deposit }
        // Native feedback and partial-withdrawal handling are private to the gameplay screen.
        private static readonly Action<MyEntity,MyInventory,int?> withdraw=(Action<MyEntity,MyInventory,int?>)Delegate.CreateDelegate(
            typeof(Action<MyEntity,MyInventory,int?>),AccessTools.Method(typeof(MyGuiScreenGamePlay),"ProcessWithdraw"));
        private static readonly Type inventoryController=typeof(MyGuiScreenGamePlay).Assembly.GetType("Sandbox.Game.Gui.MyTerminalInventoryController",true);
        private static readonly Func<MyEntity,int?,int> production=(Func<MyEntity,int?,int>)Delegate.CreateDelegate(
            typeof(Func<MyEntity,int?,int>),AccessTools.Method(inventoryController,"AddComponentsToProduction",new[] {typeof(MyEntity),typeof(int?)}));
        private static readonly Func<MyInventory,MyEntity,int> deposit=(Func<MyInventory,MyEntity,int>)Delegate.CreateDelegate(
            typeof(Func<MyInventory,MyEntity,int>),AccessTools.Method(inventoryController,"DepositAll"));
        internal static readonly ActionChoice[] Shortcuts=InventoryChoices(()=>true,RunInventory);

        internal static bool InventoryTarget(IMyUseObject target) => target is MyUseObjectBase use &&
            (use.SupportedActions & UseActionEnum.BuildPlanner)!=0 &&
            (use.PrimaryAction==UseActionEnum.OpenInventory || use.SecondaryAction==UseActionEnum.OpenInventory);

        internal static ActionChoice[] InventoryChoices(Func<bool> available,Action<Operation> run)
        {
            var labels=new[] {"Withdraw planner","Withdraw 1x / keep","Withdraw 10x / keep","Queue components","Queue 10x components","Deposit inventory"};
            var choices=new ActionChoice[labels.Length];
            for(int i=0;i<choices.Length;i++)
            {
                var operation=(Operation)i;
                choices[i]=new ActionChoice(labels[i],()=> { if(available()) run(operation); },
                    icon:NativeSprites.Hud(i>=3 && i<5 ? "MultiBlockBuilding":"OpenInventory"),enabled:available,
                    searchTerms:"build planner inventory components production deposit withdraw");
            }
            return choices;
        }

        internal static ActionChoice[] Capture(IMyUseObject target,MyCharacter character)
        {
            var choices=new List<ActionChoice>();
            if(InventoryTarget(target))
                choices.AddRange(InventoryChoices(()=>BlockActions.Available(target,character,UseActionEnum.BuildPlanner|UseActionEnum.OpenInventory),
                    operation=> { if(CanExecute) Execute(operation,(MyEntity)target.Owner,character); }));
            var missing=CaptureMissing(character);
            if(missing!=null) choices.Add(missing);
            return choices.ToArray();
        }

        private static bool CanExecute => Main.VrActive && InputRouter.Gameplay && !Main.MenuOpen;

        internal static ActionChoice CaptureMissing(MyCharacter character)
        {
            if(!BlockActions.CharacterAvailable(character) || !HandInteraction.TryRightInteractionRay(out var ray)) return null;
            var block=BlockInspection.Target(ray,out var point);
            if(block==null || !HasMissing(block)) return null;
            var local=Vector3D.Transform(point,block.CubeGrid.PositionComp.WorldMatrixInvScaled);
            bool Available()
            {
                if(!BlockActions.CharacterAvailable(character) || block.CubeGrid.Closed || block.CubeGrid.GetCubeBlock(block.Position)!=block ||
                    !HasMissing(block) || !HandInteraction.TryWorldPose(Player.HandR,out var pointer)) return false;
                if(TrackedArms.TryFreePointPose(Player.HandR,out var finger)) pointer=finger;
                return BlockActions.InReach(pointer,Vector3D.Transform(local,block.CubeGrid.WorldMatrix),MyConstants.DEFAULT_INTERACTIVE_DISTANCE);
            }
            return new ActionChoice("Add missing components",()=> {
                if(CanExecute && Available()) MyWelder.AddMissingComponentsToBuildPlanner(block);
            },icon:NativeSprites.Hud("MultiBlockBuilding"),enabled:Available,searchTerms:"add build planner unfinished block");
        }

        private static bool HasMissing(Sandbox.Game.Entities.Cube.MySlimBlock block)
        {
            if(block.CubeGrid.IsPreview) return true;
            if(block.IsFullIntegrity) return false;
            var missing=new Dictionary<string,int>();
            block.GetMissingComponents(missing);
            return missing.Count>0;
        }

        internal static void RunInventory(Operation operation)
        {
            var target=HandInteraction.CaptureRightTarget();
            var character=MySession.Static?.LocalCharacter;
            if(!CanExecute || !InventoryTarget(target) || !BlockActions.Available(target,character,UseActionEnum.BuildPlanner|UseActionEnum.OpenInventory))
            { EssentialHud.Notify("Point at an accessible inventory"); return; }
            Execute(operation,(MyEntity)target.Owner,character);
        }

        private static void Execute(Operation operation,MyEntity target,MyCharacter character)
        {
            if(operation==Operation.Withdraw || operation==Operation.WithdrawKeep || operation==Operation.WithdrawTen)
            {
                if(operation==Operation.Withdraw && character.BuildPlanner.Count==0 && MyCubeBuilder.Static?.ToolbarBlockDefinition!=null)
                    character.AddToBuildPlanner(MyCubeBuilder.Static.CurrentBlockDefinition);
                withdraw(target,character.GetInventory(),operation==Operation.Withdraw ? (int?)null:operation==Operation.WithdrawKeep ? 1:10);
            }
            else if(operation==Operation.Deposit)
            {
                int failed=deposit(character.GetInventory(),target);
                Notify(failed,MyNotificationSingletons.DepositSuccessful,MyNotificationSingletons.DepositFailed);
                character.GetDetectorComponent()?.UpdateInteractiveObjectNotification();
            }
            else if(character.BuildPlanner.Count==0) MyGuiScreenGamePlay.ShowEmptyBuildPlannerNotification();
            else
            {
                int failed=production(target,operation==Operation.ProductionTen ? (int?)10:null);
                Notify(failed,MyNotificationSingletons.PutToProductionSuccessful,MyNotificationSingletons.PutToProductionFailed);
            }
        }

        private static void Notify(int failed,MyNotificationSingletons success,MyNotificationSingletons failure)
        {
            var notification=MyHud.Notifications.Add(failed==0 ? success:failure);
            if(failed>0) notification.SetTextFormatArguments(failed);
        }
    }
}
