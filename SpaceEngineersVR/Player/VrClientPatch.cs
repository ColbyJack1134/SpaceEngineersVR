#if LINUX
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceEngineersVR.Player
{
    // vrclient asks the driver for these by KHR name, which is null unless the extension was enabled by name; DXVK enables only the core versions, so vrclient gets the core names.
    internal static unsafe class VrClientPatch
    {
        private static readonly string[] Names=
        {
            "vkGetImageMemoryRequirements2KHR",
            "vkGetBufferMemoryRequirements2KHR",
        };
        private const int Read=1,Write=2;
        private static readonly ulong PageMask=(ulong)Environment.SystemPageSize-1;

        [DllImport("libc",SetLastError=true)]
        private static extern int mprotect(IntPtr address,UIntPtr length,int protection);

        internal static void Apply(Action<string> log)
        {
            bool mapped=false;
            foreach(var line in File.ReadAllLines("/proc/self/maps"))
            {
                // The names live in read-only data, not code.
                var path=line.TrimEnd();
                if(path.EndsWith(" (deleted)",StringComparison.Ordinal)) path=path.Substring(0,path.Length-10);
                if(!path.EndsWith("/vrclient.so",StringComparison.Ordinal)) continue;
                mapped=true;
                var fields=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                if(fields[1][0]!='r'||fields[1][2]=='x') continue;
                var range=fields[0].Split('-');
                ulong start=Convert.ToUInt64(range[0],16),end=Convert.ToUInt64(range[1],16);
                int protection=Read|(fields[1][1]=='w' ? Write:0);
                Rename((byte*)start,(long)(end-start),protection,log);
            }
            if(!mapped) log("vrclient.so is not mapped; its Vulkan function names were not checked");
        }

        private static void Rename(byte* data,long length,int protection,Action<string> log)
        {
            foreach(var name in Names)
            {
                var pattern=Encoding.ASCII.GetBytes(name);
                var replacement=Encoding.ASCII.GetBytes(name.Substring(0,name.Length-3));
                for(long at=Find(data,length,pattern,0);at>=0;at=Find(data,length,pattern,at+1))
                {
                    // Whole strings only; the core name is shorter, so the rest of the slot becomes terminators.
                    if(at+pattern.Length>=length||data[at+pattern.Length]!=0) continue;
                    ulong page=(ulong)(data+at)&~PageMask;
                    var size=(UIntPtr)((((ulong)(data+at)+(ulong)pattern.Length+PageMask)&~PageMask)-page);
                    if(mprotect((IntPtr)page,size,protection|Write)!=0)
                    {
                        log("vrclient "+name+" could not be renamed: mprotect errno "+Marshal.GetLastWin32Error());
                        continue;
                    }
                    for(int i=0;i<pattern.Length;i++) data[at+i]=i<replacement.Length ? replacement[i]:(byte)0;
                    mprotect((IntPtr)page,size,protection);
                    log("vrclient "+name+" renamed to its core function");
                }
            }
        }

        private static long Find(byte* data,long length,byte[] pattern,long from)
        {
            for(long at=from;at+pattern.Length<=length;at++)
            {
                int i=0;
                while(i<pattern.Length&&data[at+i]==pattern[i]) i++;
                if(i==pattern.Length) return at;
            }
            return -1;
        }
    }
}
#endif
