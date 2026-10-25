namespace SpaceEngineersVR.Player
{
    internal readonly struct InteractionInput
    {
        internal const float GripThreshold=.8f;
        // Index grip force reads up to about .1 from a relaxed hold; its SteamVR profile ignores force below .1.
        internal const float GripReleased=.1f,GripEngaged=.15f;
        internal readonly bool Near,Right,Down,CanAcquire,Grip;
        internal readonly float Pressure;
        private readonly float gripPressure,triggerPressure;
        private readonly bool gripReady,triggerReady;
        internal InteractionInput(bool near,bool right,float pressure,bool down,bool canAcquire)
        {
            Near=near; Right=right; Pressure=pressure; Down=down; CanAcquire=canAcquire; Grip=near;
            gripPressure=near ? pressure:0; triggerPressure=near ? 0:pressure;
            gripReady=near && canAcquire; triggerReady=!near && canAcquire;
        }
        internal InteractionInput(bool right,float grip,float trigger,bool gripReady,bool triggerReady,bool? source=null)
        {
            Near=true; Right=right; gripPressure=grip; triggerPressure=trigger;
            this.gripReady=gripReady; this.triggerReady=triggerReady;
            Grip=source ?? (grip>GripThreshold || trigger<=.55f && grip>=trigger);
            Pressure=Grip ? grip:trigger;
            Down=Pressure>(Grip ? GripThreshold:.55f);
            CanAcquire=Grip ? gripReady:triggerReady;
        }
        internal InteractionInput Select(bool grip) => new InteractionInput(Right,gripPressure,triggerPressure,gripReady,triggerReady,grip);
        internal static InteractionInput Read(Controller hand,bool near)
        {
            var c=Controls.Static;
            bool right=hand==Player.HandR;
            var trigger=right ? c.PointerPressure:c.LeftTriggerPressure;
            if(near)
            {
                var grip=right ? c.RightGripPressure:c.LeftGripPressure;
                return new InteractionInput(right,grip.RawPosition.X,trigger.RawPosition.X,grip.CanPress,trigger.CanPress);
            }
            return c.Click(hand).Read(right);
        }
        internal void Consume()
        {
            var c=Controls.Static;
            var hand=Right ? Player.HandR:Player.HandL;
            c.Click(hand).Consume(Near,Down);
            c.BlockTriggerRoles(hand);
            if(!Right) { c.ThrustForward.BlockUntilRelease(); c.JumpOrClimbUp.BlockUntilRelease(); }
            if(Near) { (Right ? c.RightGripPressure:c.LeftGripPressure).BlockUntilRelease(Down); c.BlockGripRoles(hand); }
        }
    }
}
