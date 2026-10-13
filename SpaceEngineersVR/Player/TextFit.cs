using System.Collections.Generic;
using System.Drawing;

namespace SpaceEngineersVR.Player
{
    internal static class TextFit
    {
        private static readonly Dictionary<(string,FontStyle,float),Font> fonts=new Dictionary<(string,FontStyle,float),Font>();
        private static readonly StringFormat wrapping=new StringFormat {Alignment=StringAlignment.Center};

        // Largest size from font.Size down to minSize that wraps text into maxLines lines within width without breaking a word.
        internal static bool TryFont(Graphics g,string text,Font font,float width,int maxLines,float minSize,out Font fitted)
        {
            for(float size=font.Size;size>=minSize;size--)
            {
                fitted=Sized(font,size);
                if(Fits(g,text,fitted,width,maxLines)) return true;
            }
            fitted=Sized(font,minSize);
            return false;
        }
        internal static Font Font(Graphics g,string text,Font font,float width,int maxLines,float minSize)
        {
            TryFont(g,text,font,width,maxLines,minSize,out var fitted);
            return fitted;
        }

        private static bool Fits(Graphics g,string text,Font font,float width,int maxLines)
        {
            if(string.IsNullOrEmpty(text)) return true;
            foreach(string word in text.Split(' ')) if(g.MeasureString(word,font).Width>width) return false;
            g.MeasureString(text,font,new SizeF(width,10000),wrapping,out _,out int lines);
            return lines<=maxLines;
        }

        internal static Font Sized(Font font,float size)
        {
            var key=(font.FontFamily.Name,font.Style,size);
            if(!fonts.TryGetValue(key,out var sized)) fonts[key]=sized=new Font(font.FontFamily,size,font.Style,font.Unit);
            return sized;
        }
    }
}
