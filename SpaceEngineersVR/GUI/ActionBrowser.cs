using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class ActionBrowser : MyGuiScreenBase
    {
        private readonly List<MyGuiControlButton> rows=new List<MyGuiControlButton>();
        private MyGuiControlLabel pageLabel;
        private string query="";
        private int page;
        public override string GetFriendlyName() => "SEVR actions";
        public ActionBrowser() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.82f,.9f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); rows.Clear(); AddCaption("Actions");
            var search=new MyGuiControlSearchBox { Position=new Vector2(0,-.28f),Size=new Vector2(.61f,.045f) };
            search.OnTextChanged+=text=> { query=text; page=0; Refresh(); }; Controls.Add(search);
            pageLabel=new MyGuiControlLabel(new Vector2(-.07f,.28f)); Controls.Add(pageLabel);
            Controls.Add(new MyGuiControlButton(new Vector2(-.25f,.28f),size:new Vector2(.18f,.05f),text:new StringBuilder("Previous"),onButtonClick:b=> { page=Math.Max(0,page-1); Refresh(); }));
            Controls.Add(new MyGuiControlButton(new Vector2(.25f,.28f),size:new Vector2(.18f,.05f),text:new StringBuilder("Next"),onButtonClick:b=> { page++; Refresh(); }));
            Controls.Add(new MyGuiControlButton(new Vector2(0,.36f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
            Refresh();
        }
        private void Refresh()
        {
            foreach(var row in rows) Controls.Remove(row); rows.Clear();
            var choices=GameActions.Search(query);
            int pages=Math.Max(1,(choices.Length+6)/7); page=Math.Min(page,pages-1);
            pageLabel.Text=(page+1)+" / "+pages;
            for(int i=0;i<7 && page*7+i<choices.Length;i++)
            {
                var action=choices[page*7+i];
                var button=new MyGuiControlButton(new Vector2(0,-.19f+i*.065f),size:new Vector2(.61f,.055f),text:new StringBuilder(action.Label),onButtonClick:b=> {
                    CloseScreenNow(); GameActions.Schedule(action);
                });
                button.Enabled=action.Enabled; Controls.Add(button); rows.Add(button);
            }
        }
    }
}
