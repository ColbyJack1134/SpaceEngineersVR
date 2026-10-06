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
            foreach(var rig in CockpitRig.All) for(int slot=0;slot<rig.Count;slot++)
            {
                var controlSurface=CockpitButtons.Preview(rig.Subtype,slot);
                foreach(float value in new[] {0f,.5f,1f})
                {
                    controlSurface.Pose=CockpitLayout.Control(rig.Subtype,slot,out _)*(MatrixD)(rig.HandleAt(slot)?.Visual(value) ??
                        rig.BarAt(slot)?.Visual(value) ?? rig.LeverAt(slot)?.Visual(value) ?? rig.ButtonAt(slot)?.Visual(value==1) ?? Matrix.Identity);
                    Require(CockpitPanelGuard.NearSurface(controlSurface,new CockpitProbe(controlSurface.Pose)),"Live control contact is unprotected: "+rig.Subtype+"/"+slot);
                    Require(!CockpitPanelGuard.NearSurface(controlSurface,new CockpitProbe(MatrixD.CreateTranslation(controlSurface.Width/2+.04,0,0)*controlSurface.Pose)),
                        "Live control guard extends into distant space");
                }
            }
            var surface=new SurfaceView {Pose=MatrixD.CreateRotationX(.4)*MatrixD.CreateTranslation(2e6,-3e6,4e6),Width=.108f,Height=.120f,
                Keys=new[] {new SurfaceKey("",0,0,1,1)}};
            Require(CockpitPanelGuard.NearSurface(surface,new CockpitProbe(MatrixD.CreateTranslation(.070,0,0)*surface.Pose)),"Moved key margin is lost at large world coordinates");
            Require(!CockpitPanelGuard.NearSurface(surface,new CockpitProbe(MatrixD.CreateTranslation(.20,0,0)*surface.Pose)),"Key protection consumes distant firing");
            surface.Keys[0].Enabled=false;
            Require(!CockpitPanelGuard.NearSurface(surface,new CockpitProbe(surface.Pose)),"Disabled panel key blocks firing");
            surface.Keys[0].Enabled=true; surface.Enabled=false;
            Require(!CockpitPanelGuard.NearSurface(surface,new CockpitProbe(surface.Pose)),"Disabled panel blocks firing");
            var moving=new SurfaceView {Style=SurfaceStyle.ModelControl,Width=.02f,Height=.02f,
                Pose=MatrixD.CreateTranslation(0,.15,0),Keys=new[] {new SurfaceKey("",0,0,1,1)}};
            Require(CockpitPanelGuard.NearSurface(moving,new CockpitProbe(moving.Pose)) &&
                !CockpitPanelGuard.NearSurface(moving,new CockpitProbe(MatrixD.Identity)),"Moving knob protects its old track position");
            var round=new SurfaceView {Pose=MatrixD.Identity,Style=SurfaceStyle.ModelControl,Width=.1f,Height=.1f,
                Keys=new[] {new SurfaceKey("",0,0,1,1) {Round=true}}};
            Require(CockpitPanelGuard.NearSurface(round,new CockpitProbe(MatrixD.Identity)) &&
                !CockpitPanelGuard.NearSurface(round,new CockpitProbe(MatrixD.CreateTranslation(.052,.052,0))),"Round guard consumes rectangular corners");
            log("PASS connector native-state feedback and directed requests; live control contacts/endpoints, tight key margins and large-world transforms.");
        }
    }
}
