using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRage.Game.Gui;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class ShipCrosshair
    {
        internal sealed class View
        {
            public Vector3D Position,Up;
            public long Owner;
            public MyHudTexturesEnum Icon;
            public Vector4 Color;
            public Vector2 HalfSize;
            public float NativeWidth;
            public ShipReload.View Reload;
            public string Altitude;
            public Vector2 AltitudeSize,AltitudeOffset;
            public float AltitudeHeight,AltitudeSpacing;
            public string HorizonTexture;
            public Vector2 HorizonSize;
            public float HorizonPitch,HorizonRoll;
        }
        private static readonly FieldInfo sprites=AccessTools.Field(typeof(MyHudCrosshair),"m_sprites");
        private static readonly System.Type sprite=AccessTools.Inner(typeof(MyHudCrosshair),"SpriteInfo");
        private static readonly FieldInfo id=AccessTools.Field(sprite,"SpriteId"),visible=AccessTools.Field(sprite,"Visible"),
            icon=AccessTools.Field(sprite,"SpriteEnum"),color=AccessTools.Field(sprite,"Color"),size=AccessTools.Field(sprite,"HalfSize");

        internal const float HitScale=1;
        internal static Vector3D Aim(MatrixD controller) => controller.Translation+controller.Forward*1000;
        internal static bool Enabled(PluginConfig config,bool visorVisible) => config.ShipCrosshair && visorVisible && (config.ShowVitals || config.WaypointMode>0);
        internal static View Capture()
        {
            var session=MySession.Static;
            if(!(session?.ControlledEntity is MyShipController ship) || ship.Closed || ship.MarkedForClose ||
                RemoteView.Current!=null || HelmetHud.NativeSuppressed ||
                session.CameraController is MySpectatorCameraController) return null;
            var result=Read(MyHud.Crosshair,ship.WorldMatrix);
            if(result==null) return null;
            result.Owner=ship.EntityId; result.Reload=ShipReload.Capture(ship);
            if(ship.HorizonIndicatorEnabled && ship.CubeGrid.Physics!=null)
            {
                var gravity=-Sandbox.Game.GameSystems.MyGravityProviderSystem.CalculateNaturalGravityInPoint(ship.PositionComp.WorldMatrixRef.Translation);
                if(gravity.LengthSquared()>=.0025f)
                {
                    var horizon=Sandbox.Graphics.GUI.MyGuiConstants.TEXTURE_HUD_GRAVITY_HORIZON;
                    Horizon(result,MyGuiScreenHudSpace.GetHeightAboveSurface(ship.CubeGrid),gravity,ship.WorldMatrix,
                        Sandbox.Graphics.MyGuiManager.GetFont(MyStringHash.GetOrCompute("Blue")),.8f,
                        horizon.Texture,horizon.SizePx,Sandbox.Graphics.MyGuiManager.GetSafeFullscreenRectangle().Height);
                }
            }
            return result;
        }
        // Vanilla's DrawArtificialHorizonAndAltitude layout in native pixels around the crosshair center.
        internal static void Horizon(View value,float altitude,Vector3 gravity,MatrixD ship,VRageRender.MyFont font,float textScale,string texture,Vector2 textureSize,float screenHeight)
        {
            float scale=screenHeight/1080;
            value.Altitude=System.Math.Round(altitude).ToString("0",System.Globalization.CultureInfo.InvariantCulture)+" m";
            value.AltitudeSize=font.MeasureString(value.Altitude,textScale*scale);
            value.AltitudeHeight=font.MeasureString("",textScale*scale).Y;
            value.AltitudeSpacing=font.Spacing*value.AltitudeHeight/font.LineHeight;
            value.AltitudeOffset=new Vector2(0,.03f*screenHeight-value.AltitudeSize.Y/2);
            var up=Vector3.Normalize(gravity);
            value.HorizonTexture=texture; value.HorizonSize=textureSize*.4f*scale;
            value.HorizonPitch=(float)Vector3D.Dot(up,ship.Forward)*.35f*screenHeight;
            var local=new Vector2((float)Vector3D.Dot(ship.Right,up),(float)Vector3D.Dot(ship.Up,up));
            value.HorizonRoll=local.LengthSquared()>1e-5f ? (float)System.Math.Atan2(local.Y,local.X):0;
        }
        internal static View Read(MyHudCrosshair crosshair,MatrixD controller)
        {
            if(crosshair==null || !controller.IsValid()) return null;
            foreach(object value in (IEnumerable)sprites.GetValue(crosshair))
                if((MyStringId)id.GetValue(value)==MyStringId.GetOrCompute("Default") && (bool)visible.GetValue(value))
                    return new View {Position=Aim(controller),Up=controller.Up,Icon=(MyHudTexturesEnum)icon.GetValue(value),
                        Color=((Color)color.GetValue(value)).ToVector4(),HalfSize=(Vector2)size.GetValue(value),
                        NativeWidth=NativeWidth((Vector2)size.GetValue(value),Sandbox.Graphics.MyGuiManager.GetSafeFullscreenRectangle().Width)};
            return null;
        }
        internal static void Draw(Texture2D target,View value,MatrixD head,MatrixD view,MatrixD projection,bool faceViewer=false,float hitScale=HitScale)
        {
            if(value==null || !WorldMarkers.Project(value.Position,view,projection,out _) ||
                !MarkerBillboard.TryCreate(value.Position,head,value.Up,faceViewer,out var board)) return;
            board.Scale*=(Common.Config?.CrosshairScale ?? PluginConfig.DefaultCrosshairScale)/PluginConfig.DefaultCrosshairScale;
            var glyph=SignalPainter.Artwork(SignalPainter.Atlas(value.Icon,value.Color)); glyph.NativeGui=true;
            var extent=value.HalfSize/.02f;
            if(board.Project(new RectangleF(-extent.X/2,-extent.Y/2,extent.X,extent.Y),view,projection,ref glyph))
                NativeSprites.Draw(target,new[] {glyph});
            var pixels=board; pixels.Scale*=extent.X/value.NativeWidth;
            ShipReload.Draw(target,value.Reload,pixels,view,projection);
            DrawHorizon(target,value,pixels,view,projection);
            var hit=RemoteCombat.Hit;
            if(hit==null || hit.Owner!=value.Owner || (System.DateTime.UtcNow-hit.Time).TotalSeconds>.3) return;
            var marker=RemoteCombat.HitSprite(hit);
            float width=hit.Size.X*hitScale,height=hit.Size.Y*hitScale;
            if(pixels.Project(new RectangleF(-width/2,-height/2,width,height),view,projection,ref marker))
                NativeSprites.Draw(target,new[] {marker});
        }
        private static void DrawHorizon(Texture2D target,View value,MarkerBillboard pixels,MatrixD view,MatrixD projection)
        {
            if(value.HorizonTexture==null) return;
            var output=new List<NativeSprite>();
            var glyphs=new List<NativeSprite>();
            SignalFont.Add(glyphs,value.Altitude,value.AltitudeOffset.X,value.AltitudeOffset.Y,value.AltitudeHeight,value.AltitudeSize.X+1,Vector4.One,
                target.Description.Width,target.Description.Height,font:"Blue",spacing:value.AltitudeSpacing,nativeGui:true);
            foreach(var glyph in glyphs)
            {
                var sprite=glyph; sprite.NativeGui=true;
                if(pixels.Project(glyph.Bounds,view,projection,ref sprite)) output.Add(sprite);
            }
            // Vanilla rotates the sprite about the crosshair center, so pitch slides along the rolled down axis.
            var rolled=pixels; double sin=System.Math.Sin(value.HorizonRoll),cos=System.Math.Cos(value.HorizonRoll);
            rolled.Right=pixels.Right*sin-pixels.Up*cos; rolled.Up=pixels.Right*cos+pixels.Up*sin;
            rolled.Center=rolled.Point(0,value.HorizonPitch);
            var horizon=SignalPainter.Artwork(new NativeSprite(value.HorizonTexture,default(RectangleF),Vector4.One)); horizon.NativeGui=true;
            if(rolled.Project(new RectangleF(-value.HorizonSize.X/2,-value.HorizonSize.Y/2,value.HorizonSize.X,value.HorizonSize.Y),view,projection,ref horizon)) output.Add(horizon);
            if(output.Count>0) NativeSprites.Draw(target,output.ToArray());
        }
        internal static float NativeWidth(Vector2 halfSize,int screenWidth) => 2*halfSize.X*(screenWidth>0 ? screenWidth:1920);
    }
}
