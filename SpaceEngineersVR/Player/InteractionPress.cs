using SpaceEngineersVR.Player.Control;

namespace SpaceEngineersVR.Player
{
    internal sealed class InteractionPress
    {
        private readonly InputGate grip=new InputGate(),trigger=new InputGate();
        internal bool Near { get; private set; }
        internal bool Grip { get; private set; }
        internal bool Pressed { get; private set; }
        internal bool Held { get; private set; }
        internal void Block() { grip.Block(); trigger.Block(); Pressed=Held=false; }
        internal InteractionInput Read(Controller hand,bool near)
        {
            var input=InteractionInput.Read(hand,near);
            return Held && Near ? input.Select(Grip):input;
        }
        internal bool Update(bool available,InteractionInput input)
        {
            var hand=input.Right ? Player.HandR:Player.HandL;
            var physical=InteractionInput.Read(hand,true);
            return Update(available,input.Near,physical.Select(true).Down,
                input.Near ? physical.Select(false).Down:InteractionInput.Read(hand,false).Down,
                input.Near ? physical.Select(true).CanAcquire:input.CanAcquire,
                input.Near && physical.Select(false).CanAcquire);
        }
        internal bool Update(bool available,bool near,bool gripDown,bool triggerDown,bool canAcquire,bool nearTrigger=false)
        {
            grip.Update(available,gripDown); trigger.Update(available,triggerDown);
            if(!available || Held && !(Grip ? gripDown:triggerDown)) Held=false;
            bool grab=near && grip.Pressed && canAcquire;
            Pressed=!Held && (grab || trigger.Pressed && (near ? nearTrigger:canAcquire));
            if(Pressed) { Held=true; Near=near; Grip=grab; }
            return Pressed;
        }
    }
}
