using Sandbox;
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
        private readonly Control.InputGate stickSecondary=new Control.InputGate();
        private readonly Control.GripTap secondaryTap=new Control.GripTap();
        internal static readonly Control.CrouchControl Crouch=new Control.CrouchControl();
        internal static float HeadDrop => -Player.Headset.deviceToPlayer.Translation.Y;
        private MyShipController jumpOwner;
        private System.DateTime nextInputTrace;

        public static bool UsingControllerMovement;

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
            if (!Main.VrActive || !InputRouter.Gameplay || Main.MenuOpen || MySandboxGame.IsPaused || Character.IsDead || !RemoteView.Live(MySession.Static.ControlledEntity) || ThirdPersonView.Manipulating && !ThirdPersonView.FlightWhileManipulating)
            {
                StopInput();
                return;
            }
            try
            {
                if (!ReferenceEquals(inputOwner, MySession.Static.ControlledEntity)) StopInput();
                inputOwner = MySession.Static.ControlledEntity;
                DriveInput.Stop();
                if(jumpOwner!=null && !NativeActions.WheelJumpAllowed)
                { jumpOwner.WheelJump(false); jumpOwner=null; }
                if(!RemoteView.Active) BodyLocomotion.Update(Character);
                else if(RemoteView.CharacterAnchor) { CameraRig.Begin(Character); CameraRig.End(Character); }

                if(RemoteView.Turret) { RemoteView.ControlTurret(RotationSpeed); UsingControllerMovement=true; hadControllerMovement=true; }
                else if(RemoteView.OwnsInput) ApplyMoveAndRotation(Vector3.Zero,Vector2.Zero,0);
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
                if(ThirdPersonView.Manipulating) StopTools(inputOwner);
                else ControlCommonFunctions();
            }
            catch (System.Exception ex) { StopInput(); Main.Fail(ex, "VR input stopped"); }
        }

        public override void UpdateAfterSimulation()
        {
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
                SpatialUi.Publish();
                if(firstCameraAfterTick) { Logger.Info("VR camera receives post-physics character updates"); firstCameraAfterTick=false; }
            }
            catch(System.Exception ex) { Main.Fail(ex,"Post-physics VR camera update failed"); }
        }

        internal static void StopActive() => active?.StopInput();
        private void TraceInput()
        {
            var c=Controls.Static;
            if(System.DateTime.UtcNow<nextInputTrace || !(c.Primary.RawPressed || c.LeftGripPressure.RawPosition.X>.55f || c.RightGripPressure.RawPosition.X>.55f || c.ThrustRotate.RawPosition.LengthSquared()>.1f)) return;
            nextInputTrace=System.DateTime.UtcNow.AddSeconds(5);
            var ship=MySession.Static?.ControlledEntity as MyShipController;
            Logger.Info("INPUT ownership: mode="+InputRouter.Mode+"; trigger="+c.Primary.RawPressed+"/"+c.Primary.IsPressed+
                "; flightActive="+c.ThrustRotate.Active+"; clipboard="+PlacementControls.ClipboardActive+"; adjust="+PlacementControls.Adjusting+
                "; screen="+TouchScreenBridge.OwnsInput+"; feed="+RemoteView.OwnsInput+"; viewGrab="+ThirdPersonView.Manipulating+
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
            jumpOwner?.WheelJump(false); jumpOwner=null; stickSecondary.Block(); DriveInput.Stop();
            var controlled = inputOwner;
            if (hadControllerMovement) { RemoteView.Stop(controlled); controlled?.MoveAndRotateStopped(); }
            StopTools(controlled);
            if (active == this) UsingControllerMovement = false;
            hadControllerMovement = wasShooting = wasSecondary = virtualStickHeld = false;
            inputOwner = null;
        }

        private void StopTools(Sandbox.Game.Entities.IMyControllableEntity controlled)
        {
            if(wasShooting) controlled?.EndShoot(MyShootActionEnum.PrimaryAction);
            if(wasSecondary && !RemoteView.IsTurret(controlled)) controlled?.EndShoot(MyShootActionEnum.SecondaryAction);
            wasShooting=wasSecondary=false;
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
            if(controllerFlight || CockpitControls.NeedsControllerTranslation)
            {
                ReadFlightInput(true,out move,out rotate,out roll);
                if(!controllerFlight) { rotate=Vector2.Zero; roll=0; }
            }

            if (controllerFlight && !ThirdPersonView.Active && !CockpitControls.RotationOwned && Common.Config.LegacyShipTilt && controls.ThrustRoll.IsPressed && Player.HandR.pose.isTracked)
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
            DriveInput.Update(ship,move);
            ApplyMoveAndRotation(move, rotate, roll);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlWalk()
        {
            var controls = Controls.Static;

            var move = Vector3.Zero;
            var rotate = Vector2.Zero;

            move.X += controls.WalkLongitudinal.Position.X;
            move.Z -= controls.WalkLongitudinal.Position.Y;

            if (controls.WalkRotate.Active)
            {
                var v = controls.WalkRotate.Position;

                rotate.Y = v.X * RotationSpeed;
                // Keep walking pitch controlled by the headset, not the thumbstick.
            }

            // The interface call also sends the jump to the server; MyCharacter.Jump alone only predicts it locally.
            if (controls.JumpOrClimbUp.HasPressed)
                ((VRage.Game.ModAPI.Interfaces.IMyControllableEntity)Character).Jump(Vector3.Up);

            bool onFoot=(InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Building) && !TouchScreenBridge.OwnsInput;
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
            var controls = Controls.Static;

            ReadFlightInput(false, out Vector3 move, out Vector2 rotate, out float roll);



            ApplyMoveAndRotation(move, rotate, roll);
        }

        private void ReadFlightInput(bool ship, out Vector3 move, out Vector2 rotate, out float roll)
        {
            var controls = Controls.Static;
            move = FlightAxes.Translation(controls.ThrustLRUD.Position, controls.ThrustLRFB.Position,
                ship && CockpitControls.Held(Player.HandL) ? 0 : controls.ThrustUp.Position.X, WeaponHandling.ConsumesLeftGrip || !ThirdPersonView.DescentReady ? 0 : controls.ThrustDown.Position.X, controls.ThrustForward.Position.X, controls.ThrustBackward.Position.X);
            float rollSensitivity = ship ? Common.Config.ShipRollSensitivity : Common.Config.JetpackRoll;
            FlightAxes.Rotation(controls.ThrustRotate.Position, controls.ThrustRoll.IsPressed && !TouchScreenBridge.OwnsInput, ship, RotationSpeed, rollSensitivity, out rotate, out roll,ship ? Common.Config.InvertShipPitch : Common.Config.InvertJetpackPitch);
        }

        void ApplyMoveAndRotation(Vector3 move, Vector2 rotate, float roll)
        {
            if (!(MySession.Static.ControlledEntity is MyShipController)) move=BodyLocomotion.RelativeMove(move);
            move = Vector3.Clamp(move, -Vector3.One, Vector3.One);

            // Prevent the vanilla input tick from zeroing controller movement.
            UsingControllerMovement = move != Vector3.Zero || rotate != Vector2.Zero || roll != 0f;

            if (UsingControllerMovement)
                MySession.Static.ControlledEntity?.MoveAndRotate(move, rotate, roll);
            else if (hadControllerMovement)
                MySession.Static.ControlledEntity?.MoveAndRotateStopped();
            hadControllerMovement = UsingControllerMovement;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlCommonFunctions()
        {
            var controls = Controls.Static;

            var controlledEntity = MySession.Static.ControlledEntity;
            bool turret=RemoteView.Turret;
            bool stickAction=(controlledEntity is MyShipController || turret) && InputRouter.CockpitInteraction && CockpitControls.Held(Player.HandL) &&
                !CockpitControls.Adjusting && !PlacementControls.OwnsTools && !BlockInspection.ConsumesSecondary && !HelmetHud.ProtectsRight &&
                !TouchScreenBridge.OwnsInput && !RemoteView.OwnsInput && Player.HandL.pose.isTracked;
            stickSecondary.Update(stickAction && controls.LeftTriggerPressure.Active && controls.LeftTriggerPressure.CanPress,
                controls.LeftTriggerPressure.RawPosition.X>.55f);
            bool primaryPressed=!HelmetHud.ProtectsRight && !RemoteView.OwnsInput && controls.Primary.IsPressed && !GameActions.AlternateTrigger && !PlacementControls.OwnsTools && !CockpitControls.Adjusting && !TouchScreenBridge.OwnsInput;
            bool ship=controlledEntity is MyShipController;
            DriveInput.Throttle(ship && primaryPressed);
            bool gripSecondary=FlightAxes.SecondaryGrip(InputRouter.Flying,ship,CockpitControls.RotationOwned,CockpitControls.NearGrip(Player.HandR),controls.ThrustRotate.RawPosition,turret);
            // Right grip is also the roll modifier, so ships take secondary from a short tap without stick input.
            bool uiOwnsRight=SpatialUi.OwnsRight || CockpitTouch.OwnsRight || HandInteraction.OwnsRight || RemoteView.OwnsInput || TouchScreenBridge.OwnsInput;
            bool gripTap=secondaryTap.Update((ship || turret) && controls.Secondary.IsPressed,(ship || turret) && controls.Secondary.HasReleased,!gripSecondary || uiOwnsRight,System.DateTime.UtcNow);
            bool secondaryPressed=!HelmetHud.ProtectsRight && (!turret || ShipTargeting.CanLock(controlledEntity)) && !RemoteView.OwnsInput && !BlockInspection.ConsumesSecondary && (stickSecondary.Held || (ship || turret ? gripTap && gripSecondary:controls.Secondary.IsPressed && gripSecondary) || controls.Primary.IsPressed && GameActions.AlternateTrigger) && !PlacementControls.OwnsTools && !CockpitControls.Adjusting && !TouchScreenBridge.OwnsInput;
            if(controlledEntity is Sandbox.Game.Entities.Character.MyCharacter character && character.CurrentWeapon==null)
                secondaryPressed=false;

            if (primaryPressed && !wasShooting)
            {
                controlledEntity?.BeginShoot(MyShootActionEnum.PrimaryAction);
            }
            else if (!primaryPressed && wasShooting)
            {
                controlledEntity?.EndShoot(MyShootActionEnum.PrimaryAction);
            }

            if (secondaryPressed && !wasSecondary)
            {
                if(!turret) controlledEntity?.BeginShoot(MyShootActionEnum.SecondaryAction);
                ShipTargeting.SecondaryPressed(controlledEntity);
            }
            else if (!secondaryPressed && wasSecondary)
            {
                if(!turret) controlledEntity?.EndShoot(MyShootActionEnum.SecondaryAction);
            }

            wasShooting = primaryPressed;
            wasSecondary = secondaryPressed;

            GameActions.HandleButtons();
        }

        public override string ComponentTypeDebugString => "VR Movement Component";

    }
}
