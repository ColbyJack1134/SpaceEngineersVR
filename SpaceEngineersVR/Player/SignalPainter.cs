using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Sandbox.Game.Gui;
using SharpDX.Direct3D11;
using VRage.Game;
using VRage.Game.Gui;
using VRageMath;
using VRageRender;
using VRage.Utils;
using Color=System.Drawing.Color;
using RectangleF=VRageMath.RectangleF;

namespace SpaceEngineersVR.Player
{
    internal static class SignalPainter
    {
        private static OverlayCanvas labels;
        private static ShaderResourceView textTexture;
        private static string textKey,atlas;
        private static string[] textOwners=new string[0],textNames=new string[0],textFonts=new string[0];
        private static Vector4[] textColors=new Vector4[0];
        private static MyAtlasTextureCoordinate[] coordinates;
        private static DateTime nextText;
        private static readonly List<NativeSprite> sprites=new List<NativeSprite>();
        private static int textRevision=-1;
        internal static NativeSprite Atlas(MyHudTexturesEnum icon,Vector4 color)
        {
            if(coordinates==null) MyGuiScreenHudBase.LoadTextureAtlas(out atlas,out coordinates);
            var c=coordinates[(int)icon];
            return new NativeSprite(atlas,default(RectangleF),color) {UV=new Vector4(c.Offset,c.Size.X,c.Size.Y)};
        }
        internal static NativeSprite Artwork(NativeSprite sprite)
        { sprite.Tint=sprite.Tint.ToLinearRGB(); sprite.Premultiplied=true; return sprite; }
        private static void Project(List<NativeSprite> output,NativeSprite sprite,MarkerBillboard board,RectangleF bounds,MatrixD view,MatrixD projection)
        { sprite=Artwork(sprite); if(board.Project(bounds,view,projection,ref sprite)) output.Add(sprite); }
        internal static void Icon(List<NativeSprite> output,MarkerBillboard board,MatrixD view,MatrixD projection,WorldMarkers.Marker m,bool box=true,float alpha=1)
        {
            if(m.Kind=="OffscreenTarget") return;
            if(m.Symbol!=null) { Lines(output,board,view,projection,m); return; }
            if(m.Kind=="Target") { Project(output,Atlas(MyHudTexturesEnum.TargetTurret,Vector4.One),board,new RectangleF(-.5f,-.5f,1,1),view,projection); return; }
            if(box && m.Kind!="Objective") Project(output,Atlas(MyHudTexturesEnum.Target_neutral,m.Color),board,new RectangleF(-.5f,-.5f,1,1),view,projection);
            if(m.Kind=="ContractGPS" || m.Kind=="ButtonMarker") return;
            var color=m.Color; color.W*=alpha;
            var icon=m.Kind=="Ore" ? Atlas(MyHudTexturesEnum.HudOre,color):m.Kind=="Hack" ? Atlas(MyHudTexturesEnum.hit_confirmation,color):new NativeSprite(m.Icon,default(RectangleF),color);
            float scale=m.Kind=="Ore" || m.Kind=="Hack" ? .8f:.625f;
            Project(output,icon,board,new RectangleF(-scale/2,-scale/2,scale,scale),view,projection);
        }
        // Line width stays constant in icon units, so a larger Span frames the target instead of drawing a heavier icon.
        private const float LineWidth=.05f;
        private const string Blank=@"Textures\GUI\Blank.dds";
        private static void Lines(List<NativeSprite> output,MarkerBillboard board,MatrixD view,MatrixD projection,WorldMarkers.Marker m)
        {
            float half=m.Span/2;
            if(m.Symbol=="cross")
            {
                float gap=half*.35f;
                Line(output,board,view,projection,m.Color,-half,0,-gap,0); Line(output,board,view,projection,m.Color,gap,0,half,0);
                Line(output,board,view,projection,m.Color,0,-half,0,-gap); Line(output,board,view,projection,m.Color,0,gap,0,half);
                return;
            }
            float arm=Math.Min(half*.45f,.6f);
            for(int corner=0;corner<4;corner++)
            {
                float x=corner%2==0 ? -half:half,y=corner<2 ? -half:half;
                Line(output,board,view,projection,m.Color,x,y,x-Math.Sign(x)*arm,y);
                Line(output,board,view,projection,m.Color,x,y,x,y-Math.Sign(y)*arm);
            }
        }
        // An axis-aligned line between two points in icon units.
        private static void Line(List<NativeSprite> output,MarkerBillboard board,MatrixD view,MatrixD projection,Vector4 color,float x0,float y0,float x1,float y1)
        {
            const float w=LineWidth/2;
            Project(output,new NativeSprite(Blank,default(RectangleF),color),board,
                new RectangleF(Math.Min(x0,x1)-w,Math.Min(y0,y1)-w,Math.Abs(x1-x0)+2*w,Math.Abs(y1-y0)+2*w),view,projection);
        }
        // Chevron pointing along direction (screen space, y down), drawn over everything like the HUD.
        private static void Arrow(List<NativeSprite> output,MarkerBillboard board,MatrixD view,MatrixD projection,Vector4 color,Vector2 direction)
        {
            var tip=direction*.55f;
            Bar(output,board,tip,tip-Rotate(direction,.6f)*.7f,.09f,color,view,projection);
            Bar(output,board,tip,tip-Rotate(direction,-.6f)*.7f,.09f,color,view,projection);
        }
        private static Vector2 Rotate(Vector2 value,float angle)
        {
            float cos=(float)Math.Cos(angle),sin=(float)Math.Sin(angle);
            return new Vector2(value.X*cos-value.Y*sin,value.X*sin+value.Y*cos);
        }
        // A line of any direction between two points in icon units.
        private static void Bar(List<NativeSprite> output,MarkerBillboard board,Vector2 from,Vector2 to,float width,Vector4 color,MatrixD view,MatrixD projection)
        {
            var along=to-from;
            if(along.LengthSquared()<1e-8f) return;
            var normal=Vector2.Normalize(new Vector2(-along.Y,along.X))*width/2;
            MatrixD transform=view*projection;
            output.Add(new NativeSprite(Blank,default(RectangleF),color.ToLinearRGB()) {Projected=true,Premultiplied=true,IgnoreSceneDepth=true,
                TopLeft=Clip(board,from-normal,transform),TopRight=Clip(board,to-normal,transform),
                BottomLeft=Clip(board,from+normal,transform),BottomRight=Clip(board,to+normal,transform)});
        }
        private static Vector4 Clip(MarkerBillboard board,Vector2 point,MatrixD transform) =>
            (Vector4)Vector4D.Transform(new Vector4D(board.Point(point.X,point.Y),1),transform);
        internal static void GroupIcon(List<NativeSprite> output,MarkerBillboard board,MatrixD view,MatrixD projection,WorldMarkers.Marker[] values,Vector4 color,float expansion=0,float compactShift=0,WorldMarkers.Marker[] representatives=null)
        {
            Project(output,Atlas(MyHudTexturesEnum.Target_neutral,color),board,new RectangleF(-.5f,-.5f,1,1),view,projection);
            var members=representatives ?? SignalLayout.Representatives(values);
            for(int i=0;i<members.Length;i++)
            {
                var offset=SignalLayout.GroupOffset(i,expansion,compactShift);
                var small=board; small.Center=board.Point(offset.X,offset.Y); small.Scale*=.75;
                var member=members[i];
                var icon=member.Kind=="Scenario" ? @"Textures\HUD\marker_scenario.dds":@"Textures\HUD\marker_"+(SignalLayout.Relation(member)=="Own" ? "self":SignalLayout.Relation(member)=="Friendly" ? "friendly":SignalLayout.Relation(member)=="Hostile" ? "enemy":"neutral")+".dds";
                Project(output,new NativeSprite(icon,default(RectangleF),member.Color),small,new RectangleF(-.3125f,-.3125f,.625f,.625f),view,projection);
                if((member.Kind!="Scenario" || values.Select(SignalLayout.Relation).Distinct().Count()>1) && SignalLayout.HighAlert(member,values))
                    Project(output,new NativeSprite(@"Textures\HUD\marker_alert.dds",default(RectangleF),Vector4.One),small,new RectangleF(-.3125f,-.3125f,.625f,.625f),view,projection);
            }
        }
        internal static void Draw(Texture2D target,SignalLayout.Entry[] entries,MatrixD head,MatrixD view,MatrixD projection,bool faceViewer=false,Vector3D? facingUp=null)
        {
            if(labels==null)
            {
                labels=new OverlayCanvas("Signal names",WorldMarkers.AtlasWidth,WorldMarkers.AtlasHeight,1,false,target.Device,true);
                textTexture=new ShaderResourceView(target.Device,labels.Texture);
            }
            var text=entries.SelectMany(e=>e.Labels).ToArray();
            var owners=entries.SelectMany(e=>e.Labels.Select(_=>e.Id)).ToArray();
            var names=text.Select(l=>l.Name).ToArray();
            var colors=text.Select(l=>l.Color).ToArray();
            var fonts=text.Select(l=>l.Font).ToArray();
            string key=string.Join("\n",names)+"\n"+string.Join("\n",text.Select(l=>l.Distance+"|"+l.Color+"|"+l.Font+"|"+l.LeftAligned+"|"+l.Bounds.Width+"|"+l.LineHeight));
            bool contentChanged=!owners.SequenceEqual(textOwners,StringComparer.Ordinal) || !names.SequenceEqual(textNames,StringComparer.Ordinal) ||
                !Enumerable.SequenceEqual(colors,textColors) || !fonts.SequenceEqual(textFonts,StringComparer.Ordinal);
            if((contentChanged || textKey!=key || textRevision!=NativeSprites.Revision) && (contentChanged || DateTime.UtcNow>=nextText || textRevision!=NativeSprites.Revision))
            {
                labels.ClearTexture(); sprites.Clear();
                for(int i=0;i<text.Length;i++)
                {
                    var label=text[i];
                    float virtualWidth=label.Bounds.Width/label.LineHeight*31;
                    float x=i%2*WorldMarkers.LabelWidth,y=i/2*WorldMarkers.LabelHeight;
                    SignalFont.Add(sprites,label.Name,x,y,31,virtualWidth,label.Color,WorldMarkers.AtlasWidth,WorldMarkers.AtlasHeight,!label.LeftAligned,font:label.Font);
                    SignalFont.Add(sprites,label.Distance,x,y+65,29,virtualWidth,label.Color,WorldMarkers.AtlasWidth,WorldMarkers.AtlasHeight,!label.LeftAligned,font:label.Font);
                }
                NativeSprites.Draw(labels.Texture,sprites); target.Device.ImmediateContext.GenerateMips(textTexture); textRevision=NativeSprites.Revision;
                textKey=key; textOwners=owners; textNames=names; textColors=colors; textFonts=fonts; nextText=DateTime.UtcNow.AddMilliseconds(100);
            }
            sprites.Clear();
            int labelIndex=0;
            foreach(var e in entries)
            {
                if(e.Edge || !MarkerBillboard.TryCreate(e.Position,head,facingUp,faceViewer,out var board)) continue;
                board.Scale*=e.Primary.Kind=="Objective" ? 1.1:.55;
                if(e.Ring) e.Primary.Ring?.AddNative(sprites,e.Position,view,projection,target.Description.Width,facingUp,faceViewer);
                board.Scale*=e.IconScale;
                if(e.Arrow!=Vector2.Zero) Arrow(sprites,board,view,projection,e.Primary.Color,e.Arrow);
                else if(e.Members.Length==1) Icon(sprites,board,view,projection,e.Primary,alpha:e.SymbolAlpha);
                else
                {
                    GroupIcon(sprites,board,view,projection,e.Members,e.Color,e.Expansion,e.CompactShift,e.Representatives);
                }
                foreach(var label in e.Labels)
                {
                    double depth=Math.Max(.1,Vector3D.Dot(e.Position-head.Translation,head.Forward));
                    var b=label.Bounds;
                    for(int line=0;line<2;line++)
                    {
                        if(string.IsNullOrEmpty(line==0 ? label.Name:label.Distance)) continue;
                        float y=line==0 ? label.NameY:label.DistanceY;
                        var textSprite=new NativeSprite(null,default(RectangleF),new Vector4(1,1,1,line==0 ? label.NameAlpha:1)) {Texture=textTexture,
                            UV=new Vector4(labelIndex%2*.5f,(labelIndex/2*WorldMarkers.LabelHeight+(line==0 ? 0:65))/(float)WorldMarkers.AtlasHeight,
                                b.Width/label.LineHeight*31/WorldMarkers.AtlasWidth,31f/WorldMarkers.AtlasHeight)};
                        var textBoard=new MarkerBillboard {Center=e.Position,Right=board.Right,Up=board.Up,Scale=depth};
                        float scale=line==0 ? 1:label.DistanceScale;
                        if(!textBoard.Project(new RectangleF(b.X-e.Point.X+b.Width*(1-scale)/2,y-e.Point.Y,b.Width*scale,label.LineHeight*scale),view,projection,ref textSprite)) continue;
                        textSprite.Premultiplied=true; sprites.Add(textSprite);
                    }
                    labelIndex++;
                }
            }
            NativeSprites.Draw(target,sprites);
        }
    }
}
