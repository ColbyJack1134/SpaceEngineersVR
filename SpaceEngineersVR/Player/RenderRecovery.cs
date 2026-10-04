using System;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // Disables a feature after an exception, then retries with backoff instead of staying off for the session.
    internal sealed class RenderRecovery
    {
        internal const double FirstDelay=2,MaxDelay=60,QuietPeriod=120;
        private readonly string name;
        private long retryAt,lastFailure;
        private int failures;
        private volatile bool failing;
        public RenderRecovery(string name) { this.name=name; }
        public bool Failed => FailedAt(DateTime.UtcNow);
        internal bool FailedAt(DateTime now) => failing && now.Ticks<System.Threading.Interlocked.Read(ref retryAt);
        public void Fail(Exception ex,string effect) => Fail(ex,effect,DateTime.UtcNow);
        internal double Fail(Exception ex,string effect,DateTime now)
        {
            if(now.Ticks-System.Threading.Interlocked.Read(ref lastFailure)>TimeSpan.FromSeconds(QuietPeriod).Ticks) failures=0;
            double delay=Math.Min(MaxDelay,FirstDelay*Math.Pow(2,Math.Min(failures,10)));
            failures++;
            System.Threading.Interlocked.Exchange(ref lastFailure,now.Ticks);
            System.Threading.Interlocked.Exchange(ref retryAt,now.AddSeconds(delay).Ticks);
            failing=true;
            if(ex!=null) Logger.Warning(ex,effect+"; retrying in "+delay.ToString("0")+" s");
            return delay;
        }
        public void Clear() => failing=false;
        public void Succeeded()
        {
            if(!failing || Failed) return;
            failing=false;
            Logger.Info(name+" recovered");
        }
        internal static void Quietly(Action action)
        {
            try { action(); }
            catch { }
        }
    }
}
