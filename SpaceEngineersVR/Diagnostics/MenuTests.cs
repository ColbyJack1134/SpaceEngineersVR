using System;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class MenuTests
    {
        private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Set(object target,string field,object value) => AccessTools.Field(target.GetType(),field).SetValue(target,value);
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        public static void Run(Action<string> log)
        {
            // Runs inside the initialized game UI; only the world-dependent screen is a fixture.
            string tabletQuery=null;
            var tabletTarget=MenuKeyboard.CreateTextTarget("damp",text=>tabletQuery=text);
            tabletTarget.InsertChar(true,'s');
            Require(tabletQuery=="damps" && tabletTarget.Text==tabletQuery,"Standalone keyboard did not append at the query end");
            tabletTarget.KeypressBackspace(true);
            Require(tabletQuery=="damp","Standalone tablet keyboard backspace failed");
            foreach(var route in new[] {new[] {"Flight","Controller flight"},new[] {"Rendering","Reset resolution"},new[] {"Controls","Active bindings"}})
            {
                var destination=GUI.MyPluginConfigDialog.CreatePage(route[0]);destination.RecreateControls(true);
                Require(destination.Controls.OfType<MyGuiControlButton>().Any(b=>b.Text.ToString()==route[1]),"Settings search opens the wrong category: "+route[0]);
            }
            var menuScreens=Enumerable.Range(0,5).Select(i=>(MyGuiScreenBase)new GUI.MyPluginConfigDialog(i))
                .Concat(Enumerable.Range(0,10).Select(i=>(MyGuiScreenBase)new GUI.BindingHelp(i)))
                .Concat(Enumerable.Range(0,2).Select(i=>(MyGuiScreenBase)new GUI.FirstRunSetup(i)))
                .Concat(new MyGuiScreenBase[] {new GUI.HudStateOptions(),new GUI.BindingOptions()});
            foreach(var menu in menuScreens)
            {
                menu.RecreateControls(true);
                var buttons=menu.Controls.OfType<MyGuiControlButton>().ToArray();
                for(int i=0;i<buttons.Length;i++) for(int j=i+1;j<buttons.Length;j++)
                {
                    var a=buttons[i];var b=buttons[j];var gap=new VRageMath.Vector2(Math.Abs(a.Position.X-b.Position.X),Math.Abs(a.Position.Y-b.Position.Y));var extent=(a.Size+b.Size)/2;
                    Require(gap.X>=extent.X-.001f || gap.Y>=extent.Y-.001f,"Native menu buttons overlap: "+menu.GetFriendlyName()+" / "+a.Text+" / "+b.Text);
                }
            }
            log("PASS native menu layouts: settings routes, ten guide topics, setup without VR, HUD editor and binding pages have non-overlapping buttons.");
            var catalog=ActionCatalog.NativeEntries();
            Require(catalog.Length>=30 && catalog.All(a=>!string.IsNullOrWhiteSpace(a.Label)),"Native action catalog lost system actions");
            log("PASS tablet search: shared native text editing and "+catalog.Length+" native system actions without execution.");
            var screen=Bare<MyGuiScreenToolbarConfigBase>();
            var search=new MyGuiControlSearchBox();
            var textbox=search.TextBox;
            search.Name="SearchFixture";
            var controls=new MyGuiControls(screen); controls.Add(search); Set(screen,"m_controls",controls);
            Require(MenuKeyboard.TextTarget(screen)==textbox,"G-menu keyboard cannot find search after grid focus");
            Set(screen,"m_focusedControl",search);
            Require(MenuKeyboard.TextTarget(screen)==textbox,"Native search wrapper hides its textbox");
            Set(screen,"m_focusedControl",textbox);
            Require(MenuKeyboard.TextTarget(screen)==textbox,"Ordinary textbox focus changed");
            string changed=null;
            search.OnTextChanged+=text=>changed=text;
            MenuKeyboard.TextTarget(screen).Text="reactor";
            Require(changed=="reactor" && search.SearchText=="reactor","VR search edit did not notify the native search control");
            textbox.SelectAll(); textbox.InsertChar(true,'b'); textbox.InsertChar(true,'a');
            Require(changed=="ba" && textbox.Text=="ba","Spatial typing lost native selection/live search semantics");
            textbox.KeypressBackspace(true);
            Require(changed=="b" && textbox.CarriagePositionIndex==1,"Spatial backspace lost native caret");
            textbox.Enabled=false;
            Require(MenuKeyboard.TextTarget(screen)==null,"Disabled search can be edited");
            textbox.Enabled=true; search.Visible=false; Set(screen,"m_focusedControl",null);
            Require(MenuKeyboard.TextTarget(screen)==null,"Hidden search is auto-selected");
            var other=Bare<MyGuiScreenMessageBox>();
            var blueprint=Bare<MyGuiBlueprintScreen_Reworked>();
            var blueprintSearch=new MyGuiControlSearchBox();
            var blueprintControls=new MyGuiControls(blueprint); blueprintControls.Add(blueprintSearch); Set(blueprint,"m_controls",blueprintControls);
            Require(MenuKeyboard.TextTarget(blueprint)==blueprintSearch.TextBox,"Blueprint search unavailable after list selection");
            string blueprintQuery=null; blueprintSearch.OnTextChanged+=text=>blueprintQuery=text;
            MenuKeyboard.TextTarget(blueprint).InsertChar(true,'a');
            Require(blueprintQuery=="a","Blueprint search event lost");
            Require(MenuKeyboard.TextTarget(other)==null,"Non-toolbar menus acquired unsolicited text focus");
            var actions=new GUI.ActionBrowser(); actions.RecreateControls(true);
            var actionText=MenuKeyboard.TextTarget(actions);
            Require(actionText!=null,"Actions keyboard cannot find search");
            actionText.Text="auto dampeners";
            Require(actions.Controls.OfType<MyGuiControlButton>().Any(b=>b.Text=="Auto dampeners"),"Actions search lost matching entry");
            actionText.Text="landing gear";
            Require(actions.Controls.OfType<MyGuiControlButton>().Count(b=>b.Text=="Landing gear / park")==1,"Actions browser duplicates parking");
            actionText.Text="no matching action fixture";
            Require(!actions.Controls.OfType<MyGuiControlButton>().Any(b=>b.Text=="Auto dampeners"),"Actions search retained stale entries");
            actions.CloseScreenNow();
            var panel=Bare<MyGuiScreenTextPanel>();
            var editor=new MyGuiControlMultilineEditableText(contents:new System.Text.StringBuilder("abc"));
            Set(panel,"m_focusedControl",editor);
            Require(MenuKeyboard.EditTarget(panel)==editor,"LCD prompt did not expose its multiline editor");
            AccessTools.Property(typeof(MyGuiControlMultilineText),"CarriagePositionIndex").SetValue(editor,1);
            string edited=null; editor.TextChanged+=box=>edited=box.Text.ToString();
            MenuKeyboard.Insert(editor,'x');
            Require(edited=="axbc" && MenuKeyboard.TextPreview(editor)=="ax|bc","Multiline typing lost native caret or text event");
            MenuKeyboard.Backspace(editor);
            Require(edited=="abc" && MenuKeyboard.TextPreview(editor)=="a|bc","Multiline backspace lost native caret");
            MenuKeyboard.Insert(editor,'\n');
            Require(edited=="a\nbc" && MenuKeyboard.TextPreview(editor)=="|bc","Multiline Enter or current-line preview failed");
            object selection=AccessTools.Field(typeof(MyGuiControlMultilineText),"m_selection").GetValue(editor);
            AccessTools.Method(selection.GetType(),"SelectAll").Invoke(selection,new object[] {editor});
            MenuKeyboard.Insert(editor,'q');
            Require(edited=="q","Multiline typing ignored native text selection");
            editor.Enabled=false; Require(MenuKeyboard.EditTarget(panel)==null,"Disabled multiline editor accepted typing");
            editor.Enabled=true; editor.Visible=false; Require(MenuKeyboard.EditTarget(panel)==null,"Hidden multiline editor accepted typing");
            editor.Visible=true; Set(editor,"m_selectable",false); Require(MenuKeyboard.EditTarget(panel)==null,"Non-editable multiline editor accepted typing");
            Set(panel,"m_focusedControl",new MyGuiControlMultilineText());
            Require(MenuKeyboard.EditTarget(panel)==null,"Read-only LCD text opened typing");
            Require(MenuKeyboard.MakeKeys(false,multiline:true).Count(k=>k.Label=="ENTER")==1 &&
                !MenuKeyboard.MakeKeys(false).Any(k=>k.Label=="ENTER"),"Multiline keyboard Enter affected ordinary search keyboards");
            log("PASS native LCD typing: multiline focus, caret/selection, live text events, Enter/backspace, line preview and disabled/hidden/read-only guards.");
            var confirm=new InputGate();
            confirm.Update(true,false); confirm.Update(true,true); confirm.Block(); confirm.Update(true,true);
            Require(!confirm.Pressed && !confirm.Held,"G-menu opening press leaked to Enter");
            confirm.Update(true,false); confirm.Update(true,true);
            Require(confirm.Pressed,"Fresh menu confirm failed to rearm");
            log("PASS G-menu input: native search wrapper/fallback and text-change event, disabled/hidden target rejection, held opener suppression and fresh menu confirm");
        }
    }
}
