using System;
using System.Collections.Generic;
using Sandbox.Game.Gui;
using VRage.Game;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    // Build Info's marks over the native block info panel, moved onto the hand card from its top component row.
    internal static class CardAdditions
    {
        internal sealed class View
        {
            public NativeSprite[] Sprites;
            public MyDefinitionId Block;
            public int Components;
            public float Anchor;
            public double Aspect;
        }
        // Screen fraction of the panel's right edge, as Build Info places its marks.
        internal const float Right=.9894f;
        // Bottom of the top component row below the card's top, in 1080p GUI units: title .1, gap .006, list offset .04, one .037 row.
        internal const float TopRow=.183f;
        // Build Info's thin lines sit this far below the row gap they mark, over the icons; measured in 1080p GUI units.
        internal const float LineLift=.0094f;
        private const float LineHeight=.004f;
        [ThreadStatic] private static List<NativeSprite> capturing;
        private static volatile View current;
        private static volatile int revision;
        internal static bool Capturing => capturing!=null;
        internal static int Revision => revision;
        internal static void Begin() => capturing=new List<NativeSprite>();
        internal static void Add(MyBillboard billboard) { if(MenuHudLayer.ToScreen(billboard,out var sprite)) capturing.Add(sprite); }
        // anchor: screen fraction where Build Info puts the bottom of the top component row.
        internal static void End(float anchor)
        {
            var sprites=capturing; capturing=null;
            var info=MyHud.BlockInfo;
            if(sprites==null || sprites.Count==0 || info==null) { if(current!=null) { current=null; revision++; } return; }
            var view=new View { Sprites=sprites.ToArray(),Block=info.DefinitionId,Components=info.Components.Count,Anchor=anchor,Aspect=MenuHudLayer.Aspect };
            bool changed=!Same(current,view);
            current=view;
            if(changed) revision++;
        }
        private static bool Same(View a,View b)
        {
            if(a==null || a.Block!=b.Block || a.Components!=b.Components || a.Sprites.Length!=b.Sprites.Length) return false;
            for(int i=0;i<a.Sprites.Length;i++)
                if(a.Sprites[i].TopLeft!=b.Sprites[i].TopLeft || a.Sprites[i].BottomRight!=b.Sprites[i].BottomRight || a.Sprites[i].Tint!=b.Sprites[i].Tint) return false;
            return true;
        }
        internal static void Reset() { if(current!=null) { current=null; revision++; } }
#if SEVR_DIAGNOSTICS
        internal static int Count => current?.Sprites.Length ?? 0;
        internal static View Current => current;
#endif
        // Render thread, after the native card: only for the block and component list the game's panel shows.
        internal static void Draw(SharpDX.Direct3D11.Texture2D card,string key,int components)
        {
            var view=current;
            if(view==null || components!=view.Components || key==null || !key.StartsWith(view.Block+"|")) return;
            var size=card.Description;
            var sprites=Map(view,size.Width,size.Height);
            NativeSprites.Draw(card,sprites);
        }
        internal static NativeSprite[] Map(View view,int width,int height)
        {
            // Card pixels per 1080p GUI pixel; the card is 324 GUI pixels wide.
            double scale=width/(double)NativeBlockInfo.Pixels(0).X;
            double lift=0;
            Vector4 Card(Vector4 clip)
            {
                double x=width+((clip.X+1)/2-Right)*view.Aspect*1080*scale,y=((1-clip.Y)/2-view.Anchor+TopRow-lift)*1080*scale;
                return new Vector4((float)(x/width*2-1),(float)(1-y/height*2),0,1);
            }
            var result=new NativeSprite[view.Sprites.Length];
            for(int i=0;i<result.Length;i++)
            {
                var s=view.Sprites[i];
                lift=Math.Abs(s.TopLeft.Y-s.BottomLeft.Y)/2<LineHeight ? LineLift : 0;
                s.TopLeft=Card(s.TopLeft); s.TopRight=Card(s.TopRight); s.BottomLeft=Card(s.BottomLeft); s.BottomRight=Card(s.BottomRight);
                result[i]=s;
            }
            return result;
        }
    }
}
