namespace SpaceEngineersVR.Player.Control
{
    internal sealed class HoldGesture
    {
        private bool wasDown,latched,releasing;
        private double pressedAt;
        internal void Reset() { wasDown=latched=releasing=false; }
        internal bool Update(bool down,bool held,bool enabled,double now)
        {
            if(!enabled) { Reset(); return down; }
            if(down && !wasDown)
            {
                pressedAt=now;
                if(latched) { latched=false; releasing=true; }
            }
            if(!held) latched=false;
            if(!down && wasDown && held && !releasing && now-pressedAt<=.25) latched=true;
            wasDown=down;
            if(releasing) { if(!down) releasing=false; return false; }
            return down || latched;
        }
    }
}
