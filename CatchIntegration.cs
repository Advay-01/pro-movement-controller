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
        void BeginCatch(string species) {
            ValidateCatch();ReleaseEscape();controller.Pause(true,clock.Elapsed.TotalSeconds);ResetControls();
            catchFlow.Start(catchConfig,species,stableCount,clock.Elapsed.TotalSeconds);catchWorld.Reset();catchBusy=false;nextCatchScan=0;
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
                var frame=await LocalOcr.ReadCatch(image,chrome,catchConfig,species,move,slot,++catchFrameId,at);
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
                    lastScreenText=CatchVision.Describe(frame);
                    encounterStatus.Text="Read-only catching check: "+frame.View+"; opponent="+(frame.Opponent ?? "unknown")+". No input sent.";
                    log.Items.Insert(0,encounterStatus.Text);return;
                }
                if(!CatchContext(run,epoch))return;
                if(clock.Elapsed.TotalSeconds-frame.CapturedAt>CatchFlow.MaximumFrameAge) {StopCatch("Stopped — catching recognition took too long; check manually");return;}
                catchBusy=frame.View==CatchView.Busy;
                lastScreenText=CatchVision.Describe(frame);
                var action=catchFlow.Observe(frame,clock.Elapsed.TotalSeconds,true);
                encounterStatus.Text=catchFlow.Status;
                if(catchFlow.Phase==CatchPhase.Stopped) {StopCatch(catchFlow.Status);return;}
                if(catchFlow.Phase==CatchPhase.Complete) {FinishCatch();return;}
                if(action==null)return;
                catchInputPending=true;
                // Re-read the target and chosen control from a new screen immediately before input.
                var fresh=await CatchSnapshot(species,move,slot);
                if(!CatchContext(run,epoch))return;
                if(!CatchVision.Reconfirms(action,frame,fresh)) {StopCatch("Stopped — catching menu changed or could not be reconfirmed; check target manually");return;}
                action.FrameId=fresh.Id;action.Point=fresh.Control(action.Kind).Point;
                if(!catchFlow.Allows(action,fresh,clock.Elapsed.TotalSeconds,true)) {StopCatch("Stopped — catching screen was stale; no further input");return;}
                Native.ClickClient(output.Target,output.Process,action.Point);
                Activity("CATCH ACTION: "+action.Kind+"; "+action.Label+"; balls requested="+catchFlow.BallsUsed);
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
