using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;
using Color = System.Drawing.Color;

namespace SpaceEngineersVR.Player
{
    internal static class WorldMarkers
    {
        private sealed class Marker
        {
            public Vector3D Position;
            public Vector4 Color;
            public string Name, Icon;
            public bool Detail;
            public double Distance;
        }
        private sealed class View { public Marker[] Markers; public DateTime Time; }
        private static readonly FieldInfo points = AccessTools.Field(typeof(MyHudMarkerRender), "m_pointsOfInterest");
        private static readonly Type point = AccessTools.Inner(typeof(MyHudMarkerRender), "PointOfInterest");
        private static readonly PropertyInfo position = AccessTools.Property(point,"WorldPosition"),
            name = AccessTools.Property(point,"Text"), kind = AccessTools.Property(point,"POIType"),
            relationship = AccessTools.Property(point,"Relationship"), always = AccessTools.Property(point,"AlwaysVisible");
        private static readonly FieldInfo color = AccessTools.Field(point,"Color");
        private static volatile View snapshot;
        private static View renderSnapshot;
        private static MatrixD renderHead;
        private static DateTime nextSample;
        private static readonly List<NativeSprite> sprites = new List<NativeSprite>(64);
        internal const int LabelWidth=512, LabelHeight=96, AtlasWidth=1024, AtlasHeight=1536;
        private static readonly Font font = new Font("Segoe UI",32,FontStyle.Regular,GraphicsUnit.Pixel);
        private static readonly StringFormat labelFormat = new StringFormat { FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter };
        private static readonly int[] labelWidths = new int[32];
        private static OverlayCanvas labels;
        private static ShaderResourceView labelTexture;
        private static string labelKey;
        private static bool failed;

        public static void Capture(MyHudMarkerRender renderer)
        {
            if (failed || !Main.VrActive || DateTime.UtcNow<nextSample) return;
            nextSample=DateTime.UtcNow.AddMilliseconds(100);
            try
            {
                var mode=MyHudMarkerRender.SignalDisplayMode;
                if (!Main.WorldAvailable || mode==MyHudMarkerRender.SignalMode.Off) { snapshot=null; return; }
                var markers=new List<Marker>();
                var head=CameraRig.Current?.Anchor.Translation ?? Sandbox.Game.World.MySector.MainCamera.Position;
                // Snapshot values before the native renderer clusters and recycles its POIs.
                foreach (object poi in (IEnumerable)points.GetValue(renderer))
                {
                    string type=kind.GetValue(poi).ToString();
                    if (type=="Target" || type=="OffscreenTarget") continue;
                    var world=(Vector3D)position.GetValue(poi);
                    if (!world.IsValid()) continue;
                    bool pinned=(bool)always.GetValue(poi);
                    string relation=relationship.GetValue(poi).ToString();
                    bool gps=type=="GPS" || type=="ContractGPS" || type=="Objective";
                    string icon=gps ? "gps" : type=="Scenario" ? "scenario" : relation=="Owner" ? "self" :
                        relation=="Enemies" ? "enemy" : relation=="FactionShare" || relation=="Friends" ? "friendly" : "neutral";
                    var tint=(VRageMath.Color)color.GetValue(poi);
                    if (!gps) tint=relation=="Enemies" ? VRageMath.Color.OrangeRed : relation=="Owner" || relation=="FactionShare" || relation=="Friends" ? VRageMath.Color.LightGreen : tint;
                    string text=name.GetValue(poi)?.ToString() ?? "";
                    if (mode==MyHudMarkerRender.SignalMode.NoNames && !pinned) text="";
                    markers.Add(new Marker { Position=world, Color=tint.ToVector4(), Name=text,
                        Icon=type=="Ore" ? "ore" : @"Textures\HUD\marker_"+icon+".dds",
                        Detail=pinned || mode==MyHudMarkerRender.SignalMode.FullDisplay,
                        Distance=Vector3D.Distance(world,head) });
                }
                snapshot=new View { Time=DateTime.UtcNow, Markers=markers.OrderByDescending(m=>m.Detail).ThenBy(m=>m.Distance).Take(32).ToArray() };
            }
            catch (Exception ex) { failed=true; snapshot=null; Logger.Warning(ex,"VR world markers disabled; native desktop markers retained"); }
        }

        internal static bool Project(Vector3D world, MatrixD view, MatrixD projection, out Vector2 screen)
        {
            screen=Vector2.Zero;
            var local=Vector3D.Transform(world,view);
            if (!local.IsValid() || local.Z>=-0.05) return false;
            var clip=Vector4D.Transform(new Vector4D(local,1),projection);
            if (!(clip.X+clip.Y+clip.W).IsValid() || clip.W<=0) return false;
            screen=new Vector2((float)(clip.X/clip.W*0.5+0.5),(float)(0.5-clip.Y/clip.W*0.5));
            return screen.X>=0.025f && screen.X<=0.975f && screen.Y>=0.025f && screen.Y<=0.975f;
        }

        private static string Distance(double metres) => metres>=1000 ? (metres/1000).ToString("0.0")+" km" : metres.ToString("0")+" m";
        public static void BeginFrame(MatrixD head)
        {
            renderSnapshot=snapshot;
            renderHead=head;
        }

        internal static int PaintLabel(Graphics graphics, string name, string distance, int index)
        {
            int x=index%2*LabelWidth, y=index/2*LabelHeight;
            using (var glyphs=new System.Drawing.Drawing2D.GraphicsPath())
            using (var outline=new Pen(Color.FromArgb(230,0,0,0),3) { LineJoin=System.Drawing.Drawing2D.LineJoin.Round })
            {
                glyphs.AddString(name,font.FontFamily,(int)font.Style,font.Size,new System.Drawing.RectangleF(x+4,y+2,LabelWidth-12,44),labelFormat);
                glyphs.AddString(distance,font.FontFamily,(int)font.Style,font.Size,new System.Drawing.RectangleF(x+4,y+46,LabelWidth-12,44),labelFormat);
                graphics.DrawPath(outline,glyphs); graphics.FillPath(Brushes.White,glyphs);
                return Math.Max(16,Math.Min(LabelWidth,(int)Math.Ceiling(glyphs.GetBounds().Right-x+6)));
            }
        }

        internal static void AddSprites(List<NativeSprite> target, MarkerBillboard billboard, MatrixD view, MatrixD projection,
            string path, Vector4 tint, ShaderResourceView text, int index, int textWidth)
        {
            var icon=new NativeSprite(path,default(VRageMath.RectangleF),tint);
            if (path=="ore") { icon.Path=@"Textures\HUD\HudAtlas0.dds"; icon.UV=new Vector4(0.125488f,0.007813f,0.061523f,0.984375f); }
            if (billboard.Project(new VRageMath.RectangleF(-0.5f,-0.5f,1,1),view,projection,ref icon)) target.Add(icon);
            if (text==null) return;
            var label=new NativeSprite(null,default(VRageMath.RectangleF),tint) {
                Texture=text, UV=new Vector4(index%2*LabelWidth/(float)AtlasWidth,index/2*LabelHeight/(float)AtlasHeight,
                    textWidth/(float)AtlasWidth,LabelHeight/(float)AtlasHeight) };
            if (billboard.Project(new VRageMath.RectangleF(0.75f,-0.9f,1.8f*textWidth/LabelHeight,1.8f),view,projection,ref label)) target.Add(label);
        }

        public static void Draw(Texture2D target, MatrixD view, MatrixD projection)
        {
            var current=renderSnapshot;
            if (failed || !HelmetHud.Markers || Main.MenuOpen || InputRouter.RadialOpen || !Main.WorldAvailable || current==null || current.Markers.Length==0 || (DateTime.UtcNow-current.Time).TotalSeconds>1) return;
            try
            {
                string key=string.Join("\n",current.Markers.Select(m=>m.Name+"\n"+Distance(m.Distance)));
                if (labels==null)
                {
                    labels=new OverlayCanvas("Marker labels",AtlasWidth,AtlasHeight,1,false,mipMaps:true);
                    labelTexture=new ShaderResourceView(Wrappers.MyRender11.DeviceInstance,labels.Texture);
                }
                if (labelKey!=key)
                {
                    labels.Clear(Color.Transparent);
                    for (int i=0;i<current.Markers.Length;i++)
                    {
                        var marker=current.Markers[i];
                        labelWidths[i]=PaintLabel(labels.Graphics,marker.Name,Distance(marker.Distance),i);
                    }
                    labels.Upload();
                    Wrappers.MyRender11.DeviceInstance.ImmediateContext.GenerateMips(labelTexture);
                    labelKey=key;
                }
                sprites.Clear();
                for (int i=0;i<current.Markers.Length;i++)
                {
                    var marker=current.Markers[i];
                    if (!Project(marker.Position,view,projection,out _) || !MarkerBillboard.TryCreate(marker.Position,renderHead,out var billboard)) continue;
                    bool detail=HelmetHud.Names;
                    AddSprites(sprites,billboard,view,projection,marker.Icon,marker.Color,detail ? labelTexture : null,i,labelWidths[i]);
                }
                NativeSprites.Draw(target,sprites);
            }
            catch (Exception ex) { failed=true; Logger.Warning(ex,"VR marker drawing disabled"); }
        }
        public static void Reset() { snapshot=null; nextSample=DateTime.MinValue; }
    }
}
