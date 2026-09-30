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
            if (firstMovementTick) { Logger.Info("MOVEMENT component is receiving simulation ticks"); firstMovementTick = false; }
            if (!Main.VrActive || !InputRouter.Gameplay || Main.MenuOpen || MySandboxGame.IsPaused || Character.IsDead || ThirdPersonView.Manipulating)
            {
                StopInput();
                return;
            }
            try
            {
                if (!ReferenceEquals(inputOwner, MySession.Static.ControlledEntity)) StopInput();
                inputOwner = MySession.Static.ControlledEntity;
                BodyLocomotion.Update(Character);

                if (MySession.Static.ControlledEntity is MyShipController)
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
                ControlCommonFunctions();
            }
            catch (System.Exception ex) { StopInput(); Main.Fail(ex, "VR input stopped"); }
        }

        public override void UpdateAfterSimulation()
        {
            if (SeatFit.Eligible(SeatFit.Seat) && SeatFit.Seat.Pilot==Character)
            {
                TrackedArms.Update(Character);
                CockpitControls.RefreshVisuals();
                SpatialUi.Publish();
                return;
            }
            if(!Main.VrActive || !CameraRig.Owns(Character) || MySession.Static?.ControlledEntity!=Character || Character.IsDead) return;
            try
            {
                // Input/body following runs before physics. Publish a fresh camera AFTER
                // physics too: at 100 m/s the old one-tick lag was about 1.67 metres.
                CameraRig.RefreshAfterSimulation(Character);
                Patches.MotionToolPatch.Refresh(Character);
                TrackedArms.Update(Character);
                SpatialUi.Publish();
                if(firstCameraAfterTick) { Logger.Info("VR camera receives post-physics character updates"); firstCameraAfterTick=false; }
            }
            catch(System.Exception ex) { Main.Fail(ex,"Post-physics VR camera update failed"); }
        }

        internal static void StopActive() => active?.StopInput();

        public override void OnCharacterDead() => StopInput();

        public override void OnBeforeRemovedFromContainer()
        {
            StopInput();
            if (active == this) active = null;
            base.OnBeforeRemovedFromContainer();
        }

        private void StopInput()
        {
            var controlled = inputOwner;
            if (hadControllerMovement) controlled?.MoveAndRotateStopped();
            if (wasShooting) controlled?.EndShoot(MyShootActionEnum.PrimaryAction);
            if (wasSecondary) controlled?.EndShoot(MyShootActionEnum.SecondaryAction);
            if (active == this) UsingControllerMovement = false;
            hadControllerMovement = wasShooting = wasSecondary = virtualStickHeld = false;
            inputOwner = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlShip()
        {
            var controls = Controls.Static;

            ReadFlightInput(true, out Vector3 move, out Vector2 rotate, out float roll);

            if (!ThirdPersonView.Active && !CockpitControls.RotationOwned && Common.Config.LegacyShipTilt && controls.ThrustRoll.IsPressed && Player.HandR.pose.isTracked)
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

            if (controls.Dampener.HasPressed)
                MySession.Static.ControlledEntity?.SwitchDamping();

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

            if (controls.JumpOrClimbUp.HasPressed)
                Character.Jump(Vector3.Up);

            if (!WeaponHandling.ConsumesLeftGrip && controls.CrouchOrClimbDown.HasPressed)
                Character.Crouch();

            if (controls.JumpOrClimbUp.IsPressed)
                move.Y = 1f;

            if (!WeaponHandling.ConsumesLeftGrip && controls.CrouchOrClimbDown.IsPressed)
                move.Y = -1f;

            ApplyMoveAndRotation(move, rotate, 0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ControlFlight()
        {
            var controls = Controls.Static;

            ReadFlightInput(false, out Vector3 move, out Vector2 rotate, out float roll);

            if (controls.Dampener.HasPressed)
                MySession.Static.ControlledEntity?.SwitchDamping();

            ApplyMoveAndRotation(move, rotate, roll);
        }

        private void ReadFlightInput(bool ship, out Vector3 move, out Vector2 rotate, out float roll)
        {
            var controls = Controls.Static;
            move = FlightAxes.Translation(controls.ThrustLRUD.Position, controls.ThrustLRFB.Position,
                controls.ThrustUp.Position.X, WeaponHandling.ConsumesLeftGrip ? 0 : controls.ThrustDown.Position.X, controls.ThrustForward.Position.X, controls.ThrustBackward.Position.X);
            float rollSensitivity = ship ? Common.Config.ShipRollSensitivity : Common.Config.JetpackRollSensitivity;
            FlightAxes.Rotation(controls.ThrustRotate.Position, controls.ThrustRoll.IsPressed && !PlacementControls.OwnsTools && !TouchScreenBridge.OwnsInput, ship, RotationSpeed, rollSensitivity, out rotate, out roll);
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
            bool primaryPressed=controls.Primary.IsPressed && !GameActions.AlternateTrigger && !PlacementControls.OwnsTools && !CockpitControls.Adjusting && !TouchScreenBridge.OwnsInput;
            bool secondaryPressed=(controls.Secondary.IsPressed && !InputRouter.Flying || controls.Primary.IsPressed && GameActions.AlternateTrigger) && !PlacementControls.OwnsTools && !CockpitControls.Adjusting && !TouchScreenBridge.OwnsInput;

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
                controlledEntity?.BeginShoot(MyShootActionEnum.SecondaryAction);
            }
            else if (!secondaryPressed && wasSecondary)
            {
                controlledEntity?.EndShoot(MyShootActionEnum.SecondaryAction);
            }

            wasShooting = primaryPressed;
            wasSecondary = secondaryPressed;

            GameActions.HandleButtons();
        }

        public override string ComponentTypeDebugString => "VR Movement Component";

    }
}
