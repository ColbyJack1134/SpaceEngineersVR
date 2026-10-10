using System;
using System.IO;
using System.Linq;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class BindingHelp : MyGuiScreenBase
    {
        private int page;
        private MyGuiControlScrollablePanel scroll;
        private sealed class Diagram
        {
            internal readonly string Name;
            internal readonly int Width, Height;
            internal Diagram(string name, int width, int height) { Name=name; Width=width; Height=height; }
        }
        private sealed class Topic
        {
            internal readonly string Title;
            internal readonly Diagram[] Diagrams;
            internal Topic(string title, params Diagram[] diagrams) { Title=title; Diagrams=diagrams; }
        }
        private static readonly Topic[] topics={
            new Topic("Common Controls", new Diagram("start", 1600, 610), new Diagram("helmet", 1600, 520), new Diagram("radials", 1600, 1000), new Diagram("wrist-intro", 1600, 650)),
            new Topic("On Foot & Tools", new Diagram("foot", 1600, 650), new Diagram("tools", 1600, 650)),
            new Topic("Jetpack", new Diagram("jetpack", 1600, 650)),
            new Topic("Flight & Driving", new Diagram("cockpit", 1600, 650), new Diagram("vehicles", 1600, 650)),
            new Topic("Cockpit Interaction", new Diagram("cockpit-controls", 1600, 755), new Diagram("cockpit-sticks", 1600, 1170)),
            new Topic("Building", new Diagram("building", 1600, 650), new Diagram("building-rotation", 1600, 650)),
            new Topic("3rd Person", new Diagram("view", 1600, 760)),
            new Topic("Floating Menus", new Diagram("menus", 1600, 650)),
            new Topic("Other", new Diagram("seat-panel", 1600, 760), new Diagram("other", 1600, 809), new Diagram("other-end", 1600, 160)),
        };
        internal static int TopicCount => topics.Length;
        private static string GuideFolder => Path.Combine(Common.AssetFolder ?? Util.Util.GetDefaultAssetFolder(),"Guide");
        internal static string[] ImagePaths => topics.SelectMany(t=>t.Diagrams)
            .Select(d=>Path.Combine(GuideFolder,d.Name+".dds")).ToArray();
        public BindingHelp(int topic=0) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(1.03f,.91f))
        { page=Math.Max(0,Math.Min(topics.Length-1,topic)); m_closeOnEsc=true; CloseButtonEnabled=true; }
        public override string GetFriendlyName()=>"SEVR controls guide";
        internal void SelectTopic(int topic) { page=Math.Max(0,Math.Min(topics.Length-1,topic)); RecreateControls(false); }
        internal void ScrollTo(float fraction)
        {
            scroll.ScrollbarVPosition=MathHelper.Clamp(fraction,0,1)*Math.Max(0,scroll.ScrolledControl.Size.Y-scroll.ScrolledAreaSize.Y);
        }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("Controls guide");
            for(int i=0;i<topics.Length;i++)
            {
                int index=i;
                var button=MenuLayout.Button(this,-.4f+i%5*.2f,-.30f+i/5*.06f,.19f,topics[i].Title,()=>SelectTopic(index),.05f);
                button.TextScale=.65f; button.Checked=i==page;
            }
            const float width=.94f;
            // Native GUI coordinates use a 4:3 reference canvas.
            float gap=width*4f/3f*30f/1600f;
            float height=topics[page].Diagrams.Sum(d=>width*4f/3f*d.Height/d.Width)+gap*(topics[page].Diagrams.Length-1);
            var content=new MyGuiControlParent(size:new Vector2(width,height));
            float y=-height/2;
            foreach(var diagram in topics[page].Diagrams)
            {
                float h=width*4f/3f*diagram.Height/diagram.Width;
                content.Controls.Add(new MyGuiControlImage(new Vector2(-width/2,y),new Vector2(width,h),
                    textures:new[] {Path.Combine(GuideFolder,diagram.Name+".dds")},
                    originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP));
                y+=h+gap;
            }
            scroll=new MyGuiControlScrollablePanel(content) {
                Position=new Vector2(0,.108f), ScrollbarVEnabled=true, Size=new Vector2(.98f,.61f)
            };
            scroll.RefreshInternals();
            Controls.Add(scroll); FocusedControl=scroll;

        }
    }
}
