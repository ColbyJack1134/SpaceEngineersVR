using System.Collections;
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
            public MyHudTexturesEnum Icon;
            public Vector4 Color;
            public Vector2 HalfSize;
        }
        private static readonly FieldInfo sprites=AccessTools.Field(typeof(MyHudCrosshair),"m_sprites");
        private static readonly System.Type sprite=AccessTools.Inner(typeof(MyHudCrosshair),"SpriteInfo");
        private static readonly FieldInfo id=AccessTools.Field(sprite,"SpriteId"),visible=AccessTools.Field(sprite,"Visible"),
            icon=AccessTools.Field(sprite,"SpriteEnum"),color=AccessTools.Field(sprite,"Color"),size=AccessTools.Field(sprite,"HalfSize");

        internal static Vector3D Aim(MatrixD controller) => controller.Translation+controller.Forward*1000;
        internal static bool Enabled(PluginConfig config,bool visorVisible) => config.ShipCrosshair && visorVisible && (config.ShowVitals || config.WaypointMode>0);
        internal static View Capture()
        {
            var session=MySession.Static;
            if(!(session?.ControlledEntity is MyShipController ship) || ship.Closed || ship.MarkedForClose ||
                RemoteView.Current!=null || MyHud.MinimalHud || MyHud.IsHudMinimal || MyHud.CutsceneHud ||
                session.CameraController is MySpectatorCameraController) return null;
            return Read(MyHud.Crosshair,ship.WorldMatrix);
        }
        internal static View Read(MyHudCrosshair crosshair,MatrixD controller)
        {
            if(crosshair==null || !controller.IsValid()) return null;
            foreach(object value in (IEnumerable)sprites.GetValue(crosshair))
                if((MyStringId)id.GetValue(value)==MyStringId.GetOrCompute("Default") && (bool)visible.GetValue(value))
                    return new View {Position=Aim(controller),Up=controller.Up,Icon=(MyHudTexturesEnum)icon.GetValue(value),
                        Color=((Color)color.GetValue(value)).ToVector4(),HalfSize=(Vector2)size.GetValue(value)};
            return null;
        }
        internal static void Draw(Texture2D target,View value,MatrixD head,MatrixD view,MatrixD projection,bool faceViewer=false)
        {
            if(value==null || !WorldMarkers.Project(value.Position,view,projection,out _) ||
                !MarkerBillboard.TryCreate(value.Position,head,view,value.Up,faceViewer,out var board)) return;
            var glyph=SignalPainter.Atlas(value.Icon,value.Color);
            var extent=value.HalfSize/.02f;
            if(board.Project(new RectangleF(-extent.X/2,-extent.Y/2,extent.X,extent.Y),view,projection,ref glyph))
                NativeSprites.Draw(target,new[] {glyph});
        }
    }
}
