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
            s=new Scenario();s.Prepare();s.Throw();s.Step(CatchView.Commands,CatchActionKind.OpenBag);s.Throw();s.Step(CatchView.Commands);Check(s.Flow.Phase==CatchPhase.Stopped && s.Flow.BallsUsed==2,"failed balls cannot exceed budget");
            s=new Scenario();s.Prepare();var empty=s.Frame(CatchView.BallList);empty.Control(CatchActionKind.UseBall).Remaining=0;Check(s.Observe(empty)==null && !s.Flow.Active,"empty stock stops before a throw");
            s=new Scenario();s.Step(CatchView.Commands,CatchActionKind.OpenFight);var noPp=s.Frame(CatchView.Moves);noPp.Control(CatchActionKind.ChooseMove).Remaining=0;Check(s.Observe(noPp)==null && !s.Flow.Active,"zero PP stops before move input");
            result.Add("PASS: escape from balls retries only within the configured cap; zero PP and empty stock stop before input");
            s=new Scenario();var f=s.Frame();var action=s.Observe(f);Check(s.Flow.Allows(action,f,s.Now,true),"fresh selected action");Check(!s.Flow.Allows(action,f,s.Now,false) && !s.Flow.Allows(action,f,s.Now+2,true),"lease and age checks");
            s.Flow.Suspend();Check(!s.Flow.Allows(action,f,s.Now,true),"handoff revokes old action");s.Flow.Cancel("F10");Check(s.Observe(s.Frame())==null && !s.Flow.Allows(action,f,s.Now,true),"stop cancels late work");
            s=new Scenario();f=s.Frame();f.CapturedAt=-2;Check(s.Observe(f)==null,"stale frame cannot change phase");f=s.Frame();f.Opponent="ralts";Check(s.Observe(f)==null,"different species never receives a catching action");
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
            var parsed=Parse(Text("Soak","PP 20/20","Spore","PP 15/15","False Swipe","PP 30/40","Mean Look","PP 5/5"));
            Check(parsed.View==CatchView.Moves && parsed.Control(CatchActionKind.ChooseMove)!=null && parsed.Control(CatchActionKind.ChooseMove).Slot==3 && parsed.Control(CatchActionKind.ChooseMove).Remaining==30,"move OCR requires configured name, slot and PP");
            parsed=Parse(Text("unrelated text"),null,Text("Balls","Pokéball","x516"));Check(parsed.View==CatchView.BallList && parsed.Control(CatchActionKind.UseBall).Remaining==516,"observed accented ball label and stock");
            parsed=Parse(Text("unrelated text"),Text("Player: You caught a Mimikyu!"));Check(!parsed.Success,"chat-like prefix cannot be a capture acknowledgement");
            parsed=Parse(Text("unrelated text"),Text("You caught a Ralts!"));Check(!parsed.Success,"different captured species rejected");
            parsed=Parse(Text("unrelated text"),Text("Mimikyu was caught!"));Check(parsed.Success,"exact target capture text");
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
            return "PASS: Windows OCR catch fixtures confirm the exact target and active catcher, four move/PP anchors, centered ball label/stock and scoped capture acknowledgement. No game input sent.";
        }
    }
}
