using Sandbox.Game;
using Sandbox.Game.SessionComponents.Clipboard;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class PlacementControls
    {
        public static bool ClipboardActive => MyClipboardComponent.Static?.IsActive == true;
        public static bool OwnsTools => InputRouter.Mode == InputMode.Building || InputRouter.Mode == InputMode.Clipboard;
        private static object poseOwner;
        private static MatrixD? lastPose;

        // Preserve the preview while a wheel/menu owns the hands. Never use a stale
        // pose from another character, or move a preview with untracked input.
        public static bool TryPose(out MatrixD pose)
        {
            object owner = MySession.Static?.ControlledEntity;
            if (!ReferenceEquals(owner, poseOwner)) { lastPose = null; poseOwner = owner; }
            bool active = ClipboardActive || Sandbox.Game.Entities.MyCubeBuilder.Static?.IsActivated == true;
            if (!Main.VrActive || !active) { lastPose = null; pose = MatrixD.Identity; return false; }
            if (OwnsTools && !Main.MenuOpen && HandInteraction.TryWorldPose(Player.HandR, out pose)) lastPose = pose;
            pose = lastPose ?? MatrixD.Identity;
            return lastPose.HasValue;
        }

        internal static void Queue(ActionFrame frame, InputMode mode, bool primary, bool secondary, bool alternate)
        {
            if (mode == InputMode.Clipboard)
            {
                // Cancel wins if both are pressed; clipboard actions cannot also shoot/remove.
                if (secondary) frame.Queue(MyControlsSpace.COPY_PASTE_CANCEL);
                else if (primary) frame.Queue(MyControlsSpace.COPY_PASTE_ACTION);
            }
            else if (mode == InputMode.Building)
            {
                // Building owns right grip even with the jetpack on. Alternate trigger
                // is only a flight-tool option, never an implicit destructive build mode.
                if (secondary) frame.Queue(MyControlsSpace.SECONDARY_TOOL_ACTION);
                else if (primary) frame.Queue(MyControlsSpace.PRIMARY_TOOL_ACTION);
            }
        }
        public static void Cancel() { if (ClipboardActive) NativeActions.Pulse(MyControlsSpace.COPY_PASTE_CANCEL); }
        public static void PreviewClipboard()
        {
            if (!ClipboardActive) NativeActions.Pulse(MyControlsSpace.PASTE_OBJECT);
        }
        public static void FreeRotation() { if (ClipboardActive) NativeActions.Pulse(MyControlsSpace.FREE_ROTATION); }
    }
}
