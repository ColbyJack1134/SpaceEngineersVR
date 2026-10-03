using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SpaceEngineersVR.Plugin;
using Sandbox.Game.GUI.HudViewers;
using SharpDX.Direct3D11;
using VRage.Game;
using VRage.Game.Gui;
using VRageMath;
using Color=System.Drawing.Color;
using RectangleF=VRageMath.RectangleF;

namespace SpaceEngineersVR.Player
{
    internal static class WristSignals
    {
        internal sealed class Candidate
        {
            public WorldMarkers.Marker Marker;
            public Vector2 UV,Arrow;
            public RectangleF Label,Reserved;
            public bool Edge,Visible;
            public string[] Lines;
            public float Pixels,IconScale=1.5f,TextScale=1;
        }
        internal sealed class View
        {
            public Candidate[] Candidates=new Candidate[0];
            public float Tint;
            public Vector3D Head;
            public bool Group;
        }
        private static OverlayCanvas atlas;
        private static ShaderResourceView texture;
        private static string content;
        private static DateTime nextText;
        private static int revision=-1;
        private static readonly List<NativeSprite> glyphs=new List<NativeSprite>();
        internal static readonly RectangleF Aperture=new RectangleF(.025f,.15f,.95f,.83f);
        internal static void Reset() { content=null; }
        internal static void Keys(List<SurfaceKey> keys)
        {
            keys.Add(WristKnob.Key());
        }
        internal static Candidate[] Candidates(WorldMarkers.View snapshot,SurfaceView panel,Vector3D head,SignalLayout.Options options)
        {
            if(snapshot==null || snapshot.Mode==MyHudMarkerRender.SignalMode.Off) return new Candidate[0];
            var inverse=MatrixD.Invert(panel.Pose);
            var h=Vector3D.TransformNormal(head-panel.Pose.Translation,inverse);
            if(!h.IsValid() || h.Z<=.015 || panel.Width<=0 || panel.Height<=0) return new Candidate[0];
            var found=new List<Candidate>();
            foreach(var m in snapshot.Markers)
            {
                if(!options.Allows(m) || m.Kind=="Target") continue;
                var t=Vector3D.TransformNormal(m.Position-panel.Pose.Translation,inverse);
                if(!t.IsValid() || t.Z>=0) continue;
                double fraction=h.Z/(h.Z-t.Z);
                if(fraction<=0 || fraction>=1) continue;
                var hit=h+(t-h)*fraction;
                var uv=new Vector2((float)(hit.X/panel.Width+.5),(float)(.5-hit.Y/panel.Height));
                if(Aperture.Contains(uv)) found.Add(new Candidate {Marker=m,UV=uv,IconScale=options.IconScale,TextScale=options.TextScale});
            }
            return found.OrderBy(c=>c.Marker.Id,StringComparer.Ordinal).ToArray();
        }
        internal static View Resolve(WorldMarkers.View snapshot,SurfaceView panel,Vector3D head,SignalLayout.Options options,DateTime now)
        {
            var candidates=snapshot!=null && (now-snapshot.Time).TotalSeconds<=1 ? Candidates(snapshot,panel,head,options):new Candidate[0];
            var result=new View {Candidates=candidates,Head=head,Tint=options.Tint,Group=options.Group};
            var camera=panel.Pose; camera.Translation=head; Reserve(result,panel,camera); Layout(result); return result;
        }
        internal static SurfaceView Apply(SurfaceView panel,WorldMarkers.View snapshot,MatrixD head,SignalLayout.Options options,DateTime now)
        {
            var copy=panel.At(panel.Pose);
            copy.Signals=Resolve(snapshot,panel,head.Translation,options,now);
            if(options.Edges && snapshot!=null && snapshot.Mode!=MyHudMarkerRender.SignalMode.Off && (now-snapshot.Time).TotalSeconds<=1 &&
                Vector3D.Dot(panel.Pose.Backward,head.Translation-panel.Pose.Translation)>.015)
            {
                var candidates=copy.Signals.Candidates.ToList();
                foreach(var marker in snapshot.Markers.Where(m=>options.Allows(m) && m.Kind!="Target"))
                {
                    var local=Vector3D.Transform(marker.Position,MatrixD.Invert(head));
                    if(!local.IsValid() || local.LengthSquared()<.01) continue;
                    var bearing=new Vector2((float)(local.X/Math.Max(.1,Math.Abs(local.Z))),(float)(-local.Y/Math.Max(.1,Math.Abs(local.Z))));
                    if(local.Z<-.05 && Math.Abs(bearing.X)<=options.LimitX && Math.Abs(bearing.Y)<=options.LimitY) continue;
                    if(candidates.Any(c=>c.Marker.Id==marker.Id)) continue;
                    if(bearing.LengthSquared()<1e-8f) bearing=Vector2.UnitY;
                    var direction=Vector2.Normalize(bearing);
                    var offset=direction/Math.Max(Math.Abs(direction.X)/(.5f*Aperture.Width-.025f),Math.Abs(direction.Y)/(.5f*Aperture.Height-.025f));
                    candidates.Add(new Candidate {Marker=marker,UV=Aperture.Center+offset,Edge=true,Arrow=direction,IconScale=options.IconScale,TextScale=options.TextScale});
                }
                copy.Signals.Candidates=candidates.ToArray();
            }
            Reserve(copy.Signals,panel,head); Layout(copy.Signals);
            return copy;
        }
        private static void Reserve(View view,SurfaceView panel,MatrixD head)
        {
            foreach(var c in view.Candidates)
            {
                float radius=.015f*(c.Marker.Ring!=null ? 4.5f:c.IconScale);
                var padding=new Vector2(radius,radius*panel.Width/panel.Height);
                c.Reserved=new RectangleF(c.UV-padding,padding*2);
            }
            view.Candidates=view.Candidates.Where(c=>!SignalLayout.Overlaps(c.Reserved,WristKnob.Reserved)).ToArray();
        }
        internal static bool Covers(SurfaceView panel,Vector3D position,MatrixD head)
        {
            var inverse=MatrixD.Invert(panel.Pose);
            var target=Vector3D.TransformNormal(position-panel.Pose.Translation,inverse);
            if(!target.IsValid() || target.Z>=0) return false;
            var aperture=new RectangleF(Aperture.X-.015f,Aperture.Y-.015f,Aperture.Width+.03f,Aperture.Height+.03f);
            foreach(int eye in new[] {-1,0,1})
            {
                var h=Vector3D.TransformNormal(head.Translation-panel.Pose.Translation+head.Right*(eye*.04*panel.Pose.Right.Length()),inverse);
                if(h.Z<=.015) continue;
                var hit=h+(target-h)*(h.Z/(h.Z-target.Z));
                if(aperture.Contains(new Vector2((float)(hit.X/panel.Width+.5),(float)(.5-hit.Y/panel.Height)))) return true;
            }
            return false;
        }
        internal static SignalLayout.Entry[] OutsideWindow(SignalLayout.Entry[] entries,SurfaceView panel,MatrixD head)
        {
            if(Vector3D.Dot(panel.Pose.Backward,head.Translation-panel.Pose.Translation)<=0) return entries;
            var inverse=MatrixD.Invert(head);
            var min=new Vector2(float.MaxValue); var max=new Vector2(float.MinValue);
            foreach(int eye in new[] {-1,1}) foreach(float x in new[] {Aperture.X,Aperture.Right}) foreach(float y in new[] {Aperture.Y,Aperture.Bottom})
            {
                var world=Vector3D.Transform(new Vector3D((x-.5)*panel.Width,(.5-y)*panel.Height,0),panel.Pose);
                var local=Vector3D.TransformNormal(world-head.Translation-head.Right*(eye*.04*panel.Pose.Right.Length()),inverse);
                if(local.Z>=-.01) continue;
                var point=new Vector2((float)(local.X/-local.Z),(float)(local.Y/local.Z));
                min=Vector2.Min(min,point); max=Vector2.Max(max,point);
            }
            var window=new RectangleF(min,max-min);
            return entries.Where(e=>!SignalLayout.Overlaps(e.SymbolBounds,window) && !Covers(panel,e.Position,head) && !e.Members.Any(m=>Covers(panel,m.Position,head))).Select(e=> {
                var copy=e.Copy(); copy.Labels=e.Labels.Where(l=>!SignalLayout.Overlaps(l.Bounds,window)).ToArray(); return copy;
            }).ToArray();
        }
        internal static void Layout(View view)
        {
            var occupied=new List<RectangleF> {WristKnob.Reserved};
            foreach(var c in view.Candidates.OrderBy(c=>c.Edge).ThenBy(c=>c.Marker.Id,StringComparer.Ordinal))
            {
                c.Visible=false;
                c.Pixels=MathHelper.Clamp(Math.Max(SignalFont.Width(c.Marker.Name,26),SignalFont.Width(WorldMarkers.Distance(c.Marker.Distance),24))+20,120,360);
                c.Lines=SignalFont.Wrap(c.Marker.Name,26,c.Pixels-12);
                float width=.285f*c.Pixels/360*c.TextScale,height=(.041f*(c.Lines.Length+1)+.008f)*c.TextScale;
                var anchor=c.Reserved;
                var positions=new List<Vector2> {
                    new Vector2(c.UV.X-width/2,anchor.Bottom+.01f),new Vector2(c.UV.X-width/2,anchor.Y-height-.01f),
                    new Vector2(anchor.Right+.01f,c.UV.Y-height/2),new Vector2(anchor.X-width-.01f,c.UV.Y-height/2) };
                for(int row=1;row<=2;row++)
                {
                    positions.Add(new Vector2(c.UV.X-width/2,anchor.Bottom+.01f+row*(height+.015f)));
                    positions.Add(new Vector2(c.UV.X-width/2,anchor.Y-height-.01f-row*(height+.015f)));
                }
                foreach(var position in positions)
                {
                    var box=new RectangleF(MathHelper.Clamp(position.X,Aperture.X+.005f,Aperture.Right-width-.005f),
                        MathHelper.Clamp(position.Y,Aperture.Y+.005f,Aperture.Bottom-height-.005f),width,height);
                    if(occupied.Any(r=>SignalLayout.Overlaps(r,box)) || view.Candidates.Any(other=>SignalLayout.Overlaps(other.Reserved,box))) continue;
                    c.Label=box; c.Visible=true; occupied.Add(new RectangleF(box.X-.004f,box.Y-.003f,box.Width+.008f,box.Height+.006f)); break;
                }
            }
        }
        internal static void Paint(OverlayCanvas target,SurfaceView panel)
        {
            target.Clear(Color.FromArgb((int)(255*(panel.Signals?.Tint ?? .15f)),5,12,18));
            using(var bar=new SolidBrush(Color.FromArgb(255,9,18,26)))
            using(var rim=new Pen(Color.FromArgb(140,112,166,188),1.5f))
            {
                target.Graphics.FillRectangle(bar,0,0,1024,90);
                target.Graphics.DrawRectangle(rim,1,1,1022,638);
            }
            PhysicalSurface.PaintWristKeys(target,panel);
        }
        internal static Candidate[][] Symbols(View view)
        {
            var groups=new List<List<Candidate>>();
            foreach(var c in view.Candidates.Where(c=>!c.Edge))
            {
                var group=view.Group && c.Marker.Cluster && !c.Marker.Pinned ? groups.FirstOrDefault(g=>g[0].Marker.Cluster && !g[0].Marker.Pinned &&
                    SignalLayout.Overlaps(g[0].Reserved,c.Reserved)):null;
                if(group==null) groups.Add(new List<Candidate> {c}); else group.Add(c);
            }
            return groups.Select(g=>g.ToArray()).ToArray();
        }
        internal static MatrixD DetailPlane(SurfaceView panel,Candidate c)
        {
            if(c.Edge) return panel.Pose;
            var head=panel.Signals.Head;
            var normal=Vector3D.Normalize(panel.Pose.Backward);
            double near=Vector3D.Dot(panel.Pose.Translation-head,normal);
            double far=Vector3D.Dot(c.Marker.Position-head,normal);
            double scale=far/near;
            var pose=panel.Pose;
            pose.Right*=scale; pose.Up*=scale; pose.Backward*=scale;
            pose.Translation=head+(panel.Pose.Translation-head)*scale;
            return pose;
        }
        internal static MarkerBillboard Board(SurfaceView panel,Candidate c)
        {
            var pose=DetailPlane(panel,c);
            return new MarkerBillboard {Center=c.Marker.Position,Right=Vector3D.Normalize(pose.Right),Up=Vector3D.Normalize(pose.Up),
                Scale=panel.Width*.03*pose.Right.Length()};
        }
        internal static void Clip(ref NativeSprite sprite,SurfaceView panel,MatrixD view,MatrixD projection)
        {
            var a=Aperture;
            var quad=PhysicalSurface.Quad(null,panel.Pose,new RectangleF((a.X-.5f)*panel.Width,(.5f-a.Y)*panel.Height,a.Width*panel.Width,a.Height*panel.Height),Vector4.One,Vector4.One,view,projection);
            var points=new[] {quad.TopLeft,quad.TopRight,quad.BottomRight,quad.BottomLeft}.Select(p=>new Vector2(p.X/p.W,p.Y/p.W)).ToArray();
            var center=(points[0]+points[1]+points[2]+points[3])/4;
            var planes=new Vector4[4];
            for(int i=0;i<4;i++)
            {
                var start=points[i]; var edge=points[(i+1)%4]-start;
                var normal=new Vector2(-edge.Y,edge.X); float d=-Vector2.Dot(normal,start);
                if(Vector2.Dot(normal,center)+d<0) {normal=-normal; d=-d;}
                planes[i]=new Vector4(normal,d,0);
            }
            sprite.Clip0=planes[0]; sprite.Clip1=planes[1]; sprite.Clip2=planes[2]; sprite.Clip3=planes[3];
        }
        internal static void AddSprites(List<NativeSprite> sprites,SharpDX.Direct3D11.Device device,SurfaceView panel,MatrixD view,MatrixD projection)
        {
            var candidates=panel.Signals?.Candidates ?? new Candidate[0];
            var visible=candidates.Where(c=>c.Visible).Take(64).ToArray();
            if(atlas==null) {atlas=new OverlayCanvas("Wrist signal names",2048,2048,1,false,device,true); texture=new ShaderResourceView(device,atlas.Texture);}
            string names=string.Join("\n",visible.Select(c=>c.Marker.Id+"|"+c.Marker.Name));
            string key=names+"\n"+string.Join("\n",visible.Select(c=>WorldMarkers.Distance(c.Marker.Distance)+"|"+c.Marker.Color));
            if((content!=key || revision!=NativeSprites.Revision) && (DateTime.UtcNow>=nextText || revision!=NativeSprites.Revision || content==null || !content.StartsWith(names+"\n",StringComparison.Ordinal)))
            {
                atlas.Clear(Color.Transparent);
                atlas.Upload(); glyphs.Clear();
                for(int i=0;i<visible.Length;i++)
                {
                    int x=i%4*512,y=i/4*128;
                    var c=visible[i];
                    for(int line=0;line<c.Lines.Length;line++) SignalFont.Add(glyphs,c.Lines[line],x+6,y+line*28,26,c.Pixels-12,c.Marker.Color,2048,2048);
                    SignalFont.Add(glyphs,WorldMarkers.Distance(c.Marker.Distance),x+6,y+c.Lines.Length*28+4,24,c.Pixels-12,c.Marker.Color,2048,2048);
                }
                NativeSprites.Draw(atlas.Texture,glyphs); device.ImmediateContext.GenerateMips(texture); content=key; revision=NativeSprites.Revision; nextText=DateTime.UtcNow.AddMilliseconds(100);
            }
            for(int i=0;i<visible.Length;i++)
            {
                var c=visible[i]; var b=c.Label;
                float sourceHeight=28*(c.Lines.Length+1)+4;
                var label=PhysicalSurface.Quad(texture,DetailPlane(panel,c),new RectangleF((b.X-.5f)*panel.Width,(.5f-b.Y)*panel.Height,b.Width*panel.Width,b.Height*panel.Height),
                    new Vector4(i%4*.25f,i/4/16f,c.Pixels/2048,sourceHeight/2048),Vector4.One,view,projection,c.Edge ? .0005f:0);
                label.Premultiplied=true; label.IgnoreSceneDepth=!c.Edge; if(!c.Edge) Clip(ref label,panel,view,projection); sprites.Add(label);
            }
            int firstSymbol=sprites.Count;
            foreach(var group in Symbols(panel.Signals ?? new View()))
            {
                var c=group[0]; var board=Board(panel,c);
                c.Marker.Ring?.Add(sprites,board,view,projection);
                board.Scale*=c.IconScale;
                if(group.Length==1) SignalPainter.Icon(sprites,board,view,projection,c.Marker);
                else
                {
                    var members=group.Select(member=>member.Marker).ToArray();
                    var color=members.Select(SignalLayout.Relation).Distinct().Count()==1 ? members[0].Color:new Vector4(.9f,.93f,.96f,1);
                    SignalPainter.GroupIcon(sprites,board,view,projection,members,color);
                }
            }
            for(int i=firstSymbol;i<sprites.Count;i++) {var sprite=sprites[i]; sprite.IgnoreSceneDepth=true; Clip(ref sprite,panel,view,projection); sprites[i]=sprite;}
            foreach(var c in candidates.Where(c=>c.Edge && c.Visible))
            {
                var pose=MatrixD.CreateRotationZ(Math.Atan2(-c.Arrow.Y,c.Arrow.X)-Math.PI/2);
                pose.Translation=new Vector3D((c.UV.X-.5)*panel.Width,(.5-c.UV.Y)*panel.Height,.001);
                var sprite=SignalPainter.Atlas(MyHudTexturesEnum.DirectionIndicator,c.Marker.Color);
                var quad=PhysicalSurface.Quad(null,pose*panel.Pose,new RectangleF(-.0025f,.0025f,.005f,.005f),sprite.UV,sprite.Tint,view,projection);
                quad.Path=sprite.Path; sprites.Add(quad);
            }
        }
    }
}
