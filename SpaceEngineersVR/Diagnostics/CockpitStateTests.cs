using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Sandbox.ModAPI.Ingame;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitStateTests
    {
        private sealed class Connector : RealProxy
        {
            internal MyShipConnectorStatus Status;
            internal int Connects,Disconnects;
            internal Connector() : base(typeof(IMyShipConnector)) { }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message; object result=null;
                switch(call.MethodName)
                {
                    case "get_Status": result=Status; break;
                    case "Connect": Connects++; break;
                    case "Disconnect": Disconnects++; break;
                    default: throw new Exception("Unexpected connector call: "+call.MethodName);
                }
                return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
            }
        }
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }
        internal static void Run(Action<string> log)
        {
            var connector=new Connector(); var block=(IMyShipConnector)connector.GetTransparentProxy();
            connector.Status=MyShipConnectorStatus.Connectable;
            Require(CockpitSwitchState.ReadValue(block,null)==.5f,"Ready connector is not centered");
            CockpitSwitchState.SetValue(block,null,true);
            Require(connector.Connects==1 && connector.Disconnects==0 && CockpitSwitchState.ReadValue(block,null)==.5f,
                "Connect request toggles the connector or fakes successful attachment before native confirmation");
            connector.Status=MyShipConnectorStatus.Connected;
            Require(CockpitSwitchState.ReadValue(block,null)==1,"Connected connector is not up");
            CockpitSwitchState.SetValue(block,null,true);
            Require(connector.Connects==1,"Already-connected connector issues another connect request");
            CockpitSwitchState.SetValue(block,null,false);
            Require(connector.Disconnects==1 && CockpitSwitchState.ReadValue(block,null)==1,"Disconnect did not use native action/confirmation");
            connector.Status=MyShipConnectorStatus.Unconnected;
            Require(CockpitSwitchState.ReadValue(block,null)==0,"Disconnected connector is not down");
            CockpitSwitchState.SetValue(block,null,false);
            Require(connector.Disconnects==1,"Disconnected connector repeats disconnect");
            connector.Status=MyShipConnectorStatus.Connectable;
            CockpitSwitchState.SetValue(block,null,false);
            Require(connector.Disconnects==2,"Down from ready did not issue a native disconnect request");
            var other=new Connector {Status=MyShipConnectorStatus.Unconnected};
            var group=new[] {block,(IMyShipConnector)other.GetTransparentProxy()};
            var properties=new Sandbox.ModAPI.Interfaces.ITerminalProperty<bool>[2];
            Require(CockpitSwitchState.ReadValues(group,properties)==.5f,"Partly ready group cannot show its available connection");
            other.Status=MyShipConnectorStatus.Connected;
            Require(CockpitSwitchState.ReadValues(group,properties)==1,"Partly locked group falsely reports ready");
            connector.Status=MyShipConnectorStatus.Unconnected;
            Require(CockpitSwitchState.ReadValues(group,properties)==1,"Mixed locked/unconnected group falsely reports ready");
            other.Status=MyShipConnectorStatus.Unconnected;
            Require(CockpitSwitchState.ReadValues(group,properties)==0,"Fully disconnected group remains up");
            foreach(var rig in CockpitRig.All)
            {
                foreach(var region in CockpitPanelGuard.Regions(rig))
                {
                    var center=Vector3.Transform(region.Bounds.Center,region.Frame);
                    Require(region.Contains(center),"Panel guard misses its own approach volume");
                    Require(!region.Contains(Vector3.Transform(region.Bounds.Max+Vector3.One*.01f,region.Frame)),"Panel guard leaks outside its bounds");
                    Require(!region.Contains(new Vector3(float.NaN,0,0)),"Invalid tracking enters a guard");
                }
                Vector3D Tip(int slot) { var pose=CockpitLayout.Control(rig.Subtype,slot,out _); return pose.Translation+pose.Backward*.033; }
                foreach(var bank in rig.Banks) for(int slot=bank.First;slot<=bank.Last;slot++)
                {
                    Require(CockpitPanelGuard.Contains(rig.Subtype,new CockpitProbe(MatrixD.CreateTranslation(Tip(slot)))),"Switch approach is unprotected: "+rig.Subtype+"/"+slot);
                    if(slot<bank.Last) Require(CockpitPanelGuard.Contains(rig.Subtype,new CockpitProbe(MatrixD.CreateTranslation((Tip(slot)+Tip(slot+1))*.5))),
                        "Space between neighboring controls is unprotected: "+rig.Subtype+"/"+slot);
                }
                Require(!CockpitPanelGuard.Contains(rig.Subtype,new CockpitProbe(MatrixD.Identity)),"Empty cockpit space suppresses firing: "+rig.Subtype);
            }
            Require(CockpitPanelGuard.Regions(CockpitRig.Find(CockpitLayout.Fighter)).Length==14,"Fighter switch banks, pull bar or screens lost their guards");
            var surface=new SurfaceView {Pose=MatrixD.CreateRotationX(.4)*MatrixD.CreateTranslation(2e6,-3e6,4e6),Width=.108f,Height=.120f};
            Require(CockpitPanelGuard.NearSurface(surface,new CockpitProbe(MatrixD.CreateTranslation(.065,0,.04)*surface.Pose)),"Moved seat-panel margin is lost at large world coordinates");
            Require(!CockpitPanelGuard.NearSurface(surface,new CockpitProbe(MatrixD.CreateTranslation(.20,0,.04)*surface.Pose)),"Seat-panel protection consumes distant firing");
            log("PASS connector native-state feedback and directed requests; panel approach/gap coverage, invalid tracking, large-world seat margin.");
        }
    }
}
