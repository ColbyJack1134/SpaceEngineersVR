using System.Linq;
using System.Text;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using SpaceEngineers.Game.GUI;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyGuiScreenMainMenu),nameof(MyGuiScreenMainMenu.RecreateControls))]
    internal static class PauseOptionsPatch
    {
        private static void Postfix(MyGuiScreenMainMenu __instance)
        {
            if(!Main.VrActive) return;
            Add(__instance);
        }
        internal static void Add(MyGuiScreenMainMenu screen)
        {
            var options=screen.Controls.OfType<MyGuiControlButton>().FirstOrDefault(c=>c.Name=="Options");
            if(options==null || screen.Controls.Any(c=>c.Name=="SEVR.Options")) return;
            var buttons=screen.Controls.OfType<MyGuiControlButton>().Where(c=>System.Math.Abs(c.Position.X-options.Position.X)<.001f).ToArray();
            foreach(var button in buttons.Where(c=>c.Position.Y<options.Position.Y)) button.Position-=new Vector2(0,MyGuiConstants.MENU_BUTTONS_POSITION_DELTA.Y);
            var added=new MyGuiControlButton(position:options.Position-MyGuiConstants.MENU_BUTTONS_POSITION_DELTA,
                size:options.Size,originAlign:options.OriginAlign,visualStyle:options.VisualStyle,
                text:new StringBuilder("VR options"),onButtonClick:b=>Common.Plugin.OpenConfigDialog()) { Name="SEVR.Options" };
            screen.Controls.Add(added);
            (AccessTools.Field(typeof(MyGuiScreenMainMenu),"m_elementGroup").GetValue(screen) as MyGuiControlElementGroup)?.Add(added);
        }
    }
}
