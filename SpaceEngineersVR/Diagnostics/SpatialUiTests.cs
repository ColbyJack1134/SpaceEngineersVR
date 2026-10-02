using System;
using System.Linq;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class SpatialUiTests
    {
        private static void Require(bool value,string label) { if(!value) throw new Exception(label); }
        public static void Run(Action<string> log)
        {
            foreach(var context in new[] {
                new[] {false,false,false,false},new[] {false,false,false,true},new[] {true,false,false,false},
                new[] {true,false,false,true},new[] {false,true,false,false},new[] {false,true,true,false} })
            {
                var wheel=GameActions.WheelActions(context[0],context[1],context[2],context[3]);
                Require(wheel.Distinct().Count()==9 && wheel.Length==9 && wheel[0]==GameActions.PauseAction && wheel[1]==GameActions.Options,"Context moved Pause/Options or changed wheel slot count");
                Require(!wheel.Contains(GameActions.Tablet) && !wheel.Any(a=>a.Label=="Reset ship view"),"Duplicate gesture/button action consumes a radial slot");
                Require(!wheel.Contains(GameActions.RelativeDampeners) || context[3],"Auto dampeners shown outside jetpack context");
                Require(wheel.Count(a=>a.Label.StartsWith("Camera:"))==(context[2] ? 1:0),"Camera cycle leaked context or split into multiple slots");
                var panelKeys=WristPanel.Keys(null,context[0],context[1],context[2],context[3],null,false);
                Require(panelKeys.Length<=20 && panelKeys.All(k=>k.Bounds.X>=0 && k.Bounds.Y>=0 && k.Bounds.Right<=1 && k.Bounds.Bottom<=1),"Tablet context overflows the panel");
                for(int i=0;i<panelKeys.Length;i++) for(int j=i+1;j<panelKeys.Length;j++)
                    Require(panelKeys[i].Bounds.Right<=panelKeys[j].Bounds.X || panelKeys[j].Bounds.Right<=panelKeys[i].Bounds.X || panelKeys[i].Bounds.Bottom<=panelKeys[j].Bounds.Y || panelKeys[j].Bounds.Bottom<=panelKeys[i].Bounds.Y,"Tablet panelKeys overlap");
            }
            var variants=Enumerable.Range(0,17).Select(i=>new ActionChoice("Variant "+i,()=> {})).ToArray();
            var armor=new Sandbox.Definitions.MyCubeBlockDefinition { Id=new VRage.Game.MyDefinitionId(typeof(VRage.Game.MyObjectBuilder_CubeBlock),"Armor"),CubeSize=VRage.Game.MyCubeSize.Large };
            var armorCorner=new Sandbox.Definitions.MyCubeBlockDefinition { Id=new VRage.Game.MyDefinitionId(typeof(VRage.Game.MyObjectBuilder_CubeBlock),"Corner"),CubeSize=VRage.Game.MyCubeSize.Large };
            var small=new Sandbox.Definitions.MyCubeBlockDefinition { Id=new VRage.Game.MyDefinitionId(typeof(VRage.Game.MyObjectBuilder_CubeBlock),"SmallArmor"),CubeSize=VRage.Game.MyCubeSize.Small };
            var family=new Sandbox.Definitions.MyBlockVariantGroup { Blocks=new[] {armor,armorCorner,small} };
            armor.BlockVariantsGroup=armorCorner.BlockVariantsGroup=small.BlockVariantsGroup=family;
            Require(BlockVariants.Family(armorCorner,new[] {armorCorner}).SequenceEqual(new[] {armor,armorCorner}),"Selecting a armorCorner collapsed the radial variant family");
            Require(BlockVariants.Family(small,new[] {armorCorner}).SequenceEqual(new[] {small}),"Variant family leaked the previous block size");
            var pages=BlockVariants.Pages(variants,GameActions.WheelActions(true,false,false));
            Require(pages.Length==3 && pages.All(p=>p.Length==9),"Building wheel loses fixed actions or overflow pages");
            Require(pages.Take(2).SelectMany(p=>p).Where(a=>a!=null).SequenceEqual(variants),"Building variant paging loses or duplicates entries");
            Require(pages[2].SequenceEqual(GameActions.WheelActions(true,false,false)),"Building actions page moved before variants");
            Require(BlockVariants.Pages(new ActionChoice[0],GameActions.WheelActions(true,false,false)).Single().SequenceEqual(GameActions.WheelActions(true,false,false)),"No-variant block does not open actions directly");
            WristPanel.Reset(); WristPanel.Show(2); WristPanel.SetQuery("damp");
            var search=WristPanel.Keys(null,false,false,false,true,null);
            Require(search.Any(k=>k.Action==GameActions.RelativeDampeners) && search.Any(k=>k.Action==GameActions.Dampeners),"Tablet search loses native actions");
            var keyboard=WristPanel.Keys(null,false,false,false,true,null);
            Require(keyboard.All(k=>k.Bounds.X>=0 && k.Bounds.Y>=0 && k.Bounds.Right<=1 && k.Bounds.Bottom<=1),"Search results leave tablet bounds");
            WristPanel.Reset();
            for(int i=0;i<80;i++)
            {
                var body=MatrixD.CreateFromYawPitchRoll(i*.1,i*.04,-i*.03);
                double pitch=(i%15-7)*.13,yaw=i*.17;
                var look=MatrixD.CreateRotationX(pitch)*MatrixD.CreateRotationY(yaw)*body;
                var angles=DampenerTargeting.HeadAngles(look.Forward,body);
                var native=MatrixD.CreateRotationX(MathHelper.ToRadians(angles.X))*MatrixD.CreateRotationY(MathHelper.ToRadians(angles.Y))*body;
                Require(Vector3D.Dot(native.Forward,look.Forward)>.99999,"Native damping target direction disagrees with HMD");
            }
            var oldHud=new EssentialHud.View { Values=new[] {"80","70","60","50"},Icons=new string[0],Dampeners=true };
            var autoHud=new EssentialHud.View { Values=new[] {"80","70","60","50"},Icons=new string[0],Dampeners=true,AutoDampeners=true };
            Require(!oldHud.SameAs(autoHud),"Auto dampeners state fails to repaint the HUD");
            autoHud.AutoDampeners=false; autoHud.NaturalGravity="1.00";
            Require(!oldHud.SameAs(autoHud),"Natural gravity changes fail to repaint wrist HUD");
            autoHud.NaturalGravity=null; autoHud.ArtificialGravity="0.50";
            Require(!oldHud.SameAs(autoHud),"Artificial gravity changes fail to repaint wrist HUD");
            log("PASS contextual action layouts: fixed Pause/Options, one third-person camera cycle, jetpack auto dampeners, tablet bounds and HUD auto-state refresh.");
            var tabletPress=new CockpitTouch.Hand();
            tabletPress.Sample(true,1,true,"Wrist",0,softCapture:false);
            Require(!tabletPress.Pressed,"Held trigger on tablet appearance fired");
            tabletPress.Sample(true,0,false,"Wrist",0,softCapture:false);
            tabletPress.Sample(true,.3f,false,"Wrist",0,softCapture:false);
            Require(!tabletPress.Pressed && tabletPress.Held<0,"Light pressure activated tablet");
            tabletPress.Sample(true,1,true,"Wrist",0,softCapture:false);
            Require(tabletPress.Pressed && tabletPress.Held==0 && tabletPress.Consumed,"Fresh tablet trigger missed");
            tabletPress.Sample(true,1,true,"Wrist",1,softCapture:false);
            Require(!tabletPress.Pressed && tabletPress.Held==0,"Held tablet press slid or repeated");
            tabletPress.Sample(false,1,true,null,-1,softCapture:false);
            tabletPress.Sample(true,1,true,"Wrist",1,softCapture:false);
            Require(!tabletPress.Pressed,"Tracking recovery activated tablet");
            tabletPress.Sample(true,0,false,"Wrist",1,softCapture:false);
            tabletPress.Sample(true,1,true,"Wrist",1,softCapture:false);
            Require(tabletPress.Pressed && tabletPress.Held==1,"Tablet failed after release/recovery");
            var heldTablet=new CockpitTouch.SurfaceHold();
            heldTablet.Input.Sample(true,0,false,"Wrist",2,softCapture:false);
            heldTablet.Input.Sample(true,1,true,"Wrist",2,softCapture:false);
            var capturedWrist=Matrix.CreateTranslation(.03f,.02f,.1f);
            var anchor=new Vector3(.01f,-.02f,.001f);
            heldTablet.Capture(capturedWrist,anchor);
            heldTablet.Grabbed=DateTime.UtcNow.AddSeconds(-1);
            var movingPanel=MatrixD.CreateFromYawPitchRoll(.7,-.4,.2);
            movingPanel.Translation=new Vector3D(1e8,2e8,-3e8);
            heldTablet.Input.Sample(true,1,true,null,-1,reachable:true,softCapture:false);
            Require(heldTablet.Input.Committed && heldTablet.Attachment(movingPanel,out var attachedWrist,out var attachedPoint,out float blend),"Tablet lost held contact when the finger left the original key bounds");
            heldTablet.Attachment(movingPanel,out attachedWrist,out attachedPoint,out blend);
            Require(blend==1 && Vector3D.Distance(attachedPoint,Vector3D.Transform(anchor,movingPanel))<1e-6 &&
                Vector3D.Distance(attachedWrist.Translation,Vector3D.Transform(capturedWrist.Translation,movingPanel))<1e-6,"Held tablet contact failed to follow a moving parent");
            heldTablet.Input.Sample(true,0,false,null,-1,softCapture:false);
            Require(!heldTablet.Attachment(movingPanel,out _,out _,out _),"Tablet retained finger attachment after trigger release");
            var heldTab=new SurfaceKey("Toolbar",.265f,.025f,.23f,.1f);
            var changedKeys=new[] { new SurfaceKey("Controls",.02f,.025f,.23f,.1f),new SurfaceKey("Toolbar",.265f,.025f,.23f,.1f) };
            var kept=SpatialUi.HoldKey(changedKeys,heldTab,out int heldIndex);
            Require(heldIndex==1 && ReferenceEquals(kept[heldIndex],heldTab),"Tab change replaced captured button");
            var heldNext=new SurfaceKey("Next",.70f,.86f,.28f,.115f);
            kept=SpatialUi.HoldKey(kept,heldNext,out heldIndex);
            Require(ReferenceEquals(kept[heldIndex],heldNext),"Result-count change lost captured paging button");
            var compactPose=SpatialUi.WristViews(movingPanel,0,1,oldHud,changedKeys)[0].Pose;
            foreach(float opening in new[] {0f,.2f,.5f,.8f,1f,.5f,0f})
            {
                var panels=SpatialUi.WristViews(movingPanel,opening,1,oldHud,changedKeys);
                var compact=panels[0]; var menu=panels.Length>1 ? panels[1] : null;
                Require(compact.Pose==compactPose && compact.Keys.Length==1 && ReferenceEquals(compact.Status,oldHud),"Opening menu moved or removed the wrist HUD");
                Require(menu==null || menu.Id!=compact.Id && (opening>=.99f || menu.Keys.Length==0),"Moving menu accepted a new button press");
                var menuPoint=menu?.Pose ?? compact.Pose;
                Require(SpatialUi.WristTarget(compact,menu,menuPoint,menuPoint.Translation+menuPoint.Backward,"Wrist")==compact,"Opening/closing menu stole captured HUD press");
                if(menu!=null)
                {
                    Require(SpatialUi.WristTarget(compact,menu,compact.Pose,compact.Pose.Translation+compact.Pose.Backward,"WristMenu")==menu,"HUD stole captured menu press");
                    if(opening==1)
                    {
                        foreach(var candidate in panels)
                        {
                            var key=candidate.Keys[0].Bounds;
                            var tip=MatrixD.CreateTranslation((key.Center.X-.5)*candidate.Width,(.5-key.Center.Y)*candidate.Height,PhysicalSurface.KeyHeight(candidate))*candidate.Pose;
                            Require(SpatialUi.WristTarget(compact,menu,tip,candidate.Pose.Translation+candidate.Pose.Backward,null)==candidate,"Fresh touch cannot independently select HUD and menu");
                        }
                    }
                    var finalMenu=SpatialUi.WristPose(movingPanel,1,.225f,-1);
                    Require(Vector3D.Dot(menu.Pose.Up,finalMenu.Up)>.9999 && Vector3D.Dot(menu.Pose.Backward,finalMenu.Backward)>.9999,"Opening menu flips its text or face");
                    Require(Vector3D.Distance(menu.Pose.Translation-menu.Pose.Up*menu.Height/2,finalMenu.Translation-finalMenu.Up*.225/2)<1e-6,"Menu expansion slides its lower edge");
                }
            }
            var remotePanel=new SurfaceView { Id="WristMenu",Pose=movingPanel,Width=.4f,Height=.225f,Style=SurfaceStyle.WristMenu,
                Keys=new[] {new SurfaceKey("Action",.1f,.1f,.8f,.8f)} };
            var remotePointer=MatrixD.CreateTranslation(0,0,.4)*movingPanel;
            Require(SpatialUi.WristRayTarget(new[] {remotePanel},remotePointer,3,out int rayKey,out float rayDistance)==remotePanel && rayKey==0 && rayDistance>.3f,"Ray cannot select a tablet button outside capsule reach");
            var rayPress=new CockpitTouch.Hand();
            for(int i=0;i<60;i++) rayPress.Sample(true,0,false,remotePanel.Id,rayKey,guarded:true,softCapture:false);
            Require(!rayPress.Captured && !rayPress.Consumed && !rayPress.Pressed,"Idle tablet hover captures trigger input");
            rayPress.Sample(true,.3f,false,remotePanel.Id,rayKey,guarded:true,softCapture:false);
            rayPress.Sample(true,1,true,remotePanel.Id,rayKey,guarded:true,softCapture:false);
            Require(rayPress.Pressed && rayPress.Held==0,"Gradual ray squeeze failed to activate tablet");
            rayPress.Sample(true,1,true,remotePanel.Id,rayKey,guarded:true,softCapture:false);
            Require(!rayPress.Pressed && rayPress.Committed,"Ray hold repeats activation or loses ownership");
            Require(SpatialUi.WristRayTarget(new[] {remotePanel},remotePointer,.1f,out _,out _)==null,"Tablet ray passes through a nearer obstacle");
            var blank= new SurfaceView { Id="Occluder",Pose=MatrixD.CreateTranslation(0,0,.1)*movingPanel,Width=.4f,Height=.225f,Style=SurfaceStyle.WristMenu,Keys=new SurfaceKey[0] };
            Require(SpatialUi.WristRayTarget(new[] {remotePanel,blank},remotePointer,3,out rayKey,out _)==blank && rayKey<0,"Ray clicks through an unclickable panel face");
            foreach(var style in new[] {SurfaceStyle.WristStatus,SurfaceStyle.WristMenu})
            {
                var panel=new SurfaceView { Pose=movingPanel,Width=.4f,Height=.225f,Style=style };
                var ray=MatrixD.CreateTranslation(0,0,.3)*movingPanel;
                Require(SpatialUi.WristRay(panel,ray,out float length) && Math.Abs(length-(.3-PhysicalSurface.KeyHeight(panel)))<.00001,"Tablet ray misses its physical face");
                ray=MatrixD.CreateTranslation(.3,0,.3)*movingPanel;
                Require(!SpatialUi.WristRay(panel,ray,out _),"Tablet clips a ray outside its bounds");
                ray=MatrixD.CreateRotationY(Math.PI)*MatrixD.CreateTranslation(0,0,-.3)*movingPanel;
                Require(!SpatialUi.WristRay(panel,ray,out _),"Tablet accepts a back-facing ray");
            }
            var touch=new SurfaceTouch();
            Require(touch.Update("keyboard",Vector3.Zero,0)<0,"A surface appearing through a hand fired a key");
            touch.Update("keyboard",new Vector3(0,0,.06f),0);
            Require(touch.Update("keyboard",new Vector3(0,0,.008f),0)==0,"Front poke missed");
            for(int i=0;i<300;i++) Require(touch.Update("keyboard",Vector3.Zero,0)<0,"Held poke repeated");
            Require(touch.Update("keyboard",Vector3.Zero,1)<0,"Sliding across keys fired");
            touch.Update("keyboard",new Vector3(0,0,.04f),1);
            Require(touch.Update("keyboard",Vector3.Zero,1)==1,"Retracted poke failed");
            Require(touch.Update("seat",Vector3.Zero,1)<0,"Surface ownership leaked");
            touch.Update("seat",new Vector3(0,0,.04f),1);
            Require(touch.Update("seat",new Vector3(0,0,-.05f),1)<0,"Deep/backside entry accepted");
            Require(touch.Update("seat",Vector3.Zero,1)<0,"Backside retraction fired a key");
            touch.Update("seat",new Vector3(float.NaN),1);
            Require(touch.Update("seat",Vector3.Zero,1)<0,"Tracking recovery auto-tapped");

            foreach(float frame in new[] { 1f/30,1f/60,1f/120 })
            {
                Vector3 offset=Vector3.Zero;
                for(int i=0;i<(int)Math.Round(1/frame);i++) offset=SeatFit.Step(offset,Vector3.Down,frame);
                Require(Math.Abs(offset.Y+.12)<.00001,"Seat speed depends on frame rate");
            }
            Require(SeatFit.Limit(new Vector3(float.NaN))==Vector3.Zero,"Invalid saved seat fit accepted");
            Require(SeatFit.Step(new Vector3(0,-.45f,0),Vector3.Down,2).Y==-.45f,"Seat lower bound exceeded");
            Require(HelmetHud.NearTemple(Matrix.CreateTranslation(.21f,0,0),Matrix.Identity),"Temple gesture missed");
            Require(!HelmetHud.NearTemple(Matrix.CreateTranslation(.21f,-.4f,-.2f),Matrix.Identity),"Chest pose toggles helmet");
            Require(!HandInteraction.ShowRay(0,false) && !HandInteraction.ShowRay(.04f,false),"Resting trigger shows the ray");
            Require(HandInteraction.ShowRay(.06f,false) && HandInteraction.ShowRay(.04f,true),"Light squeeze or hysteresis failed");
            Require(!HandInteraction.ShowRay(.02f,true) && !HandInteraction.ShowRay(float.NaN,true),"Ray remains after release/tracking loss");
            foreach(int side in new[] { -1,1 }) for(int i=0;i<=20;i++)
            {
                float fold=i/20f,height=MathHelper.Lerp(.07f,.40f*9/16,fold);
                MatrixD mount=MatrixD.CreateFromYawPitchRoll(.4,-.6,.2); mount.Translation=new Vector3D(1e8,2e8,-3e8);
                MatrixD opened=SpatialUi.WristPose(mount,fold,height,side);
                var hingeFrame=MatrixD.CreateRotationZ(-Math.PI*fold)*opened;
                Vector3D bottom=opened.Translation+hingeFrame.Up*height*.5;
                Require(Vector3D.Distance(bottom,mount.Translation+mount.Right*side*.035-mount.Backward*(.0035*(1-fold)))<1e-6,"Wrist long-edge hinge detached");
                if(i==0) Require(Vector3D.Dot(opened.Right,-mount.Up*side)>.9999,"Folded wrist baseline does not follow forearm");
                if(i==20)
                {
                    Require(Vector3D.Dot(opened.Up,mount.Backward)>.9999,"Expanded wrist text is upside down in the watch-reading pose");
                    Require(Vector3D.Dot(opened.Backward,-mount.Right*side)>.9999,"Wrist opens away from chosen viewer side");
                }
            }
            foreach(string subtype in new[] { FighterProfile.Subtype,"OpenCockpitLarge" })
            {
                Require(SeatPanel.TryMount(subtype,out var mount,out float w,out float h) && mount.IsValid(),"Missing measured console mount");
                var panel=new SurfaceView { Width=w,Height=h,Keys=SeatPanel.Keys(true,true) };
                Require(panel.KeyAt(new Vector2(.35f,.14f))==9 && panel.KeyAt(new Vector2(.65f,.14f))==11,"Power/park top row reversed");
                Require(panel.KeyAt(new Vector2(.20f,.325f))==10 && panel.KeyAt(new Vector2(.5f,.325f))==12 &&
                    panel.KeyAt(new Vector2(.80f,.325f))==13,"Ship action second row changed");
                Require(panel.KeyAt(new Vector2(.20f,.51f))==2 && panel.KeyAt(new Vector2(.80f,.51f))==0,"Seat down/up row reversed");
                Require(panel.KeyAt(new Vector2(.5f,.51f))==1 && panel.KeyAt(new Vector2(.5f,.695f))==6,"Seat fore/aft row reversed");
                Require(panel.KeyAt(new Vector2(.20f,.695f))==3 && panel.KeyAt(new Vector2(.80f,.695f))==5,"Seat lateral row reversed");
                Require(panel.KeyAt(new Vector2(.20f,.88f))==4 && panel.KeyAt(new Vector2(.5f,.88f))==7 &&
                    panel.KeyAt(new Vector2(.80f,.88f))==8,"Seat/stick reset or central lock moved");
                Require(Vector3D.Dot(mount.Backward,Vector3D.Up)>.85,"Console controls face into the mesh");
            }
            Require(!SeatPanel.TryMount("unknown",out _,out _,out _),"Unmeasured cockpit gets a guessed floating panel");
            foreach(float tangent in new[] {.4f,.65f,1.1f})
            foreach(float cant in new[] {0f,.15f})
            {
                var projections=new MatrixD[2];
                for(int eye=0;eye<2;eye++)
                {
                    double side=eye==0 ? -1 : 1;
                    var eyeToHead=MatrixD.CreateRotationY(side*cant);
                    eyeToHead.Translation=new Vector3D(side*.036,0,0);
                    projections[eye]=MatrixD.Invert(eyeToHead)*VrMath.Projection(-tangent*.9f,tangent*1.1f,-tangent,tangent,.05);
                }
                float scale=EssentialHud.FitScale(projections[0],projections[1]);
                Require(scale>0 && scale<=1,"HUD fit enlarged or hid the panel");
                if(tangent==1.1f) Require(scale==1,"Wide FOV needlessly shrinks HUD text");
                foreach(var projection in projections)
                foreach(double x in new[] {-.8,.8})
                foreach(double y in new[] {-.75,-.05})
                {
                    var clip=Vector4D.Transform(new Vector4D(x*scale,y*scale,-1.5,1),projection);
                    Require(clip.W>0 && Math.Abs(clip.X/clip.W)<=.90001 && Math.Abs(clip.Y/clip.W)<=.90001,"HUD clips one eye's inset FOV");
                }
            }

            var window=new KeyboardWindow();
            var headPose=Matrix.CreateFromYawPitchRoll(.7f,-.2f,.1f); headPose.Translation=new Vector3(2,1.5f,-3);
            window.Place(headPose);
            Require(window.Pose.IsValid() && Vector3.Dot(window.Pose.Backward,headPose.Translation-window.Pose.Translation)>0,"Keyboard starts facing away");
            var start=window.Pose; float initialWidth=window.Width;
            var controller=Matrix.CreateRotationY(.3f)*Matrix.CreateTranslation(1,1,-3);
            window.Begin(1,controller,Vector3.Zero); window.Move(controller,Vector3.Zero);
            Require(Vector3.Distance(window.Pose.Translation,start.Translation)<1e-5,"Grabbing keyboard snaps to controller");
            controller.Translation+=new Vector3(.2f,.1f,-.3f); window.Move(controller,Vector3.Zero);
            Require(Vector3.Distance(window.Pose.Translation,start.Translation+new Vector3(.2f,.1f,-.3f))<1e-5,"Captured keyboard does not follow hand");
            window.Stop(); var released=window.Pose; controller.Translation+=Vector3.One; window.Move(controller,Vector3.Zero);
            Require(window.Pose==released,"Keyboard moves after release");
            var corner=window.Pose.Translation-window.Pose.Right*window.Width/2+window.Pose.Up*window.Height/2;
            window.Begin(2,controller,Vector3.Zero);
            window.Move(controller,new Vector3(.2f,-.2f*KeyboardWindow.Aspect,0));
            Require(Math.Abs(window.Width-initialWidth-.2f)<1e-5,"Keyboard resize does not follow dragged corner");
            Require(Vector3.Distance(corner,window.Pose.Translation-window.Pose.Right*window.Width/2+window.Pose.Up*window.Height/2)<1e-5,"Resize moves opposite corner");
            window.Move(controller,new Vector3(100,-100,0)); Require(window.Width==1,"Keyboard exceeds upper size bound");
            window.Move(controller,new Vector3(-100,100,0)); Require(window.Width==.42f,"Keyboard exceeds lower size bound");
            window.Stop(); window.Place(headPose);
            var front=Matrix.CreateTranslation(0,0,.4f)*window.Pose;
            Require(window.Pointer(front,out var point) && point.Length()<1e-5,"Keyboard pointer plane mismatch");
            Require(!window.Pointer(Matrix.CreateTranslation(0,0,-.1f)*window.Pose,out _),"Backside ray manipulates keyboard");
            Require(KeyboardWindow.Handle(new Vector2(.5f,.955f))==1 && KeyboardWindow.Handle(new Vector2(.97f,.97f))==2,"Window handles unreachable");

            for(int i=0;i<100;i++)
            {
                Matrix neutral=Matrix.CreateFromYawPitchRoll(i*.02f,.2f,-.1f);
                Matrix captured=CockpitStickMath.GripPalm(false);
                Matrix turn=CockpitStickMath.RightVisual(new Vector3(.6f,-.4f,.3f));
                Matrix wrist=captured*turn;
                Vector3 palm=Vector3.Transform(new Vector3(-.105f,-.035f,0),wrist);
                Require(Vector3.Distance(palm,Vector3.Transform(FighterProfile.RightContact+captured.Backward*.035f+captured.Up*.015f,turn))<1e-5,"Raised grasp separated from stick");
                Matrix head=Matrix.CreateRotationY(i*.07f); head.Translation=new Vector3(1,1.4f,-2);
                Matrix hand=Matrix.CreateTranslation(.3f,1.1f,-2.4f);
                var wheel=ToolbarWheel.HandPose(hand,head);
                Require(wheel.IsValid() && Math.Abs(wheel.Determinant()-1)<1e-5,"Hand wheel basis invalid");
                Require(Vector3.Dot(wheel.Backward,Vector3.Normalize(head.Translation-wheel.Translation))>.999f,"Wheel faces away");
            }
            foreach(float y in new[] { -.25f,-.45f,-.65f }) foreach(float z in new[] { -.4f,-.6f,-.8f })
            {
                var hand=Matrix.CreateTranslation(.3f,y,z);
                var wheel=ToolbarWheel.HandPose(hand,Matrix.Identity);
                Vector3 local=Vector3.Transform(hand.Translation,Matrix.Invert(wheel));
                Require(local.Y<-.30f,"Right hand overlaps bottom of the radial menu");
            }
            var keys=MenuKeyboard.MakeKeys(false);
            Require(keys.Length==46 && keys.Any(k=>k.Label=="DONE") && keys.Any(k=>k.Label=="BKSP"),"Keyboard layout incomplete");
            foreach(var key in keys) Require(KeyboardWindow.Handle(key.Bounds.Center)==0,"A key overlaps a window handle");
            var surface=new SurfaceView { Keys=keys,Width=.5f,Height=.3f,Pose=MatrixD.CreateTranslation(1e9,2e9,-3e9) };
            foreach(var key in keys)
            {
                var b=key.Bounds;
                Vector3 local=new Vector3((b.X+b.Width/2-.5f)*surface.Width,(.5f-b.Y-b.Height/2)*surface.Height,.006f);
                Vector3D world=Vector3D.Transform(local,surface.Pose);
                Require(surface.KeyAt(PhysicalSurface.UV(surface,PhysicalSurface.Point(surface,world)))==Array.IndexOf(keys,key),"Rendered key/touch mismatch at large coordinates");
            }
            log("PASS spatial input: front-only/retracted pokes, bounded seat fit, temple exclusion, light-trigger ray hysteresis, fixed forearm hinge, captured keyboard move/release/bounded resize, console arrow mappings, hand/wheel clearance, attached grasps, all 46 key touch volumes at large coordinates");
        }
    }
}
