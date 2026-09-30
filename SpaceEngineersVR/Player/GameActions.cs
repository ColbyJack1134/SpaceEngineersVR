using System;
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
        public string Icon => NativeSprites.Hud(IconName());
        public ActionChoice(string label, Action run, bool opensMenu = false) { this.label=label; Run=run; OpensMenu=opensMenu; }
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
                case "Jetpack": return "Jetpack";
                case "Pause / save / exit": return "PauseIcon";
                case "VR options": return "AdminMenu";
                case "Controller help": return "HelpScreen";
                case "Add to build planner": return "MultiBlockBuilding";
                case "Withdraw planner": return "Backpack";
                case "Connectors": return "ToggleConnectors";
                case "Landing gear / park": return "Handbrake";
                case "Power": return "GridPowerOn";
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
        public static bool AlternateTrigger { get; private set; }
        private static ActionChoice Native(string label, MyStringId control) => new ActionChoice(label, () => NativeActions.Pulse(control));
        public static readonly ActionChoice InventoryAction = new ActionChoice("Inventory", () => MySession.Static.ControlledEntity?.ShowInventory(), true);
        public static readonly ActionChoice TerminalAction = new ActionChoice("Terminal", () => MySession.Static.ControlledEntity?.ShowTerminal(), true);
        public static readonly ActionChoice ConfigureToolbarAction = new ActionChoice("G menu / toolbar", ToolbarConfig, true);
        public static readonly ActionChoice LightsAction = new ActionChoice("Lights", () => new MyActionToggleLights().ExecuteAction());
        public static readonly ActionChoice HelmetAction = new ActionChoice("Helmet", () => new MyActionToggleVisor().ExecuteAction());
        public static readonly ActionChoice JetpackAction = new ActionChoice("Jetpack", () => { if (MySession.Static.ControlledEntity == MySession.Static.LocalCharacter) ((IMyCharacter)MySession.Static.LocalCharacter).SwitchThrusts(); });
        public static readonly ActionChoice PauseAction = new ActionChoice("Pause / save / exit", PauseMenu, true);
        public static readonly ActionChoice ParkAction = new ActionChoice("Landing gear / park", () => new MyActionToggleHandbrake().ExecuteAction());
        public static readonly ActionChoice PowerAction = new ActionChoice("Power", () => new MyActionTogglePower().ExecuteAction());
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
            new ActionChoice("Dampeners", () => MySession.Static.ControlledEntity?.SwitchDamping()),
            JetpackAction,
            PauseAction,
            new ActionChoice("VR options", () => Common.Plugin.OpenConfigDialog(), true),
            new ActionChoice("Controller help", () => MyGuiSandbox.AddScreen(new GUI.BindingHelp()), true),
            new ActionChoice("Add to build planner", AddToPlanner),
            Native("Withdraw planner", MyControlsSpace.BUILD_PLANNER),
            new ActionChoice("Planner / deposit UI", () => MySession.Static.ControlledEntity?.ShowInventory(), true),
            new ActionChoice("Connectors", () => new MyActionToggleConnectors().ExecuteAction()),
            ParkAction,
            PowerAction,
            new ActionChoice("Toggle trigger action", () => { AlternateTrigger = !AlternateTrigger; EssentialHud.Notify("Right trigger: " + (AlternateTrigger ? "SECONDARY" : "PRIMARY")); }),
            new ActionChoice("Signal visibility", () => new MyActionToggleSignals().ExecuteAction()),
            BlueprintsAction,
            new ActionChoice("Switch view",ThirdPersonView.Toggle),
            new ActionChoice("Reset ship view",ThirdPersonView.ResetView),
            new ActionChoice(()=>ThirdPersonView.ModeLabel,ThirdPersonView.CycleMode)
        };
        public static readonly ActionChoice[] Developer = {
            new ActionChoice("Developer options", () => MyGuiSandbox.AddScreen(new GUI.DeveloperOptions()), true),
            new ActionChoice("Capture arm pose", Diagnostics.ArmPoseCapture.Request)
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
            new ActionChoice("Preview free rotation", PlacementControls.FreeRotation)
        };

        public static void Execute(ActionChoice choice)
        {
            if(choice!=JetpackAction) EssentialHud.Notify(choice.Label);
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
        public static void Reset() => AlternateTrigger = false;
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
            var screen=MyGuiSandbox.CreateScreen(MyPerGameSettings.GUI.ToolbarConfigScreen,0,MySession.Static.ControlledEntity as MyShipController,null);
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
            if (block == null || MySession.Static.LocalCharacter?.AddToBuildPlanner(block) != true)
                EssentialHud.Notify("Select a block before adding it to the build planner");
        }

        public static void HandleButtons()
        {
            var c = Controls.Static;
            if(InputRouter.Mode==InputMode.Piloting)
            {
                if(c.SeatTerminal.HasPressed) { Execute(TerminalAction); return; }
                if(c.Jetpack.HasPressed) { Execute(InventoryAction); return; }
            }
            if (c.Interact.HasPressed || c.Terminal.HasPressed || c.Inventory.HasPressed)
                HandInteraction.RefreshTarget();
            if (c.Reload.HasPressed) NativeActions.Pulse(MyControlsSpace.RELOAD);
            if (c.Interact.HasPressed)
            {
                if (!HandInteraction.TryInteract()) MySession.Static.ControlledEntity?.Use();
                InputRouter.Update();
                if (!InputRouter.Gameplay) return;
            }
            if (c.Helmet.HasPressed) Execute(HelmetAction);
            if (c.Jetpack.HasPressed) Execute(JetpackAction);
            if (c.Lights.HasPressed) Execute(LightsAction);
            if (c.Park.HasPressed) Execute(ParkAction);
            if (c.Power.HasPressed) Execute(PowerAction);
            if (c.Broadcasting.HasPressed) new MyActionToggleBroadcasting().ExecuteAction();
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
