using SpaceEngineersVR.Player.Control;

namespace SpaceEngineersVR.Player
{
    internal readonly struct TriggerClick
    {
        private readonly Button button;
        private readonly Analog pressure;
        internal TriggerClick(Button button,Analog pressure) { this.button=button; this.pressure=pressure; }
        internal bool Down => button.RawPressed;
        internal bool Pressed => button.HasPressed;
        internal bool Held => button.IsPressed;
        internal bool Active => button.Active;
        internal float Pressure => pressure.RawPosition.X;
        internal void Consume(bool near,bool down)
        {
            button.BlockUntilRelease();
            if(near) pressure.BlockUntilRelease(down);
        }
        internal InteractionInput Read(bool right) => new InteractionInput(false,right,pressure.RawPosition.X,Down,
            pressure.Position.X>0 || Pressed);
    }
}
