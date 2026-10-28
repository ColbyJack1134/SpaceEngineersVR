#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using Sandbox.Graphics;
using SpaceEngineersVR.GUI;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    // Uses native controls and production assignment layout, without creating a world.
    internal sealed class AssignmentPreview : MyGuiScreenBase
    {
        private sealed class PreviewToolbar : MyGuiControlToolbar
        {
            internal PreviewToolbar(MyObjectBuilder_ToolbarControlVisualStyle style) : base(style,true) { }
            public override void Draw(float alpha,float background)
            {
                // The native container also draws a world player's paint selection.
                // Its real slot grid/page labels can be drawn without that player.
                foreach(var control in Elements) if(control.Visible && !(control is MyGuiControlPanel)) control.Draw(alpha,background);
            }
        }
        private readonly MyToolbar source,target,previous;
        private readonly object previousComponent;
        private readonly MyGuiControlToolbar toolbar;
        private readonly CockpitAssignment assignment;
        private readonly MyGuiScreenToolbarConfigBase owner;
        private readonly bool ordinary;
        private readonly Player.CockpitAssignmentToolbar grouped;
        private readonly MyToolbar physical;
        private readonly int selected;
        private readonly NativeIntegrationTests.Item occupied;
        // Control 73 stands in for an action that cannot reach zero.
        private readonly bool[] springs=new bool[Player.CockpitLayout.MaximumCount];
        private int springRequests;
        private (bool Handle,bool? On) Spring(int control) => (Multiplayer.AnalogControl.IsHandle(Player.CockpitLayout.ControlSeat,control),control==73 ? (bool?)null:springs[control]);
        private static int Count => Player.CockpitLayout.Count(Player.CockpitLayout.ControlSeat);
        public override string GetFriendlyName() => "SEVR assignment preview";
        private static void Set(object instance,string field,object value) => AccessTools.Field(instance.GetType(),field).SetValue(instance,value);
        internal AssignmentPreview(bool ordinary=false,bool quickToolbar=false) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.95f,.70f))
        {
            this.ordinary=ordinary=ordinary || quickToolbar;
            var file=Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","Hud","Default.sbc");
            MyObjectBuilder_Definitions definitions;
            if(!MyObjectBuilderSerializer.DeserializeXML(file,out definitions)) throw new Exception("Native HUD definition unavailable");
            var style=definitions.Definitions.OfType<MyObjectBuilder_HudDefinition>().First().Toolbar;
            style.VisibleCondition=null;
            previousComponent=AccessTools.Field(typeof(MyToolbarComponent),"m_instance").GetValue(null);
            if(previousComponent==null) AccessTools.Field(typeof(MyToolbarComponent),"m_instance").SetValue(null,FormatterServices.GetUninitializedObject(typeof(MyToolbarComponent)));
            previous=MyToolbarComponent.CurrentToolbar;
            if(quickToolbar) target=new MyToolbar(MyToolbarType.Character,Player.QuickToolbar.Slots,1);
            else if(ordinary) target=new MyToolbar(MyToolbarType.Ship,9,3);
            else
            {
                physical=new MyToolbar(MyToolbarType.ButtonPanel,9,(Count+8)/9);
                grouped=new Player.CockpitAssignmentToolbar(physical,Player.CockpitAssignmentLayout.Find(Player.CockpitLayout.ControlSeat));
                target=grouped.Toolbar;
            }
            selected=quickToolbar ? 1:ordinary ? 10:grouped.Layout.DisplayIndex(58);
            if(ordinary && !quickToolbar) { occupied=new NativeIntegrationTests.Item(); target.SetItemAtIndex(selected,occupied); }
            source=quickToolbar ? new MyToolbar(MyToolbarType.Character,9,9) : ordinary ? null:new MyToolbar(MyToolbarType.Ship,9,3);
            target.SwitchToPage(selected/target.SlotCount);
            MyToolbarComponent.CurrentToolbar=target;
            toolbar=new PreviewToolbar(style) { Position=new Vector2(.33f,.20f),OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM };
            if(!ordinary)
            {
                var extended=new MyToolbar(MyToolbarType.ButtonPanel,9,11);
                toolbar.ShowToolbar(extended);
                extended.SwitchToPage(10);
                if(toolbar.ToolbarGrid.ColumnsCount!=9) throw new Exception("Extended assignment toolbar lost its slots");
                toolbar.ShowToolbar(target);
            }
            Controls.Add(toolbar);
            owner=(MyGuiScreenToolbarConfigBase)FormatterServices.GetUninitializedObject(typeof(MyGuiScreenToolbarConfigBase));
            Set(owner,"m_position",new Vector2(.5f));
            Set(owner,"m_controls",Controls); Set(owner,"m_toolbarControl",toolbar);
            var drag=new MyGuiControlGridDragAndDrop(MyGuiConstants.DRAG_AND_DROP_BACKGROUND_COLOR,MyGuiConstants.DRAG_AND_DROP_TEXT_COLOR,.7f,MyGuiConstants.DRAG_AND_DROP_TEXT_OFFSET,true);
            Set(owner,"m_dragAndDrop",drag); Controls.Add(drag);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.32f,.10f),text:"Switches") {Name="LabelToolbar"});
            Controls.Add(new MyGuiControlLabel(new Vector2(0,-.23f),text:quickToolbar ? "Quick toolbar assignment":ordinary ? "Toolbar assignment":"Cockpit assignment",originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
            assignment=new CockpitAssignment(owner,target,source,target.SlotCount*target.PageCount,selected,switches:!ordinary,layout:grouped?.Layout,
                title:quickToolbar ? "Quick toolbar · "+Player.QuickToolbar.Names[selected]:null,holster:!quickToolbar,
                spring:ordinary ? null:(Func<int,(bool,bool?)>)Spring,setSpring:(c,on)=>{springRequests++; springs[c]=on;}); assignment.Update();
            springs[72]=springs[74]=true;
            FillArtwork();
            if(ordinary && !quickToolbar) VerifyInputSelection();
        }
        private void FillArtwork()
        {
            string[] art={"GridPowerOn","Dampeners","Handbrake","Light","ToggleConnectors","GridPowerOn","Dampeners","Light","Handbrake"};
            foreach(var grid in Controls.OfType<MyGuiControlGrid>().Concat(new[] {toolbar.ToolbarGrid}))
                for(int i=0;i<Math.Min(9,Math.Min(grid.MaxItemCount,grid.ColumnsCount*grid.RowsCount));i++)
                    grid.SetItemAt(i,ordinary && target.CurrentPage==1 && i==1 && target.GetItemAtIndex(selected)==null ? null:new MyGuiGridItem(Player.NativeSprites.Hud(art[i]),null,"Action",null));
        }
        private void VerifyInputSelection()
        {
            var input=AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase),nameof(MyGuiScreenToolbarConfigBase.HandleInput));
            var postfix=AccessTools.Method(typeof(Patches.ToolbarAssignmentInputPatch),nameof(Patches.ToolbarAssignmentInputPatch.Postfix));
            if(!Harmony.GetPatchInfo(input).Postfixes.Any(p=>p.PatchMethod==postfix))
                throw new Exception("Assignment selection is not restored after native input");
            var select=AccessTools.Method(typeof(MyGuiControlGrid),"SelectMouseOverItem");
            foreach(int? hovered in new int?[] {null,0,8})
            {
                select.Invoke(toolbar.ToolbarGrid,new object[] {hovered});
                Patches.ToolbarAssignmentInputPatch.Postfix(owner);
                if(toolbar.ToolbarGrid.SelectedIndex!=selected%target.SlotCount || occupied.Count!=0 ||
                    !ReferenceEquals(target.GetItemAtIndex(selected),occupied) || target.SelectedSlot.HasValue)
                    throw new Exception("Native input changed the occupied assignment destination or activated equipment");
            }
            Plugin.Logger.Info("PASS native occupied assignment: input clears/changes grid selection, assignment highlight restored without activation or removal.");
        }
        internal void VerifyAndPage()
        {
            if(ordinary) target.SetItemAtIndex(selected,null);
            VerifyDoubleClick();
            if(ordinary)
            {
                if(target.CurrentPage!=1 || toolbar.ToolbarGrid.SelectedIndex!=1 || target.SelectedSlot.HasValue || target.GetItemAtIndex(10)!=null)
                    throw new Exception("Empty toolbar assignment lost its page/slot or activated equipment");
                var previous=(MyGuiControlButton)Controls.GetControlByName("SwitchPreviousPage");
                var next=(MyGuiControlButton)Controls.GetControlByName("SwitchNextPage");
                if(previous==null || next==null || Controls.GetControlByName("ShipNextPage")!=null)
                    throw new Exception("Ordinary toolbar paging has an incorrect source row");
                next.PressButton();
                if(target.CurrentPage!=2 || toolbar.ToolbarGrid.SelectedIndex.HasValue) throw new Exception("Assignment highlight followed another page");
                next.PressButton();
                if(target.CurrentPage!=0) throw new Exception("Ordinary toolbar page did not wrap");
                previous.PressButton(); previous.PressButton();
                if(target.CurrentPage!=1 || toolbar.ToolbarGrid.SelectedIndex!=1) throw new Exception("Assignment highlight was not restored");
                previous.PressButton(); FillArtwork();
                Plugin.Logger.Info("PASS native ordinary assignment: exact empty slot, page arrows and wrap, page-specific highlight, no equipment activation.");
                return;
            }
            foreach(string row in new[] {"Ship","Switch"})
            {
                var left=(MyGuiControlButton)Controls.GetControlByName(row+"PreviousPage");
                var right=(MyGuiControlButton)Controls.GetControlByName(row+"NextPage");
                if(left==null || right==null || left.Size!=right.Size) throw new Exception("Mismatched toolbar paging buttons");
                var pixels=MyGuiManager.GetScreenSizeFromNormalizedSize(left.Size);
                if(Math.Abs(pixels.X-pixels.Y)>2) throw new Exception("Toolbar paging buttons are not square: "+pixels);
            }
            var forward=(MyGuiControlButton)Controls.GetControlByName("SwitchNextPage");
            var backward=(MyGuiControlButton)Controls.GetControlByName("SwitchPreviousPage");
            var fixedForward=forward.GetPositionAbsolute(); var fixedBackward=backward.GetPositionAbsolute();
            var fixedGrid=toolbar.ToolbarGrid.GetPositionAbsoluteTopLeft();
            int initial=target.CurrentPage;
            forward.PressButton();
            if(target.CurrentPage!=(initial+1)%target.PageCount) throw new Exception("Switch next group failed");
            ((MyGuiControlButton)Controls.GetControlByName("ShipNextPage")).PressButton();
            if(source.CurrentPage!=0 || target.CurrentPage!=(initial+1)%target.PageCount) throw new Exception("Browsing ship actions changed an actual toolbar page");
            target.SwitchToPage(0); assignment.Update();
            for(int page=0;page<target.PageCount;page++)
            {
                if(target.CurrentPage!=page || toolbar.ToolbarGrid.MaxItemCount!=grouped.Layout.Pages[page].Controls.Length ||
                    forward.GetPositionAbsolute()!=fixedForward || backward.GetPositionAbsolute()!=fixedBackward ||
                    toolbar.ToolbarGrid.GetPositionAbsoluteTopLeft()!=fixedGrid) throw new Exception("Group paging moved its arrows or exposed unused slots");
                if(((System.Collections.Generic.List<MyGuiControlLabel>)AccessTools.Field(typeof(MyGuiControlToolbar),"m_pageLabelList").GetValue(toolbar)).Any(label=>label.Visible))
                    throw new Exception("Native toolbar refresh restored the obsolete numeric page strip");
                int used=grouped.Layout.Pages[page].Controls.Length;
                if(used<9)
                {
                    if(toolbar.ToolbarGrid.IsValidIndex(used)) throw new Exception("Hidden group slot can be selected");
                    var drop=new MyDragAndDropEventArgs { DropTo=new MyDragAndDropInfo {Grid=toolbar.ToolbarGrid,ItemIndex=used} };
                    if(!CockpitAssignment.HandleDrop(owner,drop)) throw new Exception("Hidden group slot accepts a drop");
                }
                forward.PressButton();
            }
            if(target.CurrentPage!=0) throw new Exception("Switch page wrap failed");
            if(Controls.OfType<MyGuiControlCheckbox>().Any(c=>c.Visible)) throw new Exception("Spring checkboxes shown on a switch page");
            backward.PressButton(); backward.PressButton(); backward.PressButton(); FillArtwork();
            VerifySprings();
            Plugin.Logger.Info("PASS native grouped assignment UI: actual Control Seat groups, hidden unused slots/drop rejection, fixed arrows/grid, independent ship browsing, all pages and wrap.");
        }
        private void VerifySprings()
        {
            var boxes=Controls.OfType<MyGuiControlCheckbox>().Where(c=>c.Visible).ToList();
            var controls=grouped.Layout.Pages[target.CurrentPage].Controls;
            if(boxes.Count!=controls.Length || boxes.Count(c=>c.Enabled)!=controls.Length-1 || !Controls.OfType<MyGuiControlLabel>().Any(l=>l.Visible && l.Text=="Spring back"))
                throw new Exception("Lever page does not show one spring checkbox per lever");
            var grid=toolbar.ToolbarGrid;
            var items=(RectangleF)AccessTools.Field(typeof(MyGuiControlGrid),"m_itemsRectangle").GetValue(grid);
            for(int i=0;i<boxes.Count;i++)
            {
                var center=boxes[i].GetPositionAbsoluteCenter();
                float left=items.X+grid.ItemStep.X*i;
                if(center.X<left || center.X>left+grid.ItemSize.X || center.Y<items.Y+grid.ItemSize.Y || center.Y>items.Y+grid.ItemSize.Y+.05f)
                    throw new Exception("Spring checkbox "+i+" is not under its lever slot");
            }
            for(int i=0;i<controls.Length;i++)
                if(boxes[i].Enabled!=(controls[i]!=73) || boxes[i].IsChecked!=springs[controls[i]]) throw new Exception("Spring checkbox "+i+" does not match its lever");
            int off=Array.FindIndex(controls,c=>c!=73 && !springs[c]);
            boxes[off].IsChecked=true;
            if(springRequests!=1 || !springs[controls[off]]) throw new Exception("Spring checkbox saved to the wrong lever");
            springs[controls[off]]=false; boxes[off].IsChecked=false; springRequests=0;
            Plugin.Logger.Info("PASS native spring checkboxes: hidden on switch pages, one per lever, unavailable action disabled, toggle saves the matching control.");
        }
        private void VerifyDoubleClick()
        {
            int page=target.CurrentPage,requests=0;
            var grid=new MyGuiControlGrid {ColumnsCount=1,RowsCount=1};
            var item=new MyGuiGridItem((string[])null,null,"Assignment",new MyGuiScreenToolbarConfigBase.GridItemUserData {
                ItemData=()=> {requests++; return new MyObjectBuilder_ToolbarItemEmpty();} });
            grid.SetItemAt(0,item);
            target.SwitchToPage(selected/9); assignment.Update();
            var drop=CockpitAssignment.DoubleClickDrop(owner,grid,item,0);
            if(drop?.DropTo.Grid!=toolbar.ToolbarGrid || drop.DropTo.ItemIndex!=selected%9 || drop.DragFrom.Grid!=grid || drop.Item!=item)
                throw new Exception("Double-click did not target the highlighted page/slot");
            var click=AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase),"OnGridItemDoubleClicked",new[] {typeof(MyGuiControlGrid),typeof(MyGuiControlGrid.EventArgs),typeof(bool)});
            click.Invoke(owner,new object[] {grid,new MyGuiControlGrid.EventArgs {RowIndex=0,ColumnIndex=0,ItemIndex=0},false});
            CockpitAssignment.HandleInput(owner);
            if(requests!=1 || target.SelectedSlot.HasValue) throw new Exception("Double-click lost the native drop path or activated equipment");
            item.Enabled=false;
            if(CockpitAssignment.DoubleClickDrop(owner,grid,item,0)!=null) throw new Exception("Disabled assignment accepted");
            item.Enabled=true;
            if(CockpitAssignment.DoubleClickDrop(owner,toolbar.ToolbarGrid,item,0)!=null) throw new Exception("Toolbar double-click was intercepted");
            MyToolbarComponent.CurrentToolbar=new MyToolbar(MyToolbarType.Character);
            if(CockpitAssignment.DoubleClickDrop(owner,grid,item,0)!=null) throw new Exception("Stale toolbar owner accepted");
            MyToolbarComponent.CurrentToolbar=target;
            target.SwitchToPage(0); assignment.Update();
            var otherPage=CockpitAssignment.DoubleClickDrop(owner,grid,item,0);
            if(ordinary ? otherPage!=null : otherPage?.DropTo.Grid!=toolbar.ToolbarGrid || otherPage.DropTo.ItemIndex!=0)
                throw new Exception("Other-page assignment did not preserve ordinary rejection or choose the first visible group slot");
            target.SwitchToPage(page); assignment.Update();
            Plugin.Logger.Info("PASS native double-click assignment: highlighted page/slot, native drop dispatch, other-page group fallback, disabled/toolbar/stale-owner rejection and no equipment activation.");
            if(!ordinary) AssignmentMenuTests.RunGrouped(owner,target,toolbar,assignment,grouped.Layout,physical,selected);
            AssignmentMenuTests.Run(owner,target,toolbar,assignment,Controls,!ordinary,selected,ordinary ? (Action)null:()=> {
                var assigned=physical.GetItemAtIndex(58) as MyToolbarItemTerminalBlock;
                if(assigned?.BlockEntityId!=987654321 || assigned.ActionId!="IncreaseOverride" || physical.GetItemAtIndex(selected)!=null)
                    throw new Exception("Native action chooser saved to a display slot instead of the highlighted physical switch");
            });
        }
        internal void Finish()
        {
            assignment.Dispose(); toolbar.OnRemoving(); grouped?.Dispose(); MyToolbarComponent.CurrentToolbar=previous; AccessTools.Field(typeof(MyToolbarComponent),"m_instance").SetValue(null,previousComponent); CloseScreenNow();
        }
    }
}
#endif
