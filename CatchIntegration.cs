using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MovementPrototype {
    sealed partial class App {
        readonly CheckBox autoCatch=new CheckBox {Text="Auto-catch exact targets using this profile's settings",AutoSize=true};
        readonly Button catchSettings=new Button {Text="Catch settings…",AutoSize=true};
        readonly Button checkCatch=new Button {Text="Check catch screen (no input)",AutoSize=true};
        readonly CatchFlow catchFlow=new CatchFlow();
        readonly VisualSceneGate catchWorld=new VisualSceneGate();
        CatchConfig catchConfig=new CatchConfig();
        string catchSettingsPath;
        bool catchDialogOpen,catchPreviewPending,catchBusy,catchInputPending;
        double nextCatchScan;
        long catchFrameId;
        int slowCatchReads;
        string lastCatchView="";
        string lastCatchMessage="";
        Rectangle catchResultArea;
        Rectangle catchIdentityArea;
        string catchIdentityPixels;
        string TargetKey() {
            var names=new List<string>(EncounterRecognition.Targets(targetNames.Text));names.Sort(StringComparer.Ordinal);
            return String.Join(", ",names);
        }
        void InitializeCatchUi(FlowLayoutPanel flow) {
            catchSettingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,managedMode ? "catch-settings-"+statsName.Replace("visual-stats-","") : "catch-settings.json");
            try {catchConfig=CatchSettingsFile.Load(catchSettingsPath);}catch(Exception ex) {Activity("CATCH: settings not loaded: "+ex.Message);}
            flow.Controls.Add(autoCatch);flow.Controls.Add(Row(catchSettings,checkCatch));
            catchSettings.Click+=delegate {Guard(delegate {
                if(controller.Running || scanning || managedArmed)return;
                string key=TargetKey();catchDialogOpen=true;
                try {
                    using(var dialog=new CatchSettingsDialog(catchConfig,key)) {
                        if(dialog.ShowDialog(TopLevelControl)==DialogResult.OK) {
                            CatchSettingsFile.Save(catchSettingsPath,dialog.Settings);catchConfig=dialog.Settings;
                            encounterStatus.Text="Catching settings saved for "+key+". Enable Auto-catch when ready.";
                        }
                    }
                }finally {catchDialogOpen=false;RefreshState();}
            });};
            checkCatch.Click+=delegate {Guard(delegate {
                if(!output.Available())throw new InvalidOperationException("Select a visible game window first.");
                StopAll("Read-only catching check queued");catchPreviewPending=true;previewPending=true;previewAt=clock.Elapsed.TotalSeconds+3;
                Native.SetForegroundWindow(output.Target);
            });};
        }
        void ValidateCatch() {
            if(!autoCatch.Checked)return;
            if(!encounters.Checked || !external.Checked || background.Checked)throw new InvalidOperationException("Auto-catch requires enabled encounter handling and visible foreground input.");
            catchConfig.Validate();
            if(catchConfig.ProtectedTargets!=TargetKey())throw new InvalidOperationException("Protected targets changed. Review and save Catch settings for these targets before enabling Auto-catch.");
        }
        void BeginCatch(string species,Rectangle? headingArea,string headerSignature) {
            ValidateCatch();ReleaseEscape();controller.Pause(true,clock.Elapsed.TotalSeconds);ResetControls();
            catchFlow.Start(catchConfig,species,stableCount,clock.Elapsed.TotalSeconds);catchWorld.Reset();catchBusy=false;nextCatchScan=0;slowCatchReads=0;
            lastCatchMessage="";catchResultArea=Rectangle.Empty;
            catchIdentityArea=headingArea ?? Rectangle.Empty;catchIdentityPixels=headerSignature;
            Activity("CATCH: started for "+species+"; bounded recipe; ball limit="+catchConfig.MaximumBalls);
            encounterStatus.Text=catchFlow.Status;
            NotifyCatch("Target found",species+" — configured catch sequence starting.");
        }
        void NotifyCatch(string title,string message) {
            if(!alerts.Checked)return;
            tray.Visible=true;tray.BalloonTipTitle=title;tray.BalloonTipText=message;tray.ShowBalloonTip(7000);
            Native.Flash(TopLevelControl==null ? Handle : TopLevelControl.Handle);System.Media.SystemSounds.Exclamation.Play();
        }
        async System.Threading.Tasks.Task<CatchFrame> CatchSnapshot(string species,string move,int slot) {
            double at=clock.Elapsed.TotalSeconds;
            using(var image=Native.CaptureClient(output.Target)) {
                var chrome=visual.Detect(image);
                Rectangle? caption=null;
                if(catchFlow.Active && chrome.Kind==ChromeKind.Battle && catchIdentityPixels!=null && !catchIdentityArea.IsEmpty &&
                    chrome.Header.Contains(catchIdentityArea) &&
                    VerifiedHeader.Signature(image,catchIdentityArea)==catchIdentityPixels)
                    caption=catchIdentityArea;
                var resultArea=BattleRegions.BottomLog(chrome,image.Size);
                var throwArea=catchResultArea;
                var resultRead=catchFlow.Phase==CatchPhase.WaitBall && chrome.Kind!=ChromeKind.Clear &&
                    !throwArea.IsEmpty && resultArea!=throwArea
                    ? LocalOcr.ReadCatchRegion(image,throwArea) : null;
                var frame=await LocalOcr.ReadCatch(image,chrome,catchConfig,species,move,slot,++catchFrameId,at,
                    catchFlow.Phase==CatchPhase.SelectCatcher || !String.IsNullOrWhiteSpace(catchConfig.ExpectedLead),
                    catchFlow.Phase==CatchPhase.SelectBall,caption);
                if(frame.Wild && String.Equals(frame.Opponent,species,StringComparison.OrdinalIgnoreCase) && !frame.HeaderSeal.IsEmpty) {
                    catchIdentityArea=frame.HeaderSeal;
                    catchIdentityPixels=VerifiedHeader.Signature(image,catchIdentityArea);
                }
                frame.ResultArea=resultArea;
                if(resultRead!=null) {
                    var result=await resultRead;
                    if((!frame.Wild || String.Equals(frame.Opponent,species,StringComparison.OrdinalIgnoreCase)) &&
                        CatchVision.CaptureMessage(result.Text,species)) {
                        frame.Success=true;frame.View=CatchView.Success;frame.Opponent=species;
                        frame.Message=frame.MessageKey=result.Text;
                    }
                }
                frame.WorldStable=catchWorld.Observe(chrome.Kind,at)==VisualDecision.Move;
                return frame;
            }
        }
        bool CatchContext(int run,int epoch) {return !closing && run==generation && epoch==catchFlow.Epoch && controller.Running && catchFlow.Active && output.Ready() && !output.Busy();}
        async void ScanCatch(bool previewOnly=false) {
            scanning=true;int run=generation,epoch=catchFlow.Epoch;nextCatchScan=clock.Elapsed.TotalSeconds+.15;
            string species=previewOnly ? "" : catchFlow.Species;
            string move=previewOnly ? (catchConfig.Moves!=null && catchConfig.Moves.Count>0 ? catchConfig.Moves[0].Name : null) : catchFlow.WantedMove;
            int slot=previewOnly ? (catchConfig.Moves!=null && catchConfig.Moves.Count>0 ? catchConfig.Moves[0].Slot : 0) : catchFlow.WantedMoveSlot;
            try {
                var frame=await CatchSnapshot(species,move,slot);
                if(closing || run!=generation)return;
                if(previewOnly) {
                    if(!output.Foreground())return;
                    Activity("CATCH PREVIEW READ: "+frame.ReadDetail.Replace('\n',' '));
                    lastScreenText=CatchVision.Describe(frame);
                    encounterStatus.Text="Read-only catching check: "+frame.View+"; opponent="+(frame.Opponent ?? "unknown")+". No input sent.";
                    log.Items.Insert(0,encounterStatus.Text);return;
                }
                if(!CatchContext(run,epoch))return;
                if(clock.Elapsed.TotalSeconds-frame.CapturedAt>CatchFlow.MaximumFrameAge) {
                    Activity("CATCH: discarded slow read; ms="+Math.Round((clock.Elapsed.TotalSeconds-frame.CapturedAt)*1000)+"; retry="+(slowCatchReads+1));
                    if(++slowCatchReads>=3)StopCatch("Stopped — three catch reads were too slow; no stale input sent");
                    return;
                }
                slowCatchReads=0;
                catchBusy=frame.View==CatchView.Busy;
                string view=frame.View+"; own="+frame.ActivePokemon+"; ready="+frame.Ready+"; ownArea="+frame.OwnSeal;if(view!=lastCatchView) {Activity("CATCH SCREEN: "+view);if(frame.View==CatchView.Moves)Activity("CATCH MOVE READ: "+frame.ReadDetail.Replace('\n',' '));lastCatchView=view;}
                if(frame.View==CatchView.BallList && catchFlow.Phase==CatchPhase.SelectBall) {
                    var ball=frame.Control(CatchActionKind.UseBall);
                    if(ball==null || ball.Remaining<0)Activity("CATCH STOCK READ: "+frame.ReadDetail);
                }
                lastScreenText=CatchVision.Describe(frame);
                if(catchFlow.Phase==CatchPhase.WaitBall && frame.Message!=lastCatchMessage) {lastCatchMessage=frame.Message;Activity("CATCH RESULT READ: "+frame.Message.Replace('\n',' '));}
                var action=catchFlow.Observe(frame,clock.Elapsed.TotalSeconds,true);
                encounterStatus.Text=catchFlow.Status;
                if(catchFlow.Phase==CatchPhase.Stopped) {StopCatch(catchFlow.Status);return;}
                if(catchFlow.Phase==CatchPhase.Complete) {FinishCatch();return;}
                if(action==null)return;
                catchInputPending=true;
                // Ready commands use a fresh screenshot and exact caption/control
                // pixels instead of repeating the same expensive OCR on identical text.
                bool pixelCheck=action.Kind==CatchActionKind.ChooseMove || action.Kind==CatchActionKind.UseBall || action.Kind==CatchActionKind.OpenFight || action.Kind==CatchActionKind.OpenBag;
                var fresh=pixelCheck ? frame : await CatchSnapshot(species,move,slot);
                if(!CatchContext(run,epoch))return;
                if(!pixelCheck && !CatchVision.Reconfirms(action,frame,fresh)) {
                    if(catchFlow.RetryUnsentNavigation(action))return;
                    StopCatch("Stopped — catching menu changed or could not be reconfirmed; check target manually");return;
                }
                action.FrameId=fresh.Id;action.Point=fresh.Control(action.Kind).Point;
                if(!catchFlow.Allows(action,fresh,clock.Elapsed.TotalSeconds,true)) {StopCatch("Stopped — catching screen was stale; no further input");return;}
                using(var current=Native.CaptureClient(output.Target)) {
                    string seal;if(!fresh.InputSeals.TryGetValue(action.Kind,out seal) || seal!=CatchVision.InputSeal(current,fresh,action.Kind)) {
                        if(catchFlow.RetryUnsentNavigation(action))return;
                        StopCatch("Stopped — target, catcher or control pixels changed before input");return;
                    }
                }
                Native.ClickClient(output.Target,output.Process,action.Point);
                catchFlow.NavigationSent();
                if(action.Kind==CatchActionKind.UseBall)catchResultArea=fresh.ResultArea;
                Activity("CATCH ACTION: "+action.Kind+"; "+action.Label+"; balls requested="+catchFlow.BallsUsed+"; screen-to-input="+Math.Round((clock.Elapsed.TotalSeconds-fresh.CapturedAt)*1000)+"ms");
            }catch(Exception ex) {
                if(!closing && run==generation) {StopCatch("Stopped — catching check failed: "+ex.Message);}
            }finally {catchInputPending=false;nextCatchScan=clock.Elapsed.TotalSeconds+(catchBusy ? .25 : .10);scanning=false;if(!closing)RefreshState();}
        }
        void StopCatch(string reason) {
            catchFlow.Cancel(reason);StopAll(reason);encounterStatus.Text=reason;NotifyCatch("Catching stopped",reason);
        }
        void FinishCatch() {
            string species=catchFlow.Species;bool resume=catchFlow.Resume;
            Activity("CATCH: confirmed capture of "+species+"; balls requested="+catchFlow.BallsUsed+"; resume="+resume);
            catchFlow.Reset();generation++;stableName="";stableCount=0;ResetControls();repelPolicy.Reset();
            scene.Reset();visualDecision=VisualDecision.ValidateWorld;lastChrome=ChromeKind.Transition;nextProbe=nextScan=0;
            if(!resume)StopAll("Caught "+species+" — stopped as configured");
            else {controller.Pause(true,clock.Elapsed.TotalSeconds);encounterStatus.Text="Caught "+species+" — checking clear world before hunting again.";}
            NotifyCatch("Pokémon caught",species+(resume ? " — hunting resumes after the world check." : " — hunting stopped."));
        }
    }
}
