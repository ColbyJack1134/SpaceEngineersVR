#if SEVR_DIAGNOSTICS
using System;
using System.Linq;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class BuildInfoTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private static bool Near(double a,double b,double tolerance=1e-4) => Math.Abs(a-b)<=tolerance;
        // Text HUD API layout on its 0.1 m plane: a background quad and a glyph near its right edge, seen from an arbitrary camera.
        private static BuildInfoPanel.View Capture(MatrixD camera,double halfHeight)
        {
            using(var capture=BuildInfoPanel.Text.Begin(true,halfHeight))
            {
                void Quad(double left,double top,double width,double height,float tint)
                {
                    Vector3D Point(double x,double y) => Vector3D.Transform(new Vector3D(x,y,-.1),camera);
                    capture.Add(new NativeSprite(null,default(RectangleF),new Vector4(tint)),Point(left,top),Point(left+width,top),Point(left+width,top-height),Point(left,top-height));
                }
                Quad(.02,.03,.04,.06,.5f);
                Quad(.055,.02,.004,.006,1);
                return capture.Complete();
            }
        }
        public static void Run(Action<string> log)
        {
            double halfHeight=.1*Math.Tan(MathHelper.ToRadians(70)/2);
            var camera=MatrixD.CreateFromYawPitchRoll(.7,-.3,.2); camera.Translation=new Vector3D(1e9,-2e8,5e8);
            var view=Capture(camera,halfHeight);
            double scale=BuildInfoPanel.MetresPerScreenHeight/(2*halfHeight);
            Require(view!=null && Near(view.Width,.04*scale) && Near(view.Height,.06*scale),"Build Info panel size does not follow its screen share");
            var background=view.Sprites[0];
            Require(Near(background.TopLeft.X,-1) && Near(background.TopLeft.Y,1) && Near(background.BottomRight.X,1) && Near(background.BottomRight.Y,-1),
                "Build Info background does not fill the panel");
            var glyph=view.Sprites[1];
            Require(glyph.TopLeft.X>.7 && glyph.TopRight.X>glyph.TopLeft.X && glyph.TopLeft.Y>glyph.BottomLeft.Y && glyph.TopLeft.Y<1,"Build Info text mirrored or misplaced");
            var moved=MatrixD.CreateFromYawPitchRoll(-1.2,.4,-.6); moved.Translation=new Vector3D(-3,7,11);
            Require(Capture(moved,halfHeight).Hash==view.Hash,"Build Info panel content changes with the camera");
            using(var empty=BuildInfoPanel.Text.Begin(false,halfHeight)) Require(empty.Complete()==null && BuildInfoPanel.Text.Current==null,"Build Info panel kept while the card is hidden");

            var card=MatrixD.CreateFromYawPitchRoll(.3,0,0); card.Translation=new Vector3D(4,5,6);
            foreach(int side in new[] {-1,1})
            {
                var panel=BuildInfoPanel.Beside(card,.16f,.12f,.1f,.2f,side);
                var offset=panel.Translation-card.Translation;
                Require(Near(offset.Dot(card.Right),side*(.08+BuildInfoPanel.Gap+.05)) && Near(offset.Dot(card.Up)+.1,.06) && Near(offset.Dot(card.Backward),0) &&
                    panel.Forward.Equals(card.Forward,1e-9),"Build Info panel is not top-aligned beside the card");
            }
            var large=BuildInfoPanel.TextureSize(.2f,.4f); var small=BuildInfoPanel.TextureSize(.05f,.04f);
            Require(large.Y==BuildInfoPanel.MaxTexture && Math.Abs(large.X-BuildInfoPanel.MaxTexture/2)<=1 && Math.Abs(small.X-.05f*BuildInfoPanel.PixelsPerMetre)<=1,"Build Info panel texture size");

            Require(PlacementControls.ModStep(.8f,1.1f)==1.1f && Near(PlacementControls.ModStep(-.8f,1.1f),1/1.1) && PlacementControls.ModStep(.8f,1)==1 && PlacementControls.ModStep(float.NaN,1.1f)==1,
                "Build Info distance step direction");
            int mode=BuildInfoBridge.TextAlways; var cycle=new[] {mode};
            for(int i=0;i<3;i++) cycle=cycle.Concat(new[] {mode=BuildInfoBridge.NextText(mode)}).ToArray();
            Require(cycle.SequenceEqual(new[] {BuildInfoBridge.TextAlways,BuildInfoBridge.TextHints,BuildInfoBridge.TextOff,BuildInfoBridge.TextAlways}) &&
                BuildInfoBridge.NextText(BuildInfoBridge.TextOnPress)==BuildInfoBridge.TextAlways,"Build Info text mode cycle reaches the keyboard-only mode");
            string jump="\ue13f\ue13f\ue13f\ue13fc\ue13f\ue13f\ue13f\n\ue13f\n\ue13f\n   Ready";
            Require(BuildInfoBridge.Status(jump)=="Ready" && BuildInfoBridge.Status("5\ue100ON\n2\ue101OFF\n1\ue102DMG")=="5 ON · 2 OFF · 1 DMG" &&
                BuildInfoBridge.Status("\ue105NoAmmo")=="! NoAmmo" && BuildInfoBridge.Status("40")=="40" && BuildInfoBridge.Status(null)==null,
                "Build Info toolbar status not readable in the default font");
            double tan=Math.Tan(MathHelper.ToRadians(70)/2),aspect=16/9.0;
            Require(MenuHudLayer.Screen(new Vector3D(.1*tan*aspect,.1*tan,-.1),tan,aspect,out var edge) && Near(edge.X,1) && Near(edge.Y,1) &&
                MenuHudLayer.Screen(new Vector3D(0,0,-.1),tan,aspect,out var centre) && Near(centre.X,0) && Near(centre.Y,0) &&
                !MenuHudLayer.Screen(new Vector3D(0,0,.1),tan,aspect,out _),"Mod HUD does not map onto the menu image");
            var fitted=MenuHudLayer.Fit(new[] {new NativeSprite(null,default(RectangleF),Vector4.One) {TopLeft=new Vector4(-1,1,0,1),BottomRight=new Vector4(1,-1,0,1)}},.6,.2,.3,.5)[0];
            Require(Near(fitted.TopLeft.X,.2) && Near(fitted.TopLeft.Y,.6) && Near(fitted.BottomRight.X,.8) && Near(fitted.BottomRight.Y,-.4),"Toolbar list misplaced on the menu image");
            // Build Info's anchor is the bottom of the card's top component row at its right edge; one row down is one card row.
            var marks=new CardAdditions.View {Aspect=16/9.0,Anchor=.678f,Sprites=new[] {new NativeSprite(null,default(RectangleF),Vector4.One) {
                TopLeft=new Vector4((float)(CardAdditions.Right*2-1),(float)(1-.678*2),0,1),BottomRight=new Vector4((float)(CardAdditions.Right*2-1),(float)(1-(.678+37/1080.0)*2),0,1)}}};
            var mapped=CardAdditions.Map(marks,1296,1000)[0];
            double unit=1296.0/NativeBlockInfo.Pixels(0).X;
            Require(Near(mapped.TopLeft.X,1) && Near((1-mapped.TopLeft.Y)/2*1000,CardAdditions.TopRow*1080*unit,.01) && Near((1-mapped.BottomRight.Y)/2*1000,(CardAdditions.TopRow*1080+37)*unit,.01),
                "Build Info panel marks misplaced on the card");
            log("PASS Build Info: panel size and layout from Text HUD API's plane at any camera pose, top-aligned beside the card on either hand, texture limits, distance step direction, text mode cycle without the keyboard-only mode, toolbar status icons and lines in the default font, mod HUD on the menu image, panel marks anchored on the card.");
        }
    }
}
#endif
