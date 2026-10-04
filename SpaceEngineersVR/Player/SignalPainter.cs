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
            if(m.Kind=="Target") { Project(output,Atlas(MyHudTexturesEnum.TargetTurret,Vector4.One),board,new RectangleF(-.5f,-.5f,1,1),view,projection); return; }
            if(box && m.Kind!="Objective") Project(output,Atlas(MyHudTexturesEnum.Target_neutral,m.Color),board,new RectangleF(-.5f,-.5f,1,1),view,projection);
            if(m.Kind=="ContractGPS" || m.Kind=="ButtonMarker") return;
            var color=m.Color; color.W*=alpha;
            var icon=m.Kind=="Ore" ? Atlas(MyHudTexturesEnum.HudOre,color):m.Kind=="Hack" ? Atlas(MyHudTexturesEnum.hit_confirmation,color):new NativeSprite(m.Icon,default(RectangleF),color);
            float scale=m.Kind=="Ore" || m.Kind=="Hack" ? .8f:.625f;
            Project(output,icon,board,new RectangleF(-scale/2,-scale/2,scale,scale),view,projection);
        }
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
            string names=string.Join("\n",text.Select(l=>l.Name));
            string key=names+"\n"+string.Join("\n",text.Select(l=>l.Distance+"|"+l.Color+"|"+l.Font+"|"+l.LeftAligned+"|"+l.Bounds.Width+"|"+l.LineHeight));
            if((textKey!=key || textRevision!=NativeSprites.Revision) && (DateTime.UtcNow>=nextText || textRevision!=NativeSprites.Revision || textKey==null || !textKey.StartsWith(names+"\n",StringComparison.Ordinal)))
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
                textKey=key; nextText=DateTime.UtcNow.AddMilliseconds(100);
            }
            sprites.Clear();
            int labelIndex=0;
            var eye=MatrixD.Invert(view).Translation;
            foreach(var e in entries)
            {
                if(e.Edge || !MarkerBillboard.TryCreate(e.Position,head,out var board)) continue;
                if(faceViewer) board.FaceViewer(eye,facingUp);
                board.Scale*=e.Primary.Kind=="Objective" ? 1.1:.55;
                if(e.Ring) e.Primary.Ring?.AddNative(sprites,e.Position,view,projection,target.Description.Width,facingUp,faceViewer);
                board.Scale*=e.IconScale;
                if(e.Members.Length==1) Icon(sprites,board,view,projection,e.Primary,alpha:e.SymbolAlpha);
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
