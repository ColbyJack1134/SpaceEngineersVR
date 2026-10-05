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
        private static int Count => Player.CockpitLayout.Count(Player.FighterProfile.Subtype);
        public override string GetFriendlyName() => "SEVR assignment preview";
        private static void Set(object instance,string field,object value) => AccessTools.Field(instance.GetType(),field).SetValue(instance,value);
        internal AssignmentPreview(bool ordinary=false) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.95f,.70f))
        {
            this.ordinary=ordinary;
            var file=Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","Hud","Default.sbc");
            MyObjectBuilder_Definitions definitions;
            if(!MyObjectBuilderSerializer.DeserializeXML(file,out definitions)) throw new Exception("Native HUD definition unavailable");
            var style=definitions.Definitions.OfType<MyObjectBuilder_HudDefinition>().First().Toolbar;
            style.VisibleCondition=null;
            previousComponent=AccessTools.Field(typeof(MyToolbarComponent),"m_instance").GetValue(null);
            if(previousComponent==null) AccessTools.Field(typeof(MyToolbarComponent),"m_instance").SetValue(null,FormatterServices.GetUninitializedObject(typeof(MyToolbarComponent)));
            previous=MyToolbarComponent.CurrentToolbar;
            target=new MyToolbar(ordinary ? MyToolbarType.Ship:MyToolbarType.ButtonPanel,9,ordinary ? 3:(Count+8)/9);
            source=ordinary ? null:new MyToolbar(MyToolbarType.Ship,9,3);
            if(ordinary) target.SwitchToPage(1);
            MyToolbarComponent.CurrentToolbar=target;
            toolbar=new PreviewToolbar(style) { Position=new Vector2(.33f,.20f),OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM };
            Controls.Add(toolbar);
            owner=(MyGuiScreenToolbarConfigBase)FormatterServices.GetUninitializedObject(typeof(MyGuiScreenToolbarConfigBase));
            Set(owner,"m_position",new Vector2(.5f));
            Set(owner,"m_controls",Controls); Set(owner,"m_toolbarControl",toolbar);
            var drag=new MyGuiControlGridDragAndDrop(MyGuiConstants.DRAG_AND_DROP_BACKGROUND_COLOR,MyGuiConstants.DRAG_AND_DROP_TEXT_COLOR,.7f,MyGuiConstants.DRAG_AND_DROP_TEXT_OFFSET,true);
            Set(owner,"m_dragAndDrop",drag); Controls.Add(drag);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.32f,.10f),text:"Switches") {Name="LabelToolbar"});
            Controls.Add(new MyGuiControlLabel(new Vector2(0,-.23f),text:ordinary ? "Toolbar assignment":"Cockpit assignment",originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
            assignment=new CockpitAssignment(owner,target,source,ordinary ? target.SlotCount*target.PageCount:Count,10,switches:!ordinary); assignment.Update();
            FillArtwork();
        }
        private void FillArtwork()
        {
            string[] art={"GridPowerOn","Dampeners","Handbrake","Light","ToggleConnectors","GridPowerOn","Dampeners","Light","Handbrake"};
            foreach(var grid in Controls.OfType<MyGuiControlGrid>().Concat(new[] {toolbar.ToolbarGrid}))
                for(int i=0;i<9;i++) if(grid!=toolbar.ToolbarGrid || target.CurrentPage*9+i<Count)
                    grid.SetItemAt(i,ordinary && target.CurrentPage==1 && i==1 ? null:new MyGuiGridItem(Player.NativeSprites.Hud(art[i]),null,"Action",null));
        }
        internal void VerifyAndPage()
        {
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
            ((MyGuiControlButton)Controls.GetControlByName("SwitchNextPage")).PressButton();
            if(target.CurrentPage!=1) throw new Exception("Switch page 2 failed");
            ((MyGuiControlButton)Controls.GetControlByName("ShipNextPage")).PressButton();
            if(source.CurrentPage!=0 || target.CurrentPage!=1) throw new Exception("Browsing ship actions changed an actual toolbar page");
            for(int page=2;page<target.PageCount;page++)
            {
                ((MyGuiControlButton)Controls.GetControlByName("SwitchNextPage")).PressButton();
                if(target.CurrentPage!=page) throw new Exception("Switch page advancement failed");
            }
            int used=Count%9;
            if(used>0 && toolbar.ToolbarGrid.GetItemAt(used)?.Enabled!=false)
                throw new Exception("Last switch page accepts an out-of-range slot");
            ((MyGuiControlButton)Controls.GetControlByName("SwitchNextPage")).PressButton();
            if(target.CurrentPage!=0) throw new Exception("Switch page wrap failed");
            ((MyGuiControlButton)Controls.GetControlByName("SwitchPreviousPage")).PressButton(); FillArtwork();
            Plugin.Logger.Info("PASS native assignment UI: square buttons, independent page changes, all switch pages, wrap and disabled unused slots.");
        }
        private void VerifyDoubleClick()
        {
            int page=target.CurrentPage,requests=0;
            var grid=new MyGuiControlGrid {ColumnsCount=1,RowsCount=1};
            var item=new MyGuiGridItem((string[])null,null,"Assignment",new MyGuiScreenToolbarConfigBase.GridItemUserData {
                ItemData=()=> {requests++; return new MyObjectBuilder_ToolbarItemEmpty();} });
            grid.SetItemAt(0,item);
            target.SwitchToPage(1); assignment.Update();
            var drop=CockpitAssignment.DoubleClickDrop(owner,grid,item,0);
            if(drop?.DropTo.Grid!=toolbar.ToolbarGrid || drop.DropTo.ItemIndex!=1 || drop.DragFrom.Grid!=grid || drop.Item!=item)
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
            if(CockpitAssignment.DoubleClickDrop(owner,grid,item,0)!=null) throw new Exception("Hidden assignment slot accepted");
            target.SwitchToPage(page); assignment.Update();
            Plugin.Logger.Info("PASS native double-click assignment: highlighted page/slot, native drop dispatch, disabled/hidden/toolbar/stale-owner rejection and no equipment activation.");
            AssignmentMenuTests.Run(owner,target,toolbar,assignment,Controls);
        }
        internal void Finish()
        {
            assignment.Dispose(); toolbar.OnRemoving(); MyToolbarComponent.CurrentToolbar=previous; AccessTools.Field(typeof(MyToolbarComponent),"m_instance").SetValue(null,previousComponent); CloseScreenNow();
        }
    }
}
