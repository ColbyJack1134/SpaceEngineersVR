namespace SpaceEngineersVR.Player.Control
{
    // Preview can sweep across targets. Once a squeeze fires or captures UI,
    // it keeps that context until release.
    internal sealed class PointerIntent
    {
        private bool armed, squeezing, firing, clickedLastFrame;
        private bool canAcquire;
        private string preferred;
        public string Owner { get; private set; }
        public bool Owned => Owner!=null;
        public bool Preview { get; private set; }

        public void Reset() { Owner=preferred=null; armed=squeezing=canAcquire=firing=clickedLastFrame=Preview=false; }
        public void Begin(bool available,float pressure,bool clicked,bool pointing=true,string preferredSurface=null)
        {
            canAcquire=Preview=false;
            if(!available || float.IsNaN(pressure)) { Reset(); return; }
            if(pressure<=.025f && !clicked)
            {
                Reset(); armed=true;
                return;
            }
            if(!armed) return;
            if(pressure>=.06f || clicked) squeezing=true;
            if(clickedLastFrame && !Owned) firing=true;
            clickedLastFrame=clicked;
            preferred=preferredSurface;
            canAcquire=squeezing && !firing && pointing;
            Preview=squeezing && !firing && (!clicked || Owned) && pointing;
        }
        public bool Capture(string surface,bool hit)
        {
            if(canAcquire && Owner==null && hit && (preferred==null || preferred==surface)) { Owner=surface; canAcquire=false; Preview=true; }
            return Owner==surface;
        }
    }
}
