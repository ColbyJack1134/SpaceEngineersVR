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
        private static int Count => Player.CockpitLayout.Count(Player.FighterProfile.Subtype);
        public override string GetFriendlyName() => "SEVR assignment preview";
        private static void Set(object instance,string field,object value) => AccessTools.Field(instance.GetType(),field).SetValue(instance,value);
        internal AssignmentPreview() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.95f,.70f))
        {
            var file=Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","Hud","Default.sbc");
            MyObjectBuilder_Definitions definitions;
            if(!MyObjectBuilderSerializer.DeserializeXML(file,out definitions)) throw new Exception("Native HUD definition unavailable");
            var style=definitions.Definitions.OfType<MyObjectBuilder_HudDefinition>().First().Toolbar;
            style.VisibleCondition=null;
            previousComponent=AccessTools.Field(typeof(MyToolbarComponent),"m_instance").GetValue(null);
            if(previousComponent==null) AccessTools.Field(typeof(MyToolbarComponent),"m_instance").SetValue(null,FormatterServices.GetUninitializedObject(typeof(MyToolbarComponent)));
            previous=MyToolbarComponent.CurrentToolbar;
            target=new MyToolbar(MyToolbarType.ButtonPanel,9,(Count+8)/9);
            source=new MyToolbar(MyToolbarType.Ship,9,3);
            MyToolbarComponent.CurrentToolbar=target;
            toolbar=new PreviewToolbar(style) { Position=new Vector2(.33f,.20f),OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM };
            Controls.Add(toolbar);
            var owner=(MyGuiScreenToolbarConfigBase)FormatterServices.GetUninitializedObject(typeof(MyGuiScreenToolbarConfigBase));
            Set(owner,"m_position",new Vector2(.5f));
            Set(owner,"m_controls",Controls); Set(owner,"m_toolbarControl",toolbar);
            var drag=new MyGuiControlGridDragAndDrop(MyGuiConstants.DRAG_AND_DROP_BACKGROUND_COLOR,MyGuiConstants.DRAG_AND_DROP_TEXT_COLOR,.7f,MyGuiConstants.DRAG_AND_DROP_TEXT_OFFSET,true);
            Set(owner,"m_dragAndDrop",drag); Controls.Add(drag);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.32f,.10f),text:"Switches") {Name="LabelToolbar"});
            Controls.Add(new MyGuiControlLabel(new Vector2(0,-.23f),text:"Cockpit assignment",originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
            assignment=new CockpitAssignment(owner,target,source,Count,10); assignment.Update();
            FillArtwork();
        }
        private void FillArtwork()
        {
            string[] art={"GridPowerOn","Dampeners","Handbrake","Light","ToggleConnectors","GridPowerOn","Dampeners","Light","Handbrake"};
            foreach(var grid in Controls.OfType<MyGuiControlGrid>().Concat(new[] {toolbar.ToolbarGrid}))
                for(int i=0;i<9;i++) if(grid!=toolbar.ToolbarGrid || target.CurrentPage*9+i<Count)
                    grid.SetItemAt(i,new MyGuiGridItem(Player.NativeSprites.Hud(art[i]),null,"Action",null));
        }
        internal void VerifyAndPage()
        {
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
        internal void Finish()
        {
            assignment.Dispose(); toolbar.OnRemoving(); MyToolbarComponent.CurrentToolbar=previous; AccessTools.Field(typeof(MyToolbarComponent),"m_instance").SetValue(null,previousComponent); CloseScreenNow();
        }
    }
}
