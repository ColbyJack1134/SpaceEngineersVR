using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Engine.Networking;
using Sandbox.Game.Gui;
using Sandbox.Game.Localization;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage;
using VRage.GameServices;
using VRage.Utils;

namespace SpaceEngineersVR.Player
{
    internal static class WristChat
    {
        internal sealed class View
        {
            public MyChatItem[] Messages;
            public int Older,Count,Timestamp;
            public bool Available,ChoosingChannel,HasFaction;
            public string Channel,Draft;
            public MyPlayer[] Players;
            public int PlayerPage;
            public string Key => Timestamp+"|"+Older+"|"+Count+"|"+Available+"|"+Channel+"|"+Draft+"|"+ChoosingChannel+"|"+PlayerPage;
        }
        private static int older,lastCount,playerPage;
        private static bool choosingChannel,stickScrolling;
        private static DateTime nextStickScroll;
        private static string draft="";
        internal static void Reset() { older=lastCount=playerPage=0; choosingChannel=stickScrolling=false; nextStickScroll=DateTime.MinValue; draft=""; }
        internal static void Open() { lastCount=MyHud.Chat.Messages.Count; older=0; choosingChannel=stickScrolling=false; nextStickScroll=DateTime.MinValue; WristPanel.OpenChat(); }
        internal static View Snapshot()
        {
            var messages=MyHud.Chat.Messages.ToArray();
            if(older>0 && messages.Length>lastCount) older+=messages.Length-lastCount;
            lastCount=messages.Length;
            older=Math.Max(0,Math.Min(older,Math.Max(0,messages.Length-1)));
            var session=MySession.Static;
            var chat=session?.ChatSystem;
            string channel=chat?.CurrentChannel.ToString() ?? "Global";
            if(chat?.CurrentChannel==ChatChannel.Private)
                channel="To "+(session.Players.TryGetIdentity(chat.CurrentTarget)?.DisplayName ?? "player");
            var players=session?.Players?.GetOnlinePlayers().Where(p=>p.Identity!=null && p.Identity.IdentityId!=session.LocalPlayerId).OrderBy(p=>p.DisplayName).ToArray() ?? new MyPlayer[0];
            playerPage=Math.Min(playerPage,Math.Max(0,(players.Length-1)/4));
            return new View {Messages=messages,Older=older,
                Count=messages.Length,Timestamp=MyHud.Chat.Timestamp,
                Available=MyMultiplayer.Static==null || MyMultiplayer.Static.IsTextChatAvailable,
                Channel=channel,Draft=draft,ChoosingChannel=choosingChannel,
                HasFaction=session?.Factions?.TryGetPlayerFaction(session.LocalPlayerId)!=null,
                Players=players,PlayerPage=playerPage};
        }
        internal static void Scroll(float value)
        {
            int count=MyHud.Chat.Messages.Count;
            older=Math.Max(0,Math.Min(count-1,(int)Math.Round((1-value)*(count-1))));
        }
        internal static bool ScrollThumbstick(float raw,float accepted,DateTime now)
        {
            if(choosingChannel || MyHud.Chat.Messages.Count<2 || Math.Abs(raw)<.15f)
            { stickScrolling=false; nextStickScroll=DateTime.MinValue; return false; }
            if(!stickScrolling && Math.Abs(accepted)>.45f) stickScrolling=true;
            if(!stickScrolling) return false;
            if(Math.Abs(raw)>.45f && now>=nextStickScroll)
            {
                older=Math.Max(0,Math.Min(MyHud.Chat.Messages.Count-1,older+3*Math.Sign(raw)));
                nextStickScroll=now.AddMilliseconds(130);
            }
            return true;
        }
        internal static void Keys(List<SurfaceKey> keys,View preview=null)
        {
            var view=preview ?? Snapshot();
            keys.Add(new SurfaceKey(view.ChoosingChannel ? "Close" : view.Channel+"  ▾",.15f,.14f,.49f,.085f) {
                Action=new ActionChoice("Chat channel",()=>choosingChannel=!choosingChannel) });
            if(view.ChoosingChannel)
            {
                keys.Add(new SurfaceKey("Global",.05f,.27f,.86f,.085f) {Action=new ActionChoice("Global chat",()=>ChooseChannel(ChatChannel.Global,0))});
                keys.Add(new SurfaceKey("Faction",.05f,.37f,.86f,.085f) {Enabled=view.HasFaction,
                    Action=new ActionChoice("Faction chat",()=>ChooseChannel(ChatChannel.Faction,0))});
                int start=view.PlayerPage*4;
                for(int i=start;i<Math.Min(view.Players.Length,start+4);i++)
                {
                    long id=view.Players[i].Identity.IdentityId;
                    keys.Add(new SurfaceKey("To "+view.Players[i].DisplayName,.05f,.48f+(i-start)*.09f,.86f,.08f) {
                        Action=new ActionChoice("Private chat",()=>ChooseChannel(ChatChannel.Private,id)) });
                }
                if(view.Players.Length>4)
                {
                    keys.Add(new SurfaceKey("<",.05f,.86f,.17f,.07f) {Enabled=view.PlayerPage>0,
                        Action=new ActionChoice("Previous players",()=>playerPage=Math.Max(0,playerPage-1))});
                    keys.Add(new SurfaceKey(">",.74f,.86f,.17f,.07f) {Enabled=(view.PlayerPage+1)*4<view.Players.Length,
                        Action=new ActionChoice("More players",()=>playerPage++)});
                }
                return;
            }
            if(view.Count>1) keys.Add(new SurfaceKey("",.93f,.265f,.05f,.515f) {Vertical=true,
                Slider=1f-(float)view.Older/(view.Count-1),Change=Scroll});
            keys.Add(new SurfaceKey(string.IsNullOrEmpty(view.Draft) ? "" : view.Draft,.025f,.855f,.95f,.09f) {
                Enabled=view.Available,Action=new ActionChoice("Type chat message",Compose) });
        }
        private static void ChooseChannel(ChatChannel channel,long playerId)
        {
            var chat=MySession.Static?.ChatSystem;
            if(chat==null) return;
            if(channel==ChatChannel.Global) chat.ChangeChatChannel_Global();
            else if(channel==ChatChannel.Faction) chat.ChangeChatChannel_Faction();
            else chat.ChangeChatChannel_Whisper(playerId);
            choosingChannel=false;
        }
        private static void Compose()
        {
            var owner=MySession.Static?.ControlledEntity;
            MenuKeyboard.Open(draft,value=>draft=value,()=>WristPanel.ChatOpen &&
                ReferenceEquals(owner,MySession.Static?.ControlledEntity),null,"SEND",Send,
                (int)(AccessTools.Method(typeof(MyGameService),"GetChatMaxMessageSize")?.Invoke(null,null) ?? 200));
        }
        private static void Send()
        {
            if(MyMultiplayer.Static!=null && !MyMultiplayer.Static.IsTextChatAvailable) return;
            string message=draft;
            draft="";
            Send(message);
        }
        internal static void Send(string message)
        {
            if(MyMultiplayer.Static!=null && !MyMultiplayer.Static.IsTextChatAvailable) return;
            if(string.IsNullOrWhiteSpace(message) || MySession.Static?.LocalHumanPlayer==null) return;
            var session=MySession.Static;
            if(session.ChatSystem.CommandSystem.CanHandle(message))
            {
                MyHud.Chat.ShowMessage(session.LocalHumanPlayer.PlatformDisplayName,message);
                session.ChatSystem.CommandSystem.Handle(message);
                return;
            }
            bool sendToOthers=true;
            MyAPIUtilities.Static.EnterMessage(session.LocalHumanPlayer.Id.SteamId,message,ref sendToOthers);
            if(!sendToOthers) return;
            if(MyMultiplayer.Static!=null)
                MyMultiplayer.Static.SendChatMessage(message,session.ChatSystem.CurrentChannel,session.ChatSystem.CurrentTarget);
            else if(MyGameService.IsActive)
                MyHud.Chat.ShowMessageColoredSP(message,session.ChatSystem.CurrentChannel,session.ChatSystem.CurrentTarget);
            else MyHud.Chat.ShowMessage(session.LocalHumanPlayer.PlatformDisplayName,message);
        }
        internal static Color SenderColor(MyChatItem message) => message.Font=="Blue" && message.SenderColor==VRageMath.Color.White
            ? Color.FromArgb(145,196,232) : Color.FromArgb(message.SenderColor.A,message.SenderColor.R,message.SenderColor.G,message.SenderColor.B);
        internal static Color BodyColor(MyChatItem message) => Color.FromArgb(message.MessageColor.A,message.MessageColor.R,message.MessageColor.G,message.MessageColor.B);
        internal static string VisibleText(string value) => (value ?? "").Replace("\ue030","").Replace("\ue031","");
        internal static void Paint(OverlayCanvas target,SurfaceView surface)
        {
            target.Clear(Color.FromArgb(255,12,20,28));
            var view=surface.ChatView;
            var g=target.Graphics;
            using(var title=new Font("Segoe UI",27,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var font=new Font("Segoe UI",21,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var small=new Font("Segoe UI",19,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var muted=new SolidBrush(Color.FromArgb(180,200,210)))
            {
                g.DrawString("Chat",title,Brushes.White,26,92);
                if(view?.Available==false) g.DrawString(MyTexts.GetString(MyCommonTexts.ChatRestricted),small,Brushes.IndianRed,670,103);
                if(view!=null && !view.ChoosingChannel)
                {
                    const float bottom=522,topLimit=161;
                    int end=Math.Max(0,view.Messages.Length-view.Older);
                    var rows=new List<Tuple<MyChatItem,float>>();
                    float used=0;
                    for(int i=end-1;i>=0;i--)
                    {
                        var message=view.Messages[i];
                        string name=VisibleText(message.Sender)+": ";
                        float x=30;
                        if(MySandboxGame.Config?.ShowChatTimestamp==true) x+=g.MeasureString("["+message.Timestamp.ToLongTimeString()+"] ",font).Width;
                        x+=Math.Min(g.MeasureString(name,font).Width,360);
                        float height=Math.Max(29,g.MeasureString(VisibleText(message.Message),font,(int)Math.Max(100,935-x)).Height)+3;
                        if(used+height>bottom-topLimit && rows.Count>0) break;
                        rows.Add(Tuple.Create(message,height)); used+=height;
                    }
                    float top=bottom-used;
                    var clip=g.Clip;
                    g.SetClip(new RectangleF(25,topLimit,965,bottom-topLimit));
                    for(int i=rows.Count-1;i>=0;i--)
                    {
                        var message=rows[i].Item1;
                        using(var sender=new SolidBrush(SenderColor(message)))
                        using(var body=new SolidBrush(BodyColor(message)))
                        {
                            string name=VisibleText(message.Sender)+": ";
                            float x=30;
                            if(MySandboxGame.Config?.ShowChatTimestamp==true)
                            {
                                string time="["+message.Timestamp.ToLongTimeString()+"] ";
                                g.DrawString(time,font,muted,x,top);
                                x+=g.MeasureString(time,font).Width;
                            }
                            float width=Math.Min(g.MeasureString(name,font).Width,360);
                            using(var label=new StringFormat {FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter})
                                g.DrawString(name,font,sender,new RectangleF(x,top,width,30),label);
                            x=Math.Min(x+width,950);
                            g.DrawString(VisibleText(message.Message),font,body,new RectangleF(x,top,935-x,rows[i].Item2));
                        }
                        top+=rows[i].Item2;
                    }
                    g.Clip=clip;
                }
            }
            PhysicalSurface.PaintWristKeys(target,surface);
        }
    }
}
