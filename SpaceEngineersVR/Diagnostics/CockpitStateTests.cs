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
        private sealed class Battery : RealProxy
        {
            internal ChargeMode Mode,Requested;
            internal int Requests;
            internal Battery() : base(typeof(IMyBatteryBlock)) { }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message; object result=null;
                switch(call.MethodName)
                {
                    case "get_ChargeMode": result=Mode; break;
                    case "set_ChargeMode": Requested=(ChargeMode)call.Args[0]; Requests++; break;
                    default: throw new Exception("Unexpected battery call: "+call.MethodName);
                }
                return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
            }
        }
        private static void Batteries(Action<string> log)
        {
            var battery=new Battery(); var block=(IMyBatteryBlock)battery.GetTransparentProxy();
            foreach(string id in new[] {"Recharge","Discharge"})
            foreach(string suffix in new[] {"","_On","_Off"})
            foreach(ChargeMode current in new[] {ChargeMode.Auto,ChargeMode.Recharge,ChargeMode.Discharge})
            foreach(bool on in new[] {false,true})
            {
                string action=id+suffix;
                ChargeMode target=id=="Recharge" ? ChargeMode.Recharge:ChargeMode.Discharge;
                battery.Mode=current; battery.Requests=0;
                Require(CockpitSwitchState.BatteryAction(block,action) &&
                    CockpitSwitchState.ReadValue(block,null,action)==(current==target ? 1:0),"Battery switch does not observe its selected mode");
                CockpitSwitchState.SetValue(block,null,on,action);
                bool change=on ? current!=target:current==target;
                Require(battery.Requests==(change ? 1:0) && (!change || battery.Requested==(on ? target:ChargeMode.Auto)) &&
                    battery.Mode==current && CockpitSwitchState.ReadValue(block,null,action)==(current==target ? 1:0),
                    "Battery switch repeats a request, clears a different mode or invents confirmation");
            }
            Require(!CockpitSwitchState.BatteryAction(block,"Auto") && !CockpitSwitchState.BatteryAction(block,"OnOff"),"Battery mapping consumes unrelated actions");
            var other=new Battery {Mode=ChargeMode.Discharge};
            battery.Mode=ChargeMode.Recharge;
            var group=new[] {block,(IMyBatteryBlock)other.GetTransparentProxy()};
            var values=new Sandbox.ModAPI.Interfaces.ITerminalProperty<bool>[2];
            Require(CockpitSwitchState.ReadValues(group,values,"Recharge")==.5f &&
                CockpitSwitchState.ReadValues(group,values,"Discharge")==.5f,"Mixed battery modes lose the middle state");
            CockpitSwitchState.SetValue(block,null,false,"Discharge");
            Require(battery.Mode==ChargeMode.Recharge,"Inactive discharge changes recharge");
            other.Mode=ChargeMode.Recharge;
            Require(CockpitSwitchState.ReadValues(group,values,"Recharge")==1 &&
                CockpitSwitchState.ReadValues(group,values,"Discharge")==0,"Matching battery groups show conflicting active modes");
            log("PASS battery switch modes: Auto/Recharge/Discharge, both directions, all action variants, inactive-mode safety, observed confirmation and mixed groups.");
        }
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }
        internal static void Run(Action<string> log)
        {
            Batteries(log);
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
