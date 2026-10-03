using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Player;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace SpaceEngineersVR.Multiplayer
{
    public static class MultiplayerSupport
    {
        private static Harmony harmony;
        public static void Start()
        {
            if(MultiplayerRuntime.Enabled) return;
            harmony=new Harmony("SpaceEngineersVR.Multiplayer");
            foreach(var type in new[] {typeof(CockpitStoragePatch),typeof(CockpitRemapPatch),typeof(RemoteArmsRestorePatch),typeof(RemoteArmsUpdatePatch)})
                harmony.CreateClassProcessor(type).Patch();
            MultiplayerRuntime.Enabled=true;
        }
        public static void Update() => MultiplayerRuntime.Update();
        public static void Stop()
        {
            MultiplayerRuntime.Reset(); MultiplayerRuntime.Enabled=false;
            harmony?.UnpatchAll("SpaceEngineersVR.Multiplayer"); harmony=null;
        }
    }

    internal static class MultiplayerRuntime
    {
        private const ushort PoseChannel=48983,StateChannel=48984;
        internal static bool Enabled;
        internal static Func<uint,PlayerPose> Capture { get; set; }
        internal static Action<string> Notify=Log;
        internal static Func<MyCockpit,int> ControlCount=seat=>CockpitMemory.MaximumControls;
        internal static double Now => Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        private sealed class Message { internal ushort Channel; internal byte[] Data; internal ulong Sender; internal bool Server; }
        private sealed class Peer { internal double Seen; internal readonly RemoteArms Arms=new RemoteArms(); }
        private static readonly ConcurrentQueue<Message> incoming=new ConcurrentQueue<Message>();
        private static readonly Dictionary<ulong,Peer> peers=new Dictionary<ulong,Peer>();
        private static readonly Dictionary<long,RemoteArms> characters=new Dictionary<long,RemoteArms>();
        private static readonly Dictionary<long,CockpitMemory.Record> states=new Dictionary<long,CockpitMemory.Record>();
        private static readonly Dictionary<long,double> requested=new Dictionary<long,double>();
        private static readonly List<IMyPlayer> players=new List<IMyPlayer>();
        private static MySession session;
        private static IMyMultiplayer network;
        private static int queued;
        private static double nextHello,nextPlayers,nextPose;
        private static uint sequence;
        internal static void Log(string text) => MyLog.Default.WriteLine("SEVR multiplayer: "+text);
        internal static void Reset()
        {
            if(network!=null)
            {
                network.UnregisterSecureMessageHandler(PoseChannel,Receive);
                network.UnregisterSecureMessageHandler(StateChannel,Receive);
            }
            foreach(var peer in peers.Values) peer.Arms.Clear();
            peers.Clear(); characters.Clear(); states.Clear(); requested.Clear(); players.Clear();
            while(incoming.TryDequeue(out _)) Interlocked.Decrement(ref queued);
            network=null; session=null; nextHello=nextPlayers=nextPose=0; sequence=0;
        }
        private static void Receive(ushort channel,byte[] data,ulong sender,bool fromServer)
        {
            if(data==null || data.Length>(channel==PoseChannel ? PlayerPose.Size:CockpitMemory.Limit*2+64)) return;
            if(Interlocked.Increment(ref queued)>256) { Interlocked.Decrement(ref queued); return; }
            incoming.Enqueue(new Message {Channel=channel,Data=data,Sender=sender,Server=fromServer});
        }
        private static MySession failedSession;
        internal static void Update()
        {
            if(!Enabled || failedSession!=null && ReferenceEquals(failedSession,MySession.Static)) return;
            try { UpdateCore(); }
            catch(Exception error)
            {
                var failed=MySession.Static;
                Log("Support stopped for this session: "+error);
                Reset(); failedSession=failed;
                Notify("Multiplayer support stopped. See the plugin log.");
            }
        }
        private static void UpdateCore()
        {
            if(session!=MySession.Static) Reset();
            if(MySession.Static==null || MyAPIGateway.Multiplayer==null || MyAPIGateway.Players==null) return;
            if(network==null)
            {
                session=MySession.Static; network=MyAPIGateway.Multiplayer;
                network.RegisterSecureMessageHandler(PoseChannel,Receive);
                network.RegisterSecureMessageHandler(StateChannel,Receive);
                Log("Session connected; "+(network.IsServer ? "host persistence active":"awaiting host support"));
            }
            double now=Now;
            if(now>=nextPlayers)
            {
                nextPlayers=now+1; players.Clear(); MyAPIGateway.Players.GetPlayers(players);
                foreach(var id in peers.Where(p=>now-p.Value.Seen>15 || !players.Any(player=>player.SteamUserId==p.Key)).Select(p=>p.Key).ToArray())
                { Forget(peers[id].Arms); peers.Remove(id); }
                foreach(var peer in peers.Values) if(peer.Arms.Character?.Closed==true) Forget(peer.Arms);
            }
            for(int i=0;i<256 && incoming.TryDequeue(out var message);i++)
            {
                Interlocked.Decrement(ref queued);
                try { Handle(message,now); }
                catch(Exception error) { Log("Rejected message: "+error.Message); }
            }
            if(network.MultiplayerActive && now>=nextHello)
            {
                nextHello=now+5;
                network.SendMessageToOthers(StateChannel,Packet(0,0,""),true);
            }
            if(!Sandbox.Engine.Platform.Game.IsDedicated)
                foreach(var peer in peers.Values)
                    if(peer.Arms.Character?.IsSitting==true) peer.Arms.Apply(now);
            if(Capture!=null && now>=nextPose)
            {
                nextPose=now+.05;
                var pose=Capture(++sequence);
                if(pose!=null)
                {
                    var encoded=pose.Encode();
                    foreach(var pair in peers)
                    {
                        var player=players.FirstOrDefault(p=>p.SteamUserId==pair.Key);
                        if(player?.Character!=null && session.LocalCharacter!=null &&
                            VRageMath.Vector3D.DistanceSquared(player.Character.GetPosition(),session.LocalCharacter.PositionComp.GetPosition())<250*250)
                            network.SendMessageTo(PoseChannel,encoded,pair.Key,false);
                    }
                }
            }
        }
        private static void Handle(Message message,double now)
        {
            if(message.Sender==network.MyId) return;
            var player=players.FirstOrDefault(p=>p.SteamUserId==message.Sender);
            if(player==null && !message.Server) return;
            if(message.Channel==PoseChannel)
            {
                if(Sandbox.Engine.Platform.Game.IsDedicated || player==null || !peers.TryGetValue(message.Sender,out var peer)) return;
                var pose=PlayerPose.Decode(message.Data);
                if(pose==null || player.Character?.EntityId!=pose.Character ||
                    !(player.Character is MyCharacter character) || (character.Parent?.EntityId ?? 0)!=pose.Seat) return;
                if(peer.Arms.Character!=character) { Forget(peer.Arms); peer.Arms.Character=character; characters[character.EntityId]=peer.Arms; }
                peer.Arms.Stream.Push(pose,now); peer.Seen=now; return;
            }
            using(var reader=new BinaryReader(new MemoryStream(message.Data,false)))
            {
                if(reader.ReadInt32()!=0x31535653) return;
                byte kind=reader.ReadByte(); long seat=reader.ReadInt64(); string value=reader.ReadString();
                if(reader.BaseStream.Position!=reader.BaseStream.Length) return;
                if(kind==0 || kind==7)
                {
                    if(!peers.TryGetValue(message.Sender,out var peer)) peers.Add(message.Sender,peer=new Peer());
                    peer.Seen=now;
                    if(kind==0) network.SendMessageTo(StateChannel,Packet(7,0,""),message.Sender,true);
                    return;
                }
                if(kind==4 && message.Server)
                {
                    var record=CockpitMemory.Decode(value);
                    if(!states.TryGetValue(seat,out var previous) || record.Revision>=previous.Revision)
                    {
                        states[seat]=record;
                        if(MyEntities.TryGetEntityById(seat,out MyCockpit cockpit)) CockpitMemory.Write(cockpit,record);
                    }
                    return;
                }
                if(kind==5 && message.Server)
                { Notify("Cockpit settings could not be shared. See the plugin log."); Log(value); return; }
                if(network.IsServer && player!=null && kind>=1 && kind<=3) HostRequest(player,kind,seat,value);
            }
        }
        internal static byte[] Packet(byte kind,long seat,string value)
        {
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream))
            { writer.Write(0x31535653); writer.Write(kind); writer.Write(seat); writer.Write(value); return stream.ToArray(); }
        }
        private static void HostRequest(IMyPlayer player,byte kind,long id,string value)
        {
            if(!MyEntities.TryGetEntityById(id,out MyCockpit seat) || seat.Closed || ControlCount(seat)==0 ||
                !((IMyTerminalBlock)seat).HasPlayerAccess(player.IdentityId)) return;
            try
            {
                var record=CockpitMemory.Read(seat);
                if(kind!=1)
                {
                    if(seat.Pilot==null || player.Character?.EntityId!=seat.Pilot.EntityId) return;
                    int count=ControlCount(seat);
                    if(kind==2)
                    {
                        if(!CockpitMemory.ValidToolbar(value,count)) throw new InvalidDataException("Invalid cockpit toolbar.");
                        record.Toolbar=value;
                    }
                    else
                    {
                        var covers=Convert.FromBase64String(value);
                        if(covers.Length!=2 || covers[0]>=count || covers[1]>1) throw new InvalidDataException("Invalid cockpit cover.");
                        if(record.Covers.Length<CockpitMemory.MaximumControls) Array.Resize(ref record.Covers,CockpitMemory.MaximumControls);
                        record.Covers[covers[0]]=covers[1]!=0;
                    }
                    record.Revision=checked(record.Revision+1); CockpitMemory.Write(seat,record);
                }
                Publish(seat,record,player.SteamUserId,kind!=1);
            }
            catch(Exception error)
            {
                Log("Cockpit "+id+": "+error.Message);
                if(player.SteamUserId!=network.MyId) network.SendMessageTo(StateChannel,Packet(5,id,error.Message),player.SteamUserId,true);
                else Notify("Cockpit settings could not be saved.");
            }
        }
        private static void Publish(MyCockpit seat,CockpitMemory.Record record,ulong recipient,bool broadcast)
        {
            states[seat.EntityId]=record;
            var packet=Packet(4,seat.EntityId,CockpitMemory.Encode(record));
            foreach(var player in players)
                if(player.SteamUserId!=network.MyId && (player.SteamUserId==recipient || broadcast && peers.ContainsKey(player.SteamUserId) && ((IMyTerminalBlock)seat).HasPlayerAccess(player.IdentityId)))
                    network.SendMessageTo(StateChannel,packet,player.SteamUserId,true);
        }
        private static void Request(byte kind,MyCockpit seat,string value)
        {
            if(network==null || seat==null) return;
            if(network.IsServer)
            {
                var local=players.FirstOrDefault(p=>p.SteamUserId==network.MyId);
                if(local!=null) HostRequest(local,kind,seat.EntityId,value);
            }
            else network.SendMessageToServer(StateChannel,Packet(kind,seat.EntityId,value),true);
        }
        internal static bool Get(MyCockpit seat,out CockpitMemory.Record record)
        {
            if(network==null) { record=null; return false; }
            if(!states.ContainsKey(seat.EntityId) && (!requested.TryGetValue(seat.EntityId,out double last) || Now-last>=2))
            { requested[seat.EntityId]=Now; Request(1,seat,""); }
            return states.TryGetValue(seat.EntityId,out record);
        }
        internal static void SaveToolbar(MyCockpit seat,string xml) => Request(2,seat,xml);
        internal static void SaveCover(MyCockpit seat,int index,bool open) => Request(3,seat,Convert.ToBase64String(new[] {(byte)index,(byte)(open ? 1:0)}));
        private static void Forget(RemoteArms arms)
        {
            if(arms.Character!=null) characters.Remove(arms.Character.EntityId);
            arms.Clear();
        }
        internal static void Restore(MyCharacter character)
        {
            if(characters.TryGetValue(character.EntityId,out var arms)) arms.Restore();
        }
        internal static void Animate(MyCharacter character)
        {
            if(!Enabled || character==MySession.Static?.LocalCharacter) return;
            if(characters.TryGetValue(character.EntityId,out var arms)) arms.Apply(Now);
        }
    }
}
