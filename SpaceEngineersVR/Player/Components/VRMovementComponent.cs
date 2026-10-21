using Sandbox;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Plugin;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.World;
using System.Runtime.CompilerServices;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Player.Components
{

    internal class VRMovementComponent : MyCharacterComponent
    {
        private static VRMovementComponent active;
        private Sandbox.Game.Entities.IMyControllableEntity inputOwner;
        private bool virtualStickHeld;
        private bool firstMovementTick = true;
        private bool firstCameraAfterTick = true;
        private Matrix virtualStickOrigin;
        private bool hadControllerMovement;
        private bool wasShooting;
        private bool wasSecondary;
        // A vanilla secondary shot in progress; tracked apart from the press, which also drives targeting.
        private bool secondaryShooting;
        private bool lockChordWas;
        private readonly Control.InputGate stickSecondary=new Control.InputGate();
        private readonly Control.GripTap secondaryTap=new Control.GripTap();
        private readonly Control.TurnInput turnInput=new Control.TurnInput();
        internal static readonly Control.CrouchControl Crouch=new Control.CrouchControl();
        internal static float HeadDrop => -Player.Headset.deviceToPlayer.Translation.Y;
        private MyShipController jumpOwner;
        private System.DateTime nextInputTrace;

        public float RotationSpeed = 10;

        public override void OnAddedToContainer()
        {
            // The base registers this component for character update ticks.
            base.OnAddedToContainer();
            this.NeedsUpdateBeforeSimulation = true;
            this.NeedsUpdateAfterSimulation = true;
        }

        public override void UpdateBeforeSimulation()
        {
            TrackedArms.Restore(Character);
            if (Character != MySession.Static?.LocalCharacter) { StopInput(); return; }
            active = this;
            TraceInput();
            if (firstMovementTick) { Logger.Info("MOVEMENT component is receiving simulation ticks"); firstMovementTick = false; }
            bool keyboardOnly = InputRouter.KeyboardMouse;
            if (!Main.VrActive || !InputRouter.Gameplay && !keyboardOnly || Main.MenuOpen || MySandboxGame.IsPaused || Character.IsDead || !RemoteView.Live(MySession.Static.ControlledEntity) || ThirdPersonView.Manipulating && !ThirdPersonView.FlightWhileManipulating)
            {
                StopInput();
                // The body keeps turning and stepping with the player while a menu is open.
                if(Main.VrActive && Main.MenuOpen && Main.WorldAvailable && !MySandboxGame.IsPaused && !SpectatorView.Active) BodyLocomotion.Update(Character);
                return;
            }
            try
            {
                if(SpectatorView.Active) { StopInput(); SpectatorView.Move(); return; }
                if (!ReferenceEquals(inputOwner, MySession.Static.ControlledEntity)) StopInput();
                inputOwner = MySession.Static.ControlledEntity;
                DriveInput.Stop();
                if(jumpOwner!=null && !NativeActions.WheelJumpAllowed)
                { jumpOwner.WheelJump(false); jumpOwner=null; }
                // A standing character watching a camera or remote feed still turns and steps with the player.
                if(!RemoteView.Active || RemoteView.CharacterAnchor) BodyLocomotion.Update(Character);

                if(keyboardOnly) ApplyMoveAndRotation(Vector3.Zero,Vector2.Zero,0);
                else if(RemoteView.Turret) { RemoteView.ControlTurret(RotationSpeed); hadControllerMovement=true; }
                else if(FloatingWindows.OwnsInput) ApplyMoveAndRotation(Vector3.Zero,Vector2.Zero,0);
                else if(PlacementControls.Adjusting) ApplyMoveAndRotation(Vector3.Zero,Vector2.Zero,0);
                else if (MySession.Static.ControlledEntity is MyShipController)
                {
                    ControlShip();
                }

                else if (((IMyCharacter)Character).EnabledThrusts)
                {
                    ControlFlight();
                }
                else
                {
                    ControlWalk();
                }
                if(keyboardOnly || ThirdPersonView.Manipulating) StopTools(inputOwner);
                else ControlCommonFunctions();
            }
            catch (System.Exception ex) { StopInput(); Main.FailToMenu(ex, "VR input stopped"); }
        }

        private static readonly int[] lastBegin=new int[2];
        // A second press within half a second sets the gameplay screen's double-click flag, which EndShoot reads to keep a tool running.
        private static void DoubleTap(MyShootActionEnum action,bool pressed,bool onFoot)
        {
            var detected=Sandbox.Game.Gui.MyGuiScreenGamePlay.DoubleClickDetected;
            if(detected==null || (int)action>=detected.Length) return;
            if(!pressed) { detected[(int)action]=false; return; }
            int now=MySandboxGame.TotalGamePlayTimeInMilliseconds;
            if(onFoot && now-lastBegin[(int)action]<500) detected[(int)action]=true;
            else { detected[(int)action]=false; lastBegin[(int)action]=now; }
        }
        public override void UpdateAfterSimulation()
        {
            if(SpectatorView.Active) { SpectatorView.Publish(); return; }
            if (SeatFit.Eligible(SeatFit.Seat) && SeatFit.Seat.Pilot==Character)
            {
                TrackedArms.Update(Character);
                CockpitControls.RefreshVisuals();
                RemoteView.Refresh();
                SpatialUi.Publish();
                return;
            }
            if(!Main.VrActive || !CameraRig.Owns(Character) || (MySession.Static?.ControlledEntity!=Character && !RemoteView.CharacterAnchor) || Character.IsDead) return;
            try
            {
                // Input/body following runs before physics. Publish a fresh camera AFTER
                // physics too: at 100 m/s the old one-tick lag was about 1.67 metres.
                CameraRig.RefreshAfterSimulation(Character);
                RemoteView.Refresh();
                Patches.MotionToolPatch.Refresh(Character);
                TrackedArms.Update(Character);
                if(WeaponHandling.ToolEquipped) HandInteraction.Update();
                SpatialUi.Publish();
                if(firstCameraAfterTick) { Logger.Info("VR camera receives post-physics character updates"); firstCameraAfterTick=false; }
            }
            catch(System.Exception ex) { Main.FailToMenu(ex,"Post-physics VR camera update failed"); }
        }

        internal static void StopActive() => active?.StopInput();
        internal static void StopOwner(Sandbox.Game.Entities.IMyControllableEntity owner)
        { if(owner!=null && ReferenceEquals(active?.inputOwner,owner)) active.StopInput(); }
        private void TraceInput()
        {
            var c=Controls.Static;
            if(System.DateTime.UtcNow<nextInputTrace || !(c.Primary.RawPressed || c.LeftGripPressure.RawPosition.X>.55f || c.RightGripPressure.RawPosition.X>.55f || c.ThrustRotate.RawPosition.LengthSquared()>.1f)) return;
            nextInputTrace=System.DateTime.UtcNow.AddSeconds(5);
            var ship=MySession.Static?.ControlledEntity as MyShipController;
            Logger.Info("INPUT ownership: mode="+InputRouter.Mode+"; trigger="+c.Primary.RawPressed+"/"+c.Primary.IsPressed+
                "; flightActive="+c.ThrustRotate.Active+"; clipboard="+PlacementControls.ClipboardActive+"; adjust="+PlacementControls.Adjusting+
                "; screen="+(TouchScreenBridge.OwnsInput || ArthurLcdBridge.OwnsInput)+"; arthur="+ArthurLcdBridge.Status+"; feed="+RemoteView.OwnsInput+"; viewGrab="+ThirdPersonView.Manipulating+
                "; physicalOnly="+Common.Config.PhysicalShipControlsOnly+"; ship="+ship?.EntityId+"; control="+ship?.EnableShipControl+
                "; sticks="+CockpitControls.Status);
        }

        public override void OnCharacterDead() => StopInput();

        public override void OnBeforeRemovedFromContainer()
        {
            StopInput();
            if (active == this) active = null;
            base.OnBeforeRemovedFromContainer();
        }

        private void StopInput()
        {
            turnInput.Reset();
            jumpOwner?.WheelJump(false); jumpOwner=null; stickSecondary.Block(); DriveInput.Stop();
            var controlled = inputOwner;
            if (hadControllerMovement)
            {
                // Native ship stop can return after the pilot/control owner has detached.
                controlled?.MoveAndRotate(Vector3.Zero,Vector2.Zero,0);
                controlled?.MoveAndRotateStopped();
            }
            StopTools(controlled);
            hadControllerMovement = wasShooting = wasSecondary = secondaryShooting = lockChordWas = virtualStickHeld = false;
            inputOwner = null;
        }

        private void StopTools(Sandbox.Game.Entities.IMyControllableEntity controlled)
        {
            if(wasShooting) controlled?.EndShoot(MyShootActionEnum.PrimaryAction);
            if(secondaryShooting) controlled?.EndShoot(MyShootActionEnum.SecondaryAction);
            wasShooting=wasSecondary=secondaryShooting=false;
            PaintGun.Firing=PaintGun.Deep=false;
            stickSecondary.Block();
            secondaryTap.Update(false,false,true,System.DateTime.UtcNow);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlShip()
        {
            var controls = Controls.Static;
            var ship=(MyShipController)MySession.Static.ControlledEntity;
            if(NativeActions.WheelJumpAllowed)
            {
                jumpOwner=ship;
                ship.WheelJump(NativeActions.Read(Sandbox.Game.MyControlsSpace.WHEEL_JUMP,VRage.Input.MyControlStateType.PRESSED));
            }
            else if(jumpOwner!=null) { jumpOwner.WheelJump(false); jumpOwner=null; }

            Vector3 move=Vector3.Zero; Vector2 rotate=Vector2.Zero; float roll=0;
            bool controllerFlight=FlightAxes.ControllerInputAllowed(true,ThirdPersonView.Active || RemoteView.RemoteGrid && RemoteView.HomeSeat==null,Common.Config.PhysicalShipControlsOnly);
            if(controllerFlight || CockpitControls.NeedsControllerTranslation || CockpitControls.NeedsControllerRotation)
            {
                ReadFlightInput(true,out move,out rotate,out roll);
                FlightAxes.MissingStickAxes(controllerFlight,CockpitControls.NeedsControllerTranslation,CockpitControls.NeedsControllerRotation,ref move,ref rotate,ref roll);
            }

            if (controllerFlight && !ThirdPersonView.Active && !CockpitControls.RotationOwned && !CockpitControls.IsSteering && Common.Config.LegacyShipTilt && controls.ThrustRoll.IsPressed && Player.HandR.pose.isTracked)
            {
                Matrix hand = Player.HandR.deviceToPlayer;
                hand.Translation = Vector3.Zero;
                if (!virtualStickHeld) virtualStickOrigin = hand;
                Matrix delta = hand * Matrix.Invert(virtualStickOrigin);
                var f = delta.Forward;
                rotate.X = -MathHelper.Clamp((float)System.Math.Atan2(f.Y, -f.Z) / 0.5f, -1, 1) * RotationSpeed;
                rotate.Y = MathHelper.Clamp((float)System.Math.Atan2(f.X, -f.Z) / 0.5f, -1, 1) * RotationSpeed;
                float handRoll = -MathHelper.Clamp((float)System.Math.Atan2(delta.Up.X, delta.Up.Y) / 0.5f, -1, 1);
                roll = FlightAxes.Roll(handRoll, true, RotationSpeed, Common.Config.ShipRollSensitivity);
                virtualStickHeld = true;
            }
            else virtualStickHeld = false;

            CockpitControls.ApplyFlight(RotationSpeed,ref move,ref rotate,ref roll);
            // With nothing selected the trigger is the throttle, except on WeaponCore grids, where it fires without a selection.
            bool gas=!ThirdPersonView.Manipulating && PrimaryPressed && SeatTuning.RightTrigger==RightTrigger.Forward && TargetLock.ShooterGrid()==null;
            if(CockpitControls.VehicleFlight)
            {
                if(gas && ship.Toolbar?.SelectedItem==null)
                    move.Z=-System.Math.Max(-move.Z,MathHelper.Clamp(controls.FirePressure.Position.X,0,1));
            }
            else move=DriveInput.Update(ship,move,gas ? MathHelper.Clamp(controls.FirePressure.Position.X,0,1):0,
                ThirdPersonView.Manipulating || CockpitControls.Adjusting ? 0:CockpitControls.Throttle);
            ApplyMoveAndRotation(move, rotate, roll);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlWalk()
        {
            var controls = Controls.Static;
            // Ladder climbing moves the native ladder offset directly; native moves would start animated steps.
            LadderClimb.Update(Character,controls.WalkLongitudinal.Position.Y);
            if (Character.IsOnLadder)
            {
                if (controls.JumpOrClimbUp.HasPressed)
                    ((VRage.Game.ModAPI.Interfaces.IMyControllableEntity)Character).Jump(Vector3.Up);
                ApplyMoveAndRotation(Vector3.Zero,Vector2.Zero,0);
                return;
            }

            var move = Vector3.Zero;
            var rotate = Vector2.Zero;

            move.X += controls.WalkLongitudinal.Position.X;
            move.Z -= controls.WalkLongitudinal.Position.Y;

            if (controls.WalkRotate.Active)
            {
                var v = controls.WalkRotate.Position;

                rotate.Y = turnInput.Read(v.X,Common.Config.SnapTurn,RotationSpeed*Common.Config.TurnSensitivity,
                    Common.Config.SnapTurnAngle,Character.RotationSpeed,System.DateTime.UtcNow);
                // Keep walking pitch controlled by the headset, not the thumbstick.
            }

            // The interface call also sends the jump to the server; MyCharacter.Jump alone only predicts it locally.
            if (controls.JumpOrClimbUp.HasPressed)
                ((VRage.Game.ModAPI.Interfaces.IMyControllableEntity)Character).Jump(Vector3.Up);

            bool onFoot=(InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Building) && !TouchScreenBridge.OwnsInput && !ArthurLcdBridge.Owns(Player.HandR);
            bool physical=onFoot && !Common.Config.SeatedPlay && !ThirdPersonView.Character && !Character.IsOnLadder && Player.Headset.pose.isTracked;
            if (Crouch.Update(controls.WalkRotate.Active ? controls.WalkRotate.Position : Vector2.Zero,onFoot && controls.WalkRotate.Active,physical,
                HeadDrop,BodyFit.CrouchDepth(Character),Character.IsCrouching,System.DateTime.UtcNow))
                Character.Crouch();

            if (controls.JumpOrClimbUp.IsPressed)
                move.Y = 1f;


            ApplyMoveAndRotation(move, rotate, 0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlFlight()
        {
            // Gripping a rung while flying mounts the ladder.
            LadderClimb.Update(Character,0);

            ReadFlightInput(false, out Vector3 move, out Vector2 rotate, out float roll);



            ApplyMoveAndRotation(move, rotate, roll);
        }

        private void ReadFlightInput(bool ship, out Vector3 move, out Vector2 rotate, out float roll)
        {
            var controls = Controls.Static;
            float up=ship && !CockpitControls.IsSteering && CockpitControls.Held(Player.HandL) ? 0 : controls.ThrustUp.Position.X,back=controls.ThrustBackward.Position.X;
            if(ship) FlightAxes.ApplyLeftTrigger(SeatTuning.LeftTrigger,ref up,ref back);
            move = FlightAxes.Translation(controls.ThrustLRUD.Position, controls.ThrustLRFB.Position,
                up, WeaponHandling.ConsumesSupportGrip || ship && CockpitControls.IsSteering && CockpitControls.Held(Player.HandL) ? 0 : ThirdPersonView.Descent(controls.ThrustDown.Position.X), controls.ThrustForward.Position.X, back);
            float rollSensitivity = ship ? Common.Config.ShipRollSensitivity : Common.Config.JetpackRoll;
            FlightAxes.Rotation(controls.ThrustRotate.Position, controls.ThrustRoll.IsPressed && !(ship && CockpitControls.IsSteering && CockpitControls.Held(Player.HandR)) && !TouchScreenBridge.OwnsInput && !ArthurLcdBridge.Owns(Handedness.ActionHand) && !(WeaponHandling.ToolModifier && controls.Fire.IsPressed) && !(ship && ShipChord), ship, RotationSpeed, rollSensitivity, out rotate, out roll,ship ? Common.Config.InvertShipPitch : Common.Config.InvertJetpackPitch);
            if(!ship) rotate.Y=turnInput.Read(rotate.Y/RotationSpeed,Common.Config.SnapTurn,RotationSpeed*Common.Config.TurnSensitivity,
                Common.Config.SnapTurnAngle,Character.RotationSpeed,System.DateTime.UtcNow);
        }

        void ApplyMoveAndRotation(Vector3 move, Vector2 rotate, float roll)
        {
            var controlled = MySession.Static.ControlledEntity;
            if (!(controlled is MyShipController)) move=BodyLocomotion.RelativeMove(move);
            // Added after RelativeMove so keyboard movement stays body-relative, as in the base game.
            KeyboardMouseInput.Add(controlled, ref move, ref rotate, ref roll);
            move = Vector3.Clamp(move, -Vector3.One, Vector3.One);

            bool moving = move != Vector3.Zero || rotate != Vector2.Zero || roll != 0f;
            if (moving)
                controlled?.MoveAndRotate(move, rotate, roll);
            else if (hadControllerMovement)
                controlled?.MoveAndRotateStopped();
            hadControllerMovement = moving;
        }

        private static FlightTuning SeatTuning => FlightSettings.For(MySession.Static?.ControlledEntity as MyCockpit);
        private static bool PrimaryPressed => !HelmetHud.ProtectsFire && !FloatingWindows.OwnsInput && Controls.Static.Fire.IsPressed &&
            !GameActions.AlternateTrigger && !PlacementControls.OwnsTools && !CockpitControls.Adjusting && !TouchScreenBridge.OwnsInput && !ArthurLcdBridge.Owns(Handedness.ActionHand) &&
            FlightAxes.StickTriggerAllowed(MySession.Static?.ControlledEntity is MyShipController,Common.Config.PhysicalShipControlsOnly,
                CockpitControls.SticksAvailable,CockpitControls.AnyHeld);
        // Grip + trigger is ship secondary, as with tools on foot. A held physical stick owns the grip.
        private static bool ShipChord => Controls.Static.Fire.IsPressed && Controls.Static.ActionGripClick.IsPressed && !CockpitControls.Held(Handedness.ActionHand);
        internal static bool PrimaryFire => PrimaryPressed && !TriggerArm.AnyArmed && !(MySession.Static?.ControlledEntity is MyShipController && ShipChord);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlCommonFunctions()
        {
            var controls = Controls.Static;

            var controlledEntity = MySession.Static.ControlledEntity;
            bool turret=RemoteView.Turret;
            bool stickAction=(controlledEntity is MyShipController || turret) && InputRouter.CockpitInteraction && !CockpitControls.IsSteering && CockpitControls.Held(Player.HandL) &&
                !CockpitControls.Adjusting && !PlacementControls.OwnsTools && !BlockInspection.ConsumesSecondary && !HelmetHud.ProtectsRight &&
                !TouchScreenBridge.OwnsInput && !ArthurLcdBridge.Owns(Player.HandL) && !FloatingWindows.OwnsInput && Player.HandL.pose.isTracked;
            stickSecondary.Update(stickAction && controls.LeftTriggerPressure.Active && controls.LeftTriggerPressure.CanPress,
                controls.LeftTriggerPressure.RawPosition.X>.55f);
            bool primaryPressed=PrimaryPressed && !TriggerArm.AnyArmed;
            bool ship=controlledEntity is MyShipController;
            var action=Handedness.ActionHand;
            var grip=controls.ActionGripClick;
            bool gripSecondary=FlightAxes.SecondaryGrip(InputRouter.Flying,ship,CockpitControls.RotationOwned,CockpitControls.NearGrip(action),controls.ThrustRotate.RawPosition,turret);
            // The action grip is also the roll modifier, so ships take secondary from a short tap without stick input.
            bool uiOwnsAction=SpatialUi.Owns(action) || CockpitTouch.Owns(action) || HandInteraction.Consumes(action) || FloatingWindows.OwnsInput || TouchScreenBridge.OwnsInput || ArthurLcdBridge.Owns(action);
            bool chord=ship && ShipChord && !uiOwnsAction;
            if(chord) primaryPressed=false;
            // On WeaponCore grids grip + trigger aims a lock with a pointer and its release locks.
            bool lockChord=chord && TargetLock.ShooterGrid()!=null;
            bool gripTap=secondaryTap.Update((ship || turret) && grip.IsPressed,(ship || turret) && grip.HasReleased,!gripSecondary || uiOwnsAction || chord,System.DateTime.UtcNow);
            bool secondaryAllowed=!HelmetHud.ProtectsFire && (!turret || ShipTargeting.CanLock(controlledEntity)) && !FloatingWindows.OwnsInput && !BlockInspection.ConsumesSecondary &&
                !PlacementControls.OwnsTools && !CockpitControls.Adjusting && !TouchScreenBridge.OwnsInput && !ArthurLcdBridge.Owns(action);
            bool secondaryPressed=secondaryAllowed && (stickSecondary.Held || chord && !lockChord || (ship || turret ? gripTap && gripSecondary:grip.IsPressed && gripSecondary) || controls.Fire.IsPressed && GameActions.AlternateTrigger);
            TargetLock.Aim(lockChord && secondaryAllowed);
            if(lockChordWas && !lockChord)
            {
                if(secondaryAllowed) TargetLock.OnLockPress(false);
                // The trigger may outlast the grip; the release must not become a shot.
                controls.Fire.BlockUntilRelease();
            }
            lockChordWas=lockChord;
            if(controlledEntity is Sandbox.Game.Entities.Character.MyCharacter character && character.CurrentWeapon==null)
                secondaryPressed=false;

            bool paintGun=PaintGun.Equipped;
            if(WeaponHandling.ToolEquipped || paintGun)
            {
                bool trigger=controls.Fire.IsPressed && !HelmetHud.ProtectsFire && !uiOwnsAction && !PlacementControls.OwnsTools && !TriggerArm.AnyArmed;
                // The Paint Gun reads the grip as deep paint, never ironsight.
                primaryPressed=trigger && (paintGun || !WeaponHandling.ToolModifier);
                secondaryPressed=trigger && !paintGun && WeaponHandling.ToolModifier;
            }
            // The shoot calls bypass the control blacklist, so a blocked fire control must not start the vanilla weapon.
            if(NativeActions.Blocked(Sandbox.Game.MyControlsSpace.PRIMARY_TOOL_ACTION)) primaryPressed=false;
            PaintGun.Firing=paintGun && primaryPressed;
            PaintGun.Deep=paintGun && WeaponHandling.GripModifier;
            bool secondaryShoot=secondaryPressed && !turret && !NativeActions.Blocked(Sandbox.Game.MyControlsSpace.SECONDARY_TOOL_ACTION);
            // Release the previous action before starting the other half of a tool chord.
            if(!primaryPressed && wasShooting) { controlledEntity?.EndShoot(MyShootActionEnum.PrimaryAction); DoubleTap(MyShootActionEnum.PrimaryAction,false,false); }
            if(!secondaryShoot && secondaryShooting) { controlledEntity?.EndShoot(MyShootActionEnum.SecondaryAction); secondaryShooting=false; }
            if(!secondaryPressed && wasSecondary && !turret) DoubleTap(MyShootActionEnum.SecondaryAction,false,false);
            bool onFoot=controlledEntity is Sandbox.Game.Entities.Character.MyCharacter;
            if (primaryPressed && !wasShooting)
            {
                DoubleTap(MyShootActionEnum.PrimaryAction,true,onFoot);
                // Empty-handed BeginShoot is a native Use.
                if(controlledEntity is Sandbox.Game.Entities.Character.MyCharacter walker && walker.CurrentWeapon==null) ModApi.Use(HandInteraction.UseHand);
                controlledEntity?.BeginShoot(MyShootActionEnum.PrimaryAction);
            }
            // BeginShoot reads the double-click flag, so it is set first.
            if(secondaryPressed && !wasSecondary) DoubleTap(MyShootActionEnum.SecondaryAction,true,onFoot);
            if(secondaryShoot && !secondaryShooting) { controlledEntity?.BeginShoot(MyShootActionEnum.SecondaryAction); secondaryShooting=true; }
            // Lock-on follows the press even when the fire control is blocked.
            if(secondaryPressed && !wasSecondary) ShipTargeting.SecondaryPressed(controlledEntity,stickSecondary.Held || CockpitControls.Held(action));

            bool primaryHeld=primaryPressed && wasShooting;
            wasShooting = primaryPressed;
            wasSecondary = secondaryPressed;

            GameActions.HandleButtons(primaryHeld);
        }

        public override string ComponentTypeDebugString => "VR Movement Component";

    }
}
