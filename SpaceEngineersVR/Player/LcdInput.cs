using System;
using SpaceEngineersVR.Player.Control;

namespace SpaceEngineersVR.Player
{
    internal sealed class LcdInput
    {
        internal struct Bindings
        {
            internal Button Primary,LeftClick,Secondary,Interact,ThrustRoll,JumpOrClimbUp,CrouchOrClimbDown;
            internal Analog PointerPressure,LeftTriggerPressure,RightGripPressure,LeftGripPressure,ThrustRotate,WalkRotate,ThrustLRFB,WalkLongitudinal,ThrustUp,ThrustForward,ThrustDown;
            internal Bindings(Controls c)
            {
                Primary=c.Primary; LeftClick=c.LeftClick; Secondary=c.Secondary; Interact=c.Interact; ThrustRoll=c.ThrustRoll; JumpOrClimbUp=c.JumpOrClimbUp; CrouchOrClimbDown=c.CrouchOrClimbDown;
                PointerPressure=c.PointerPressure; LeftTriggerPressure=c.LeftTriggerPressure; RightGripPressure=c.RightGripPressure; LeftGripPressure=c.LeftGripPressure; ThrustRotate=c.ThrustRotate; WalkRotate=c.WalkRotate; ThrustLRFB=c.ThrustLRFB; WalkLongitudinal=c.WalkLongitudinal; ThrustUp=c.ThrustUp; ThrustForward=c.ThrustForward; ThrustDown=c.ThrustDown;
            }
        }
        internal readonly struct Sample
        {
            internal readonly bool Click,Grip,TriggerAllowed,NearAllowed,GripAllowed,ScrollAllowed;
            internal readonly bool TriggerActive,GripActive,ScrollActive;
            internal readonly float Pressure,GripPressure,Scroll;
            internal Sample(bool click,float pressure,bool grip,float gripPressure,bool triggerAllowed,bool gripAllowed,float scroll=0,bool scrollAllowed=true,bool? nearAllowed=null,bool triggerActive=true,bool gripActive=true,bool scrollActive=true)
            { Click=click; Pressure=pressure; Grip=grip; GripPressure=gripPressure; TriggerAllowed=triggerAllowed; NearAllowed=nearAllowed ?? triggerAllowed; GripAllowed=gripAllowed; Scroll=scroll; ScrollAllowed=scrollAllowed; TriggerActive=triggerActive; GripActive=gripActive; ScrollActive=scrollActive; }
        }
        private readonly InteractionPress trigger=new InteractionPress();
        private readonly InputGate grip=new InputGate(),scroll=new InputGate();
        private bool triggerArmed,scrollArmed;
        private DateTime nextScroll;
        private int pendingScroll;
        internal bool Targeted { get; private set; }
        internal bool ReserveTrigger { get; private set; }
        internal bool ReserveGrip { get; private set; }
        internal bool ReserveScroll { get; private set; }
        internal bool Reserved => Captured || ReserveTrigger || ReserveGrip || ReserveScroll;
        internal bool Primary => trigger.Held;
        internal bool Secondary => grip.Held;
        internal bool Captured => Primary || Secondary;
        internal bool Pressed { get; private set; }
        internal void Cancel()
        {
            trigger.Block(); grip.Block(); scroll.Block();
            Targeted=triggerArmed=scrollArmed=Pressed=false;
            pendingScroll=0; nextScroll=DateTime.MinValue;
            // Reservations survive cancellation until the physical controls are released.
        }
        internal Sample Read(Controls c,bool right,bool flying,bool scrollEnabled=true) => Read(new Bindings(c),right,flying,scrollEnabled);
        internal Sample Read(Bindings c,bool right,bool flying,bool scrollEnabled=true)
        {
            var click=right ? c.Primary:c.LeftClick;
            var pressure=right ? c.PointerPressure:c.LeftTriggerPressure;
            var gripPressure=right ? c.RightGripPressure:c.LeftGripPressure;
            var axis=right ? (flying ? c.ThrustRotate:c.WalkRotate):(flying ? c.ThrustLRFB:c.WalkLongitudinal);
            return new Sample(click.RawPressed,pressure.RawPosition.X,
                right ? c.Secondary.RawPressed:gripPressure.RawPosition.X>InteractionInput.GripThreshold,gripPressure.RawPosition.X,
                click.HasPressed || Targeted && ReserveTrigger,
                right ? c.Secondary.HasPressed || Targeted && ReserveGrip:gripPressure.CanPress,
                scrollEnabled ? axis.RawPosition.Y:0,scrollEnabled && axis.Position.Y!=0,pressure.CanPress && (click.IsPressed || Targeted),
                click.Active && pressure.Active,right ? c.Secondary.Active && gripPressure.Active:gripPressure.Active,axis.Active);
        }
        internal void Update(Sample input,bool targeted,bool near,DateTime now)
        {
            Targeted=targeted; Pressed=false;
            ReserveTrigger=(targeted || ReserveTrigger) && (!input.TriggerActive || input.Click || input.Pressure>.025f);
            ReserveGrip=(targeted || ReserveGrip) && (!input.GripActive || input.Grip || input.GripPressure>.025f);
            ReserveScroll=ReserveScroll && (!input.ScrollActive || Math.Abs(input.Scroll)>.15f);
            if(!targeted) { Cancel(); return; }
            if(input.ScrollActive && Math.Abs(input.Scroll)<.15f) scrollArmed=true;
            bool triggerDown=(trigger.Held ? trigger.Near:near) ? input.Pressure>.55f:input.Click;
            if(input.TriggerActive && !triggerDown) triggerArmed=true;
            bool allowed=triggerArmed && ((trigger.Held ? trigger.Near:near) ? input.NearAllowed:input.TriggerAllowed);
            trigger.Update(input.TriggerActive,trigger.Held ? trigger.Near:near,false,triggerDown,allowed,allowed);
            grip.Update(input.GripActive,input.Grip);
            if(grip.Pressed && !input.GripAllowed) grip.Block();
            Pressed=trigger.Pressed || grip.Pressed;
            if(Primary) ReserveTrigger=true;
            scroll.Update(input.ScrollActive && input.TriggerActive && scrollArmed,input.Pressure>=.06f && Math.Abs(input.Scroll)>=.5f);
            if(scroll.Pressed && !input.ScrollAllowed) scroll.Block();
            if(!scroll.Held) { pendingScroll=0; nextScroll=DateTime.MinValue; }
            else
            {
                ReserveScroll=true;
                if(now>=nextScroll) { pendingScroll=Math.Sign(input.Scroll)*120; nextScroll=now.AddMilliseconds(120); }
            }
        }
        internal int TakeScroll() { int value=pendingScroll; pendingScroll=0; return value; }
        internal void Consume(Controls c,bool right) => Consume(new Bindings(c),right);
        internal void Consume(Bindings c,bool right)
        {
            if(ReserveTrigger)
            {
                (right ? c.Primary:c.LeftClick).BlockUntilRelease();
                (right ? c.PointerPressure:c.LeftTriggerPressure).BlockUntilRelease(!Targeted);
                if(right) c.Interact.BlockUntilRelease();
                else { c.ThrustUp.BlockUntilRelease(); c.ThrustForward.BlockUntilRelease(); c.JumpOrClimbUp.BlockUntilRelease(); }
            }
            if(ReserveGrip)
            {
                (right ? c.RightGripPressure:c.LeftGripPressure).BlockUntilRelease(!Targeted);
                if(right) { c.Secondary.BlockUntilRelease(); c.ThrustRoll.BlockUntilRelease(); }
                else { c.ThrustDown.BlockUntilRelease(); c.CrouchOrClimbDown.BlockUntilRelease(); }
            }
            if(ReserveScroll)
            {
                (right ? c.WalkRotate:c.WalkLongitudinal).BlockUntilRelease();
                (right ? c.ThrustRotate:c.ThrustLRFB).BlockUntilRelease();
            }
        }
    }
}
