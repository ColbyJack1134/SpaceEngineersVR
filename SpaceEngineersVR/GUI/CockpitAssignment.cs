using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    // Native toolbar paging and selection, with an optional source row for cockpit switches.
    internal sealed class CockpitAssignment : IDisposable
    {
        private static CockpitAssignment current;
        private readonly MyGuiScreenToolbarConfigBase screen;
        private readonly MyToolbar target,source;
        private readonly int count,selected;
        private readonly bool switches;
        private MyGuiControlToolbar toolbar;
        private MyGuiControlGrid sourceGrid;
        private MyGuiControlGridDragAndDrop drag;
        private MyGuiControlLabel sourceLabel;
        private int sourcePage;
        public CockpitAssignment(MyGuiScreenToolbarConfigBase screen,MyToolbar target,MyToolbar source,int count,int selected,bool switches=true)
        { this.screen=screen; this.target=target; this.source=source; this.count=count; this.selected=selected; this.switches=switches; sourcePage=source?.CurrentPage ?? 0; current=this; screen.Closed+=Closed; }
        internal static void Update(MyGuiScreenToolbarConfigBase screen)
        { if(current?.screen==screen) current.Update(); }
        private void Closed(MyGuiScreenBase screen,bool unloading) => Dispose();
        private T Field<T>(string name) where T:class => AccessTools.Field(typeof(MyGuiScreenToolbarConfigBase),name)?.GetValue(screen) as T;
        public void Update()
        {
            var next=Field<MyGuiControlToolbar>("m_toolbarControl");
            if(next==null) return;
            if(toolbar!=next)
            {
                toolbar=next; drag=Field<MyGuiControlGridDragAndDrop>("m_dragAndDrop");
                AddPaging(toolbar.ToolbarGrid,false);
                if(source!=null && drag!=null) AddSource();
            }
            var label=screen.Controls.GetControlByName("LabelToolbar") as MyGuiControlLabel;
            int first=target.CurrentPage*target.SlotCount;
            if(label!=null) { label.Text=switches ? (selected>=first && selected<first+target.SlotCount ? "Switch "+(selected+1)+" · " : "Switches ")+ (first+1)+"–"+Math.Min(first+target.SlotCount,count) :
                    (selected>=first && selected<first+target.SlotCount ? "Assign slot "+(selected-first+1)+" · " : "Toolbar · ")+"Page "+(target.CurrentPage+1)+" / "+target.PageCount;
                label.TextScale=.65f;
                label.Position=toolbar.ToolbarGrid.GetPositionAbsoluteTopLeft()-screen.GetPosition()-new Vector2(0,.017f);
                label.OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER; }
            if(selected>=0) toolbar.ToolbarGrid.SelectedIndex=selected>=first && selected<first+target.SlotCount ? (int?)(selected-first) : null;
            for(int i=0;i<target.SlotCount;i++)
                if(first+i>=count)
                {
                    var item=toolbar.ToolbarGrid.GetItemAt(i);
                    if(item==null) toolbar.ToolbarGrid.SetItemAt(i,new MyGuiGridItem((string[])null,null,"",null) { Enabled=false });
                    else item.Enabled=false;
                }
        }
        private void AddSource()
        {
            foreach(string name in new[] { "m_gridBlocksPanel","m_researchPanel" })
            {
                var panel=Field<MyGuiControlScrollablePanel>(name);
                if(panel!=null) { panel.Size-=new Vector2(0,.11f); panel.Position-=new Vector2(0,.055f); }
            }
            var grid=toolbar.ToolbarGrid;
            sourceGrid=new MyGuiControlGrid { VisualStyle=MyGuiControlGridStyleEnum.Toolbar,ColumnsCount=source.SlotCount,RowsCount=1,
                OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM,
                Position=grid.GetPositionAbsoluteTopLeft()-screen.GetPosition()+new Vector2(0,grid.Size.Y-.11f) };
            var style=AccessTools.Field(typeof(MyGuiControlGrid),"m_styleDef")?.GetValue(grid) as MyGuiStyleDefinition;
            if(style!=null) sourceGrid.SetCustomStyleDefinition(style);
            sourceGrid.ItemDragged+=DragSource;
            screen.Controls.Add(sourceGrid);
            sourceLabel=new MyGuiControlLabel(sourceGrid.Position-new Vector2(0,sourceGrid.Size.Y+.018f),textScale:.65f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            screen.Controls.Add(sourceLabel);
            AddPaging(sourceGrid,true);
            FillSource();
        }
        internal void ChangePage(bool ship,int direction)
        {
            if(ship) { sourcePage=(sourcePage+source.PageCount+direction)%source.PageCount; FillSource(); }
            else
            {
                int pages=(count+target.SlotCount-1)/target.SlotCount;
                target.SwitchToPage((target.CurrentPage+pages+direction)%pages);
                Update();
            }
        }
        private void AddPaging(MyGuiControlGrid grid,bool ship)
        {
            int pages=ship ? source.PageCount : (count+target.SlotCount-1)/target.SlotCount;
            if(pages<2) return;
            var topLeft=grid.GetPositionAbsoluteTopLeft()-screen.GetPosition();
            var size=grid.ItemSize*.82f;
            foreach(int step in new[] {-1,1})
            {
                int direction=step;
                var position=topLeft+new Vector2(step<0 ? -size.X-.004f : grid.Size.X+.004f,(grid.Size.Y-size.Y)/2);
                screen.Controls.Add(new MyGuiControlButton(position,size:size,
                    visualStyle:MyGuiControlButtonStyleEnum.Square,text:new StringBuilder(step<0 ? "‹" : "›"),textScale:1.6f,
                    originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
                    onButtonClick:b=>ChangePage(ship,direction)) { Name=(ship ? "Ship" : "Switch")+(step<0 ? "PreviousPage" : "NextPage"),
                    CustomStyle=new MyGuiControlButton.StyleDefinition {
                        NormalTexture=MyGuiConstants.TEXTURE_RECTANGLE_BUTTON_BORDER,
                        HighlightTexture=MyGuiConstants.TEXTURE_RECTANGLE_BUTTON_HIGHLIGHTED_BORDER,
                        FocusTexture=MyGuiConstants.TEXTURE_RECTANGLE_BUTTON_FOCUS_BORDER,SizeOverride=size } });
            }
        }
        private void FillSource()
        {
            sourceLabel.Text="Ship toolbar "+(sourcePage+1)+" · drag to a switch";
            for(int i=0;i<source.SlotCount;i++)
            {
                var item=source.GetItemAtIndex(sourcePage*source.SlotCount+i);
                sourceGrid.SetItemAt(i,item==null ? null : new MyGuiGridItem(item.Icons,item.SubIcon,item.DisplayName.ToString(),item) {
                    Enabled=item.AllowedInToolbarType(MyToolbarType.ButtonPanel) });
            }
        }
        private void DragSource(MyGuiControlGrid sender,MyGuiControlGrid.EventArgs args)
        {
            var item=sender.GetItemAt(args.ItemIndex);
            if(item?.Enabled!=true) return;
            drag.StartDragging(MyDropHandleType.MouseRelease,args.Button,item,
                new MyDragAndDropInfo { Grid=sender,ItemIndex=args.ItemIndex },includeTooltip:false);
            sender.HideToolTip();
        }
        internal static bool HandleDrop(MyGuiScreenToolbarConfigBase screen,MyDragAndDropEventArgs args)
        {
            var c=current;
            if(c==null || c.screen!=screen) return false;
            bool target=args.DropTo!=null && c.toolbar.IsToolbarGrid(args.DropTo.Grid);
            int index=target ? c.target.SlotToIndex(args.DropTo.ItemIndex) : -1;
            if(target && index>=c.count) return true;
            if(c.sourceGrid==null) return false;
            if(args.DropTo?.Grid==c.sourceGrid) return true;
            if(args.DragFrom?.Grid!=c.sourceGrid) return false;
            if(target && index>=0 && args.Item.UserData is MyToolbarItem item && item.AllowedInToolbarType(MyToolbarType.ButtonPanel))
            {
                var copy=MyToolbarItemFactory.CreateToolbarItem(item.GetObjectBuilder());
                if(copy!=null) c.target.SetItemAtIndex(index,copy);
            }
            return true;
        }
        public void Dispose()
        {
            screen.Closed-=Closed;
            if(sourceGrid!=null) sourceGrid.ItemDragged-=DragSource;
            if(current==this) current=null;
        }
    }
}
