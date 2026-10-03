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
        private static void Project(List<NativeSprite> output,NativeSprite sprite,MarkerBillboard board,RectangleF bounds,MatrixD view,MatrixD projection)
        { if(board.Project(bounds,view,projection,ref sprite)) output.Add(sprite); }
        internal static void Icon(List<NativeSprite> output,MarkerBillboard board,MatrixD view,MatrixD projection,WorldMarkers.Marker m,bool box=true)
        {
            if(m.Kind=="OffscreenTarget") return;
            if(m.Kind=="Target") { Project(output,Atlas(MyHudTexturesEnum.TargetTurret,Vector4.One),board,new RectangleF(-.5f,-.5f,1,1),view,projection); return; }
            if(box && m.Kind!="Objective") Project(output,Atlas(MyHudTexturesEnum.Target_neutral,m.Color),board,new RectangleF(-.5f,-.5f,1,1),view,projection);
            if(m.Kind=="ContractGPS" || m.Kind=="ButtonMarker") return;
            var icon=m.Kind=="Ore" ? Atlas(MyHudTexturesEnum.HudOre,m.Color):m.Kind=="Hack" ? Atlas(MyHudTexturesEnum.hit_confirmation,m.Color):new NativeSprite(m.Icon,default(RectangleF),m.Color);
            float scale=m.Kind=="Ore" || m.Kind=="Hack" ? .8f:.67f;
            Project(output,icon,board,new RectangleF(-scale/2,-scale/2,scale,scale),view,projection);
        }
        internal static void GroupIcon(List<NativeSprite> output,MarkerBillboard board,MatrixD view,MatrixD projection,WorldMarkers.Marker[] values,Vector4 color)
        {
            Project(output,Atlas(MyHudTexturesEnum.Target_neutral,color),board,new RectangleF(-.5f,-.5f,1,1),view,projection);
            var members=values.GroupBy(SignalLayout.Relation).Select(g=>g.First()).Concat(values).Distinct().Take(4).ToArray();
            for(int i=0;i<members.Length;i++)
            {
                var small=board; small.Center=board.Point((i%2==0 ? -.23:.23),(i<2 ? -.18:.18)); small.Scale*=.5;
                Icon(output,small,view,projection,members[i],false);
            }
        }
        internal static void Draw(Texture2D target,SignalLayout.Entry[] entries,MatrixD head,MatrixD view,MatrixD projection)
        {
            if(labels==null)
            {
                labels=new OverlayCanvas("Signal names",WorldMarkers.AtlasWidth,WorldMarkers.AtlasHeight,1,false,target.Device,true);
                textTexture=new ShaderResourceView(target.Device,labels.Texture);
            }
            var text=entries.SelectMany(e=>e.Labels).ToArray();
            string names=string.Join("\n",text.Select(l=>l.Name));
            string key=names+"\n"+string.Join("\n",text.Select(l=>l.Distance+"|"+l.Color+"|"+l.Bounds.Width+"|"+l.LineHeight));
            if((textKey!=key || textRevision!=NativeSprites.Revision) && (DateTime.UtcNow>=nextText || textRevision!=NativeSprites.Revision || textKey==null || !textKey.StartsWith(names+"\n",StringComparison.Ordinal)))
            {
                labels.Clear(Color.Transparent);
                labels.Upload(); sprites.Clear();
                for(int i=0;i<text.Length;i++)
                {
                    var label=text[i];
                    float virtualWidth=label.Bounds.Width/label.LineHeight*31;
                    float x=i%2*WorldMarkers.LabelWidth,y=i/2*WorldMarkers.LabelHeight;
                    SignalFont.Add(sprites,label.Name,x,y,31,virtualWidth,label.Color,WorldMarkers.AtlasWidth,WorldMarkers.AtlasHeight,true);
                    SignalFont.Add(sprites,label.Distance,x,y+65,29,virtualWidth,label.Color,WorldMarkers.AtlasWidth,WorldMarkers.AtlasHeight,true);
                }
                NativeSprites.Draw(labels.Texture,sprites); target.Device.ImmediateContext.GenerateMips(textTexture); textRevision=NativeSprites.Revision;
                textKey=key; nextText=DateTime.UtcNow.AddMilliseconds(100);
            }
            sprites.Clear();
            int labelIndex=0;
            foreach(var e in entries)
            {
                if(e.Edge || !MarkerBillboard.TryCreate(e.Position,head,out var board)) continue;
                board.Scale*=e.Primary.Kind=="Objective" ? 1.1:.55;
                if(e.Ring) e.Primary.Ring?.Add(sprites,board,view,projection);
                board.Scale*=e.IconScale;
                if(e.Members.Length==1) Icon(sprites,board,view,projection,e.Primary);
                else
                {
                    GroupIcon(sprites,board,view,projection,e.Members,e.Color);
                }
                foreach(var label in e.Labels)
                {
                    double depth=Math.Max(.1,Vector3D.Dot(e.Position-head.Translation,head.Forward));
                    var pose=head; pose.Translation=head.Translation+head.Forward*depth;
                    var b=label.Bounds;
                    for(int line=0;line<2;line++)
                    {
                        if(line==0 && string.IsNullOrEmpty(label.Name)) continue;
                        float y=line==0 ? label.NameY:label.DistanceY;
                        var textSprite=PhysicalSurface.Quad(textTexture,pose,new RectangleF((float)(b.X*depth),(float)(-y*depth),(float)(b.Width*depth),(float)(label.LineHeight*depth)),
                            new Vector4(labelIndex%2*.5f,(labelIndex/2*WorldMarkers.LabelHeight+(line==0 ? 0:65))/(float)WorldMarkers.AtlasHeight,
                                b.Width/label.LineHeight*31/WorldMarkers.AtlasWidth,31f/WorldMarkers.AtlasHeight),Vector4.One,view,projection);
                        textSprite.Premultiplied=true; sprites.Add(textSprite);
                    }
                    labelIndex++;
                }
            }
            NativeSprites.Draw(target,sprites);
        }
    }
}
