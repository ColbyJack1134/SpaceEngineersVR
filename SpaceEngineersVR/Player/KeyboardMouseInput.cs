using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.SessionComponents;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Reads the same native movement inputs as MyGuiScreenGamePlay.MoveAndRotatePlayerOrCamera,
    // so controller and keyboard/mouse input reach the controlled entity through one MoveAndRotate call.
    internal static class KeyboardMouseInput
    {
        internal static void Add(IMyControllableEntity controlled,ref Vector3 move,ref Vector2 rotate,ref float roll)
        {
            if(!Common.Config.EnableKeyboardAndMouseControls || MyInput.Static==null) return;
            var mouse=MyInput.Static.GetRotation();
            // The headset owns the walking character's head pitch; jetpack pitch turns the body like gyros.
            if(controlled is MyCharacter character && !((IMyCharacter)character).EnabledThrusts) mouse.X=0;
            rotate+=mouse;
            if(MySessionComponentVoxelHand.Static?.BuildMode==true || MyCubeBuilder.Static?.IsBuildMode==true) return;
            move+=MyInput.Static.GetPositionDelta();
            roll+=MyInput.Static.GetRoll();
        }
    }
}
