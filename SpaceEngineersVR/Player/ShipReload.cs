using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using SharpDX.Direct3D11;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class ShipReload
    {
        internal sealed class View
        {
            public SignalRing Ring;
            public Vector2 RingOffset,TextOffset,TextSize;
            public string Text,Font;
            public float TextHeight,TextSpacing;
            public Vector4 TextColor;
        }
        private delegate bool ReloadFactor(MyGridSelectionSystem selection,out float factor,out int count,MyDefinitionId? definition);
        private static readonly ReloadFactor reload=AccessTools.MethodDelegate<ReloadFactor>(AccessTools.Method(typeof(MyGridSelectionSystem),"GetBestGunReloadFactor"));
        private static readonly FieldInfo controls=AccessTools.Field(typeof(MyHudCrosshair),"m_statControls"),
            bindings=AccessTools.Field(typeof(MyStatControls),"m_bindings");
        private static readonly System.Type binding=AccessTools.Inner(typeof(MyStatControls),"StatBinding");
        private static readonly FieldInfo control=AccessTools.Field(binding,"Control"),stat=AccessTools.Field(binding,"Stat");
        private static readonly FieldInfo textFont=AccessTools.Field(typeof(MyStatControlText),"m_font");
        internal static View Capture(MyShipController ship)
        {
            if(!reload(ship.GridSelectionSystem,out float progress,out int count,null) || progress<=0) return null;
            var parent=controls.GetValue(MyHud.Crosshair) as MyStatControls;
            if(parent==null) return null;
            MyStatControlCircularProgressBar ring=null; MyStatControlText text=null;
            foreach(object entry in (IEnumerable)bindings.GetValue(parent))
            {
                var id=(stat.GetValue(entry) as VRage.ModAPI.IMyHudStat)?.Id;
                if(id==MyStringHash.GetOrCompute("controlled_reloading")) ring=control.GetValue(entry) as MyStatControlCircularProgressBar;
                if(id==MyStringHash.GetOrCompute("controlled_reloading_count")) text=control.GetValue(entry) as MyStatControlText;
            }
            return Read(ring,text,parent.Position,progress,count);
        }
        internal static View Read(MyStatControlCircularProgressBar ring,MyStatControlText text,Vector2 center,float progress,int count)
        {
            if(ring==null || progress<=0 || progress>=1 || count<=0) return null;
            var result=new View {Ring=SignalRing.Read(ring),RingOffset=ring.Position+ring.Size/2-center};
            result.Ring.Progress=progress;
            if(text!=null)
            {
                result.Text=count.ToString(System.Globalization.CultureInfo.InvariantCulture)+"x"; result.Font=text.Font;
                var font=(VRageRender.MyFont)textFont.GetValue(text);
                result.TextSize=font.MeasureString(result.Text,text.Scale);
                result.TextHeight=font.MeasureString("",text.Scale).Y;
                result.TextSpacing=font.Spacing*result.TextHeight/font.LineHeight;
                result.TextOffset=text.Position+text.Size/2-center-result.TextSize/2;
                result.TextColor=text.TextColorMask;
            }
            return result;
        }
        internal static void Draw(Texture2D target,View value,MarkerBillboard pixels,MatrixD view,MatrixD projection)
        {
            if(value==null) return;
            var ring=pixels; ring.Center=pixels.Point(value.RingOffset.X,value.RingOffset.Y); ring.Scale*=value.Ring.Size.X/4.5;
            var output=new List<NativeSprite>();
            value.Ring.Add(output,ring,view,projection,true);
            if(!string.IsNullOrEmpty(value.Text))
            {
                var glyphs=new List<NativeSprite>();
                SignalFont.Add(glyphs,value.Text,value.TextOffset.X,value.TextOffset.Y,value.TextHeight,value.TextSize.X+1,
                    value.TextColor,target.Description.Width,target.Description.Height,font:value.Font,spacing:value.TextSpacing,nativeGui:true);
                foreach(var glyph in glyphs)
                {
                    var sprite=glyph;
                    sprite.NativeGui=true;
                    if(pixels.Project(glyph.Bounds,view,projection,ref sprite)) output.Add(sprite);
                }
            }
            NativeSprites.Draw(target,output);
        }
    }
}
