using System;
using System.Diagnostics;
using System.Drawing;
using SpaceEngineersVR.Plugin;
using VRageMath;
using Color=System.Drawing.Color;

namespace SpaceEngineersVR.Player
{
    internal static class PerformanceHud
    {
        private sealed class Notice
        {
            internal readonly string Text;
            internal readonly DateTime Until;
            internal Notice(string text,double seconds) { Text=text; Until=DateTime.UtcNow.AddSeconds(seconds); }
        }
        private static volatile Notice notice;
        internal static volatile bool Enabled;
        private static OverlayCanvas canvas;
        private static bool failed;
        private static long nextDraw;
        private static readonly Font title=new Font("Segoe UI",26,FontStyle.Bold,GraphicsUnit.Pixel);
        private static readonly Font text=new Font("Segoe UI",20,FontStyle.Regular,GraphicsUnit.Pixel);
        private static readonly Font small=new Font("Segoe UI",17,FontStyle.Regular,GraphicsUnit.Pixel);
        private static readonly string[] names={"World touch","Nearby query","HUD state","HUD paint","Visor","Cockpit","Cockpit touch","Switch update","Arms","Geometry setup","Actor setup","Switch action"};
        internal static void Toggle() => Enabled=!Enabled;
        internal static void Hide() => canvas?.Hide();
        internal static void Notify(string text,double seconds=4)
        {
            notice=new Notice(text,seconds); nextDraw=0;
            Logger.Info(text);
        }
        internal static void PaintStatus(OverlayCanvas target,string status)
        {
            target.Clear(Color.Transparent);
            var g=target.Graphics;
            using(var background=new SolidBrush(Color.FromArgb(225,12,20,27))) g.FillRectangle(background,0,205,640,130);
            g.DrawString("Developer",title,Brushes.LightCyan,20,216);
            g.DrawString(status,text,Brushes.LightCyan,new System.Drawing.RectangleF(22,260,596,64));
        }
        internal static void Paint(OverlayCanvas target,RenderPerformance.View current,FeatureTiming.Measurement[] features,long now,string status=null)
        {
            target.Clear(Color.FromArgb(225,12,20,27));
            var g=target.Graphics;
            g.DrawString(current==null ? "Performance · collecting" : $"{current.Fps:F1} FPS   {current.FrameMs:F1} ms",title,Brushes.LightCyan,20,14);
            g.DrawString("Application frames · 1 second · Headset "+(current==null ? "--" : Value(current.RefreshHz))+" Hz",small,Brushes.LightSteelBlue,22,53);
            string gpu=current==null ? "GPU app --   SteamVR total --" : $"GPU app {Value(current.AppGpuMs)} ms   SteamVR total {Value(current.TotalGpuMs)} ms";
            g.DrawString(gpu,text,Brushes.LightCyan,20,82);
            g.DrawString(current==null ? "Repeat presents --   Dropped --" : $"Repeat presents/frame {Value(current.Repeated)}   Dropped {current.Dropped}",small,Brushes.LightSteelBlue,22,114);
            g.DrawString("Plugin scopes (elapsed ms/call)",small,Brushes.LightCyan,22,154);
            g.DrawString("Mean         p95         Peak",small,Brushes.LightCyan,341,154);
            for(int i=0;i<names.Length;i++)
            {
                var value=features[i]; int y=183+i*25;
                var brush=value!=null && now-value.Time<15*Stopwatch.Frequency ? Brushes.LightCyan : Brushes.SlateGray;
                g.DrawString(names[i],small,brush,22,y);
                g.DrawString(value==null ? "--" : value.Mean.ToString("F3"),small,brush,341,y);
                g.DrawString(value==null ? "--" : value.P95.ToString("F3"),small,brush,428,y);
                g.DrawString(value==null ? "--" : value.Peak.ToString("F3"),small,brush,515,y);
            }
            g.DrawString(status ?? "Up to 600 calls · scopes overlap · gray = stale/unavailable",small,Brushes.LightSteelBlue,22,497);
        }
        private static string Value(double value) => double.IsNaN(value) ? "--" : value.ToString("F1");
        internal static void Draw()
        {
            if(!Main.WorldAvailable) notice=null;
            var currentNotice=notice;
            string status=currentNotice!=null && DateTime.UtcNow<currentNotice.Until ? currentNotice.Text : null;
            if((!Enabled && status==null) || failed || !Main.WorldAvailable || Main.MenuOpen || InputRouter.RadialOpen)
            { canvas?.Hide(); nextDraw=0; return; }
            long now=Stopwatch.GetTimestamp();
            if(now<nextDraw) return;
            try
            {
                if(canvas==null)
                {
                    canvas=new OverlayCanvas("Developer performance",640,540,.58f);
                    canvas.Position(Matrix.CreateTranslation(.40f,.22f,-1.2f),true);
                }
                if(Enabled)
                {
                    var features=new FeatureTiming.Measurement[names.Length];
                    for(int i=0;i<features.Length;i++) features[i]=FeatureTiming.Latest((FeatureTiming.Area)i);
                    Paint(canvas,RenderPerformance.Current,features,now,status);
                }
                else PaintStatus(canvas,status);
                canvas.Upload();
                nextDraw=now+Stopwatch.Frequency/2;
            }
            catch(Exception ex) { failed=true; canvas?.Hide(); Logger.Warning(ex,"Performance display unavailable"); }
        }
    }
}
