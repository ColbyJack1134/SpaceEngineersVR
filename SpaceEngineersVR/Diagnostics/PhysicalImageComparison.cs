using System;
using System.Drawing;
using System.IO;
using System.Linq;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Diagnostics
{
    public static class PhysicalImageComparison
    {
        public static int Difference(string first,string second)
        {
            int changed=0;
            using(var a=new Bitmap(first))
            using(var b=new Bitmap(second))
            {
                if(a.Size!=b.Size) throw new InvalidOperationException("Comparison images have different sizes");
                for(int y=0;y<a.Height;y+=4) for(int x=0;x<a.Width;x+=4)
                {
                    var p=a.GetPixel(x,y); var q=b.GetPixel(x,y);
                    if(Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B)>12) changed++;
                }
            }
            return changed;
        }
        internal static bool Rig(string directory,string subtype,Action<string> log)
        {
            string prefix=Path.Combine(directory,"rig-"+subtype+"-");
            double tolerance;
            // Preserve the original 720p sample-area tolerance at other resolutions.
            using(var image=new Bitmap(prefix+"native.png")) tolerance=10d*image.Width*image.Height/(1280*720);
            int rest=Difference(prefix+"native.png",prefix+"rest.png");
            int restored=Difference(prefix+"native.png",prefix+"restored.png");
            int moved=Difference(prefix+"rest.png",prefix+"moved.png");
            bool pass=rest<=tolerance && restored<=tolerance && moved>=5;
            log((pass ? "PASS":"FAIL")+" native cockpit rig articulation/restoration: "+subtype+
                "; rest="+rest+", restored="+restored+", moved="+moved+", tolerance="+tolerance.ToString("F2"));
            return pass;
        }
        public static void VerifyRigs(string directory,Action<string> log)
        {
            var failures=CockpitRig.All.Where(r=>r.ActorCount>0).Where(r=>!Rig(directory,r.Subtype,log)).Select(r=>r.Subtype).ToArray();
            if(failures.Length>0) throw new InvalidOperationException("Cockpit image mismatch: "+string.Join(", ",failures));
        }
    }
}
