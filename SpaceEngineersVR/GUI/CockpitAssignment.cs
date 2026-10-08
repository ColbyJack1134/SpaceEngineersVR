using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using VRage.Collections;
using VRage.Game;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    // Native toolbar paging and selection, with an optional source row for cockpit switches.
    internal sealed class CockpitAssignment : IDisposable
    {
        private static CockpitAssignment current;
        private static readonly System.Reflection.MethodInfo nativeDrop=AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase),"OnDragAndDropOnDrop");
        private static readonly System.Reflection.MethodInfo nativeDoubleClick=AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase),"OnGridItemDoubleClicked",
            new[] {typeof(MyGuiControlGrid),typeof(MyGuiControlGrid.EventArgs),typeof(bool)});
        internal const double HoldSeconds=.4;
        private readonly MyGuiScreenToolbarConfigBase screen;
        private readonly MyToolbar target,source;
        private readonly int count,selected;
        private readonly bool switches;
        private readonly CockpitAssignmentLayout layout;
        private MyGuiControlToolbar toolbar;
        private MyGuiControlGrid sourceGrid;
        private MyGuiControlGridDragAndDrop drag;
        private MyDragAndDropEventArgs pendingClick;
        private MyGuiControlContextMenu clickMenu;
        private MyGuiControlLabel sourceLabel;
        private int sourcePage,dropSlot=-1,dropPage=-1,pendingPage=-1,heldItem=-1;
        private DateTime heldSince;
        internal static string MarkerIcon => System.IO.Path.Combine(Plugin.Common.AssetFolder,"Icons","vr.dds");
        [HarmonyPatch(typeof(MyToolbarComponent),nameof(MyToolbarComponent.GetSlotControlText))]
        private static class PageLabel
        {
            // Native page labels reuse slot bindings, which end at the ninth slot.
            private static void Postfix(int slotIndex,ref StringBuilder __result)
            {
                if(__result==null && slotIndex>=0) __result=new StringBuilder((slotIndex+1).ToString());
            }
        }
        public CockpitAssignment(MyGuiScreenToolbarConfigBase screen,MyToolbar target,MyToolbar source,int count,int selected,bool switches=true,CockpitAssignmentLayout layout=null)
        { this.screen=screen; this.target=target; this.source=source; this.count=count; this.selected=selected; this.switches=switches; this.layout=layout; sourcePage=source?.CurrentPage ?? 0; current=this; screen.Closed+=Closed; }
        private int Control(int index) => layout!=null ? layout.Control(index):index>=0 && index<count ? index:-1;
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
            if(layout!=null)
                foreach(var page in (System.Collections.Generic.List<MyGuiControlLabel>)AccessTools.Field(typeof(MyGuiControlToolbar),"m_pageLabelList").GetValue(toolbar)) page.Visible=false;
            var label=screen.Controls.GetControlByName("LabelToolbar") as MyGuiControlLabel;
            int first=target.CurrentPage*target.SlotCount;
            if(label!=null) { label.Text=layout!=null ? layout.Pages[target.CurrentPage].Name+" · "+(target.CurrentPage+1)+" / "+target.PageCount : switches ? (selected>=first && selected<first+target.SlotCount ? "Switch "+(selected+1)+" · " : "Switches ")+ (first+1)+"–"+Math.Min(first+target.SlotCount,count) :
                    (selected>=first && selected<first+target.SlotCount ? "Assign slot "+(selected-first+1)+" · " : "Toolbar · ")+"Page "+(target.CurrentPage+1)+" / "+target.PageCount;
                label.TextScale=.65f;
                label.Position=toolbar.ToolbarGrid.GetPositionAbsoluteTopLeft()-screen.GetPosition()-new Vector2(0,.017f);
                label.OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER; }
            if(layout!=null) toolbar.ToolbarGrid.MaxItemCount=layout.Pages[target.CurrentPage].Controls.Length;
            if(selected>=0) toolbar.ToolbarGrid.SelectedIndex=selected>=first && selected<first+target.SlotCount ? (int?)(selected-first) : null;
            for(int i=0;i<target.SlotCount;i++)
                if(Control(first+i)<0)
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
                pendingClick=null; heldItem=-1; dropSlot=dropPage=-1;
                clickMenu?.Deactivate(); clickMenu=null;
                Field<MyGuiControlContextMenu>("m_onDropContextMenu")?.Deactivate();
                toolbar.HideContextMenu();
                int pages=layout?.Pages.Length ?? (count+target.SlotCount-1)/target.SlotCount;
                target.SwitchToPage((target.CurrentPage+pages+direction)%pages);
                Update();
            }
        }
        private void AddPaging(MyGuiControlGrid grid,bool ship)
        {
            int pages=ship ? source.PageCount : layout?.Pages.Length ?? (count+target.SlotCount-1)/target.SlotCount;
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
            sourceLabel.Text="Ship toolbar "+(sourcePage+1)+" · drag to assign";
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
        internal static MyDragAndDropEventArgs DoubleClickDrop(MyGuiScreenToolbarConfigBase screen,MyGuiControlGrid grid,MyGuiGridItem item,int itemIndex)
        {
            var c=current;
            if(c==null || c.screen!=screen || c.toolbar==null || c.Control(c.selected)<0 ||
                !ReferenceEquals(MyToolbarComponent.CurrentToolbar,c.target) || c.toolbar.IsToolbarGrid(grid)) return null;
            int slot=c.selected-c.target.CurrentPage*c.target.SlotCount;
            if(c.layout!=null && (slot<0 || slot>=c.target.SlotCount))
            {
                slot=-1;
                for(int i=0;i<c.layout.Pages[c.target.CurrentPage].Controls.Length;i++)
                    if(c.target.GetSlotItem(i)==null) { slot=i; break; }
            }
            if(slot<0 || slot>=c.target.SlotCount || item?.Enabled!=true) return null;
            return new MyDragAndDropEventArgs { Item=item,DragFrom=new MyDragAndDropInfo {Grid=grid,ItemIndex=itemIndex},
                DropTo=new MyDragAndDropInfo {Grid=c.toolbar.ToolbarGrid,ItemIndex=slot} };
        }
        internal static bool HandleDoubleClick(MyGuiScreenToolbarConfigBase screen,MyGuiControlGrid grid,MyGuiControlGrid.EventArgs args)
        {
            var drop=DoubleClickDrop(screen,grid,grid.TryGetItemAt(args.RowIndex,args.ColumnIndex),args.ItemIndex);
            if(drop==null) return current?.screen==screen && current.layout!=null && current.toolbar!=null && ReferenceEquals(MyToolbarComponent.CurrentToolbar,current.target) && !current.toolbar.IsToolbarGrid(grid);
            // The catalog still owns the second press and can reclaim focus on release.
            current.pendingClick=drop;
            current.pendingPage=current.target.CurrentPage;
            return true;
        }
        internal static bool HandleInput(MyGuiScreenToolbarConfigBase screen)
        {
            var c=current;
            if(c==null || c.screen!=screen) return false;
            if(c.dropPage>=0 && c.dropPage!=c.target.CurrentPage)
            {
                c.pendingClick=null; c.clickMenu?.Deactivate(); c.clickMenu=null;
                c.Field<MyGuiControlContextMenu>("m_onDropContextMenu")?.Deactivate();
                c.dropSlot=c.dropPage=-1;
            }
            if(c.pendingClick==null && c.clickMenu==null) c.HoldToAssign(DateTime.UtcNow);
            else c.heldItem=-1;
            if(c.pendingClick!=null)
            {
                if(MyInput.Static.IsPrimaryButtonPressed()) return true;
                var drop=c.pendingClick; c.pendingClick=null;
                if(c.pendingPage!=c.target.CurrentPage || DoubleClickDrop(screen,drop.DragFrom.Grid,drop.Item,drop.DragFrom.ItemIndex)==null) return true;
                nativeDrop.Invoke(screen,new object[] {drop.DragFrom.Grid,drop});
                var menu=c.Field<MyGuiControlContextMenu>("m_onDropContextMenu");
                if(menu?.Enabled==true)
                {
                    menu.Enabled=false;
                    c.Field<MyGuiControlContextMenu>("m_contextMenu").Enabled=false;
                    c.toolbar.HideContextMenu();
                    menu.AllowKeyboardNavigation=true;
                    menu.Activate();
                    screen.FocusedControl=menu.GetInnerList();
                    c.clickMenu=menu;
                }
                return true;
            }
            if(c.clickMenu==null) return false;
            if(!c.clickMenu.Visible)
            {
                if(MyInput.Static.IsPrimaryButtonPressed()) return true;
                c.clickMenu=null;
                return false;
            }
            if(!ReferenceEquals(MyToolbarComponent.CurrentToolbar,c.target))
            { c.clickMenu.Deactivate(); c.clickMenu=null; return true; }
            // Route the overlay before the catalog, including outside-click and Escape cancellation.
            c.clickMenu.IsMouseOver=c.clickMenu.GetInnerList().CheckMouseOver(false);
            c.clickMenu.HandleInput();
            return true;
        }
        // Holding the trigger on one catalog item acts as a double click. Leaving the item first keeps the native drag.
        private void HoldToAssign(DateTime now)
        {
            var grid=Field<MyGuiControlGrid>("m_gridBlocks");
            if(grid==null || !MyInput.Static.IsPrimaryButtonPressed()) { heldItem=-1; return; }
            int over=grid.MouseOverIndex;
            if(MyInput.Static.IsNewPrimaryButtonPressed())
            {
                heldItem=grid.IsValidIndex(over) && grid.MouseOverItem?.Enabled==true ? over:-1;
                heldSince=now; return;
            }
            if(heldItem<0) return;
            if(over!=heldItem) { heldItem=-1; return; }
            if((now-heldSince).TotalSeconds<HoldSeconds) return;
            heldItem=-1;
            drag?.Stop();
            var args=new MyGuiControlGrid.EventArgs { ItemIndex=over,RowIndex=over/grid.ColumnsCount,ColumnIndex=over%grid.ColumnsCount,Button=MySharedButtonsEnum.Primary };
            nativeDoubleClick.Invoke(screen,new object[] {grid,args,false});
        }
        internal static bool HandleDrop(MyGuiScreenToolbarConfigBase screen,MyDragAndDropEventArgs args)
        {
            var c=current;
            if(c==null || c.screen!=screen) return false;
            bool target=args.DropTo!=null && c.toolbar.IsToolbarGrid(args.DropTo.Grid);
            int index=target ? c.target.SlotToIndex(args.DropTo.ItemIndex) : -1;
            c.dropSlot=c.Control(index); c.dropPage=target ? c.target.CurrentPage:-1;
            if(target && (c.dropSlot<0 || !ReferenceEquals(MyToolbarComponent.CurrentToolbar,c.target))) return true;
            if(c.sourceGrid==null) return false;
            if(args.DropTo?.Grid==c.sourceGrid) return true;
            if(args.DragFrom?.Grid!=c.sourceGrid) return false;
            if(target && index>=0 && args.Item.UserData is MyToolbarItem item && item.AllowedInToolbarType(MyToolbarType.ButtonPanel))
            {
                var data=item.GetObjectBuilder();
                var copy=data==null ? null:MyToolbarItemFactory.CreateToolbarItem(data);
                if(copy!=null) c.target.SetItemAtIndex(index,copy);
            }
            return true;
        }
        internal static bool FillActions(MyGuiScreenToolbarConfigBase screen,MyGuiControlContextMenu menu,MyToolbarItemActions item,out bool filled)
        {
            filled=false;
            var c=current;
            // Only the chooser that follows a drop onto a switch; the catalog's secondary-click menu has no target switch.
            if(c==null || c.screen!=screen || !c.switches || c.dropSlot<0 || item==null || menu!=c.Field<MyGuiControlContextMenu>("m_onDropContextMenu")) return false;
            int slot=c.dropSlot;
            filled=Fill(menu,item.PossibleActions(c.target.ToolbarType),id=>CockpitActions.Compatible(slot,item,id));
            return true;
        }
        internal static bool Assign(MyToolbarItem item,int slot,string action=null)
        {
            var c=current;
            if(c?.layout==null || !ReferenceEquals(MyToolbarComponent.CurrentToolbar,c.target)) return false;
            int index=c.target.SlotToIndex(slot),control=c.Control(index);
            if(slot<0 || slot>=c.target.SlotCount || control<0) return true;
            var actions=item as MyToolbarItemActions;
            string previous=actions?.ActionId;
            if(action!=null && actions!=null) actions.ActionId=action;
            Action<bool> commit=success=> {
                if(!success || current!=c || !ReferenceEquals(MyToolbarComponent.CurrentToolbar,c.target) || c.target.SlotToIndex(slot)!=index)
                { if(action!=null && actions!=null) actions.ActionId=previous; return; }
                c.target.SetItemAtIndex(index,item);
            };
            // Physical controls may intentionally share an action; native hotbar deduplication would clear another control.
            if(item==null) commit(true);
            else if(actions!=null && CockpitActions.Analog(control,actions))
            { Multiplayer.AnalogControl.DefaultParameters(item,actions.ActionId); commit(true); }
            else MyGuiScreenToolbarConfigBase.RequestItemParameters(item,commit);
            return true;
        }
        // Compatible actions first with the VR icon in place of the action icon; native order is kept within each group.
        internal static bool Fill(MyGuiControlContextMenu menu,ListReader<ITerminalAction> actions,Func<string,bool> compatible)
        {
            if(actions.Count==0) return false;
            menu.Enabled=true;
            menu.CreateNewContextMenu();
            foreach(var entry in actions.Select(a=>new { Action=a,Compatible=compatible(a.Id) }).OrderBy(a=>a.Compatible ? 0:1).ToList())
                menu.AddItem(entry.Action.Name,"",entry.Compatible ? MarkerIcon:entry.Action.Icon,entry.Action.Id);
            return true;
        }
        // A handle supplies the target value itself, so its numeric actions skip the native value dialogs.
        internal static bool SkipParameters(MyToolbarItem item)
        {
            var c=current;
            // Only the item chosen from the drop chooser, so dropSlot is the slot it is assigned to.
            if(c==null || !c.switches || c.dropSlot<0 || !ReferenceEquals(MyToolbarComponent.CurrentToolbar,c.target) ||
                !ReferenceEquals(item,c.Field<MyToolbarItem>("m_onDropContextMenuItem")) ||
                !(item is MyToolbarItemActions action) || !CockpitActions.Analog(c.dropSlot,action)) return false;
            Multiplayer.AnalogControl.DefaultParameters(item,action.ActionId);
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
