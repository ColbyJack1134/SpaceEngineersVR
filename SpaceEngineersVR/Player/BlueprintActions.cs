using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.Screens.Helpers.RadialMenuActions;
using Sandbox.Game.World;
using VRage.Utils;

namespace SpaceEngineersVR.Player
{
    internal static class BlueprintActions
    {
        internal static bool Creative => MySession.Static!=null && (MySession.Static.CreativeMode || MySession.Static.CreativeToolsEnabled(Sync.MyId));
        private static bool SelectionAvailable => MySession.Static?.LocalCharacter?.IsDead==false && !PlacementControls.ClipboardActive;
        internal static bool ClipboardAllowed => MySession.Static?.IsCopyPastingEnabled==true && !(MySession.Static.ControlledEntity is MyShipController);
        internal static bool Allowed(string action) => action!="Copy" && action!="Cut" && action!="Delete" || ClipboardAllowed;
        internal static readonly ActionChoice Library=new ActionChoice("Blueprint library",()=>GameActions.BlueprintsAction.Run(),true,
            icon:NativeSprites.Hud("BlueprintsScreen"),enabled:()=>MySession.Static!=null);
        internal static readonly ActionChoice Create=new ActionChoice("Create blueprint",()=>new MyActionCreateBlueprint().ExecuteAction(),
            icon:NativeSprites.Hud("PasteGrid"),enabled:()=>SelectionAvailable);
        internal static readonly ActionChoice Copy=new ActionChoice("Copy",()=>new MyActionCopyGrid().ExecuteAction(),
            icon:NativeSprites.Hud("CopyGrid"),enabled:()=>ClipboardAllowed && SelectionAvailable);
        internal static readonly ActionChoice Cut=new ActionChoice("Cut",()=>new MyActionCutGrid().ExecuteAction(),
            icon:NativeSprites.Hud("CutGrid"),enabled:()=>ClipboardAllowed && SelectionAvailable);
        internal static readonly ActionChoice Paste=new ActionChoice("Paste",PlacementControls.PreviewClipboard,
            icon:NativeSprites.Hud("PasteGrid"),enabled:()=>ClipboardAllowed && SelectionAvailable);
        internal static readonly ActionChoice Delete=new ActionChoice("Delete",()=>NativeActions.Pulse(MyControlsSpace.DELETE_OBJECT),
            icon:NativeSprites.Hud("CutGrid"),enabled:()=>ClipboardAllowed && SelectionAvailable);
        private static readonly ActionChoice[] survival={Library,Create};
        private static readonly ActionChoice[] creative={Library,Create,Copy,Cut,Paste,Delete};
        internal static ActionChoice[] Choices(bool creativeTools) => creativeTools ? creative:survival;
        internal static bool IsAction(MySystemAction action) => action==MySystemAction.BlueprintsScreen || action==MySystemAction.CreateBlueprint ||
            action==MySystemAction.CopyGrid || action==MySystemAction.CutGrid || action==MySystemAction.PasteGrid;
        internal static bool IsControl(MyStringId control)
        {
            string name=control.String;
            return name=="PASTE_OBJECT" || name=="BLUEPRINTS_MENU" || name.StartsWith("CREATE_BLUEPRINT") ||
                name.StartsWith("COPY_OBJECT") || name.StartsWith("CUT_OBJECT") || name.StartsWith("DELETE_OBJECT");
        }
    }
}
