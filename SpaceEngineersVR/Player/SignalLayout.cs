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
            public bool Group=true,Edges=true,Rings=true,Gps=true,Contacts=true,Resources=true,Distances=true;
            public float LimitX=EdgeX,LimitY=EdgeY,IconScale=1.5f,TextScale=1,Tint=.15f;
            internal void Projection(MatrixD projection)
            { LimitX=Math.Min(EdgeX,.96f/(float)Math.Abs(projection.M11)); LimitY=Math.Min(EdgeY,.96f/(float)Math.Abs(projection.M22)); }
            public static Options Current => new Options { IconScale=Common.Config.SignalIconScale,TextScale=Common.Config.SignalTextScale,Tint=Common.Config.WristSignalTint,Group=Common.Config.GroupSignals,Distances=Common.Config.ShowSignalDistances,Edges=Common.Config.SignalEdges,
                Rings=Common.Config.SignalRings,Gps=Common.Config.ShowGps,Contacts=Common.Config.ShowContacts,Resources=Common.Config.ShowResources };
            public bool Allows(WorldMarkers.Marker m)
            {
                if(m.Kind=="OffscreenTarget") return Contacts && Rings && m.Ring!=null;
                // Shown whenever markers are.
                if(m.Kind=="WeaponCore") return true;
                if(m.Kind=="Ore" || m.Kind=="Hack") return Resources;
                if(m.Kind=="GPS" || m.Kind=="ContractGPS" || m.Kind=="Objective" || m.Kind=="Scenario") return Gps;
                return m.Kind=="ButtonMarker" || Contacts;
            }
        }
        internal sealed class Entry
        {
            public WorldMarkers.Marker[] Members,Representatives;
            public WorldMarkers.Marker Primary => Members[0];
            public Vector3D Position;
            public Vector2 Point;
            // Non-zero while an EdgeArrow marker is out of view: the screen direction (y down) toward it.
            public Vector2 Arrow;
            public Vector4 Color;
            public RectangleF SymbolBounds;
            public string Id,Relation;
            public bool Edge,Ring;
            public float Expansion,CompactShift,NameAlpha,MemberAlpha,SymbolAlpha=1;
            public float IconScale,TextScale;
            public Label[] Labels=new Label[0];
            internal Entry Copy() => (Entry)MemberwiseClone();
        }
        internal sealed class Label
        {
            public string Name,Distance;
            public RectangleF Bounds;
            public float NameY,DistanceY,LineHeight,NameAlpha=1,DistanceScale=1;
            public Vector4 Color;
            public string Font="White";
            public bool LeftAligned;
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
        internal static bool HighAlert(WorldMarkers.Marker marker,WorldMarkers.Marker[] members) =>
            marker.Relation!="Neutral" && marker.Relation!="NoOwnership" && (marker.Kind=="Scenario" || members.Any(other=>
                ((Relation(marker)=="Hostile" && (Relation(other)=="Own" || Relation(other)=="Friendly")) ||
                 (Relation(other)=="Hostile" && (Relation(marker)=="Own" || Relation(marker)=="Friendly"))) &&
                Vector3D.DistanceSquared(marker.Position,other.Position)<1000000));
        internal static WorldMarkers.Marker[] Representatives(WorldMarkers.Marker[] members)
        {
            var significance=Comparer<WorldMarkers.Marker>.Create((a,b)=> {
                int rank=HighAlert(a,members).CompareTo(HighAlert(b,members));
                if(rank==0 && a.NativeType>=WorldMarkers.EntityTypeStart && b.NativeType>=WorldMarkers.EntityTypeStart) rank=a.NativeType.CompareTo(b.NativeType);
                if(rank==0 && a.GridBlocks>0 && b.GridBlocks>0) rank=a.GridBlocks.CompareTo(b.GridBlocks);
                if(rank==0) rank=b.Distance.CompareTo(a.Distance);
                return rank;
            });
            var ranked=members.OrderByDescending(m=>m,significance).ThenBy(m=>m.Id,StringComparer.Ordinal);
            if(members.Select(Relation).Distinct().Count()==1) return ranked.Take(4).ToArray();
            return new[] {"Own","Friendly","Neutral","Hostile"}.Select(r=>ranked.FirstOrDefault(m=>Relation(m)==r)).Where(m=>m!=null).ToArray();
        }
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
            return new Entry { Members=values,Representatives=Representatives(values),Position=position,Point=p,Edge=edge,Color=relations.Length==1 ? values[0].Color : Vector4.One,
                Id=string.Join("|",values.Select(m=>m.Id).OrderBy(id=>id,StringComparer.Ordinal)),Relation=relations.Length==1 ? relations[0]:"Mixed",
                IconScale=options.IconScale,TextScale=options.TextScale,SymbolBounds=SymbolBounds(p,values.Any(m=>m.Ring!=null),options.IconScale*(values[0].Kind=="Objective" ? 2:values[0].Span)),
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
            var entries=groups.Select(g=>Create(g,head,options)).Where(e=>!e.Edge || EdgeArrow(e,head,options)).ToList();
            var result=entries.ToArray();
            int budget=64;
            bool avoid=entries.Any(e=>e.Primary.AvoidOverlap);
            var placed=new List<RectangleF>();
            foreach(var e in entries)
            {
                Focus(e,source,options);
                if(budget==0 || e.Primary.Kind=="Target") continue;
                bool names=source.Mode!=MyHudMarkerRender.SignalMode.NoNames || source.Reveal || e.Primary.Pinned;
                if(e.Members.Length>1)
                {
                    e.Labels=GroupRows(e,head,names,options.Distances).Take(budget).ToArray();
                }
                else
                {
                    var row=Row(e.Primary);
                    if(!names || e.NameAlpha<=0) row.Name="";
                    if(!options.Distances) row.Distance="";
                    if(string.IsNullOrEmpty(row.Name) && string.IsNullOrEmpty(row.Distance)) continue;
                    row.NameAlpha=e.NameAlpha;
                    row.DistanceScale=(.5f+.2f*e.NameAlpha)/.7f;
                    float width=MathHelper.Clamp(Math.Max(SignalFont.Width(row.Name,31),SignalFont.Width(row.Distance,29))*.025f/31+.015f,.12f,.8f)*options.TextScale;
                    row.LineHeight=.025f*options.TextScale;
                    var primary=e.Primary;
                    float half=GroupUnit(e.IconScale)*.5f*(e.Arrow==Vector2.Zero ? primary.Span:1);
                    float offset=e.Ring ? .048f:half+GroupUnit(e.IconScale)*.4375f;
                    row.NameY=e.Point.Y-offset-row.LineHeight/2;
                    row.DistanceY=e.Point.Y+offset*(.7f+.3f*e.NameAlpha)-row.LineHeight*row.DistanceScale/2;
                    float x=e.Point.X-width/2;
                    if(primary.LabelSide!=null)
                    {
                        if(primary.LabelSide=="below") row.NameY=e.Point.Y+offset-row.LineHeight/2;
                        else { row.NameY=e.Point.Y-half-row.LineHeight*2; x=primary.LabelSide=="upleft" ? e.Point.X-half-width:e.Point.X+half; }
                        row.DistanceY=row.NameY+row.LineHeight;
                    }
                    if(primary.AvoidOverlap) AvoidOverlap(row,e,placed,x,width,primary.LabelSide);
                    if(string.IsNullOrEmpty(row.Name) && string.IsNullOrEmpty(row.Distance)) continue;
                    row.Bounds=new RectangleF(x,string.IsNullOrEmpty(row.Name) ? row.DistanceY:row.NameY,width,
                        string.IsNullOrEmpty(row.Name) ? row.LineHeight:row.DistanceY+row.LineHeight-row.NameY);
                    e.Labels=new[] {row};
                }
                budget-=e.Labels.Length;
                if(avoid) foreach(var label in e.Labels) placed.AddRange(Ink(label));
            }
            return result;
        }
        // Entries arrive in priority order, so earlier labels keep their place. A name steps up and a distance down, away from
        // the symbol; a side label moves as one block. A threat line still blocked is dropped rather than drifting off its target.
        private static void AvoidOverlap(Label row,Entry e,List<RectangleF> placed,float x,float width,string side)
        {
            int tries=e.Primary.Pinned ? 6:3;
            float step=row.LineHeight*1.05f;
            bool Blocked(string text,float y) => !string.IsNullOrEmpty(text) && placed.Any(p=>Overlaps(p,new RectangleF(x,y,width,row.LineHeight)));
            if(side==null)
            {
                for(int i=0;i<tries && Blocked(row.Name,row.NameY);i++) row.NameY-=step;
                for(int i=0;i<tries && Blocked(row.Distance,row.DistanceY);i++) row.DistanceY+=step;
                if(e.Primary.Pinned) return;
                if(Blocked(row.Name,row.NameY)) row.Name="";
                if(Blocked(row.Distance,row.DistanceY)) row.Distance="";
                return;
            }
            float direction=side=="below" ? 1:-1;
            for(int i=0;i<tries && (Blocked(row.Name,row.NameY) || Blocked(row.Distance,row.DistanceY));i++) { row.NameY+=direction*step; row.DistanceY+=direction*step; }
        }
        // Edge arrows sit this far ahead of the eye; they ignore scene depth, so cockpit geometry cannot hide them.
        internal const double ArrowDepth=3;
        private const float ArrowReachX=.55f,ArrowReachY=.4f;
        // A marker out of view stays on the HUD as an arrow inside the view edge, pointing toward it.
        private static bool EdgeArrow(Entry e,MatrixD head,Options options)
        {
            var m=e.Primary;
            if(!m.EdgeArrow) return false;
            var local=Vector3D.Transform(e.Position,MatrixD.Invert(head));
            var direction=new Vector2((float)local.X,(float)-local.Y);
            direction=direction.LengthSquared()<1e-8f ? Vector2.UnitY:Vector2.Normalize(direction);
            // An oval near the centre, so wide headsets do not push it to the edge of view; never past 80% of the limits.
            float rx=Math.Min(options.LimitX*.8f,ArrowReachX),ry=Math.Min(options.LimitY*.8f,ArrowReachY);
            float reach=1/(float)Math.Sqrt(direction.X*direction.X/(rx*rx)+direction.Y*direction.Y/(ry*ry));
            e.Point=direction*reach; e.Arrow=direction; e.Edge=false;
            e.Position=Vector3D.Transform(new Vector3D(e.Point.X,-e.Point.Y,-1)*ArrowDepth,head);
            e.SymbolBounds=SymbolBounds(e.Point,false,e.IconScale);
            return true;
        }
        private static void Focus(Entry e,WorldMarkers.View source,Options options)
        {
            float distance=new Vector2(e.Point.X*.48f/options.LimitX,e.Point.Y*.27f/options.LimitY).Length();
            e.NameAlpha=distance<.15f ? (float)Math.Pow(MathHelper.Clamp(1-(distance-.03f)/.12f,0,1),2):0;
            float members=distance<=.03f ? 1:distance<.07f ? (float)Math.Pow(1-(distance-.03f)/.04f,2):
                distance<.15f ? (float)Math.Pow(1-(distance-.07f)/.08f,2):0;
            e.Expansion=distance<.07f ? members:0;
            e.CompactShift=distance<.07f ? 1:members;
            e.MemberAlpha=distance<.07f ? members:0;
            e.SymbolAlpha=MathHelper.Clamp((distance-.2f)/.5f,0,1);
            if(source.Reveal || source.Mode==MyHudMarkerRender.SignalMode.FullDisplay || e.Primary.Pinned)
            { e.NameAlpha=e.MemberAlpha=e.Expansion=e.CompactShift=e.SymbolAlpha=1; }
        }
        internal static float GroupUnit(float iconScale) => (float)(2*Math.Tan(MarkerBillboard.IconAngle/2)*.55)*iconScale;
        internal static Vector2 GroupOffset(int index,float expansion,float compactShift)
        {
            var compact=new Vector2((index%2==0 ? -6:6)+22*compactShift,index<2 ? -4:4);
            var expanded=new Vector2(16,-4+8*index);
            var offset=Vector2.Lerp(compact,expanded,expansion);
            return new Vector2(offset.X/25.6f,offset.Y/14.4f);
        }
        private static Label[] GroupRows(Entry e,MatrixD head,bool names,bool distances)
        {
            float unit=GroupUnit(e.IconScale);
            float line=.025f*.55f/.7f*e.TextScale;
            var result=new List<Label>();
            var primary=e.Primary;
            if(names && e.NameAlpha>0)
            {
                var title=new Label {Name=GroupName(e.Members),Color=primary.FontColor,Font=primary.Font,LineHeight=.025f*e.TextScale,NameAlpha=e.NameAlpha};
                float width=Math.Max(.08f,SignalFont.Width(title.Name,31)/31*title.LineHeight);
                title.NameY=e.Point.Y-unit*.9375f-title.LineHeight/2;
                title.Bounds=new RectangleF(e.Point.X-width/2,title.NameY,width,title.LineHeight); result.Add(title);
            }
            if(names && e.MemberAlpha>0)
                for(int i=0;i<e.Representatives.Length;i++)
                {
                    var member=e.Representatives[i];
                    if(string.IsNullOrEmpty(member.Name)) continue;
                    float width=MathHelper.Clamp(SignalFont.Width(member.Name,31)/31*line,.04f,.6f*e.TextScale);
                    var row=new Label {Name=member.Name,Color=member.GroupFontColor,Font=member.Font,LineHeight=line,LeftAligned=true,NameAlpha=e.MemberAlpha};
                    var offset=GroupOffset(i,e.Expansion,e.CompactShift)*unit;
                    row.NameY=e.Point.Y+offset.Y-line/2;
                    row.Bounds=new RectangleF(e.Point.X+offset.X+unit*8/25.6f,row.NameY,width,line); result.Add(row);
                }
            if(distances)
            {
                var distance=new Label {Distance=WorldMarkers.Distance(Vector3D.Distance(e.Position,head.Translation)),Color=primary.GroupFontColor,Font=primary.Font,
                    LineHeight=.025f*e.TextScale,DistanceScale=(.5f+.2f*e.NameAlpha)/.7f};
                float width=Math.Max(.06f,SignalFont.Width(distance.Distance,31)/31*distance.LineHeight);
                var compact=new Vector2(0,12/14.4f);
                var expanded=new Vector2((16+42.66667f)/25.6f,(-4+8*e.Representatives.Length+4)/14.4f);
                var offset=Vector2.Lerp(compact,expanded,e.Expansion)*unit;
                distance.DistanceY=e.Point.Y+offset.Y-distance.LineHeight*distance.DistanceScale/2;
                distance.Bounds=new RectangleF(e.Point.X+offset.X-width/2,distance.DistanceY,width,distance.LineHeight); result.Add(distance);
            }
            return result.ToArray();
        }
        internal static IEnumerable<RectangleF> Ink(Label label)
        {
            if(!string.IsNullOrEmpty(label.Name)) yield return new RectangleF(label.Bounds.X,label.NameY,label.Bounds.Width,label.LineHeight);
            if(!string.IsNullOrEmpty(label.Distance)) yield return new RectangleF(label.Bounds.X,label.DistanceY,label.Bounds.Width,label.LineHeight);
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
        private static Label Row(WorldMarkers.Marker m) => new Label { Name=m.Ring!=null ? "":m.Name ?? "",Distance=WorldMarkers.Distance(m.Distance),Color=m.Ring!=null ? m.Color:m.FontColor,Font=m.Font };
        internal static bool Overlaps(RectangleF a,RectangleF b) => a.X<b.X+b.Width && a.X+a.Width>b.X && a.Y<b.Y+b.Height && a.Y+a.Height>b.Y;
    }
}
