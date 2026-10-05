using System;
using System.Collections.Generic;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class GripDescent
    {
        public const double Delay=.15,Stale=1;
        private DateTime? pressed;
        private readonly Queue<KeyValuePair<DateTime,float>> samples=new Queue<KeyValuePair<DateTime,float>>();
        private DateTime lastSample;
        private float replayed;
        public bool Ready { get; private set; }
        public void Update(bool available,float left,DateTime now)
        {
            if(!available || left<=.025f || float.IsNaN(left)) { pressed=null; Ready=false; return; }
            if(!pressed.HasValue) pressed=now;
            Ready=(now-pressed.Value).TotalSeconds>=Delay;
        }
        public void Cancel() { samples.Clear(); replayed=0; }
        // Thrust plays back Delay seconds late, so a two-grip pan can claim the grip before any thruster fires
        // while taps and repeated presses keep their exact length and pressure.
        public float Replay(float pressure,DateTime now)
        {
            // Samples from before a pause in flight input describe a grip that may have changed since.
            if((now-lastSample).TotalSeconds>Stale) Cancel();
            lastSample=now;
            samples.Enqueue(new KeyValuePair<DateTime,float>(now,float.IsNaN(pressure) ? 0 : pressure));
            while(samples.Count>0 && (now-samples.Peek().Key).TotalSeconds>=Delay) replayed=samples.Dequeue().Value;
            return replayed;
        }
    }
}
