using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class HeadGesture
    {
        private readonly InputGate trigger=new InputGate();
        public bool Inside { get; private set; }
        public bool Pressed => Inside && trigger.Pressed;
        public void Update(bool active,Matrix hand,Matrix head,bool actuated)
        {
            Vector3 p=(hand*Matrix.Invert(head)).Translation;
            // Keep an acquired gesture through small hand/head movements between presses.
            Inside=active && (HelmetHud.NearTemple(hand,head) || Inside &&
                p.X>.07f && p.X<.34f && p.Y>-.20f && p.Y<.24f && p.Z>-.21f && p.Z<.35f);
            trigger.Update(active,actuated);
        }
    }
}
