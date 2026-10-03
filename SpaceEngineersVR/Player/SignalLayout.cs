using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.GUI.HudViewers;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SignalLayout
    {
        internal sealed class Options
        {
            public bool Group=true,Edges=true,Rings=true,Gps=true,Contacts=true,Resources=true;
            public float LimitX=EdgeX,LimitY=EdgeY,IconScale=1.5f,TextScale=1,Tint=.15f;
            public RectangleF[] Reserved=new RectangleF[0];
            internal void Projection(MatrixD projection)
            { LimitX=Math.Min(EdgeX,.96f/(float)Math.Abs(projection.M11)); LimitY=Math.Min(EdgeY,.96f/(float)Math.Abs(projection.M22)); }
            public static Options Current => new Options { IconScale=Common.Config.SignalIconScale,TextScale=Common.Config.SignalTextScale,Tint=Common.Config.WristSignalTint,Group=Common.Config.GroupSignals,Edges=Common.Config.SignalEdges,
                Rings=Common.Config.SignalRings,Gps=Common.Config.ShowGps,Contacts=Common.Config.ShowContacts,Resources=Common.Config.ShowResources,Reserved=Common.Config.ShowVitals && HelmetHud.Visible ? EssentialHud.SignalLabelReservations(EssentialHud.DisplayScale,EssentialHud.Current?.Piloting==true):new RectangleF[0] };
            public bool Allows(WorldMarkers.Marker m)
            {
                if(m.Kind=="OffscreenTarget") return Contacts && Rings && m.Ring!=null;
                if(m.Kind=="Ore" || m.Kind=="Hack") return Resources;
                if(m.Kind=="GPS" || m.Kind=="ContractGPS" || m.Kind=="Objective" || m.Kind=="Scenario") return Gps;
                return m.Kind=="ButtonMarker" || Contacts;
            }
        }
        internal sealed class Entry
        {
            public WorldMarkers.Marker[] Members;
            public WorldMarkers.Marker Primary => Members[0];
            public Vector3D Position;
            public Vector2 Point;
            public Vector4 Color;
            public RectangleF SymbolBounds;
            public string Id,Relation;
            public bool Edge,Ring;
            public float IconScale,TextScale;
            public Label[] Labels=new Label[0];
            internal Entry Copy() => (Entry)MemberwiseClone();
        }
        internal sealed class Label
        {
            public string Name,Distance;
            public RectangleF Bounds;
            public float NameY,DistanceY,LineHeight;
            public Vector4 Color;
        }
        internal const float EdgeX=1.5f,EdgeY=1.5f;
        internal static bool Nearby(Vector3D a,Vector3D b,Vector3D head)
        {
            var half=(a-b)*.5;
            double radius=Math.Sin(Math.PI/36)*(b+half-head).Length();
            return half.LengthSquared()<=radius*radius;
        }
        internal static string Relation(WorldMarkers.Marker m)
        {
            if(m.Relation=="Owner") return "Own";
            if(m.Relation=="Friends" || m.Relation=="FactionShare") return "Friendly";
            if(m.Relation=="Enemies") return "Hostile";
            return "Neutral";
        }
        private static int Priority(WorldMarkers.Marker m) => !string.IsNullOrEmpty(m.LockState) || m.Kind=="OffscreenTarget" ? 0 :
            m.Pinned ? 1 : m.Kind=="Objective" || m.Encounter ? 2 : m.Relation=="Enemies" ? 3:4;
        private static Entry Create(List<WorldMarkers.Marker> members,MatrixD head,Options options)
        {
            var values=members.OrderBy(Priority).ThenBy(m=>m.Id,StringComparer.Ordinal).ToArray();
            Vector3D position=values[0].Position,offset=Vector3D.Zero;
            foreach(var m in values) offset+=m.Position-position;
            position+=offset/values.Length;
            var local=Vector3D.Transform(position,MatrixD.Invert(head));
            var p=new Vector2((float)(local.X/Math.Max(.1,Math.Abs(local.Z))),(float)(-local.Y/Math.Max(.1,Math.Abs(local.Z))));
            bool edge=local.Z>=-.05 || Math.Abs(p.X)>options.LimitX || Math.Abs(p.Y)>options.LimitY;
            var relations=values.Select(Relation).Distinct().ToArray();
            return new Entry { Members=values,Position=position,Point=p,Edge=edge,Color=relations.Length==1 ? values[0].Color : new Vector4(.9f,.93f,.96f,1),
                Id=string.Join("|",values.Select(m=>m.Id).OrderBy(id=>id,StringComparer.Ordinal)),Relation=relations.Length==1 ? relations[0]:"Mixed",
                IconScale=options.IconScale,TextScale=options.TextScale,SymbolBounds=SymbolBounds(p,values.Any(m=>m.Ring!=null),options.IconScale*(values[0].Kind=="Objective" ? 2:1)),
                Ring=options.Rings && values.Any(m=>m.Ring!=null) };
        }
        private static RectangleF SymbolBounds(Vector2 p,bool ring,float scale) => ring ? new RectangleF(p.X-.048f,p.Y-.048f,.096f,.096f):new RectangleF(p.X-.011f*scale,p.Y-.011f*scale,.022f*scale,.022f*scale);
        internal static Entry[] Build(WorldMarkers.View source,MatrixD head,Options options,DateTime now)
        {
            if(source==null || source.Mode==MyHudMarkerRender.SignalMode.Off || !head.IsValid()) { return new Entry[0]; }
            var input=source.Markers.Where(m=>options.Allows(m) && m.Position.IsValid() && Vector3D.DistanceSquared(m.Position,head.Translation)>.01 &&
                (m.Kind!="Target" || source.Mode==MyHudMarkerRender.SignalMode.FullDisplay || Vector3D.DistanceSquared(m.Position,head.Translation)<=4000000))
                .OrderBy(Priority).ThenBy(m=>m.Id,StringComparer.Ordinal).ToArray();
            var groups=new List<List<WorldMarkers.Marker>>();
            foreach(var m in input)
            {
                var group=options.Group && m.Cluster && !m.Pinned ? groups.FirstOrDefault(g=>g[0].Cluster && !g[0].Pinned && Nearby(g[0].Position,m.Position,head.Translation)):null;
                if(group==null) groups.Add(new List<WorldMarkers.Marker> {m}); else group.Add(m);
            }
            // Off-view bearings belong to the wrist window, never the world HUD.
            var entries=groups.Select(g=>Create(g,head,options)).Where(e=>!e.Edge).ToList();
            var result=entries.ToArray();
            int budget=64;
            foreach(var e in entries.OrderBy(e=>Priority(e.Primary)).ThenBy(e=>e.Id,StringComparer.Ordinal))
            {
                bool show=source.Reveal || source.Mode!=MyHudMarkerRender.SignalMode.NoNames || e.Ring;
                if(!show || budget==0 || e.Primary.Kind=="Target") continue;
                var row=e.Members.Length==1 ? Row(e.Primary):new Label { Name=GroupName(e.Members),Distance=WorldMarkers.Distance(e.Members.Average(m=>m.Distance)),Color=e.Color };
                float width=MathHelper.Clamp(Math.Max(SignalFont.Width(row.Name,31),SignalFont.Width(row.Distance,29))*.025f/31+.015f,.12f,.8f)*options.TextScale;
                row.LineHeight=.025f*options.TextScale;
                float radius=e.Ring ? .048f:.011f*options.IconScale;
                row.NameY=e.Point.Y-radius-row.LineHeight-.006f;
                row.DistanceY=e.Point.Y+radius+.006f;
                bool nearby=!options.Group && entries.Any(other=>other!=e && Math.Abs(other.Point.X-e.Point.X)<width && Math.Abs(other.Point.Y-e.Point.Y)<.075f);
                bool placed=false;
                float originalName=row.NameY,originalDistance=row.DistanceY;
                for(int attempt=0;attempt<(options.Group ? 1:5);attempt++)
                {
                    if(attempt>0 || nearby)
                    {
                        int placement=nearby ? attempt+1:attempt;
                        float height=row.LineHeight*2+.003f;
                        float top=placement%2==0 ? e.Point.Y+radius+.009f+((placement-1)/2)*height : e.Point.Y-radius-.009f-height-(placement/2)*height;
                        row.NameY=top; row.DistanceY=top+row.LineHeight+.003f;
                    }
                    else {row.NameY=originalName; row.DistanceY=originalDistance;}
                    row.Bounds=new RectangleF(e.Point.X-width/2,string.IsNullOrEmpty(row.Name) ? row.DistanceY:row.NameY,width,
                        string.IsNullOrEmpty(row.Name) ? row.LineHeight:row.DistanceY+row.LineHeight-row.NameY);
                    var ink=Ink(row).ToArray();
                    if(ink.Any(r=>r.X < -options.LimitX || r.Right > options.LimitX || r.Y < -options.LimitY || r.Bottom > options.LimitY)) continue;
                    if(ink.Any(r=>options.Reserved.Any(b=>Overlaps(r,b)) || entries.Any(other=>Overlaps(r,other.SymbolBounds)) ||
                        entries.SelectMany(other=>other.Labels).SelectMany(Ink).Any(b=>Overlaps(r,b)))) continue;
                    placed=true; break;
                }
                if(!placed) continue;
                e.Labels=new[] {row}; budget--;
            }
            return result;
        }
        internal static IEnumerable<RectangleF> Ink(Label label)
        {
            if(!string.IsNullOrEmpty(label.Name)) yield return new RectangleF(label.Bounds.X,label.NameY,label.Bounds.Width,label.LineHeight);
            yield return new RectangleF(label.Bounds.X,label.DistanceY,label.Bounds.Width,label.LineHeight);
        }
        internal static string GroupName(WorldMarkers.Marker[] members)
        {
            var relations=members.Select(m=>m.Relation=="Owner" ? "Owner":m.Relation=="FactionShare" || m.Relation=="Friends" ? "FactionShare":m.Relation=="Enemies" ? "Enemies":"Neutral").Distinct().ToArray();
            var id=relations.Length==1 ? relations[0]=="Owner" ? Sandbox.Game.Localization.MySpaceTexts.Signal_Own:
                relations[0]=="FactionShare" ? Sandbox.Game.Localization.MySpaceTexts.Signal_Friendly:
                relations[0]=="Enemies" ? Sandbox.Game.Localization.MySpaceTexts.Signal_Enemy:Sandbox.Game.Localization.MySpaceTexts.Signal_Neutral:
                relations.All(r=>r=="Owner" || r=="FactionShare") ? Sandbox.Game.Localization.MySpaceTexts.Signal_Friendly:Sandbox.Game.Localization.MySpaceTexts.Signal_Mixed;
            return members.Length+VRage.MyTexts.GetString(id);
        }
        private static Label Row(WorldMarkers.Marker m) => new Label { Name=m.Ring!=null ? "":m.Name ?? "",Distance=WorldMarkers.Distance(m.Distance),Color=m.Color };
        internal static bool Overlaps(RectangleF a,RectangleF b) => a.X<b.X+b.Width && a.X+a.Width>b.X && a.Y<b.Y+b.Height && a.Y+a.Height>b.Y;
    }
}
