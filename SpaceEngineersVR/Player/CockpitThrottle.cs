using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class CockpitThrottle
    {
        private bool wasHeld,wasCommon;
        private WristKnob.Turn turn;
        private Matrix correction=Matrix.Identity,previous=Matrix.Identity;
        internal void Reset()
        {
            wasHeld=wasCommon=false; correction=previous=Matrix.Identity; turn=null;
        }
        internal float Update(bool held,bool commonActive,Matrix wrist,Matrix frame,Vector3 shaft,float travel)
        {
            if(!held || !wrist.IsValid() || !frame.IsValid()) {Reset(); return 0;}
            Matrix local=wrist.GetOrientation()*Matrix.Transpose(frame.GetOrientation());
            // A grip handoff changes the compensation frame, not the rider's throttle angle.
            if(wasHeld && commonActive!=wasCommon) correction=Matrix.Transpose(local)*previous;
            previous=local*correction;
            if(!wasHeld)
            {
                turn=new WristKnob.Turn(shaft,travel,1); turn.Begin(previous,0);
            }
            else turn.Move(previous);
            wasHeld=true; wasCommon=commonActive;
            return turn.Value;
        }
    }
}
