using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.GUI;
using VRage.Collections;
using VRage.Game;
using VRage.Input;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class AssignmentMenuTests
    {
        private sealed class ActionItem : MyToolbarItemActions
        {
            public override ListReader<ITerminalAction> AllActions => new List<ITerminalAction>();
            public override ListReader<ITerminalAction> PossibleActions(MyToolbarType type) => AllActions;
            internal ActionItem() { SetEnabled(true); SetDisplayName("Light"); SetIcons(new[] {Player.NativeSprites.Hud("Light")}); }
            public override bool Activate() => throw new Exception("Assignment activated the action");
            public override bool Init(MyObjectBuilder_ToolbarItem data) => true;
            public override MyObjectBuilder_ToolbarItem GetObjectBuilder() => new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=987654321,_Action=ActionId};
            public override bool AllowedInToolbarType(MyToolbarType type) => true;
        }
        private sealed class Input : RealProxy
        {
            private readonly IMyInput original;
            internal bool Held,Pressed,Escape;
            internal Input(IMyInput original) : base(typeof(IMyInput)) { this.original=original; }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message;
                var method=(MethodInfo)call.MethodBase;
                object result;
                switch(call.MethodName)
                {
                    case "IsPrimaryButtonPressed": result=Held; break;
                    case "IsAnyNewMouseOrJoystickPressed": case "IsNewPrimaryButtonPressed": result=Pressed; break;
                    case "IsNewMousePressed": result=Pressed && (MyMouseButtonsEnum)call.Args[0]==MyMouseButtonsEnum.Left; break;
                    case "IsKeyPress": result=Escape && (MyKeys)call.Args[0]==MyKeys.Escape; break;
                    default: result=method.ReturnType==typeof(bool) ? false:method.Invoke(original,call.Args); break;
                }
                return new ReturnMessage(result,call.Args,call.Args.Length,call.LogicalCallContext,call);
            }
        }
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Set(object owner,string name,object value) => AccessTools.Field(owner.GetType(),name).SetValue(owner,value);
        private static MyToolbarItemTerminalBlock TerminalItem(long block,string action="OnOff") =>
            (MyToolbarItemTerminalBlock)MyToolbarItemFactory.CreateToolbarItem(new MyObjectBuilder_ToolbarItemTerminalBlock {
                BlockEntityId=block,_Action=action });
        internal static void RunGrouped(MyGuiScreenToolbarConfigBase owner,MyToolbar target,MyGuiControlToolbar toolbar,
            CockpitAssignment assignment,Player.CockpitAssignmentLayout layout,MyToolbar physical,int selected)
        {
            int initialPage=target.CurrentPage;
            int page=Enumerable.Range(0,layout.Pages.Length).First(p=>p!=selected/9 &&
                layout.Pages[p].Controls.Length>=2 && layout.Pages[p].Controls.Length<9);
            int first=page*9,used=layout.Pages[page].Controls.Length;
            var saved=Enumerable.Range(first,9).Select(target.GetItemAtIndex).ToArray();
            var original=MyInput.Static;
            var input=new Input(original);
            var menuField=AccessTools.Field(typeof(MyGuiScreenToolbarConfigBase),"m_onDropContextMenu");
            var contextField=AccessTools.Field(typeof(MyGuiScreenToolbarConfigBase),"m_contextMenu");
            var savedMenu=menuField.GetValue(owner);
            var savedContext=contextField.GetValue(owner);
            var menu=new MyGuiControlContextMenu {Enabled=false}; menu.Deactivate();
            var context=new MyGuiControlContextMenu {Enabled=false}; context.Deactivate();
            var click=AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase),"OnGridItemDoubleClicked",
                new[] {typeof(MyGuiControlGrid),typeof(MyGuiControlGrid.EventArgs),typeof(bool)});
            int requests=0;
            const long block=986543210;
            var grid=new MyGuiControlGrid {ColumnsCount=1,RowsCount=1};
            var catalog=new MyGuiGridItem((string[])null,null,"Grouped assignment",
                new MyGuiScreenToolbarConfigBase.GridItemUserData {ItemData=()=> {
                    requests++;
                    return new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=block+1,_Action="OnOff"};
                }});
            grid.SetItemAt(0,catalog);
            Action queue=()=> {
                input.Held=true; input.Pressed=true;
                click.Invoke(owner,new object[] {grid,new MyGuiControlGrid.EventArgs {ItemIndex=0,RowIndex=0,ColumnIndex=0},false});
                CockpitAssignment.HandleInput(owner);
            };
            Action release=()=> { input.Held=false; input.Pressed=false; CockpitAssignment.HandleInput(owner); };
            try
            {
                MyInput.Static=(IMyInput)input.GetTransparentProxy();
                menuField.SetValue(owner,menu); contextField.SetValue(owner,context);
                assignment.ChangePage(false,1);
                target.SwitchToPage(page); assignment.Update();
                for(int i=0;i<9;i++) target.SetItemAtIndex(first+i,null);
                var a=TerminalItem(block); var b=TerminalItem(block);
                Require(a.Equals(b) && !ReferenceEquals(a,b),"Duplicate fixture does not contain equal independent native terminal items");
                MyGuiScreenToolbarConfigBase.DropGridItemToToolbar(a,0);
                MyGuiScreenToolbarConfigBase.DropGridItemToToolbar(b,1);
                Require(ReferenceEquals(target.GetSlotItem(0),a) && ReferenceEquals(target.GetSlotItem(1),b) &&
                    physical.GetItemAtIndex(layout.Control(first))?.Equals(a)==true &&
                    physical.GetItemAtIndex(layout.Control(first+1))?.Equals(b)==true,
                    "Native grouped drop deduplicated another visible physical control");
                MyGuiScreenToolbarConfigBase.DropGridItemToToolbar(null,0);
                Require(target.GetSlotItem(0)==null && physical.GetItemAtIndex(layout.Control(first))==null &&
                    ReferenceEquals(target.GetSlotItem(1),b) && physical.GetItemAtIndex(layout.Control(first+1))?.Equals(b)==true,
                    "Clearing a grouped control removed its identical neighbor");
                MyGuiScreenToolbarConfigBase.DropGridItemToToolbar(TerminalItem(block,"OnOff_On"),0);
                var changed=target.GetSlotItem(0);
                MyGuiScreenToolbarConfigBase.UpdateGridItemByRightClick(changed,0,"OnOff");
                Require(changed.Equals(b) && ReferenceEquals(target.GetSlotItem(0),changed) &&
                    ReferenceEquals(target.GetSlotItem(1),b) &&
                    physical.GetItemAtIndex(layout.Control(first))?.Equals(b)==true &&
                    physical.GetItemAtIndex(layout.Control(first+1))?.Equals(b)==true,
                    "Native grouped right-click action change deduplicated another physical control");
                Plugin.Logger.Info("PASS native grouped duplicate assignments: equal TerminalBlock items through static drop, independent removal and right-click action change preserve both physical IDs.");

                for(int i=1;i<9;i++) target.SetItemAtIndex(first+i,null);
                var drop=CockpitAssignment.DoubleClickDrop(owner,grid,catalog,0);
                Require(drop?.DropTo.Grid==toolbar.ToolbarGrid && drop.DropTo.ItemIndex==1,
                    "Other-page catalog assignment did not choose the first visible empty slot");
                queue();
                Require(requests==0 && target.GetSlotItem(1)==null &&
                    AccessTools.Field(typeof(CockpitAssignment),"pendingClick").GetValue(assignment)!=null,
                    "Other-page double-click did not queue assignment until release");
                release();
                var assigned=target.GetSlotItem(1) as MyToolbarItemTerminalBlock;
                Require(requests==1 && assigned?.BlockEntityId==block+1 &&
                    (physical.GetItemAtIndex(layout.Control(first+1)) as MyToolbarItemTerminalBlock)?.BlockEntityId==block+1 &&
                    ReferenceEquals(target.GetSlotItem(0),changed),"Other-page native double-click did not assign the first visible empty physical control");

                for(int i=2;i<used;i++) target.SetItemAtIndex(first+i,TerminalItem(block+10+i));
                var occupied=Enumerable.Range(first,used).Select(target.GetItemAtIndex).ToArray();
                Require(CockpitAssignment.DoubleClickDrop(owner,grid,catalog,0)==null,"Full short group offered a hidden fallback slot");
                queue(); release();
                Require(requests==1 && Enumerable.Range(0,used).All(i=>ReferenceEquals(target.GetItemAtIndex(first+i),occupied[i])) &&
                    Enumerable.Range(used,9-used).All(i=>target.GetItemAtIndex(first+i)==null),
                    "Full short group reached native fallback, replaced a visible control or populated padding");
                MyGuiScreenToolbarConfigBase.DropGridItemToToolbar(TerminalItem(block+99),used);
                Require(Enumerable.Range(0,used).All(i=>ReferenceEquals(target.GetItemAtIndex(first+i),occupied[i])) &&
                    target.GetItemAtIndex(first+used)==null,"Native static drop accepted hidden grouped padding");

                target.SetItemAtIndex(first+1,null);
                queue();
                Require(AccessTools.Field(typeof(CockpitAssignment),"pendingClick").GetValue(assignment)!=null &&
                    (int)AccessTools.Field(typeof(CockpitAssignment),"pendingPage").GetValue(assignment)==page,
                    "Stale-page fixture did not queue a real pending assignment");
                target.SwitchToPage(selected/9); assignment.Update();
                var other=Enumerable.Range(target.CurrentPage*9,9).Select(target.GetItemAtIndex).ToArray();
                release();
                Require(requests==1 && target.GetItemAtIndex(first+1)==null &&
                    physical.GetItemAtIndex(layout.Control(first+1))==null &&
                    Enumerable.Range(0,9).All(i=>ReferenceEquals(target.GetItemAtIndex(target.CurrentPage*9+i),other[i])),
                    "Native page change dispatched a stale pending catalog assignment");
                Require(!target.SelectedSlot.HasValue,"Grouped assignment activated equipment");
                Plugin.Logger.Info("PASS native grouped catalog fallback: other-page first visible empty slot on release, full short-page native fallback suppressed, static padding drop rejected and stale pending page canceled.");
            }
            finally
            {
                input.Held=false; input.Pressed=false;
                assignment.ChangePage(false,1);
                for(int i=0;i<9;i++) target.SetItemAtIndex(first+i,saved[i]);
                menu.Deactivate(); context.Deactivate();
                menuField.SetValue(owner,savedMenu); contextField.SetValue(owner,savedContext);
                MyInput.Static=original;
                target.SwitchToPage(initialPage); assignment.Update();
            }
        }
        internal static void Run(MyGuiScreenToolbarConfigBase owner,MyToolbar target,MyGuiControlToolbar toolbar,CockpitAssignment assignment,MyGuiControls controls,bool switches,int selected,Action assigned=null)
        {
            var original=MyInput.Static;
            var cursor=MyGuiManager.MouseCursorPosition;
            int page=target.CurrentPage,requests=0;
            var input=new Input(original);
            var menu=new MyGuiControlContextMenu(); menu.Deactivate(); controls.Add(menu);
            var context=new MyGuiControlContextMenu(); context.Deactivate();
            if(switches)
            {
                var battery=(Sandbox.ModAPI.Ingame.IMyBatteryBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.MyBatteryBlock));
                var ids=new[] {"Auto","Recharge","Discharge","Recharge_On","Recharge_Off","Discharge_On","Discharge_Off"};
                CockpitAssignment.Fill(menu,ids.Select(id=>(ITerminalAction)new MyTerminalAction<Sandbox.Game.Entities.MyBatteryBlock>(id,new StringBuilder(id),"")).ToList(),
                    id=>Player.CockpitSwitchState.BatteryAction(battery,id));
                var batteryList=(MyGuiControlListbox)menu.GetInnerList();
                Require(batteryList.Items.Select(i=>(string)i.UserData).SequenceEqual(ids.Skip(1).Concat(ids.Take(1))) &&
                    batteryList.Items.Take(6).All(i=>i.Icon==CockpitAssignment.MarkerIcon) && batteryList.Items.Last().Icon!=CockpitAssignment.MarkerIcon,
                    "Battery mode actions do not rank first with the VR icon or incorrectly mark Auto");
                menu.Deactivate();
                Plugin.Logger.Info("PASS native battery action priority: Recharge/Discharge and On/Off variants first with VR marks; Auto keeps ordinary action marking.");
            }
            Set(owner,"m_onDropContextMenu",menu); Set(owner,"m_contextMenu",context);
            var clicked=AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase),"OnDropContextMenuItemClicked");
            menu.ItemClicked+=(sender,args)=>clicked.Invoke(owner,new object[] {sender,args});
            var grid=new MyGuiControlGrid {ColumnsCount=1,RowsCount=1};
            var action=new ActionItem();
            // Supply a terminal action menu without creating blocks or loading a world.
            grid.SetItemAt(0,new MyGuiGridItem((string[])null,null,"Light",new MyGuiScreenToolbarConfigBase.GridItemUserData {ItemData=()=> {
                requests++;
                // Thruster actions on an analog handle: numeric actions rank first with the VR icon; the ship toolbar keeps native order.
                CockpitAssignment.Fill(menu,new List<ITerminalAction> {
                    new MyTerminalAction<Sandbox.Game.Entities.MyThrust>("OnOff",new StringBuilder("On/Off"),""),
                    new MyTerminalAction<Sandbox.Game.Entities.MyThrust>("IncreaseOverride",new StringBuilder("Increase thrust override"),""),
                    new MyTerminalAction<Sandbox.Game.Entities.MyThrust>("SetOverride",new StringBuilder("Set thrust override"),"") },
                    id=>switches && id!="OnOff");
                Set(owner,"m_onDropContextMenuToolbarIndex",1); Set(owner,"m_onDropContextMenuItem",action);
                return new MyObjectBuilder_ToolbarItemEmpty();
            }}));
            var click=AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase),"OnGridItemDoubleClicked",new[] {typeof(MyGuiControlGrid),typeof(MyGuiControlGrid.EventArgs),typeof(bool)});
            Action open=()=> {
                input.Held=true; input.Pressed=true;
                click.Invoke(owner,new object[] {grid,new MyGuiControlGrid.EventArgs {ItemIndex=0},false});
                owner.HandleInput(false);
                Require(!menu.Visible,"Action chooser opened before the second click was released");
                input.Held=false; input.Pressed=false;
                owner.HandleInput(false);
                Require(menu.Visible && menu.IsActiveControl && owner.FocusedControl==menu.GetInnerList(),"Action chooser did not take focus on release");
            };
            try
            {
                MyInput.Static=(IMyInput)input.GetTransparentProxy();
                target.SwitchToPage(selected/9); assignment.Update();
                MyGuiManager.MouseCursorPosition=new Vector2(.48f,.35f);
                open();
                Require(requests==1 && target.GetItemAtIndex(selected)==null,"Opening the chooser changed the assignment");
                var list=(MyGuiControlListbox)menu.GetInnerList();
                string first=switches ? "IncreaseOverride":"OnOff";
                Require(list.Items.Select(i=>(string)i.UserData).SequenceEqual(switches ? new[] {"IncreaseOverride","SetOverride","OnOff"}:new[] {"OnOff","IncreaseOverride","SetOverride"}) &&
                    list.Items.Count(i=>i.Icon==CockpitAssignment.MarkerIcon)==(switches ? 2:0),
                    "Compatible actions are not first and marked in native order");
                MyGuiManager.MouseCursorPosition=list.GetPositionAbsoluteTopLeft()+new Vector2(.025f,.015f);
                owner.FocusedControl=grid;
                menu.IsMouseOver=false; menu.HandleInput();
                Require(list.MouseOverItem==null,"Native focus-loss reproduction did not lose row hover");
                owner.HandleInput(false);
                Require(list.MouseOverItem==list.Items[0],"Action row cannot be hovered after catalog focus");
                input.Pressed=true; input.Held=true; owner.HandleInput(false);
                Require(!menu.Visible && target.GetItemAtIndex(selected)==action && action.ActionId==first && !target.SelectedSlot.HasValue,
                    "Action click did not assign the highlighted slot without activation");
                assigned?.Invoke();
                Require(CockpitAssignment.HandleInput(owner),"Action click leaked to the catalog while held");
                target.SetItemAtIndex(selected,null);
                input.Pressed=false; input.Held=false;
                Require(!CockpitAssignment.HandleInput(owner),"Closed chooser retained input");
                open(); input.Escape=true; owner.HandleInput(false); input.Escape=false;
                Require(!menu.Visible && target.GetItemAtIndex(selected)==null,"Escape committed an assignment");
                open(); MyGuiManager.MouseCursorPosition=new Vector2(.1f); input.Pressed=true; owner.HandleInput(false);
                Require(!menu.Visible && target.GetItemAtIndex(selected)==null,"Outside click committed an assignment");
                input.Pressed=false; input.Held=false;
                Require(!CockpitAssignment.HandleInput(owner),"Canceled chooser retained input");
                Plugin.Logger.Info("PASS native assignment action chooser: compatible actions first and marked, waits for release, hover after catalog focus, first-row click to highlighted slot, no activation, Escape/outside cancellation and input recovery.");
                MyGuiManager.MouseCursorPosition=new Vector2(.48f,.35f); open();
                list=(MyGuiControlListbox)menu.GetInnerList();
                MyGuiManager.MouseCursorPosition=list.GetPositionAbsoluteTopLeft()+new Vector2(.025f,.015f); owner.HandleInput(false);
            }
            finally
            {
                MyInput.Static=original; MyGuiManager.MouseCursorPosition=cursor;
                target.SwitchToPage(page); assignment.Update();
            }
        }
    }
}
