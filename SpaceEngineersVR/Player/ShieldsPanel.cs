using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Player
{
    // Defense Shields for VR: a wrist "Shields" page (status, charge, heat, modulation, a per-face diagram) and warnings for shield down,
    // low and overheating. Replaces the mod's screen-corner HUD icon, which is hidden in the headset.
    // Inactive unless the Defense Shields API answered.
    internal static class ShieldsPanel
    {
        private static DateTime next;

        // Per-face state for the diagram, as Defense Shields' own HUD shows it: health, offline, shunted.
        internal sealed class View
        {
            public float[] Health=new float[6];
            public bool[] Online=new bool[6],Shunted=new bool[6];
            public bool Known,Shunting;
            public string Key;
        }
        private static View view=new View {Key=""};
        private static DateTime viewTime;

        internal static View Current
        {
            get
            {
                var now=DateTime.UtcNow;
                if(now<viewTime) return view;
                viewTime=now.AddSeconds(.2);
                var entity=ControlledGrid;
                var next=new View();
                next.Known=ShieldsBridge.TryFaces(entity,out var health,out var online);
                if(next.Known) { next.Health=health; next.Online=online; }
                var shunting=ShieldsBridge.Shunting(entity);
                next.Shunting=shunting.HasValue;
                for(int i=0;i<6;i++) next.Shunted[i]=shunting.HasValue && ShieldsBridge.FaceShunted(shunting.Value,i);
                var key=new System.Text.StringBuilder(next.Known ? "k":"u");
                for(int i=0;i<6;i++) key.Append('|').Append(Math.Round(next.Health[i]*50)).Append(next.Online[i] ? 'o':'x').Append(next.Shunted[i] ? 's':'-');
                next.Key=key.ToString();
                return view=next;
            }
        }
        private static bool wasOnline,low,hot;
        private static long grid;

        internal static MyEntity ControlledGrid => (MySession.Static?.ControlledEntity?.Entity as MyCubeBlock)?.CubeGrid;
        internal static bool Available => ShieldsBridge.Ready && ShieldsBridge.Of(ControlledGrid).Exists;

        internal static void Update()
        {
            if(Main.WorldAvailable) ShieldsBridge.Load();
            if(!ShieldsBridge.Ready) return;
            var now=DateTime.UtcNow;
            if(now<next) return;
            next=now.AddSeconds(.25);
            try
            {
                var entity=ControlledGrid;
                var info=ShieldsBridge.Of(entity);
                long id=entity?.EntityId ?? 0;
                // A new seat or grid starts quiet; warnings fire on changes, not on what was already true.
                if(id!=grid || !info.Exists) { grid=id; wasOnline=info.Online; low=info.Online && info.Percent<25; hot=info.Heat>=90; return; }
                if(wasOnline && !info.Online) Warn("Shield down",.7f);
                else if(info.Online && !low && info.Percent<25) Warn("Shield low  "+Math.Round(info.Percent)+"%",.45f);
                if(info.Online && !hot && info.Heat>=90) Warn("Shield overheating",.45f);
                wasOnline=info.Online;
                if(info.Percent<25) low=true; else if(info.Percent>35) low=false;
                if(info.Heat>=90) hot=true; else if(info.Heat<70) hot=false;
            }
            catch(Exception ex) { Logger.Warning(ex,"Defense Shields read failed"); next=now.AddSeconds(5); }
        }

        private static void Warn(string text,float amplitude)
        {
            EssentialHud.Notify(text);
            if(Common.Config?.WarningPulse==false) return;
            if(Player.HandL.pose.isTracked) Player.HandL.Vibrate(0,.1f,140,amplitude);
            if(Player.HandR.pose.isTracked) Player.HandR.Vibrate(0,.1f,140,amplitude);
        }

        internal static void Reset() { grid=0; wasOnline=low=hot=false; next=DateTime.MinValue; }

        internal static void Keys(List<SurfaceKey> keys)
        {
            var entity=ControlledGrid;
            var info=ShieldsBridge.Of(entity);
            if(!info.Exists)
            {
                keys.Add(new SurfaceKey("No shield on this grid",.025f,.24f,.48f,.08f) {Enabled=false});
            }
            else
            {
                string status=ShieldsBridge.Status(entity);
                keys.Add(new SurfaceKey("Status",.025f,.24f,.48f,.075f) {Enabled=false,Value=string.IsNullOrEmpty(status) ? (info.Online ? "Online":"Offline") : status.Trim('[',']')});
                keys.Add(new SurfaceKey("Charge",.025f,.33f,.48f,.082f)
                    {Enabled=false,Value=info.Online ? Math.Round(info.Percent)+"%   "+Hp(info.Charge)+" / "+Hp(info.MaxCharge) : "—",Slider=info.Online ? Math.Max(0,Math.Min(1,info.Percent/100)) : 0});
                keys.Add(new SurfaceKey("Heat",.025f,.425f,.48f,.082f)
                    {Enabled=false,Value=info.Heat+"%"+(info.Heat>=90 ? "   critical":""),Slider=Math.Max(0,Math.Min(1,info.Heat/100f))});
                var modulation=ShieldsBridge.Modulation(entity);
                if(modulation.HasValue)
                {
                    var m=modulation.Value;
                    string extras=(m.Item1 ? "   reinforce":"")+(m.Item2 ? "   EMP":"");
                    keys.Add(new SurfaceKey("Kinetic",.025f,.52f,.48f,.07f) {Enabled=false,Value=Math.Round(m.Item3*100)+"%"});
                    keys.Add(new SurfaceKey("Energy",.025f,.60f,.48f,.07f) {Enabled=false,Value=Math.Round(m.Item4*100)+"%"});
                    if(extras.Length>0) keys.Add(new SurfaceKey("Enhancers",.025f,.68f,.48f,.07f) {Enabled=false,Value=extras.Trim()});
                }
            }
            keys.Add(new SurfaceKey("Back",.025f,.91f,.22f,.07f) {Action=new ActionChoice("Back",()=>WristPanel.Show(0))});
        }

        // Heading, the per-face diagram on the right half, then the keys. Seen from above:
        // the Up face is the central ellipse, Front / Right / Back / Left are arcs around it (Front at the top),
        // and the Down face is a smaller ellipse below.
        internal static void Paint(OverlayCanvas target,SurfaceView panel)
        {
            target.Clear(Color.FromArgb(255,12,20,28));
            var g=target.Graphics;
            var faces=panel.ShieldsView;
            using(var heading=new Font("Segoe UI",27,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var small=new Font("Segoe UI",17,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var value=new Font("Segoe UI",22,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var muted=new SolidBrush(Color.FromArgb(157,184,199)))
            {
                g.DrawString("SHIELDS",heading,Brushes.White,26,95);
                if(faces!=null && (faces.Known || faces.Shunting))
                {
                    var mode=g.SmoothingMode; g.SmoothingMode=SmoothingMode.AntiAlias;
                    try
                    {
                        g.DrawString("FRONT ▲",small,muted,722,98);
                        const float cx=772,cy=300,outerX=210,outerY=162,ring=64,gap=6;
                        // Arcs: GDI angles start at 3 o'clock and run clockwise. Face index: 0 Front, 1 Back, 2 Left, 3 Right.
                        var arcs=new[] {new {Face=0,Centre=270f},new {Face=3,Centre=0f},new {Face=1,Centre=90f},new {Face=2,Centre=180f}};
                        foreach(var arc in arcs)
                        {
                            using(var path=Sector(cx,cy,outerX,outerY,ring,arc.Centre-45+gap/2,90-gap))
                                DrawFace(g,path,faces,arc.Face);
                            double a=arc.Centre*Math.PI/180;
                            float lx=cx+(float)Math.Cos(a)*(outerX-ring/2),ly=cy+(float)Math.Sin(a)*(outerY-ring/2);
                            Label(g,faces,arc.Face,lx,ly,small,value,arc.Face<=1 ? 0:1);
                        }
                        using(var up=new GraphicsPath())
                        {
                            up.AddEllipse(cx-(outerX-ring-12),cy-(outerY-ring-12),2*(outerX-ring-12),2*(outerY-ring-12));
                            DrawFace(g,up,faces,4);
                        }
                        Label(g,faces,4,cx,cy,small,value,2);
                        using(var down=new GraphicsPath())
                        {
                            down.AddEllipse(cx-95,cy+outerY+28,190,62);
                            DrawFace(g,down,faces,5);
                        }
                        Label(g,faces,5,cx,cy+outerY+59,small,value,0);
                    }
                    finally { g.SmoothingMode=mode; }
                    g.DrawString("green → red: health   grey: offline",small,muted,600,560);
                    g.DrawString("purple outline: shunted",small,muted,600,584);
                }
            }
            PhysicalSurface.PaintWristKeys(target,panel);
        }

        private static Color Fill(View faces,int face) =>
            !faces.Known ? Color.FromArgb(31,47,60) : !faces.Online[face] ? Color.FromArgb(90,98,106) : Health(faces.Health[face]);

        private static void DrawFace(Graphics g,GraphicsPath path,View faces,int face)
        {
            using(var brush=new SolidBrush(Fill(faces,face))) g.FillPath(brush,path);
            using(var edge=new Pen(Color.FromArgb(12,20,28),3)) g.DrawPath(edge,path);
            if(faces.Shunted[face]) using(var pen=new Pen(Color.FromArgb(170,70,230),7) {LineJoin=LineJoin.Round}) g.DrawPath(pen,path);
        }

        // layout: 0 one line (front/back arcs, down), 1 two small lines (narrow side arcs), 2 name over large value (up).
        private static void Label(Graphics g,View faces,int face,float x,float y,Font small,Font value,int layout)
        {
            var fill=Fill(faces,face);
            using(var text=new SolidBrush(fill.GetBrightness()>.6f ? Color.FromArgb(12,20,28):Color.White))
            using(var centered=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            {
                string state=!faces.Known ? "—" : !faces.Online[face] ? "OFF" : faces.Health[face]<0 ? "ON" : Math.Round(faces.Health[face]*100)+"%";
                string name=ShieldsBridge.FaceNames[face];
                if(layout==0) { g.DrawString(name+" "+state,small,text,new RectangleF(x-80,y-12,160,24),centered); return; }
                if(layout==1)
                {
                    g.DrawString(name,small,text,new RectangleF(x-40,y-22,80,22),centered);
                    g.DrawString(state,small,text,new RectangleF(x-40,y,80,22),centered);
                    return;
                }
                g.DrawString(name,small,text,new RectangleF(x-80,y-26,160,24),centered);
                g.DrawString(state,value,text,new RectangleF(x-80,y-2,160,30),centered);
            }
        }

        // An elliptical ring segment: outer ellipse radii (rx, ry), ring thickness, start angle and sweep in degrees.
        private static GraphicsPath Sector(float cx,float cy,float rx,float ry,float ring,float start,float sweep)
        {
            var path=new GraphicsPath();
            path.AddArc(cx-rx,cy-ry,2*rx,2*ry,start,sweep);
            path.AddArc(cx-(rx-ring),cy-(ry-ring),2*(rx-ring),2*(ry-ring),start+sweep,-sweep);
            path.CloseFigure();
            return path;
        }

        // Health ramp: green when full, through amber, to red when empty. Teal when this client cannot read health.
        private static Color Health(float ratio)
        {
            if(ratio<0) return Color.FromArgb(40,133,151);
            ratio=Math.Max(0,Math.Min(1,ratio));
            return ratio>=.5f ? Lerp(Color.FromArgb(225,170,40),Color.FromArgb(55,170,80),(ratio-.5f)*2) : Lerp(Color.FromArgb(200,50,45),Color.FromArgb(225,170,40),ratio*2);
        }
        private static Color Lerp(Color a,Color b,float t) =>
            Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));

        private static string Hp(float value) => value>=1e6f ? Math.Round(value/1e6f,1)+"M" : value>=1e3f ? Math.Round(value/1e3f,1)+"k" : Math.Round(value).ToString();
    }
}
