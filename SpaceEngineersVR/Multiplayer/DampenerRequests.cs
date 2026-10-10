using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class DampenerRequests
    {
        private const ushort Channel=48985;
        private const int RequestSize=70,ResultSize=18;
        internal static Action<uint,long,bool> Result { get; set; }
        private sealed class Message { internal byte[] Data; internal ulong Sender; internal bool Server; }
        private static readonly ConcurrentQueue<Message> messages=new ConcurrentQueue<Message>();
        private static IMyMultiplayer network;
        private static int queued;
        internal static void Reset()
        {
            network?.UnregisterSecureMessageHandler(Channel,Receive); network=null;
            while(messages.TryDequeue(out _)) Interlocked.Decrement(ref queued);
        }
        private static void Receive(ushort channel,byte[] data,ulong sender,bool server)
        {
            if(data==null || (data.Length!=RequestSize && data.Length!=ResultSize)) return;
            if(Interlocked.Increment(ref queued)>64) { Interlocked.Decrement(ref queued); return; }
            messages.Enqueue(new Message {Data=data,Sender=sender,Server=server});
        }
        internal static byte[] Packet(uint token,long owner,long target,LineD ray)
        {
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                writer.Write((byte)1); writer.Write((byte)0); writer.Write(token); writer.Write(owner); writer.Write(target);
                writer.Write(ray.From.X); writer.Write(ray.From.Y); writer.Write(ray.From.Z);
                var direction=ray.Direction;
                writer.Write(direction.X); writer.Write(direction.Y); writer.Write(direction.Z);
                return stream.ToArray();
            }
        }
        internal sealed class RequestData
        {
            internal uint Token;
            internal long Owner,Target;
            internal Vector3D Start,Direction;
        }
        internal static bool Decode(byte[] bytes,out RequestData request)
        {
            request=null;
            if(bytes==null || bytes.Length!=RequestSize) return false;
            using(var stream=new MemoryStream(bytes)) using(var reader=new BinaryReader(stream))
            {
                if(reader.ReadByte()!=1 || reader.ReadByte()!=0) return false;
                var value=new RequestData {Token=reader.ReadUInt32(),Owner=reader.ReadInt64(),Target=reader.ReadInt64(),
                    Start=new Vector3D(reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble()),
                    Direction=new Vector3D(reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble())};
                if(value.Token==0 || value.Owner==0 || !ValidRay(value.Start,value.Direction)) return false;
                request=value; return true;
            }
        }
        internal static bool Request(uint token,long owner,long target,LineD ray)
        {
            if(MySession.Static==null || MyAPIGateway.Multiplayer==null) return false;
            Update();
            var packet=Packet(token,owner,target,ray);
            if(network.IsServer) messages.Enqueue(new Message {Data=packet,Sender=Sync.MyId});
            else if(!network.SendMessageToServer(Channel,packet,true)) return false;
            if(network.IsServer) Interlocked.Increment(ref queued);
            return true;
        }
        internal static void Update()
        {
            if(MySession.Static==null || MyAPIGateway.Multiplayer==null) { Reset(); return; }
            if(network!=MyAPIGateway.Multiplayer)
            {
                Reset(); network=MyAPIGateway.Multiplayer; network.RegisterSecureMessageHandler(Channel,Receive);
            }
            for(int i=0;i<64 && messages.TryDequeue(out var message);i++)
            {
                Interlocked.Decrement(ref queued);
                using(var stream=new MemoryStream(message.Data)) using(var reader=new BinaryReader(stream))
                {
                    if(reader.ReadByte()!=1) continue;
                    byte kind=reader.ReadByte(); uint token=reader.ReadUInt32(); long ownerId=reader.ReadInt64();
                    if(kind==1 && message.Server && !network.IsServer && message.Data.Length==ResultSize)
                    { Result?.Invoke(token,ownerId,reader.ReadInt32()==1); continue; }
                    if(kind!=0 || !network.IsServer || message.Data.Length!=RequestSize) continue;
                    if(!Decode(message.Data,out var request)) continue;
                    bool accepted=Apply(message.Sender,ownerId,request.Target,request.Start,request.Direction);
                    if(message.Sender==Sync.MyId) Result?.Invoke(token,ownerId,accepted);
                    else
                    {
                        using(var result=new MemoryStream()) using(var writer=new BinaryWriter(result))
                        {
                            writer.Write((byte)1); writer.Write((byte)1); writer.Write(token); writer.Write(ownerId); writer.Write(accepted ? 1:0);
                            network.SendMessageTo(Channel,result.ToArray(),message.Sender,true);
                        }
                    }
                }
            }
        }
        internal static bool ValidRay(Vector3D start,Vector3D direction) => start.IsValid() && direction.IsValid() &&
            Math.Abs(direction.LengthSquared()-1)<.001;
        internal static bool Owns(long senderIdentity,long controllingIdentity) => senderIdentity!=0 && senderIdentity==controllingIdentity;
        private static bool Apply(ulong sender,long ownerId,long targetId,Vector3D start,Vector3D direction)
        {
            if(!ValidRay(start,direction) || !(MyEntities.GetEntityByIdOrDefault(ownerId) is Sandbox.Game.Entities.IMyControllableEntity controlled) ||
                controlled.Entity.Closed || controlled.Entity.MarkedForClose ||
                !Owns(MySession.Static.Players.TryGetIdentityId(sender),controlled.ControllerInfo?.ControllingIdentityId ?? 0)) return false;
            // Detached VR views can put the pointer away from the body; native target reach still applies to the body.
            if(Vector3D.DistanceSquared(start,controlled.Entity.PositionComp.GetPosition())>20000d*20000) return false;
            var ray=new LineD(start,start+direction*1000);
            var result=MyEntities.GetIntersectionWithLine(ref ray,controlled.Entity,controlled.Entity.GetTopMostParent(),ignoreChildren:false,ignoreFloatingObjects:false);
            var target=result?.Entity?.GetTopMostParent() as MyCubeGrid;
            if(target==null || target.Closed || target.MarkedForClose || target.Physics==null ||
                target==controlled.Entity.GetTopMostParent() || targetId!=0 && target.EntityId!=targetId ||
                !MyEntityThrustComponent.IsInRangeOfRelativeDampening(controlled,target)) return false;
            controlled.RelativeDampeningEntity=target;
            if(controlled.RelativeDampeningEntity!=target) return false;
            if(!controlled.EnabledDamping) controlled.SwitchDamping();
            MyMultiplayer.RaiseStaticEvent((VRage.Network.IMyEventOwner x)=>MyPlayerCollection.SetDampeningEntityClient,ownerId,target.EntityId);
            return true;
        }
    }
}
