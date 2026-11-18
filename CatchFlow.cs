using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace MovementPrototype {
    public sealed class CatchMove {
        public string Name="False Swipe";
        public int Slot=1,Uses=1;
    }
    public sealed class CatchConfig {
        public string Catcher="",Ball="Ultra Ball",ExpectedLead="",ProtectedTargets="";
        public int PartySlot=1,MaximumBalls=10;
        public bool Resume=true,VerifiedRecipe;
        public List<CatchMove> Moves=new List<CatchMove> {new CatchMove()};
        public static readonly string[] AllowedMoves={"False Swipe","Soak","Spore","Hypnosis","Sleep Powder","Stun Spore","Thunder Wave","Mean Look","Foresight","Odor Sleuth"};
        public static readonly string[] Balls={"Poke Ball","Great Ball","Ultra Ball","Dusk Ball","Net Ball","Nest Ball","Repeat Ball","Timer Ball","Quick Ball","Premier Ball","Luxury Ball","Dive Ball","Heal Ball","Master Ball"};
        public void Validate(bool requireVerified=true) {
            if(String.IsNullOrWhiteSpace(Catcher) || Catcher.Trim().Length>32)throw new ArgumentException("Enter the catcher's displayed name.");
            if(PartySlot<1 || PartySlot>6 || MaximumBalls<1 || MaximumBalls>100)throw new ArgumentException("Choose party slot 1–6 and a ball limit of 1–100.");
            if(Array.IndexOf(Balls,Ball)<0)throw new ArgumentException("Choose a supported ball type.");
            if(requireVerified && !VerifiedRecipe)throw new ArgumentException("Test and confirm the catching recipe for your protected species, including Ghost immunity and escape moves.");
            if(Moves==null || Moves.Count<1 || Moves.Count>8)throw new ArgumentException("Configure 1–8 catching steps.");
            bool swipe=false;int uses=0;
            foreach(var move in Moves) {
                if(move==null || Array.IndexOf(AllowedMoves,move.Name)<0 || move.Slot<1 || move.Slot>4 || move.Uses<1 || move.Uses>5)throw new ArgumentException("Each move needs a recognized name, slot 1–4 and 1–5 uses.");
                swipe|=move.Name=="False Swipe";uses+=move.Uses;
            }
            if(!swipe || uses>20)throw new ArgumentException("Include False Swipe; use at most 20 move turns per target.");
        }
        public CatchConfig Copy() {
            var copy=new CatchConfig {Catcher=Catcher,Ball=Ball,ExpectedLead=ExpectedLead,ProtectedTargets=ProtectedTargets,PartySlot=PartySlot,MaximumBalls=MaximumBalls,Resume=Resume,VerifiedRecipe=VerifiedRecipe,Moves=new List<CatchMove>()};
            foreach(var move in Moves)copy.Moves.Add(new CatchMove {Name=move.Name,Slot=move.Slot,Uses=move.Uses});return copy;
        }
    }
    public enum CatchView {Unknown,Commands,Moves,Party,Bag,BallList,Busy,Success,World}
    public enum CatchActionKind {OpenParty,ChooseCatcher,OpenFight,ChooseMove,OpenBag,OpenBalls,UseBall,CloseSummary}
    public sealed class CatchControl {
        public string Name;public Point Point;public int Slot,Remaining=-1;
        public bool ListedBattleItem;
        public Rectangle Bounds;
    }
    public sealed class CatchFrame {
        public long Id;
        public double CapturedAt;
        public CatchView View;
        public bool Wild,Ready,Success,Failure,WorldStable;
        public string Opponent,ActivePokemon,LeadPokemon,Message="",MessageKey="",FailureReason;
        public Dictionary<CatchActionKind,CatchControl> Controls=new Dictionary<CatchActionKind,CatchControl>();
        public Rectangle HeaderSeal,OwnSeal,ResultArea;
        public string ReadDetail="";
        public Dictionary<CatchActionKind,string> InputSeals=new Dictionary<CatchActionKind,string>();
        public CatchControl Control(CatchActionKind kind) {CatchControl value;return Controls.TryGetValue(kind,out value) ? value : null;}
    }
    public sealed class CatchAction {
        public CatchActionKind Kind;public Point Point;public long FrameId;public int Epoch;
        public string Label;
    }
    public enum CatchPhase {Idle,SelectCatcher,PrepareMove,WaitMove,SelectBall,WaitBall,ConfirmWorld,Complete,Stopped}
    // This policy never clicks or sends keys. Only a fresh leased screen may authorize an action.
    public sealed class CatchFlow {
        public const double MaximumFrameAge=2.5;
        CatchConfig config;
        string species,oldMessage="";
        int step,repeat,menuRequests,knownBallStock=-1;
        int unsentMenuRetries;
        double began,pendingAt,unclearAt=-1,progressAt,resultWorldAt=-1;
        bool sawBusy,successSeen;
        long lastFrame=-1;
        CatchActionKind? pendingMenu;
        public CatchPhase Phase {get;private set;}
        public int Epoch {get;private set;}
        public int BallsUsed {get;private set;}
        public string Status {get;private set;}
        public string Species {get{return species;}}
        public bool Resume {get{return config!=null && config.Resume;}}
        public bool Active {get{return Phase!=CatchPhase.Idle && Phase!=CatchPhase.Complete && Phase!=CatchPhase.Stopped;}}
        public bool WaitingAnimation {get{return Phase==CatchPhase.WaitMove || Phase==CatchPhase.WaitBall;}}
        public void Reset() {Epoch++;Phase=CatchPhase.Idle;Status="Auto-catch idle";config=null;species=null;step=repeat=BallsUsed=menuRequests=unsentMenuRetries=0;knownBallStock=-1;successSeen=sawBusy=false;lastFrame=-1;pendingMenu=null;unclearAt=resultWorldAt=-1;}
        public void Start(CatchConfig settings,string target,int confirmations,double now) {
            settings.Validate();if(confirmations<2 || String.IsNullOrEmpty(target))throw new InvalidOperationException("Two fresh exact target readings are required.");
            Reset();config=settings.Copy();species=target;began=progressAt=now;Phase=CatchPhase.SelectCatcher;Status="Target confirmed — selecting catcher";
        }
        public void Suspend() {Epoch++;lastFrame=-1;unclearAt=-1;knownBallStock=-1;}
        public void Cancel(string reason) {Epoch++;Phase=CatchPhase.Stopped;Status=reason;pendingMenu=null;}
        public bool RetryUnsentNavigation(CatchAction action) {
            if(action==null || action.Epoch!=Epoch || pendingMenu!=action.Kind || ++unsentMenuRetries>4)return false;
            pendingMenu=null;
            Status="Waiting for the catching menu to settle";
            return true;
        }
        public void NavigationSent() {unsentMenuRetries=0;}
        CatchAction Request(CatchActionKind kind,CatchControl control,CatchFrame frame,double now) {
            if(control==null)return null;
            if(++menuRequests>100) {Cancel("Stopped — catching navigation limit reached");return null;}
            pendingMenu=kind;pendingAt=now;
            if(kind!=CatchActionKind.CloseSummary)progressAt=now;
            return new CatchAction {Kind=kind,Point=control.Point,FrameId=frame.Id,Epoch=Epoch,Label=control.Name};
        }
        bool SameName(string a,string b) {return !String.IsNullOrEmpty(a) && EncounterRecognition.Normalize(a)==EncounterRecognition.Normalize(b);}
        bool OwnActionMessage(CatchFrame frame,string move) {
            return frame.MessageKey!=oldMessage && Regex.IsMatch(frame.Message ?? "",@"\b"+Regex.Escape(config.Catcher)+@"\b.*\bused\s+"+Regex.Escape(move)+@"\b",RegexOptions.IgnoreCase);
        }
        public CatchAction Observe(CatchFrame frame,double now,bool leaseReady) {
            if(!Active || !leaseReady || frame==null || frame.Id<=lastFrame || now-frame.CapturedAt<0 || now-frame.CapturedAt>MaximumFrameAge)return null;
            lastFrame=frame.Id;
            if(now-began>180) {Cancel("Stopped — catching time limit reached; target left for manual handling");return null;}
            if(frame.Failure && (frame.MessageKey!=oldMessage || !WaitingAnimation)) {Cancel("Stopped — "+(frame.FailureReason ?? "catching action failed"));return null;}
            if(frame.Success && BallsUsed>0 && Phase==CatchPhase.WaitBall && SameName(frame.Opponent,species)) {
                successSeen=true;Phase=CatchPhase.ConfirmWorld;pendingMenu=null;progressAt=now;Status="Capture confirmed — checking world and party";
            }
            if(Phase==CatchPhase.ConfirmWorld) {
                if(frame.View==CatchView.World && frame.WorldStable) {
                    if(!String.IsNullOrWhiteSpace(config.ExpectedLead) && !SameName(frame.LeadPokemon,config.ExpectedLead)) {Cancel("Stopped — leading Pokémon was not confirmed after catching");return null;}
                    Phase=CatchPhase.Complete;Status="Caught "+species+" — "+(config.Resume ? "resume hunting" : "stopped as configured");return null;
                }
                if(now-progressAt>12) {Cancel("Stopped — capture confirmed but the world/party did not return");return null;}
                if(pendingMenu.HasValue)return null;
                return Request(CatchActionKind.CloseSummary,frame.Control(CatchActionKind.CloseSummary),frame,now);
            }
            if(frame.View==CatchView.World) {
                if(Phase==CatchPhase.WaitBall) {if(resultWorldAt<0)resultWorldAt=now;if(now-resultWorldAt<2)return null;}
                Cancel(successSeen ? "Stopped — catch result needs confirmation" : "Stopped — battle ended without confirmed capture");return null;
            }
            if(frame.Wild && !SameName(frame.Opponent,species)) {Cancel("Stopped — opponent changed during catching");return null;}
            if(!frame.Wild) {
                if(unclearAt<0)unclearAt=now;
                if(now-unclearAt>8)Cancel("Stopped — target identity could not be reconfirmed");
                return null;
            }
            unclearAt=-1;
            if(Phase==CatchPhase.WaitMove || Phase==CatchPhase.WaitBall) {
                double actionWait=Phase==CatchPhase.WaitBall ? 30 : 18;
                if(frame.View==CatchView.Busy) {sawBusy=true;if(now-pendingAt>actionWait)Cancel("Stopped — catching action stayed busy too long");return null;}
                bool moveAck=Phase==CatchPhase.WaitMove && OwnActionMessage(frame,config.Moves[step].Name);
                bool ballAck=Phase==CatchPhase.WaitBall && frame.MessageKey!=oldMessage && Regex.IsMatch(frame.Message ?? "",@"\b(broke free|broke out|escaped from the ball|almost had it|appeared to be caught)\b",RegexOptions.IgnoreCase);
                if(frame.View==CatchView.Commands && frame.Ready && (sawBusy || moveAck || ballAck)) {
                    if(Phase==CatchPhase.WaitMove) {repeat++;if(repeat>=config.Moves[step].Uses) {step++;repeat=0;}Phase=step<config.Moves.Count ? CatchPhase.PrepareMove : CatchPhase.SelectBall;}
                    else Phase=CatchPhase.SelectBall;
                    pendingMenu=null;sawBusy=false;progressAt=now;Status="Battle ready — continuing catching sequence";
                } else {if(now-pendingAt>actionWait)Cancel("Stopped — catching action was not acknowledged");return null;}
            }
            if(pendingMenu.HasValue) {
                bool changed=(pendingMenu==CatchActionKind.OpenParty && frame.View==CatchView.Party) || (pendingMenu==CatchActionKind.OpenFight && frame.View==CatchView.Moves) || (pendingMenu==CatchActionKind.OpenBag && (frame.View==CatchView.Bag || frame.View==CatchView.BallList)) || (pendingMenu==CatchActionKind.OpenBalls && frame.View==CatchView.BallList) || (pendingMenu==CatchActionKind.ChooseCatcher && SameName(frame.ActivePokemon,config.Catcher));
                if(changed) {pendingMenu=null;progressAt=now;}
                else {if(now-pendingAt>4)Cancel("Stopped — catching menu did not change as expected");return null;}
            }
            if(now-progressAt>10) {Cancel("Stopped — catching controls could not be verified");return null;}
            if(Phase==CatchPhase.SelectCatcher) {
                if(SameName(frame.ActivePokemon,config.Catcher)) {Phase=CatchPhase.PrepareMove;Status="Catcher confirmed — preparing target";}
                else if(frame.View==CatchView.Commands && frame.Ready)return Request(CatchActionKind.OpenParty,frame.Control(CatchActionKind.OpenParty),frame,now);
                else if(frame.View==CatchView.Party) {
                    var catcher=frame.Control(CatchActionKind.ChooseCatcher);
                    if(catcher!=null && SameName(catcher.Name,config.Catcher) && catcher.Slot==config.PartySlot)return Request(CatchActionKind.ChooseCatcher,catcher,frame,now);
                    return null;
                } else return null;
            }
            if(Phase==CatchPhase.PrepareMove) {
                if(!SameName(frame.ActivePokemon,config.Catcher)) {Cancel("Stopped — active catcher changed");return null;}
                if((frame.View==CatchView.Commands || frame.View==CatchView.Party || frame.View==CatchView.BallList || frame.View==CatchView.Bag) && frame.Ready)return Request(CatchActionKind.OpenFight,frame.Control(CatchActionKind.OpenFight),frame,now);
                if(frame.View==CatchView.Moves) {
                    var move=frame.Control(CatchActionKind.ChooseMove);var wanted=config.Moves[step];
                    if(move==null || !SameName(move.Name,wanted.Name) || move.Slot!=wanted.Slot)return null;
                    if(move.Remaining<=0) {Cancel("Stopped — move PP is unavailable or exhausted");return null;}
                    pendingAt=progressAt=now;oldMessage=frame.MessageKey;sawBusy=false;Phase=CatchPhase.WaitMove;pendingMenu=null;
                    return new CatchAction {Kind=CatchActionKind.ChooseMove,Point=move.Point,FrameId=frame.Id,Epoch=Epoch,Label=move.Name};
                }
            }
            if(Phase==CatchPhase.SelectBall) {
                if(BallsUsed>=config.MaximumBalls) {Cancel("Stopped — configured ball limit reached; target left for manual handling");return null;}
                if((frame.View==CatchView.Commands || frame.View==CatchView.Moves || frame.View==CatchView.Party) && frame.Ready)return Request(CatchActionKind.OpenBag,frame.Control(CatchActionKind.OpenBag),frame,now);
                if(frame.View==CatchView.Bag)return Request(CatchActionKind.OpenBalls,frame.Control(CatchActionKind.OpenBalls),frame,now);
                if(frame.View==CatchView.BallList) {
                    var ball=frame.Control(CatchActionKind.UseBall);
                    if(ball==null || !SameName(ball.Name,config.Ball))return null;
                    if(ball.Remaining==0) {Cancel("Stopped — selected ball stock is exhausted");return null;}
                    if(ball.Remaining>0)knownBallStock=knownBallStock<0 ? ball.Remaining : Math.Min(knownBallStock,ball.Remaining);
                    if(knownBallStock==0 || knownBallStock<0 && !ball.ListedBattleItem)return null;
                    if(knownBallStock>0)knownBallStock--;
                    BallsUsed++;pendingAt=progressAt=now;oldMessage=frame.MessageKey;sawBusy=false;Phase=CatchPhase.WaitBall;pendingMenu=null;
                    return new CatchAction {Kind=CatchActionKind.UseBall,Point=ball.Point,FrameId=frame.Id,Epoch=Epoch,Label=ball.Name};
                }
            }
            return null;
        }
        public bool Allows(CatchAction action,CatchFrame frame,double now,bool leaseReady) {
            return Active && action!=null && frame!=null && leaseReady && action.Epoch==Epoch && action.FrameId==frame.Id && now-frame.CapturedAt>=0 && now-frame.CapturedAt<=MaximumFrameAge && (frame.Wild && SameName(frame.Opponent,species) || successSeen && action.Kind==CatchActionKind.CloseSummary);
        }
        public string WantedMove {get{return config!=null && step<config.Moves.Count ? config.Moves[step].Name : null;}}
        public int WantedMoveSlot {get{return config!=null && step<config.Moves.Count ? config.Moves[step].Slot : 0;}}
    }
}
