using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // The VR keyboard cannot paste, so the GPS tab sends the selected GPS to chat; receiving games add it from the text.
    [HarmonyPatch]
    internal static class GpsShare
    {
        private static readonly System.Type controller=AccessTools.TypeByName("Sandbox.Game.Screens.Terminal.MyTerminalGpsController");
        private static readonly FieldInfo copy=AccessTools.Field(controller,"m_buttonCopy"),list=AccessTools.Field(controller,"m_listboxGps"),
            synced=AccessTools.Field(controller,"m_syncedGps");
        private static readonly MethodInfo trySync=AccessTools.Method(controller,"TrySync");
        private static MethodBase TargetMethod() => AccessTools.Method(controller,"Init");

        private static void Postfix(object __instance,IMyGuiControlsParent controlsParent)
        {
            if(!Plugin.Main.VrActive || !(copy.GetValue(__instance) is MyGuiControlButton source)) return;
            var share=new MyGuiControlButton(source.Position-new Vector2(source.Size.X+.004f,0),MyGuiControlButtonStyleEnum.Rectangular,source.Size,
                originAlign:source.OriginAlign,text:new System.Text.StringBuilder("Share in chat"),textScale:.8f,
                onButtonClick:_=>Send(__instance)) { Name="buttonShareInChat",IsAutoEllipsisEnabled=true,Enabled=source.Enabled };
            source.EnabledChanged+=c=>share.Enabled=c.Enabled;
            controlsParent.Controls.Add(share);
        }
        private static void Send(object instance)
        {
            var selected=(list.GetValue(instance) as MyGuiControlListbox)?.SelectedItems;
            if(selected==null || selected.Count==0) return;
            // As Copy does: unsaved edits are synced first and that copy is shared.
            var gps=(bool)trySync.Invoke(instance,null) ? synced.GetValue(instance) as MyGps : selected[0].UserData as MyGps;
            if(gps!=null) WristChat.Send(gps.ToString());
        }
    }
}
