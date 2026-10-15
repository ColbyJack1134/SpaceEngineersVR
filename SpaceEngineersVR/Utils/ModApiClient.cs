using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Util
{
    // Client of a mod's message API: requests its endpoint dictionary on a channel and hands the delegates to attach,
    // which reports whether the API is usable. Unload unregisters and calls detach to drop the delegates.
    internal sealed class ModApiClient
    {
        private readonly string name;
        private readonly long channel;
        private readonly Func<IReadOnlyDictionary<string,Delegate>,bool> attach;
        private readonly Action detach;
        private readonly bool ignoreImmutable;
        private readonly Action<object> handler;
        private bool registered;
        internal bool Ready { get; private set; }

        // ignoreImmutable skips ImmutableDictionary copies other clients echo back on the channel (Defense Shields' tamper check).
        internal ModApiClient(string name,long channel,Func<IReadOnlyDictionary<string,Delegate>,bool> attach,Action detach,bool ignoreImmutable=false)
        {
            this.name=name; this.channel=channel; this.attach=attach; this.detach=detach; this.ignoreImmutable=ignoreImmutable; handler=Receive;
        }

        internal void Load()
        {
            if(registered || MyAPIGateway.Utilities==null) return;
            registered=true;
            MyAPIGateway.Utilities.RegisterMessageHandler(channel,handler);
            MyAPIGateway.Utilities.SendModMessage(channel,"ApiEndpointRequest");
        }

        internal void Unload()
        {
            if(registered && MyAPIGateway.Utilities!=null) MyAPIGateway.Utilities.UnregisterMessageHandler(channel,handler);
            registered=false; Ready=false;
            detach();
        }

        private void Receive(object message)
        {
            // Our own request string echoes back on the channel; only dictionaries are endpoints.
            if(!(message is IReadOnlyDictionary<string,Delegate> methods) || ignoreImmutable && message.GetType().Name.StartsWith("Immutable")) return;
            if(methods.Count==0) { Ready=false; return; }
            try { Ready=attach(methods); Logger.Info(name+" API attached; ready="+Ready); }
            catch(Exception ex) { Ready=false; Logger.Warning(ex,name+" API attach failed; its VR support is disabled"); }
        }

        internal static T Get<T>(IReadOnlyDictionary<string,Delegate> methods,string name) where T:class =>
            methods.TryGetValue(name,out var method) ? method as T : null;
    }
}
