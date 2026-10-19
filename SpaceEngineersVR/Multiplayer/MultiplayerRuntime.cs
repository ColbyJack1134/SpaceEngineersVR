using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using Sandbox.Game.Screens.Helpers;
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
            foreach(var type in new[] {typeof(CockpitStoragePatch),typeof(CockpitRemapPatch),typeof(AnalogSavePatch),typeof(RemoteArmsRestorePatch),typeof(RemoteArmsUpdatePatch),typeof(HeldWeaponShotPatch),typeof(HeldProjectileStartPatch),typeof(HeldDirectionPatch),typeof(HeldStraightAimPatch),typeof(HeldReloadAnimationPatch),typeof(HeldMagazineAlignmentPatch),typeof(ToolSensorPatch),typeof(HeldDrillPosePatch),typeof(ToolFeedbackPatch),typeof(HeldDrillContactPatch),typeof(WelderContactEffectPatch)})
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
        // The server drops client-to-client SendMessageTo, so the host relays client poses on RelayChannel.
        internal const ushort PoseChannel=48983,StateChannel=48984,DampenerChannel=48985,RelayChannel=48986;
        internal const int RelaySize=PlayerPose.Size+8;
        private const double Reach=250;
        internal static bool Enabled;
        internal static Func<uint,PlayerPose> Capture { get; set; }
        internal static Action<string> Notify=Log;
        internal static Func<MyCockpit,int> ControlCount=seat=>CockpitMemory.MaximumControls;
        internal static Func<MyCockpit,CockpitMemory.Record> LoadLocal { get; set; }
        internal static Action<MyCockpit,CockpitMemory.Record> SaveLocal { get; set; }
        internal static double Now => Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        private sealed class Message { internal ushort Channel; internal byte[] Data; internal ulong Sender; internal bool Server; }
        private sealed class Peer { internal double Seen; internal readonly RemoteArms Arms=new RemoteArms(); }
        private static readonly ConcurrentQueue<Message> incoming=new ConcurrentQueue<Message>();
        private static readonly Dictionary<ulong,Peer> peers=new Dictionary<ulong,Peer>();
        private static readonly Dictionary<long,RemoteArms> characters=new Dictionary<long,RemoteArms>();
        private static readonly Dictionary<long,CockpitMemory.Record> states=new Dictionary<long,CockpitMemory.Record>();
        private sealed class AnalogToolbar { internal MyCockpit Seat; internal string Xml,Raw; internal MyToolbar Toolbar; internal CockpitMemory.Record Record; }
        private static readonly Dictionary<long,AnalogToolbar> analogToolbars=new Dictionary<long,AnalogToolbar>();
        private static readonly Dictionary<long,double> requested=new Dictionary<long,double>();
        private static readonly Dictionary<ulong,SenderBudget> budgets=new Dictionary<ulong,SenderBudget>();
        // Wildlife bots are players under their creator's Steam id, so lookups by id take only real players.
        private static readonly List<IMyPlayer> players=new List<IMyPlayer>();
        private static MySession session;
        private static IMyMultiplayer network;
        private static int queued;
        private static double nextHello,nextPlayers,nextPose,connected;
        private static bool hostSupport;
        // Hosts without SEVR never answer the hello.
        internal static bool LocalOnly => network!=null && !network.IsServer && !hostSupport && LoadLocal!=null && Now-connected>6;
        private static uint sequence;
        internal static void Log(string text) => MyLog.Default?.WriteLine("SEVR multiplayer: "+text);
        internal static void Reset()
        {
            DampenerRequests.Reset();
            if(network!=null)
            {
                network.UnregisterSecureMessageHandler(PoseChannel,Receive);
                network.UnregisterSecureMessageHandler(StateChannel,Receive);
                network.UnregisterSecureMessageHandler(RelayChannel,Receive);
            }
            AnalogControl.Reset();
            foreach(var toolbar in analogToolbars.Values) ClearAnalogToolbar(toolbar);
            analogToolbars.Clear();
            foreach(var peer in peers.Values) peer.Arms.Clear();
            peers.Clear(); characters.Clear(); states.Clear(); requested.Clear(); budgets.Clear(); players.Clear();
            while(incoming.TryDequeue(out _)) Interlocked.Decrement(ref queued);
            network=null; session=null; nextHello=nextPlayers=nextPose=connected=0; sequence=0; hostSupport=false;
        }
        private static void Receive(ushort channel,byte[] data,ulong sender,bool fromServer)
        {
            if(data==null || data.Length>(channel==PoseChannel ? PlayerPose.Size:channel==RelayChannel ? RelaySize:CockpitMemory.Limit*2+64)) return;
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
                session=MySession.Static; network=MyAPIGateway.Multiplayer; connected=Now;
                network.RegisterSecureMessageHandler(PoseChannel,Receive);
                network.RegisterSecureMessageHandler(StateChannel,Receive);
                network.RegisterSecureMessageHandler(RelayChannel,Receive);
                Log("Session connected; "+(network.IsServer ? "host persistence active":"awaiting host support"));
            }
            DampenerRequests.Update();
            double now=Now;
            if(now>=nextPlayers)
            {
                nextPlayers=now+1; players.Clear(); MyAPIGateway.Players.GetPlayers(players,p=>!p.IsBot);
                foreach(var id in peers.Where(p=>now-p.Value.Seen>15 || !players.Any(player=>player.SteamUserId==p.Key)).Select(p=>p.Key).ToArray())
                { Forget(peers[id].Arms); peers.Remove(id); }
                foreach(var id in budgets.Keys.Where(id=>!players.Any(player=>player.SteamUserId==id)).ToArray()) budgets.Remove(id);
                foreach(var peer in peers.Values) if(peer.Arms.Character?.Closed==true) Forget(peer.Arms);
            }
            for(int i=0;i<256 && incoming.TryDequeue(out var message);i++)
            {
                Interlocked.Decrement(ref queued);
                try { Handle(message,now); }
                catch(Exception error) { Log("Rejected message: "+error.Message); }
            }
            if(network.IsServer || LocalOnly) AnalogControl.Update();
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
                    if(!network.IsServer) network.SendMessageToServer(PoseChannel,encoded,false);
                    else if(session.LocalCharacter!=null) Fanout(PoseChannel,encoded,network.MyId,session.LocalCharacter.PositionComp.GetPosition());
                }
            }
        }
        private static VRageMath.Vector3D? Locate(ulong id) => players.FirstOrDefault(p=>p.SteamUserId==id)?.Character?.GetPosition();
        private static void Fanout(ushort channel,byte[] data,ulong origin,VRageMath.Vector3D from)
        {
            foreach(ulong id in Audience(origin,network.MyId,from,peers.Keys,Locate)) network.SendMessageTo(channel,data,id,false);
        }
        internal static IEnumerable<ulong> Audience(ulong origin,ulong self,VRageMath.Vector3D from,IEnumerable<ulong> candidates,Func<ulong,VRageMath.Vector3D?> locate)
        {
            foreach(ulong id in candidates)
                if(id!=origin && id!=self && locate(id) is VRageMath.Vector3D at && VRageMath.Vector3D.DistanceSquared(at,from)<Reach*Reach) yield return id;
        }
        internal static byte[] RelayPacket(ulong origin,byte[] pose)
        {
            var data=new byte[8+pose.Length];
            Array.Copy(BitConverter.GetBytes(origin),data,8); Array.Copy(pose,0,data,8,pose.Length); return data;
        }
        // Only the host relays; a relay from a client could claim any origin.
        internal static bool ReadRelay(byte[] data,bool fromServer,out ulong origin,out byte[] pose)
        {
            origin=0; pose=null;
            if(!fromServer || data==null || data.Length<=8) return false;
            origin=BitConverter.ToUInt64(data,0); pose=new byte[data.Length-8]; Array.Copy(data,8,pose,0,pose.Length); return true;
        }
        private static void Handle(Message message,double now)
        {
            if(message.Sender==network.MyId) return;
            if(message.Server) hostSupport=true;
            if(message.Channel==PoseChannel)
            {
                if(Accept(message.Sender,message.Data,now,out var character) && network.IsServer)
                    Fanout(RelayChannel,RelayPacket(message.Sender,message.Data),message.Sender,character.PositionComp.GetPosition());
                return;
            }
            if(message.Channel==RelayChannel)
            {
                if(ReadRelay(message.Data,message.Server,out ulong origin,out var relayed)) Accept(origin,relayed,now,out _);
                return;
            }
            var player=players.FirstOrDefault(p=>p.SteamUserId==message.Sender);
            if(player==null && !message.Server) return;
            using(var reader=new BinaryReader(new MemoryStream(message.Data,false)))
            {
                if(reader.ReadInt32()!=0x31535653) return;
                byte kind=reader.ReadByte(); long seat=reader.ReadInt64(); string value=CockpitMemory.ReadBoundedString(reader);
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
                if(!network.IsServer || player==null || !(kind>=1 && kind<=3 || kind==6 || kind==8)) return;
                if(!budgets.TryGetValue(message.Sender,out var sender)) budgets.Add(message.Sender,sender=new SenderBudget());
                var budget=kind==6 ? sender.Analog:sender.Edits;
                if(!budget.Allow(now))
                {
                    if(!budget.Reported) { budget.Reported=true; Log((kind==6 ? "Analog updates":"Cockpit requests")+" from "+player.DisplayName+" exceed the rate limit; dropping"); }
                    return;
                }
                HostRequest(player,kind,seat,value);
            }
        }
        // True only for a pose newer than the last accepted one, so stale duplicates are not relayed.
        private static bool Accept(ulong origin,byte[] data,double now,out MyCharacter character)
        {
            character=null;
            var player=players.FirstOrDefault(p=>p.SteamUserId==origin);
            if(player==null || !peers.TryGetValue(origin,out var peer)) return false;
            var pose=PlayerPose.Decode(data);
            if(pose==null || player.Character?.EntityId!=pose.Character ||
                !(player.Character is MyCharacter owner) || (owner.Parent?.EntityId ?? 0)!=pose.Seat) return false;
            character=owner;
            if(peer.Arms.Character!=character) { Forget(peer.Arms); peer.Arms.Character=character; characters[character.EntityId]=peer.Arms; }
            bool fresh=peer.Arms.Stream.Push(pose,now); peer.Seen=now; return fresh;
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
                if(kind==6)
                {
                    if(seat.Pilot==null || player.Character?.EntityId!=seat.Pilot.EntityId) return;
                    ApplyAnalog(seat,AnalogRecord(seat),player.IdentityId,value);
                    return;
                }
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
                    else if(kind==8) record.Flight=FlightTuning.Decode(value);
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
        private static void ClearAnalogToolbar(AnalogToolbar value)
        {
            Detach(value.Toolbar);
            value.Toolbar.Clear();
        }
        internal static readonly System.Reflection.MethodInfo FatBlockClosed=AccessTools.Method(typeof(MyToolbar),"OnFatBlockClosed");
        // Undoes the inventory and grid subscriptions MyToolbar.Init adds for its owner.
        internal static void Detach(MyToolbar toolbar)
        {
            if(toolbar?.Owner?.HasInventory==true) toolbar.Owner.GetInventory().ContentsChanged-=toolbar.CharacterInventory_OnContentsChanged;
            DetachGrid(toolbar);
        }
        // MyToolbar.Init subscribes to the owner grid's OnFatBlockClosed and never unsubscribes, since vanilla
        // cockpit toolbars live as long as the cockpit. Ours are re-initialized, so release the grid's reference first.
        internal static void DetachGrid(MyToolbar toolbar)
        {
            if(!(toolbar?.Owner is MyCockpit cockpit) || cockpit.CubeGrid==null) return;
            cockpit.CubeGrid.OnFatBlockClosed-=(Action<MyCubeBlock>)Delegate.CreateDelegate(typeof(Action<MyCubeBlock>),toolbar,FatBlockClosed);
        }
        private static string RawStorage(MyCockpit seat) => seat.Storage!=null && seat.Storage.TryGetValue(CockpitMemory.Key,out string raw) ? raw:null;
        // Analog updates arrive at 20 Hz per hand; the stored record is decoded again only when the storage changed.
        private static CockpitMemory.Record AnalogRecord(MyCockpit seat)
        {
            if(analogToolbars.TryGetValue(seat.EntityId,out var cached) && ReferenceEquals(cached.Seat,seat) && cached.Record!=null && cached.Raw==RawStorage(seat))
                return cached.Record;
            return CockpitMemory.Read(seat);
        }
        private static void ApplyAnalog(MyCockpit seat,CockpitMemory.Record record,long identity,string value)
        {
            var parts=value.Split(':');
            if(parts.Length!=2 || !int.TryParse(parts[0],out int slot) || slot<0 || slot>=ControlCount(seat) || !AnalogControl.IsHandle(seat.BlockDefinition.Id.SubtypeName,slot) ||
                !float.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float position) ||
                !AnalogControl.Finite(position) || position<0 || position>1) return;
            if(!analogToolbars.TryGetValue(seat.EntityId,out var cached) || cached.Xml!=record.Toolbar || !ReferenceEquals(cached.Seat,seat))
            {
                if(cached!=null) ClearAnalogToolbar(cached);
                cached=new AnalogToolbar {Seat=seat,Xml=record.Toolbar,Toolbar=new MyToolbar(VRage.Game.MyToolbarType.ButtonPanel,9,(CockpitMemory.MaximumControls+8)/9)};
                cached.Toolbar.Init(CockpitMemory.Toolbar(record.Toolbar),seat);
                analogToolbars[seat.EntityId]=cached;
            }
            cached.Record=record; cached.Raw=RawStorage(seat);
            cached.Toolbar.UpdateItemForIdentity(slot,identity,false);
            var item=cached.Toolbar.GetItemAtIndex(slot);
            if(item?.Enabled!=true) return;
            var channels=AnalogControl.Resolve(item,identity);
            if(channels!=null) AnalogControl.Set(seat,identity,channels,position);
        }
        internal static void SetAnalog(MyCockpit seat,int slot,float position) => Request(6,seat,slot.ToString(CultureInfo.InvariantCulture)+":"+position.ToString("R",CultureInfo.InvariantCulture));
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
            else if(LocalOnly) LocalRequest(kind,seat,value);
            else network.SendMessageToServer(StateChannel,Packet(kind,seat.EntityId,value),true);
        }
        private static void LocalRequest(byte kind,MyCockpit seat,string value)
        {
            if(!states.TryGetValue(seat.EntityId,out var record)) states[seat.EntityId]=record=LoadLocal(seat);
            if(kind==1 || seat.Pilot==null || seat.Pilot!=session.LocalCharacter) return;
            try
            {
                if(kind==6) { ApplyAnalog(seat,record,session.LocalPlayerId,value); return; }
                if(kind==2) record.Toolbar=value;
                else if(kind==8) record.Flight=FlightTuning.Decode(value);
                else if(kind==3)
                {
                    var covers=Convert.FromBase64String(value);
                    if(covers.Length!=2 || covers[0]>=CockpitMemory.MaximumControls) return;
                    if(record.Covers.Length<CockpitMemory.MaximumControls) Array.Resize(ref record.Covers,CockpitMemory.MaximumControls);
                    record.Covers[covers[0]]=covers[1]!=0;
                }
                else return;
                record.Revision++;
                SaveLocal(seat,record);
            }
            catch(Exception error) { Log("Cockpit "+seat.EntityId+": "+error.Message); }
        }
        internal static bool Get(MyCockpit seat,out CockpitMemory.Record record)
        {
            if(network==null) { record=null; return false; }
            if(!states.ContainsKey(seat.EntityId) && (!requested.TryGetValue(seat.EntityId,out double last) || Now-last>=2))
            { requested[seat.EntityId]=Now; Request(1,seat,""); }
            return states.TryGetValue(seat.EntityId,out record);
        }
        internal static void SaveFlight(MyCockpit seat,FlightTuning value) => Request(8,seat,value.Encode());
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
        internal static bool ItemPose(MyCharacter character,out VRageMath.MatrixD model)
        {
            model=VRageMath.MatrixD.Identity;
            return Enabled && character!=null && characters.TryGetValue(character.EntityId,out var arms) && arms.TryItemPose(Now,out model);
        }
        internal static bool ItemSupported(MyCharacter character) => Enabled && character!=null &&
            characters.TryGetValue(character.EntityId,out var arms) && arms.Supported(Now);
        internal static bool ItemMirrored(MyCharacter character) => Enabled && character!=null &&
            characters.TryGetValue(character.EntityId,out var arms) && arms.Mirrored(Now);
        internal static bool ToolRay(MyCharacter character,out VRageMath.MatrixD ray)
        {
            ray=VRageMath.MatrixD.Identity;
            return Enabled && character!=null && characters.TryGetValue(character.EntityId,out var arms) && arms.TryToolRay(Now,out ray);
        }
        internal static void Animate(MyCharacter character)
        {
            if(!Enabled || character==MySession.Static?.LocalCharacter) return;
            if(characters.TryGetValue(character.EntityId,out var arms)) arms.Apply(Now);
        }
    }
}
