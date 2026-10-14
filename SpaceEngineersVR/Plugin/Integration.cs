using System;

namespace SpaceEngineersVR.Plugin
{
    // Optional mod integration: a failure disables it until the next world instead of stopping the VR update.
    internal sealed class Integration
    {
        private readonly string name;
        private readonly Action reset,refresh,update,draw;
        internal bool Failed { get; private set; }
        internal Integration(string name,Action reset,Action refresh,Action update=null,Action draw=null)
        {
            this.name=name; this.reset=reset; this.refresh=refresh; this.update=update; this.draw=draw;
        }
        internal void Refresh() => Run(refresh);
        internal void Update() => Run(update);
        internal void Draw() => Run(draw);
        internal void Reset() { Failed=false; reset(); }
        private void Run(Action action)
        {
            if(Failed || action==null) return;
            try { action(); }
            catch(Exception ex)
            {
                Failed=true;
                Logger.Warning(ex,name+" integration disabled until the next world");
                try { reset(); }
                catch(Exception cleanup) { Logger.Warning(cleanup,name+" integration cleanup failed"); }
            }
        }
    }
}
