using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace MovementPrototype {
    public static class CatchTests {
        static void Check(bool value,string message) {if(!value)throw new Exception("Auto-catch: "+message);}
        static CatchConfig Config() {return new CatchConfig {Catcher="Smeargle",PartySlot=2,VerifiedRecipe=true,Ball="Poke Ball",MaximumBalls=2,ProtectedTargets="mimikyu"};}
        sealed class Scenario {
            public CatchFlow Flow=new CatchFlow();public double Now;long id;
            public Scenario(CatchConfig config=null) {Flow.Start(config ?? Config(),"mimikyu",2,0);}
            public CatchFrame Frame(CatchView view=CatchView.Commands) {
                Now+=.2;var f=new CatchFrame {Id=++id,CapturedAt=Now,View=view,Wild=true,Opponent="mimikyu",ActivePokemon="Smeargle",Ready=view==CatchView.Commands};
                f.Controls[CatchActionKind.OpenFight]=C("Fight");f.Controls[CatchActionKind.OpenParty]=C("Pokemon");f.Controls[CatchActionKind.OpenBag]=C("Items");
                f.Controls[CatchActionKind.ChooseCatcher]=C("Smeargle",2);
                f.Controls[CatchActionKind.ChooseMove]=C(Flow.WantedMove ?? "False Swipe",Flow.WantedMoveSlot,20);
                f.Controls[CatchActionKind.UseBall]=C("Poke Ball",0,10);return f;
            }
            public CatchAction Observe(CatchFrame f) {return Flow.Observe(f,Now,true);}
            public CatchAction Step(CatchView view,CatchActionKind? kind=null) {
                var f=Frame(view);var action=Observe(f);
                Check(kind.HasValue ? action!=null && action.Kind==kind : action==null,"expected "+kind+" from "+view+"; phase="+Flow.Phase+"; "+Flow.Status);return action;
            }
            public void Prepare() {Step(CatchView.Commands,CatchActionKind.OpenFight);Step(CatchView.Moves,CatchActionKind.ChooseMove);Step(CatchView.Busy);Step(CatchView.Commands,CatchActionKind.OpenBag);}
            public void Throw() {Step(CatchView.BallList,CatchActionKind.UseBall);Step(CatchView.Busy);}
        }
        static CatchControl C(string name,int slot=0,int remaining=-1) {return new CatchControl {Name=name,Slot=slot,Remaining=remaining,Point=new Point(800,400)};}
        static CatchTextRegion Text(params string[] lines) {
            var region=new CatchTextRegion {Text=String.Join("\n",lines)};
            int y=200;foreach(var line in lines) {
                var row=new CatchTextRow {Text=line,Bounds=new RectangleF(760,y,150,16)};int x=760;
                foreach(string word in line.Split(' ')) {row.Words.Add(new ButtonWord(word,x,y,Math.Max(8,word.Length*8),16));x+=word.Length*8+8;}
                region.Rows.Add(row);y+=45;
            }return region;
        }
        static CatchFrame Parse(CatchTextRegion menu,CatchTextRegion log=null,CatchTextRegion bag=null) {
            using(var image=new Bitmap(1000,800)) {
                return CatchVision.Parse(Text("Advay VS. Wild Mimikyu"),menu,Text("Smeargle"),log ?? Text("Smeargle used False Swipe!"),Text("Ralts"),new ChromeReading {Kind=ChromeKind.Battle},image,Config(),"mimikyu","False Swipe",3,1,0,bag);
            }
        }
        public static List<string> Run() {
            var result=new List<string>();bool rejected=false;
            Check(CatchVision.MovePp("61/64")==61 && CatchVision.MovePp("61164")==61 && CatchVision.MovePp("2424")==24 && CatchVision.MovePp("30'30")==30,"pixel-font PP separators");
            Check(CatchVision.MovePp("111")==-1 && CatchVision.MovePp("3000")==-1 && CatchVision.MovePp("20/10")==-1 && CatchVision.MovePp("0/64")==0,"ambiguous or impossible PP cannot authorize input");
            result.Add("PASS: compact pixel-font PP accepts only a unique current/total pair; ambiguous, exhausted and impossible counters remain blocked");
            using(var image=new Bitmap(1000,800)) {
                var own=Text("Breoom");var cfg=Config();cfg.Catcher="Breloom";
                var frame=CatchVision.Parse(Text("Advay VS. Wild Mimikyu"),Text("unused"),own,Text(""),Text(""),new ChromeReading {Kind=ChromeKind.Battle},image,cfg,"mimikyu","False Swipe",1,1,0);
                Check(frame.ActivePokemon=="Breloom" && !CatchVision.OwnCaption(own,"Breloom",image.Size).IsEmpty,"unique missing-letter catcher read retains narrow caption bounds");
                frame=CatchVision.Parse(Text("Advay VS. Wild Mimikyu"),Text("unused"),Text("Breloom"),Text(""),Text(""),new ChromeReading {Kind=ChromeKind.Battle},image,Config(),"mimikyu","False Swipe",1,1,0);
                Check(frame.ActivePokemon==null,"another valid species cannot become the configured catcher");
                frame.HeaderSeal=new Rectangle(5,5,30,15);frame.OwnSeal=new Rectangle(50,50,30,15);frame.Controls[CatchActionKind.OpenFight]=C("Fight");
                string seal=CatchVision.InputSeal(image,frame,CatchActionKind.OpenFight);image.SetPixel(400,600,Color.Red);
                Check(seal==CatchVision.InputSeal(image,frame,CatchActionKind.OpenFight),"unrelated animated scenery must not invalidate input");
                using(var g=Graphics.FromImage(image))g.FillRectangle(Brushes.White,frame.OwnSeal);
                Check(seal!=CatchVision.InputSeal(image,frame,CatchActionKind.OpenFight),"changed catcher pixels revoke input");
                frame.Control(CatchActionKind.OpenFight).Bounds=new Rectangle(750,390,100,20);
                seal=CatchVision.InputSeal(image,frame,CatchActionKind.OpenFight);image.SetPixel(753,400,Color.White);
                Check(seal!=CatchVision.InputSeal(image,frame,CatchActionKind.OpenFight),"full control label, including its outer edge, is sealed");
                seal=CatchVision.InputSeal(image,frame,CatchActionKind.OpenFight);image.SetPixel(10,10,Color.White);
                Check(seal!=CatchVision.InputSeal(image,frame,CatchActionKind.OpenFight),"changed target caption pixels revoke input");
            }
            result.Add("PASS: catcher OCR tolerates one unique missing letter while pixel seals cover caption/control evidence and exclude unrelated scenery");
            try {new CatchConfig().Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"unfinished recipe must reject start");
            var config=Config();config.VerifiedRecipe=false;config.Validate(false);rejected=false;try {config.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"draft settings can be saved but cannot run");
            config=Config();config.Moves[0].Name="Tackle";rejected=false;try {config.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"ordinary damaging moves must be rejected");
            config=Config();config.MaximumBalls=101;rejected=false;try {config.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"ball budget bounds");
            var flow=new CatchFlow();rejected=false;try {flow.Start(Config(),"mimikyu",1,0);}catch(InvalidOperationException) {rejected=true;}Check(rejected,"two exact target confirmations required");
            result.Add("PASS: catching configuration validates draft/start separately, rejects damaging recipes and unbounded stock use, and requires two exact target readings");
            var s=new Scenario();s.Prepare();s.Throw();var success=s.Frame(CatchView.Success);success.Success=true;s.Observe(success);
            Check(s.Flow.Phase==CatchPhase.ConfirmWorld,"capture text required before resuming");var world=s.Frame(CatchView.World);world.Wild=false;s.Observe(world);Check(s.Flow.Active,"one unstable world cannot resume");world=s.Frame(CatchView.World);world.Wild=false;world.WorldStable=true;s.Observe(world);
            Check(s.Flow.Phase==CatchPhase.Complete && s.Flow.Resume && s.Flow.BallsUsed==1,"complete bounded swipe/ball/capture/world lifecycle");
            result.Add("PASS: configured False Swipe, one ball, exact capture acknowledgement and stable world complete the sequence; world alone cannot resume");
            s=new Scenario();s.Prepare();s.Throw();var previewFrame=s.Frame(CatchView.Success);
            previewFrame=CatchVision.Preview(Text("Mimikyu Lv2 PREVIEW X"),"mimikyu",previewFrame.Id,s.Now);
            var closePreview=s.Observe(previewFrame);Check(previewFrame.Success && closePreview!=null && closePreview.Kind==CatchActionKind.CloseSummary,"new exact capture preview closes after an owned ball throw");
            world=s.Frame(CatchView.World);world.Wild=false;world.WorldStable=true;s.Observe(world);Check(s.Flow.Phase==CatchPhase.Complete,"closed capture preview plus clear world resumes");
            var wrongPreview=CatchVision.Preview(Text("Ralts Lv2 PREVIEW X"),"mimikyu",1,0);Check(!wrongPreview.Success && wrongPreview.View==CatchView.Unknown,"wrong-species preview is not a clear world or capture confirmation");
            Check(!CatchVision.Preview(Text("Mimikyu Lv2"),"mimikyu",1,0).Success,"ordinary species text cannot confirm capture");
            s=new Scenario();previewFrame=CatchVision.Preview(Text("Mimikyu Lv2 PREVIEW X"),"mimikyu",1,0);Check(s.Observe(previewFrame)==null && s.Flow.Phase!=CatchPhase.ConfirmWorld,"a preview without an owned ball throw cannot confirm capture");
            s=new Scenario();s.Prepare();s.Throw();world=s.Frame(CatchView.World);world.Wild=false;s.Observe(world);Check(s.Flow.Active,"brief world-to-preview transition stays paused");
            world=s.Frame(CatchView.World);s.Now+=2.1;world.CapturedAt=s.Now;world.Wild=false;s.Observe(world);Check(!s.Flow.Active,"missing capture acknowledgement still stops after the preview grace period");
            var leadRows=Text("Ralts","Smeargle");leadRows.Rows[0].Bounds=new RectangleF(10,30,80,14);leadRows.Rows[1].Bounds=new RectangleF(10,80,80,14);var leadConfig=Config();leadConfig.ExpectedLead="Smeargle";
            Check(CatchVision.LeadName(leadRows,leadConfig,new Size(1000,1000))==null,"second HUD member cannot pass world fast-path lead verification");
            result.Add("PASS: capture preview requires the exact species and an owned throw; its close and stable world complete the flow; wrong/absent results and a changed lead remain blocked");
            s=new Scenario();s.Prepare();s.Throw();s.Step(CatchView.Commands,CatchActionKind.OpenBag);s.Throw();s.Step(CatchView.Commands);Check(s.Flow.Phase==CatchPhase.Stopped && s.Flow.BallsUsed==2,"failed balls cannot exceed budget");
            s=new Scenario();s.Prepare();var empty=s.Frame(CatchView.BallList);empty.Control(CatchActionKind.UseBall).Remaining=0;Check(s.Observe(empty)==null && !s.Flow.Active,"empty stock stops before a throw");
            s=new Scenario();s.Step(CatchView.Commands,CatchActionKind.OpenFight);var noPp=s.Frame(CatchView.Moves);noPp.Control(CatchActionKind.ChooseMove).Remaining=0;Check(s.Observe(noPp)==null && !s.Flow.Active,"zero PP stops before move input");
            result.Add("PASS: escape from balls retries only within the configured cap; zero PP and empty stock stop before input");
            s=new Scenario();var f=s.Frame();var action=s.Observe(f);Check(s.Flow.Allows(action,f,s.Now,true),"fresh selected action");Check(!s.Flow.Allows(action,f,s.Now,false) && !s.Flow.Allows(action,f,s.Now+CatchFlow.MaximumFrameAge+1,true),"lease and age checks");
            s.Flow.Suspend();Check(!s.Flow.Allows(action,f,s.Now,true),"handoff revokes old action");s.Flow.Cancel("F10");Check(s.Observe(s.Frame())==null && !s.Flow.Allows(action,f,s.Now,true),"stop cancels late work");
            s=new Scenario();f=s.Frame();f.CapturedAt=-10;Check(s.Observe(f)==null,"stale frame cannot change phase");f=s.Frame();f.Opponent="ralts";Check(s.Observe(f)==null,"different species never receives a catching action");
            s=new Scenario();f=s.Frame(CatchView.World);f.Wild=false;Check(s.Observe(f)==null && !s.Flow.Active,"battle disappearance cannot imply capture");
            result.Add("PASS: wrong species, stale frames, revoked foreground lease, handoff epochs, emergency cancellation and unconfirmed battle exit authorize no catching input");
            config=Config();config.Moves=new List<CatchMove> {new CatchMove {Name="Soak",Slot=2},new CatchMove {Name="False Swipe",Slot=3,Uses=2}};s=new Scenario(config);
            foreach(string move in new string[] {"Soak","False Swipe","False Swipe"}) {
                s.Step(CatchView.Commands,CatchActionKind.OpenFight);Check(s.Flow.WantedMove==move,"ordered configured recipe");s.Step(CatchView.Moves,CatchActionKind.ChooseMove);s.Step(CatchView.Busy);
            }
            s.Step(CatchView.Commands,CatchActionKind.OpenBag);Check(s.Flow.Phase==CatchPhase.SelectBall,"all configured preparation turns acknowledged");
            s=new Scenario();f=s.Frame();f.ActivePokemon="Ralts";Check(s.Observe(f).Kind==CatchActionKind.OpenParty,"switch to configured catcher first");f=s.Frame(CatchView.Party);f.ActivePokemon="Ralts";f.Control(CatchActionKind.ChooseCatcher).Slot=1;Check(s.Observe(f)==null,"same name in wrong party slot is rejected");f=s.Frame(CatchView.Party);f.ActivePokemon="Ralts";Check(s.Observe(f).Kind==CatchActionKind.ChooseCatcher,"matching party name and slot");
            result.Add("PASS: ordered preparation and repeated False Swipe turns are acknowledged individually; catcher selection requires matching name and party slot");
            s=new Scenario();s.Step(CatchView.Commands,CatchActionKind.OpenFight);f=s.Frame();s.Now+=5;f.CapturedAt=s.Now;s.Observe(f);Check(!s.Flow.Active,"ignored menu navigation is time bounded");
            s=new Scenario();s.Step(CatchView.Commands,CatchActionKind.OpenFight);s.Step(CatchView.Moves,CatchActionKind.ChooseMove);f=s.Frame();s.Now+=19;f.CapturedAt=s.Now;s.Observe(f);Check(!s.Flow.Active,"unacknowledged move cannot be repeated");
            config=Config();config.Resume=false;config.ExpectedLead="Ralts";s=new Scenario(config);s.Prepare();s.Throw();f=s.Frame(CatchView.Success);f.Success=true;s.Observe(f);f=s.Frame(CatchView.World);f.WorldStable=true;f.Wild=false;f.LeadPokemon="Smeargle";s.Observe(f);Check(!s.Flow.Active && s.Flow.Phase==CatchPhase.Stopped,"changed lead prevents unattended resume");
            result.Add("PASS: ignored menus and unacknowledged attacks reach bounded stops; optional expected leading Pokémon is verified before resume");
            s=new Scenario();s.Prepare();s.Throw();f=s.Frame(CatchView.Busy);s.Now+=20;f.CapturedAt=s.Now;s.Observe(f);Check(s.Flow.Active,"ball animation may exceed the shorter move timeout");
            f=s.Frame(CatchView.Busy);s.Now+=11;f.CapturedAt=s.Now;s.Observe(f);Check(!s.Flow.Active,"ball animation still has a bounded 30-second deadline");
            var parsed=Parse(Text("Soak","PP 20/20","Spore","PP 15/15","False Swipe","PP 30/40","Mean Look","PP 5/5"));
            Check(parsed.View==CatchView.Moves && parsed.Control(CatchActionKind.ChooseMove)!=null && parsed.Control(CatchActionKind.ChooseMove).Slot==3 && parsed.Control(CatchActionKind.ChooseMove).Remaining==30,"move OCR requires configured name, slot and PP");
            parsed=Parse(Text("unrelated text"),null,Text("Balls","Pokéball","x516"));Check(parsed.View==CatchView.BallList && parsed.Control(CatchActionKind.UseBall).Remaining==516,"observed accented ball label and stock");
            var compactBag=Text("Pokéball","158","Great Ball","x20","Choose Item");
            compactBag.Rows[1].Bounds=new RectangleF(760,220,40,12);
            parsed=Parse(compactBag);Check(parsed.View==CatchView.BallList && parsed.Control(CatchActionKind.UseBall).Remaining==158,"battle Choose Item list accepts a scoped bare stock counter");
            compactBag.Rows[1].Bounds=new RectangleF(900,220,40,12);parsed=Parse(compactBag);
            var stockCrop=CatchVision.StockArea(compactBag,"Poke Ball",new Size(1000,800),true);
            Check(stockCrop.HasValue && stockCrop.Value.Top<=220 && stockCrop.Value.Bottom>231 && stockCrop.Value.Bottom<compactBag.Rows[2].Bounds.Top,"compact count retry includes the count but excludes the next item");
            Check(parsed.Control(CatchActionKind.UseBall).Remaining<0,"an unaligned number cannot be ball stock");
            s=new Scenario();f=s.Frame(CatchView.BallList);f.Ready=true;Check(s.Observe(f).Kind==CatchActionKind.OpenFight,"starting with Items open still prepares configured moves first");
            s=new Scenario();s.Prepare();f=s.Frame(CatchView.BallList);f.Control(CatchActionKind.UseBall).Remaining=-1;
            Check(s.Observe(f)==null && s.Flow.Active && s.Flow.BallsUsed==0,"unreadable stock remains paused for fresh evidence");
            s.Step(CatchView.BallList,CatchActionKind.UseBall);
            parsed=Parse(Text("unrelated text"),Text("Player: You caught a Mimikyu!"));Check(!parsed.Success,"chat-like prefix cannot be a capture acknowledgement");
            parsed=Parse(Text("unrelated text"),Text("You caught a Ralts!"));Check(!parsed.Success,"different captured species rejected");
            parsed=Parse(Text("unrelated text"),Text("Mimikyu was caught!"));Check(parsed.Success,"exact target capture text");
            parsed=Parse(Text("unrelated text"),Text("Mimikyu was successfully caught!"));Check(parsed.Success,"PRO successfully-caught wording");
            parsed=Parse(Text("unrelated text"),Text("Success! You caught Mimikyu!"));Check(parsed.Success,"observed live PRO success acknowledgement");
            parsed=Parse(Text("unrelated text"),Text("Ralts was successfully caught!"));Check(!parsed.Success,"successfully-caught wording still requires the exact target");
            parsed=Parse(Text("Fight Pokemon Items Run"));Check(parsed.Control(CatchActionKind.OpenFight)==null,"a line mentioning commands cannot replace the two-row control grid");
            result.Add("PASS: scoped OCR parses four move/PP anchors and accented ball names with stock; unrelated messages and other species cannot confirm a catch");
            var before=Parse(Text("unused"),null,Text("Balls","Pokeball","x516"));var fresh=Parse(Text("unused"),null,Text("Balls","Pokeball","x516"));
            action=new CatchAction {Kind=CatchActionKind.UseBall};Check(CatchVision.Reconfirms(action,before,fresh),"same menu/stock can reconfirm");fresh.Opponent="ralts";Check(!CatchVision.Reconfirms(action,before,fresh),"changed opponent invalidates fresh action");fresh.Opponent="mimikyu";fresh.Control(CatchActionKind.UseBall).Remaining=515;Check(!CatchVision.Reconfirms(action,before,fresh),"changed stock invalidates fresh action");
            result.Add("PASS: second-screen validation checks the same species, menu, control, position, active catcher and stock before input");
            string path=Path.Combine(Path.GetTempPath(),"pro-catch-settings-test-"+Guid.NewGuid()+".json");
            try {config=Config();CatchSettingsFile.Save(path,config);var loaded=CatchSettingsFile.Load(path);loaded.Moves[0].Uses=2;Check(config.Moves[0].Uses==1 && loaded.Catcher==config.Catcher && loaded.ProtectedTargets=="mimikyu","independent JSON settings");var copy=config.Copy();copy.Moves[0].Uses=3;Check(config.Moves[0].Uses==1,"profile copy isolation");}
            finally {if(File.Exists(path))File.Delete(path);}
            result.Add("PASS: profile recipe persistence and deep copies keep each window's configuration independent");
            return result;
        }
        public static string Ocr() {
            using(var image=new Bitmap(1000,800))using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",14,FontStyle.Bold)) {
                g.Clear(Color.FromArgb(20,28,20));g.FillRectangle(Brushes.DimGray,130,90,680,20);g.FillRectangle(Brushes.Black,630,110,180,260);
                g.DrawString("Advay",font,Brushes.White,220,105);g.DrawString("VS.",font,Brushes.Gold,320,105);g.DrawString("Wild Zigzagoon",font,Brushes.White,370,105);g.Flush();
                var chrome=new BattleVisual().Detect(image);Check(chrome.Kind==ChromeKind.Battle && chrome.Header.Bottom>=130,"gradient header retains the full caption below its gray edge");
                var title=LocalOcr.ReadBattle(image,chrome.Header,false).GetAwaiter().GetResult();
                Check(EncounterRecognition.Parse("[Encounter header]\n"+title.Header+"\n[Battle controls]\n\n[Full screen]\n").Name=="zigzagoon","full gradient caption OCR; "+title.Header);
            }
            using(var previewImage=new Bitmap(1000,800))using(var g=Graphics.FromImage(previewImage))using(var font=new Font("Segoe UI",14,FontStyle.Bold)) {
                g.Clear(Color.FromArgb(32,78,45));g.FillRectangle(Brushes.DimGray,630,290,320,35);g.DrawString("Seedot",font,Brushes.White,650,298);g.DrawString("PREVIEW",font,Brushes.White,800,298);g.DrawString("X",font,Brushes.White,920,298);g.Flush();
                var preview=LocalOcr.ReadCatch(previewImage,new ChromeReading {Kind=ChromeKind.Clear},Config(),"seedot",null,0,1,0).GetAwaiter().GetResult();
                Check(preview.Success && preview.Control(CatchActionKind.CloseSummary)!=null,"fast world OCR reads exact capture preview and close control; "+preview.ReadDetail);
            }
            using(var title=new Bitmap(800,90))using(var g=Graphics.FromImage(title))using(var font=new Font("Segoe UI",18,FontStyle.Bold)) {
                g.Clear(Color.FromArgb(55,55,59));g.DrawString("Advay",font,Brushes.White,20,20);g.DrawString("VS.",font,Brushes.Gold,170,20);g.DrawString("Wild Arbok",font,Brushes.White,240,20);g.Flush();
                using(var mask=LocalOcr.RepelOcrImage(title,new Rectangle(Point.Empty,title.Size),true)) {
                    var read=EncounterRecognition.Parse("[Encounter header]\n"+LocalOcr.Read(mask).GetAwaiter().GetResult()+"\n[Battle controls]\n\n[Full screen]\n");
                    Check(read.Name=="arbok","gold/white contrast pass preserves exact Arbok identity and VS marker");Check(!RunButtonMatch.AllowsOpponent("arbok",read,EncounterRecognition.Targets("Arbok")),"contrast recognition must still protect Arbok when targeted");
                }
            }
            using(var image=new Bitmap(1000,800))using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",14,FontStyle.Bold)) {
                g.Clear(Color.Black);g.DrawString("Advay VS. Wild Mimikyu",font,Brushes.White,180,60);g.DrawString("Smeargle",font,Brushes.White,430,500);
                string[] names={"Soak","Spore","False Swipe","Mean Look"};
                for(int n=0;n<4;n++) {g.DrawString(names[n],font,Brushes.White,760,190+n*80);g.DrawString("PP 20/40",font,Brushes.White,760,215+n*80);}g.Flush();
                var chrome=new ChromeReading {Kind=ChromeKind.Battle,Header=new Rectangle(100,40,860,65)};
                var frame=LocalOcr.ReadCatch(image,chrome,Config(),"mimikyu","False Swipe",3,1,0).GetAwaiter().GetResult();
                Check(frame.View==CatchView.Moves && frame.Opponent=="mimikyu" && frame.ActivePokemon=="Smeargle" && frame.Control(CatchActionKind.ChooseMove)!=null && frame.Control(CatchActionKind.ChooseMove).Slot==3,"Windows OCR for scoped move name, PP and slot; "+CatchVision.Describe(frame));
                g.FillRectangle(Brushes.Black,720,100,280,600);g.DrawString("Balls",font,Brushes.White,340,180);g.DrawString("Pokeball",font,Brushes.White,340,240);g.DrawString("x516",font,Brushes.White,340,275);g.Flush();
                frame=LocalOcr.ReadCatch(image,chrome,Config(),"mimikyu",null,0,2,0).GetAwaiter().GetResult();
                Check(frame.View==CatchView.BallList && frame.Control(CatchActionKind.UseBall)!=null && frame.Control(CatchActionKind.UseBall).Remaining==516,"Windows OCR for centered Balls pocket, label and count; "+CatchVision.Describe(frame)+"; fixture text="+LocalOcr.ReadCatchRegion(image,new Rectangle(200,144,580,384)).GetAwaiter().GetResult().Text);
                g.FillRectangle(Brushes.Black,200,140,530,390);g.DrawString("Mimikyu was caught!",font,Brushes.White,30,650);g.Flush();
                frame=LocalOcr.ReadCatch(image,chrome,Config(),"mimikyu",null,0,3,0).GetAwaiter().GetResult();Check(frame.Success,"Windows OCR for exact scoped capture acknowledgement");
            }
            using(var image=new Bitmap(1000,800))using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",12,FontStyle.Bold))using(var bar=new SolidBrush(Color.FromArgb(55,55,59))) {
                g.Clear(Color.FromArgb(32,78,45));g.FillRectangle(bar,130,90,680,32);g.FillRectangle(Brushes.Black,630,122,180,210);
                g.DrawString("Advay",font,Brushes.White,220,92);g.DrawString("VS.",font,Brushes.Gold,320,92);g.DrawString("Wild Seedot",font,Brushes.White,360,92);
                g.DrawString("Smeargle",font,Brushes.White,480,345);
                g.FillRectangle(Brushes.Gray,635,400,60,36);g.FillRectangle(Brushes.Gray,735,400,60,36);
                g.DrawString("Fight",font,Brushes.White,640,380);g.DrawString("Pokemon",font,Brushes.White,720,380);g.DrawString("Items",font,Brushes.White,640,440);g.DrawString("Run",font,Brushes.White,745,440);g.Flush();
                var chrome=new BattleVisual().Detect(image);Check(chrome.Kind==ChromeKind.Battle && chrome.BattleBox.HasValue,"translated smaller panel is a battle");
                var frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot","False Swipe",3,10,0).GetAwaiter().GetResult();
                Check(frame.View==CatchView.Commands && frame.Wild && frame.Opponent=="seedot" && frame.ActivePokemon=="Smeargle" && frame.Ready,"inset panel OCR uses panel-relative commands and own caption; "+CatchVision.Describe(frame)+"; box="+chrome.BattleBox+"; menu="+LocalOcr.ReadCatchRegion(image,BattleRegions.Menu(chrome,image.Size)).GetAwaiter().GetResult().Text);
                var run=LocalOcr.ReadBattle(image,chrome.Header,true,BattleRegions.Menu(chrome,image.Size)).GetAwaiter().GetResult();Check(run.ClientPoint.HasValue && run.ItemsPoint.HasValue && run.ClientPoint.Value.X>740,"inset Run/Items pair maps to original client coordinates");
                using(var logFont=new Font("Segoe UI",10,FontStyle.Bold))g.DrawString("Seedot was caught!",logFont,Brushes.White,638,200);g.Flush();frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot",null,0,11,0).GetAwaiter().GetResult();Check(frame.Success,"inset capture message read from battle log rather than the world/chat area");
                g.FillRectangle(Brushes.Black,630,122,180,210);g.FillRectangle(Brushes.Black,135,422,480,65);
                using(var logFont=new Font("Segoe UI",11,FontStyle.Bold))g.DrawString("Seedot was successfully caught!",logFont,Brushes.White,145,450);g.Flush();
                frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot",null,0,18,0).GetAwaiter().GetResult();Check(frame.Success,"capture acknowledgement in the observed bottom battle-message strip");
                g.FillRectangle(Brushes.Black,135,422,480,65);
                g.FillRectangle(bar,130,90,680,32);g.DrawString("Advay",font,Brushes.White,220,92);g.DrawString("VS.",font,Brushes.Gold,320,92);g.DrawString("Rival",font,Brushes.White,360,92);g.Flush();
                frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot",null,0,12,0).GetAwaiter().GetResult();Check(!frame.Wild && !frame.Success && frame.Control(CatchActionKind.OpenFight)==null,"trainer caption does not authorize wild catching or a stale capture result");
                g.FillRectangle(bar,130,90,680,32);g.DrawString("Advay",font,Brushes.White,220,92);g.DrawString("VS.",font,Brushes.Gold,320,92);g.DrawString("Wild Seedot",font,Brushes.White,360,92);g.FillRectangle(Brushes.Black,630,122,180,220);
                string[] moves={"False Swipe","Soak","Spore","Mean Look"};
                using(var ppFont=new Font("Segoe UI",10,FontStyle.Bold))for(int n=0;n<4;n++) {g.DrawString(moves[n],font,Brushes.White,638,130+n*45);g.DrawString("PP 20/40",ppFont,Brushes.White,690,155+n*45);}g.Flush();
                frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot","False Swipe",1,13,0).GetAwaiter().GetResult();Check(frame.View==CatchView.Moves && frame.Control(CatchActionKind.ChooseMove)!=null && frame.Control(CatchActionKind.ChooseMove).Slot==1,"open moves take priority over still-visible command buttons; "+CatchVision.Describe(frame)+"; fixture="+LocalOcr.ReadCatchRegion(image,BattleRegions.Menu(chrome,image.Size)).GetAwaiter().GetResult().Text);
                g.FillRectangle(Brushes.Black,630,122,180,220);g.DrawString("Choose Pokemon",font,Brushes.White,636,312);
                string[] party={"Ralts","Smeargle","Alakazam","Marowak"};Brush[] colors={Brushes.LimeGreen,Brushes.Orange,Brushes.Red,Brushes.MediumPurple};
                for(int n=0;n<4;n++) {g.DrawString(party[n],font,Brushes.White,10,30+n*50);var point=CatchVision.PartyIcon(chrome.BattleBox.Value,n+1);g.FillEllipse(colors[n],point.X-12,point.Y-14,24,28);}g.Flush();
                frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot","False Swipe",1,14,0).GetAwaiter().GetResult();Check(frame.View==CatchView.Party && frame.Control(CatchActionKind.ChooseCatcher)!=null && frame.Control(CatchActionKind.ChooseCatcher).Slot==2,"icon party requires full matching HUD names, slot and icon count; "+CatchVision.Describe(frame));
                var checkedLead=Config();checkedLead.ExpectedLead="Smeargle";frame=LocalOcr.ReadCatch(image,chrome,checkedLead,"seedot","False Swipe",1,15,0).GetAwaiter().GetResult();Check(frame.LeadPokemon==null,"a Pokémon in slot two must not pass leading-Pokémon verification");
                checkedLead.ExpectedLead="Ralts";frame=LocalOcr.ReadCatch(image,chrome,checkedLead,"seedot","False Swipe",1,16,0).GetAwaiter().GetResult();Check(frame.LeadPokemon=="Ralts","first HUD row verifies the expected leading Pokémon");
                g.FillRectangle(Brushes.Black,0,78,148,30);g.Flush();frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot","False Swipe",1,15,0).GetAwaiter().GetResult();Check(frame.View==CatchView.Party && frame.Control(CatchActionKind.ChooseCatcher)==null,"missing HUD member cannot shift the configured slot");
                g.DrawString("Smeargle",font,Brushes.White,10,80);g.FillRectangle(Brushes.DarkBlue,630,140,180,60);g.Flush();frame=LocalOcr.ReadCatch(image,chrome,Config(),"seedot","False Swipe",1,17,0).GetAwaiter().GetResult();Check(frame.View==CatchView.Party && frame.Control(CatchActionKind.ChooseCatcher)==null,"a colored overlay covering the icon row cannot pass as party icons");
            }
            return "PASS: Windows OCR covers targets, catcher, move/PP, stock, capture log, inset Run/Items coordinates and trainer rejection. Open moves override persistent commands; icon-party selection requires matching HUD name, slot and visible icon count, and rejects a missing HUD member. No game input sent.";
        }
    }
}
