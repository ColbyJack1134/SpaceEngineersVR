using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.Screens.Helpers.RadialMenuActions;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace SpaceEngineersVR.Player
{
    internal sealed class ActionChoice
    {
        private readonly string label;
        private readonly Func<string> labelProvider;
        public string Label => labelProvider?.Invoke() ?? label;
        public readonly Action Run;
        public readonly bool OpensMenu;
        private readonly string icon;
        private readonly Func<bool> enabled;
        public readonly string SearchTerms;
        public bool Enabled => enabled?.Invoke() ?? true;
        public string Icon => icon ?? NativeSprites.Hud(IconName());
        public ActionChoice(string label, Action run, bool opensMenu = false,string icon=null,Func<bool> enabled=null,string searchTerms=null) { SearchTerms=searchTerms; this.label=label; Run=run; OpensMenu=opensMenu; this.icon=icon; this.enabled=enabled; }
        public ActionChoice(Func<string> label,Action run) { labelProvider=label; Run=run; }
        private string IconName()
        {
            if (Label.StartsWith("Yaw") || Label.StartsWith("Pitch") || Label.StartsWith("Roll") || Label.StartsWith("Camera:")) return "BlockRotate";
            switch (Label)
            {
                case "Inventory": case "Planner / deposit UI": return "OpenInventory";
                case "Terminal": return "ToggleConnectedGrid";
                case "G menu / toolbar": return "RadialMenu";
                case "Reload": return "ReloadGame";
                case "Lights": return "Light";
                case "Helmet": return "PlayerHelmetOn";
                case "Dampeners": return "Dampeners";
                case "Auto dampeners": return "DampenersAuto";
                case "Jetpack": return "Jetpack";
                case "Pause": case "Pause / save / exit": return "PauseIcon";
                case "VR options": return "AdminMenu";
                case "Controller help": return "HelpScreen";
                case "Add to build planner": return "MultiBlockBuilding";
                case "Withdraw planner": return "Backpack";
                case "Landing gear / park": return "Handbrake";
                case "Power": return "GridPowerOn";
                case "Broadcast": return "ToggleBroadcasting";
                case "Detach boots": return "Magboot";
                case "Toggle trigger action": return "DrillIcon";
                case "Signal visibility": return "SignalMode";
                case "Closer": return "MoveCloser";
                case "Further": return "MoveFurther";
                case "Large / small": return "GridSize";
                case "Previous variant": return "ValueDecrease";
                case "Next variant": return "ValueIncrease";
                case "Color / skin palette": case "Paint tool": case "Pick color": return "ColorPicker";
                case "Symmetry on / off": return "Symmetry";
                case "Symmetry planes": return "SymmetrySetup";
                case "Placement mode": return "PlacementMode";
                case "Align mount point": return "Autorotate";
                default: return "RadialMenu";
            }
        }
    }

    internal static class GameActions
    {
        private static ActionChoice pending;
        private static object pendingOwner;
        private static DateTime pendingUntil;
        internal static ActionChoice[] Search(string query) => ActionCatalog.Search(query);
        public static readonly ActionChoice UnequipAction=new ActionChoice("Unequip / cancel preview",Unequip);
        public static readonly ActionChoice RecenterAction=new ActionChoice("Recenter",()=>Player.Headset.RequestRecenter(),searchTerms:"Ctrl Alt R hotkey");
        public static readonly ActionChoice AllActions=new ActionChoice("All actions",()=>MyGuiSandbox.AddScreen(new GUI.ActionBrowser()),true);
        public static readonly ActionChoice RelativeDampeners=new ActionChoice("Auto dampeners",DampenerTargeting.Activate);
        public static readonly ActionChoice Dampeners=Native("Dampeners",MyControlsSpace.DAMPING);
        public static readonly ActionChoice ResetFeed=new ActionChoice("Reset camera screen",RemoteView.Recenter);
        public static readonly ActionChoice ExitFeed=new ActionChoice("Exit camera control",RemoteView.Exit);
        public static readonly ActionChoice Inspect=new ActionChoice(()=>Common.Config?.InspectWithoutGrip==true ? "Block info: automatic" : "Block info: hold grip",()=>Common.Config.InspectWithoutGrip=!Common.Config.InspectWithoutGrip);
        public static readonly ActionChoice PlayPosture=new ActionChoice(()=>Common.Config?.SeatedPlay==true ? "Switch to standing play":"Switch to seated play",()=>BodyFit.SetSeated(!Common.Config.SeatedPlay));
        public static readonly ActionChoice Tablet=new ActionChoice("Tablet",SpatialUi.Expand);
        public static readonly ActionChoice Options=new ActionChoice("VR options",()=>Common.Plugin.OpenConfigDialog(),true);
        public static void Schedule(ActionChoice action)
        {
            foreach(var screen in MyScreenManager.Screens.ToArray())
                if(screen.GetType().Namespace=="SpaceEngineersVR.GUI") screen.CloseScreenNow();
            pending=action; pendingOwner=MySession.Static?.ControlledEntity; pendingUntil=DateTime.UtcNow.AddSeconds(2);
        }
        public static void RunScheduled()
        {
            if(pending==null) return;
            if(DateTime.UtcNow>pendingUntil || !ReferenceEquals(pendingOwner,MySession.Static?.ControlledEntity) || !MenuPointer.GameFocused)
            { pending=null; pendingOwner=null; return; }
            if(!InputRouter.Gameplay || Main.MenuOpen) return;
            var action=pending; pending=null; pendingOwner=null; Execute(action);
        }
        public static ActionChoice[] WheelActions(bool building,bool seated,bool thirdPerson,bool jetpack=false)
        {
            if(RemoteView.Active) return new[] { PauseAction,Options,TerminalAction,ExitFeed,ResetFeed,ConfigureToolbarAction,Native("Previous camera",MyControlsSpace.SWITCH_LEFT),Native("Next camera",MyControlsSpace.SWITCH_RIGHT),Inspect };
            if(thirdPerson) return new[] { PauseAction,Options,TerminalAction,Quick[22],LightsAction,Dampeners,PowerAction,ParkAction,BroadcastAction };
            if(building && PlacementControls.ClipboardActive) return ClipboardActions();
            if(building) return new[] { PauseAction,Options,TerminalAction,Building[17],PlacementAction,Building[8],Building[9],Building[10],PaletteAction };
            if(seated) return new[] { PauseAction,Options,TerminalAction,LightsAction,Dampeners,PowerAction,ParkAction,HelmetAction,BroadcastAction };
            if(jetpack) return new[] { PauseAction,Options,TerminalAction,RelativeDampeners,Dampeners,JetpackAction,LightsAction,HelmetAction,BroadcastAction };
            return new[] { PauseAction,Options,TerminalAction,JetpackAction,LightsAction,HelmetAction,BroadcastAction,Quick[11],Quick[12] };
        }
        internal static ActionChoice[] ClipboardActions() => new[] { PauseAction,Options,TerminalAction,AlignGravity,Building[21],Building[20],Building[6],Building[7],BlueprintsAction };
        public static ActionChoice[] TabletActions(bool building,bool seated,bool thirdPerson,bool jetpack)
        {
            var actions=new List<ActionChoice> { PauseAction,Options,InventoryAction,TerminalAction,LightsAction,HelmetAction,
                seated ? PowerAction:JetpackAction,Dampeners,BroadcastAction,HudOptions };
            if(seated) actions.Add(ParkAction);
            else { if(jetpack) actions.Add(RelativeDampeners); if(!building) actions.Add(Quick[3]); actions.Add(DetachBootsAction); }
            if(thirdPerson) actions.Add(Quick[22]);
            if(building) actions.AddRange(new[] { Building[17],Building[8],PaletteAction,SymmetryAction });
            return actions.ToArray();
        }
        public static readonly ActionChoice HudOptions=new ActionChoice("HUD",()=>MyGuiSandbox.AddScreen(new GUI.SettingsPage("HUD & Interface")),true);
        public static bool AlternateTrigger { get; private set; }
        private static ActionChoice Native(string label, MyStringId control) => new ActionChoice(label, () => NativeActions.Pulse(control));
        public static readonly ActionChoice InventoryAction = new ActionChoice("Inventory", () => MySession.Static.ControlledEntity?.ShowInventory(), true);
        public static readonly ActionChoice TerminalAction = new ActionChoice("Terminal", () => MySession.Static.ControlledEntity?.ShowTerminal(), true);
        public static readonly ActionChoice ConfigureToolbarAction = new ActionChoice("G menu / toolbar", ToolbarConfig, true);
        public static readonly ActionChoice LightsAction = new ActionChoice("Lights", () => new MyActionToggleLights().ExecuteAction());
        public static readonly ActionChoice HelmetAction = new ActionChoice("Helmet", () => ((IMyControllableEntity)MySession.Static?.LocalCharacter)?.SwitchHelmet());
        public static readonly ActionChoice AlignGravity = new ActionChoice("Align to gravity",PlacementControls.AlignGravity);
        public static readonly ActionChoice JetpackAction = new ActionChoice("Jetpack", () => { if (MySession.Static.ControlledEntity == MySession.Static.LocalCharacter) ((IMyCharacter)MySession.Static.LocalCharacter).SwitchThrusts(); });
        public static readonly ActionChoice PauseAction = new ActionChoice("Pause", PauseMenu, true);
        public static readonly ActionChoice ParkAction = new ActionChoice("Landing gear / park", () => {
            var controlled=MySession.Static?.ControlledEntity;
            if(controlled?.CanSwitchLandingGears==true) controlled.SwitchLandingGears();
        });
        public static readonly ActionChoice PowerAction = new ActionChoice("Power", () => new MyActionTogglePower().ExecuteAction());
        public static readonly ActionChoice BroadcastAction = new ActionChoice("Broadcast", () => new MyActionToggleBroadcasting().ExecuteAction());
        public static readonly ActionChoice DetachBootsAction = new ActionChoice("Detach boots", () => {
            var character=MySession.Static?.LocalCharacter;
            if(character!=null && MySession.Static.ControlledEntity==character && character.IsMagneticBootsActive)
                character.Jump(VRageMath.Vector3.Zero);
        });
        public static readonly ActionChoice PaletteAction = new ActionChoice("Color / skin palette", () => new MyActionColorPicker().ExecuteAction(), true);
        public static readonly ActionChoice PaintAction = new ActionChoice("Paint tool", () => new MyActionColorTool().ExecuteAction());
        public static readonly ActionChoice SymmetryAction = new ActionChoice("Symmetry on / off", () => new MyActionToggleSymmetry().ExecuteAction());
        public static readonly ActionChoice SymmetrySetupAction = new ActionChoice("Symmetry planes", () => new MyActionSymmetrySetup().ExecuteAction());
        public static readonly ActionChoice PlacementAction = new ActionChoice("Placement mode", () => new MyActionPlacementMode().ExecuteAction());
        public static readonly ActionChoice BlueprintsAction = new ActionChoice("Blueprints", () => new MyActionBlueprintScreen().ExecuteAction(), true);

        public static readonly ActionChoice[] Quick = {
            InventoryAction,
            TerminalAction,
            ConfigureToolbarAction,
            Native("Reload", MyControlsSpace.RELOAD),
            LightsAction,
            HelmetAction,
            Dampeners,
            JetpackAction,
            PauseAction,
            new ActionChoice("VR options", () => Common.Plugin.OpenConfigDialog(), true),
            new ActionChoice("Controller help", () => MyGuiSandbox.AddScreen(new GUI.BindingHelp()), true),
            new ActionChoice("Add to build planner", AddToPlanner),
            Native("Withdraw planner", MyControlsSpace.BUILD_PLANNER),
            new ActionChoice("Planner / deposit UI", () => MySession.Static.ControlledEntity?.ShowInventory(), true),
            ParkAction,
            ParkAction,
            PowerAction,
            new ActionChoice("Toggle trigger action", () => AlternateTrigger = !AlternateTrigger),
            new ActionChoice("Signal visibility", () => Common.Config.WaypointMode=(Common.Config.WaypointMode+1)%3),
            BlueprintsAction,
            new ActionChoice("Switch view",ThirdPersonView.Toggle),
            new ActionChoice("Reset ship view",ThirdPersonView.ResetView),
            new ActionChoice(()=>ThirdPersonView.ModeLabel,ThirdPersonView.CycleMode),
            BroadcastAction,
            DetachBootsAction,
            RelativeDampeners,
            Tablet,
            Inspect,ResetFeed,ExitFeed,PlayPosture,
            new ActionChoice("Turrets and cameras",()=>MyGuiSandbox.AddScreen(new GUI.SettingsPage("Turrets and cameras")),true),
            new ActionChoice("Body and seated play",()=>MyGuiSandbox.AddScreen(new GUI.BodyOptions()),true)
        };
        public static readonly ActionChoice[] Developer = {
            new ActionChoice("Developer options", () => MyGuiSandbox.AddScreen(new GUI.DeveloperOptions()), true),
            new ActionChoice("Capture arm pose", Diagnostics.ArmPoseCapture.Request),
            new ActionChoice(()=>PerformanceHud.Enabled ? "Hide performance" : "Show performance", PerformanceHud.Toggle)
        };
        public static readonly ActionChoice[] Building = {
            Native("Yaw +", MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE),
            Native("Yaw -", MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE),
            Native("Pitch +", MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE),
            Native("Pitch -", MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE),
            Native("Roll +", MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE),
            Native("Roll -", MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE),
            Native("Closer", MyControlsSpace.MOVE_CLOSER),
            Native("Further", MyControlsSpace.MOVE_FURTHER),
            Native("Large / small", MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE),
            Native("Previous variant", MyControlsSpace.PREV_BLOCK_STAGE),
            Native("Next variant", MyControlsSpace.NEXT_BLOCK_STAGE),
            PaletteAction,
            PaintAction,
            Native("Pick color", MyControlsSpace.QUICK_PICK_COLOR),
            SymmetryAction,
            SymmetrySetupAction,
            PlacementAction,
            Native("Align mount point", MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT),
            BlueprintsAction,
            new ActionChoice("Preview clipboard", PlacementControls.PreviewClipboard),
            new ActionChoice("Cancel preview", PlacementControls.Cancel),
            new ActionChoice("Preview free rotation", PlacementControls.FreeRotation),
            AlignGravity
        };

        public static void Execute(ActionChoice choice)
        {
            if(!choice.Enabled) return;
            if(MenuKeyboard.Standalone) MenuKeyboard.Close();
            choice.Run();
            if (choice.OpensMenu || VRGUIManager.IsAnyDialogOpen()) Main.MenuOpen = true;
            InputRouter.Update();
        }
        public static void Unequip()
        {
            AlternateTrigger = false;
            if (PlacementControls.ClipboardActive) { PlacementControls.Cancel(); return; }
            new MyActionUnequip().ExecuteAction();
        }
        private static readonly Control.JumpHold jumpHold=new Control.JumpHold();
        internal static void ResetJumpHold() => jumpHold.Reset();
        public static void Reset() { AlternateTrigger=false; ResetJumpHold(); }
        public static void ToolbarConfig() => ToolbarConfig(-1);
        public static void AssignToolbarSlot(int slot)
        {
            ToolbarConfig(slot);
            // AddScreen can queue the screen until the next native UI update.
            // Claim menu ownership immediately, as other menu actions do.
            Main.MenuOpen=true;
            InputRouter.Update();
        }
        private static void ToolbarConfig(int slot)
        {
            if (MyGuiScreenToolbarConfigBase.Static != null) return;
            var screen=MyGuiSandbox.CreateScreen(MyPerGameSettings.GUI.ToolbarConfigScreen,0,MyToolbarComponent.CurrentToolbar?.Owner as MyCubeBlock,null);
            if(slot>=0 && screen.Controls.GetControlByName("LabelToolbar") is MyGuiControlLabel label)
                label.Text="Assign slot "+(slot+1)+" · drag item below";
            MyGuiSandbox.AddScreen(MyGuiScreenGamePlay.ActiveGameplayScreen=screen);
        }
        public static void PauseMenu()
        {
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateScreen(MyPerGameSettings.GUI.MainMenu, !MySandboxGame.IsPaused));
        }
        public static void AddToPlanner()
        {
            var block = MyCubeBuilder.Static?.CurrentBlockDefinition;
            if (block == null)
                EssentialHud.Notify("Select a block before adding it to the build planner");
            else if(MySession.Static.LocalCharacter?.AddToBuildPlanner(block) != true)
                EssentialHud.Notify("Could not add this block to the build planner");
        }

        public static void HandleButtons()
        {
            var c = Controls.Static;
            if(InputRouter.Mode==InputMode.Piloting || InputRouter.Mode==InputMode.Turret)
            {
                if(c.SeatTerminal.HasPressed) { Execute(TerminalAction); return; }

            }
            var jump=InputRouter.Flying ? c.SeatTerminal:c.JumpOrClimbUp;
            bool character=InputRouter.Gameplay && InputRouter.Mode!=InputMode.Piloting &&
                MySession.Static?.ControlledEntity==MySession.Static?.LocalCharacter;
            if(jumpHold.Update(character,jump.HasPressed,jump.IsPressed,DateTime.UtcNow)) Execute(JetpackAction);
            if (c.Interact.HasPressed || c.Terminal.HasPressed || c.Inventory.HasPressed)
                HandInteraction.RefreshTarget();
            if (c.Reload.HasPressed) NativeActions.Pulse(MyControlsSpace.RELOAD);
            if (c.Interact.HasPressed && RemoteView.Active) { RemoteView.Exit(); InputRouter.Update(); return; }
            if (c.Interact.HasPressed)
            {
                if (!HandInteraction.TryInteract()) { MySession.Static.ControlledEntity?.Use(); HandInteraction.Feedback(); }
                InputRouter.Update();
                if (!InputRouter.Gameplay) return;
            }
            if (c.Helmet.HasPressed) Execute(HelmetAction);
            if (c.Jetpack.HasPressed && !ToolbarWheel.QuickPending)
            {
                if(InputRouter.Mode==InputMode.Building && c.Secondary.IsPressed) Building[8].Run();
                else Execute(JetpackAction);
            }
            if (c.Lights.HasPressed) Execute(LightsAction);
            if (c.Park.HasPressed) Execute(ParkAction);
            if (c.Power.HasPressed) Execute(PowerAction);
            if (c.Broadcasting.HasPressed) BroadcastAction.Run();
            if (c.Terminal.HasPressed) { Execute(TerminalAction); return; }
            if (c.Inventory.HasPressed) { Execute(InventoryAction); return; }
            if (c.ToolbarConfig.HasPressed || c.BlockSelector.HasPressed) { Execute(ConfigureToolbarAction); return; }
            if (c.Pause.HasPressed) { Execute(PauseAction); return; }
            if (c.CubeSize.HasPressed) NativeActions.Pulse(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE);
            if (c.BuildPlanner.HasPressed) AddToPlanner();
            if (c.ColorSelector.HasPressed) Execute(PaintAction);
            if (c.ColorPicker.HasPressed) Execute(PaletteAction);
            if (c.ToggleSymmetry.HasPressed) Execute(SymmetryAction);
            if (c.SymmetrySetup.HasPressed) Execute(SymmetrySetupAction);
            if (c.PlacementMode.HasPressed) Execute(PlacementAction);
            if (c.ToggleSignals.HasPressed) new MyActionToggleSignals().ExecuteAction();
            if (c.ToggleView.HasPressed) ThirdPersonView.Toggle();
            if (c.CutGrid.HasPressed) new MyActionCutGrid().ExecuteAction();
            if (c.CopyGrid.HasPressed) new MyActionCopyGrid().ExecuteAction();
            if (c.PasteGrid.HasPressed) new MyActionPasteGrid().ExecuteAction();
            if (c.Chat.HasPressed) new MyActionChat().ExecuteAction();
            if (c.Contract.HasPressed) new MyActionTradeLedgerScreen().ExecuteAction();
            if (c.Respawn.HasPressed) MySession.Static.ControlledEntity?.Die();
        }
    }
}
