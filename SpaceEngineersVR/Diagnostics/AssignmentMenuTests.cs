using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
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
            public override MyObjectBuilder_ToolbarItem GetObjectBuilder() => null;
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
        internal static void Run(MyGuiScreenToolbarConfigBase owner,MyToolbar target,MyGuiControlToolbar toolbar,CockpitAssignment assignment,MyGuiControls controls,bool switches)
        {
            var original=MyInput.Static;
            var cursor=MyGuiManager.MouseCursorPosition;
            int page=target.CurrentPage,requests=0;
            var input=new Input(original);
            var menu=new MyGuiControlContextMenu(); menu.Deactivate(); controls.Add(menu);
            var context=new MyGuiControlContextMenu(); context.Deactivate();
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
                target.SwitchToPage(1); assignment.Update();
                MyGuiManager.MouseCursorPosition=new Vector2(.48f,.35f);
                open();
                Require(requests==1 && target.GetItemAtIndex(10)==null,"Opening the chooser changed the assignment");
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
                Require(!menu.Visible && target.GetItemAtIndex(10)==action && action.ActionId==first && !target.SelectedSlot.HasValue,
                    "Action click did not assign the highlighted slot without activation");
                Require(CockpitAssignment.HandleInput(owner),"Action click leaked to the catalog while held");
                target.SetItemAtIndex(10,null);
                input.Pressed=false; input.Held=false;
                Require(!CockpitAssignment.HandleInput(owner),"Closed chooser retained input");
                open(); input.Escape=true; owner.HandleInput(false); input.Escape=false;
                Require(!menu.Visible && target.GetItemAtIndex(10)==null,"Escape committed an assignment");
                open(); MyGuiManager.MouseCursorPosition=new Vector2(.1f); input.Pressed=true; owner.HandleInput(false);
                Require(!menu.Visible && target.GetItemAtIndex(10)==null,"Outside click committed an assignment");
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
