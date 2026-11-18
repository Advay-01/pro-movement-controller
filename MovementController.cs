using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace MovementPrototype {
    public sealed class Settings {
        public Keys First = Keys.Left, Second = Keys.Right;
        public double Hold = 0.8, Gap = 0.15, Limit = 30;
        public bool UntilStopped;
        public bool AutoFocus;
        public void Validate() {
            if (!Allowed(First) || !Allowed(Second) || First == Second)
                throw new ArgumentException("Choose two different movement keys.");
            if (!Finite(Hold) || Hold < 0.05 || Hold > 5 || !Finite(Gap) || Gap < 0.05 || Gap > 5 ||
                !Finite(Limit) || Limit < 1 || Limit > 120)
                throw new ArgumentException("Hold: 0.05–5s; gap: 0.05–5s; run: 1–120s.");
        }
        static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        public static bool Allowed(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.W || k == Keys.A || k == Keys.S || k == Keys.D; }
    }
    public interface IOutput { bool Ready(); void Down(Keys key); void Up(Keys key); }
    public interface IFocusOutput : IOutput {
        bool Available();
        bool Foreground();
        bool Busy();
        void RequestFocus();
    }
    // No background loops: one state transition per tick. Time is monotonic.
    public sealed class Controller {
        readonly IOutput output;
        Settings config;
        Keys? held;
        double began, next;
        bool second;
        bool? turnStartSide;
        readonly FocusRecovery recovery = new FocusRecovery();
        public bool RecoveringFocus { get { return recovery.Active; } }
        public event Action FocusRecoveryStarted;
        public bool Running { get; private set; }
        public bool Latched { get; private set; }
        public string Status { get; private set; }
        public Keys? Held { get { return held; } }
        public Controller(IOutput sink) { output = sink; Status = "Ready — practice mode"; }
        public void Start(Settings s, double now, bool initiallyPaused=false) {
            if (Latched) throw new InvalidOperationException("Reset the emergency stop first.");
            if (Running) return;
            s.Validate();
            if (!output.Ready()) throw new InvalidOperationException("The selected target is unavailable. Keep it restored; foreground mode also requires focus and released modifier keys.");
            config = new Settings { First=s.First, Second=s.Second, Hold=s.Hold, Gap=s.Gap, Limit=s.Limit, UntilStopped=s.UntilStopped, AutoFocus=s.AutoFocus };
            recovery.Cancel();
            began = now; next = now; second = false;turnStartSide=null; Paused=initiallyPaused; Running = true;
            Status = initiallyPaused ? "Starting — visual scene check" : "Running";
            Tick(now);
        }
        public void Tick(double now) {
            if (!Running) return;
            try {
                if (!config.UntilStopped && now - began >= config.Limit) { Stop("Stopped — run limit reached"); return; }
                var focus = output as IFocusOutput;
                if (config.AutoFocus && focus != null && (recovery.Active || (!output.Ready() && !focus.Foreground()))) {
                    if (!recovery.Active) {
                        Release();
                        recovery.Begin(now);
                        if (FocusRecoveryStarted != null) FocusRecoveryStarted();
                    }
                    var result = recovery.Step(now,focus.Available(),output.Ready(),focus.Busy(),focus.RequestFocus);
                    Status = recovery.Status;
                    if (result == FocusRecoveryResult.Failed) { Stop(recovery.Status); return; }
                    if (result == FocusRecoveryResult.Recovered) {
                        recovery.Cancel();
                        next = now + config.Gap;
                        Status = "Focus restored — released gap before resuming";
                    }
                    return;
                }
                if (!output.Ready()) { Stop("Stopped — target unavailable or focus safety check failed"); return; }
                if (Paused) return;
                if (now < next) return;
                if (held.HasValue) {
                    Release(); second = !second; next = now + config.Gap;
                    Status = "Running — gap";
                } else {
                    if(!turnStartSide.HasValue)turnStartSide=second;
                    Keys k = second ? config.Second : config.First;
                    // Remember before sending, so an uncertain send is followed by key-up.
                    held = k; output.Down(k); next = now + config.Hold;
                    Status = "Running — holding " + k;
                }
            } catch {
                recovery.Cancel();
                Running = false; Latched = true; Status = "Input error — emergency stop";
                Release(); throw;
            }
        }
        void Release() { if (held.HasValue) { output.Up(held.Value); held = null; } }
        public bool Paused { get; private set; }
        public void BeginTurn() {turnStartSide=null;}
        public void YieldTurn(double now) {
            if(!Running)return;
            Release();if(turnStartSide.HasValue)second=!turnStartSide.Value;
            turnStartSide=null;Paused=true;Status="Paused — waiting for shared focus";
        }
        public void Pause(bool paused, double now) {
            if(!Running) return;
            if(paused) { Release(); Paused=true; Status="Paused — scene transition"; }
            else if(Paused) { Paused=false; next=now; Status="Running"; }
        }
        public void Stop(string reason) { recovery.Cancel(); Running = false; Paused=false; Status = reason; Release(); }
        public void Emergency() { Latched = true; Stop("EMERGENCY STOP — reset required"); }
        public void Reset() { Release(); Latched = false; Status = "Ready — stopped"; }
    }
    static class Native {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        public delegate bool EnumWindowProc(IntPtr h, IntPtr data);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc callback, IntPtr data);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref POINT point);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
        [StructLayout(LayoutKind.Sequential)] struct FLASH { public uint size; public IntPtr window; public uint flags,count,timeout; }
        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASH info);
        public static void Flash(IntPtr h) { var f=new FLASH {size=(uint)Marshal.SizeOf(typeof(FLASH)),window=h,flags=3,count=6,timeout=0}; FlashWindowEx(ref f); }
        public static Bitmap CaptureClient(IntPtr h) {
            RECT rect; var point=new POINT();
            if(IsIconic(h) || !GetClientRect(h,out rect) || !ClientToScreen(h,ref point) || GetForegroundWindow()!=h)
                throw new InvalidOperationException("Keep PRO visible and focused for encounter reading.");
            int width=rect.Right-rect.Left, height=rect.Bottom-rect.Top;
            if(width<100 || height<100) throw new InvalidOperationException("The selected window is too small to read.");
            var bitmap=new Bitmap(width,height);
            try { using(var g=Graphics.FromImage(bitmap)) g.CopyFromScreen(point.X,point.Y,0,0,bitmap.Size); return bitmap; }
            catch { bitmap.Dispose(); throw; }
        }
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        public static readonly uint ActivateMessage = RegisterWindowMessage("MovementPrototype.Controller.Activate.v1");
        [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr h, uint message, IntPtr key, IntPtr data);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint key, uint type);
        public static uint KeyMessageData(Keys key, bool up) {
            uint data=1 | ((MapVirtualKey((uint)key,0) & 0xFF) << 16);
            if(key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down) data |= 1u << 24;
            if(up) data |= (1u<<30) | (1u<<31);
            return data;
        }
        public static void PostKey(IntPtr target, uint process, Keys key, bool up) {
            uint actual; GetWindowThreadProcessId(target,out actual);
            if(target==IntPtr.Zero || !IsWindow(target) || actual!=process) {
                if(up) return; // A destroyed/replaced target must never receive a release.
                throw new InvalidOperationException("The selected target has closed or changed. Select it again.");
            }
            if(!PostMessage(target,up ? 0x101u : 0x100u,new IntPtr((int)key),new IntPtr(unchecked((int)KeyMessageData(key,up)))))
                throw new InvalidOperationException("Windows rejected the background key message. Background mode is unavailable for this target.");
        }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
        [StructLayout(LayoutKind.Explicit)] struct UNION {
            [FieldOffset(0)] public KEYBOARD keyboard;
            [FieldOffset(0)] public MOUSE mouse;
        }
        [StructLayout(LayoutKind.Sequential)] struct KEYBOARD { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int x,y; public uint data,flags,time; public UIntPtr extra; }
        public static void Send(Keys key, bool up) {
            uint flags = up ? 2u : 0u;
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down) flags |= 1;
            // Use the physical right Shift scan code so left/right bindings remain distinct.
            bool rightShift = key == Keys.RShiftKey;
            INPUT input = new INPUT { type = 1, data = new UNION { keyboard = new KEYBOARD { key=rightShift ? (ushort)0 : (ushort)key, scan=rightShift ? (ushort)0x36 : (ushort)0, flags=flags | (rightShift ? 8u : 0u) } } };
            if (SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1)
                throw new InvalidOperationException("Windows rejected the key event. Stop and check the target. Do not elevate to bypass restrictions.");
        }
        public static void ClickClient(IntPtr window,uint expectedProcess,Point point) {
            RECT bounds; uint actual; GetWindowThreadProcessId(window,out actual);
            if(actual!=expectedProcess || GetForegroundWindow()!=window || IsIconic(window) || !GetClientRect(window,out bounds) || point.X<0 || point.Y<0 || point.X>=bounds.Right || point.Y>=bounds.Bottom)
                throw new InvalidOperationException("Run button target changed; no click sent.");
            var screen=new POINT {X=point.X,Y=point.Y};
            if(!ClientToScreen(window,ref screen) || !SetCursorPos(screen.X,screen.Y)) throw new InvalidOperationException("Cannot reach the visible Run button.");
            if(GetForegroundWindow()!=window) throw new InvalidOperationException("Focus changed before Run click; no click sent.");
            var down=new INPUT {type=0,data=new UNION {mouse=new MOUSE {flags=2}}};
            var up=new INPUT {type=0,data=new UNION {mouse=new MOUSE {flags=4}}};
            try {
                if(SendInput(1,new INPUT[] {down},Marshal.SizeOf(typeof(INPUT)))!=1) throw new InvalidOperationException("Windows rejected Run mouse-down.");
                System.Threading.Thread.Sleep(60);
            } finally {
                if(SendInput(1,new INPUT[] {up},Marshal.SizeOf(typeof(INPUT)))!=1) throw new InvalidOperationException("Mouse release failed. Input stopped; release the mouse button manually.");
            }
        }
        public static string Title(IntPtr h) { var s = new System.Text.StringBuilder(512); GetWindowText(h,s,s.Capacity); return s.ToString(); }
    }
    sealed class Output : IFocusOutput {
        readonly Func<int,bool> keyDown;
        public RunAwayKey RunKey;
        public Output(Func<int,bool> state=null) { keyDown=state ?? delegate(int key) { return (Native.GetAsyncKeyState(key)&0x8000)!=0; }; }
        bool BlockingKey(int key) { return keyDown(key) && !((key==0x10 || key==(int)RunAwayKey.Key) && RunKey!=null && RunKey.Held); }
        public bool ModifiersDown() {
            foreach(int key in new int[] {0x10,0xA0,0xA1,0x11,0x12,0x5B,0x5C}) if(BlockingKey(key)) return true;
            return false;
        }
        public bool External;
        public bool Background;
        public bool LeaseRequired,LeaseGranted;
        public IntPtr Target;
        public uint Process;
        public Keys? PracticeKey;
        public bool Available() {
            uint pid; Native.GetWindowThreadProcessId(Target,out pid);
            return Target!=IntPtr.Zero && Native.IsWindow(Target) && Native.IsWindowVisible(Target) && !Native.IsIconic(Target) && pid==Process;
        }
        public bool Foreground() { return Native.GetForegroundWindow()==Target; }
        public bool Busy() {
            foreach(int key in new int[] {0x01,0x02,0x04,0x05,0x06}) if(keyDown(key)) return true;
            if(ModifiersDown()) return true;
            return false;
        }
        public void RequestFocus() {
            if(External && !Background && Available() && !Busy()) Native.SetForegroundWindow(Target);
        }
        public bool Ready() {
            if(LeaseRequired && !LeaseGranted)return false;
            if (!External) return true;
            uint pid; Native.GetWindowThreadProcessId(Target, out pid);
            if(Target==IntPtr.Zero || !Native.IsWindow(Target) || Native.IsIconic(Target) || pid!=Process) return false;
            if(Background) return true;
            bool modifiers = ModifiersDown();
            return !modifiers && Target != IntPtr.Zero && Native.IsWindow(Target) && !Native.IsIconic(Target) && Native.GetForegroundWindow() == Target && pid == Process;
        }
        public void Down(Keys key) {
            if(LeaseRequired && !LeaseGranted)throw new InvalidOperationException("This profile does not hold the shared input lease.");
            if (External) {
                if (!Ready()) throw new InvalidOperationException("Target lost focus.");
                if(Background) Native.PostKey(Target,Process,key,false); else Native.Send(key, false);
            } else PracticeKey = key;
        }
        public void Up(Keys key) {
            if(External) { if(Background) Native.PostKey(Target,Process,key,true); else Native.Send(key,true); }
            else PracticeKey=null;
        }
    }
    sealed partial class App : Form,ISharedHunt {
        readonly bool managedMode;
        readonly string statsName;
        bool managedArmed,managedStarted,managedResumePending,managedBattleContinuity,managedWaiting,managedReadinessObserved;
        sealed class WindowChoice {
            public IntPtr Handle; public uint Process; public string Title;
            public override string ToString() { return Title + " (" + Process + ")"; }
        }
        readonly Output output = new Output();
        readonly Controller controller;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 15 };
        readonly ComboBox first = new ComboBox(), second = new ComboBox();
        readonly NumericUpDown hold = Number(0.8m,0.05m,5), gap = Number(0.15m,0.05m,5), limit = Number(30,1,120);
        readonly CheckBox untilStop = new CheckBox { Text="Until I stop it (no time limit)", Checked=true, AutoSize=true };
        readonly CheckBox continueRepel = new CheckBox { Text="Continue Repel — use another when the expired prompt appears", AutoSize=true };
        readonly RepelPromptVisual repelVisual=new RepelPromptVisual();
        readonly RepelRenewalPolicy repelPolicy=new RepelRenewalPolicy();
        int repelReads,repelClicks;
        readonly CheckBox encounters = new CheckBox { Text="Keep targets; run from other wild Pokémon (local OCR)", AutoSize=true };
        readonly CheckBox clickFallback = new CheckBox {Text="Use the recognised Run button immediately (Right Shift otherwise)",Checked=true,AutoSize=true};
        readonly TextBox targetNames = new TextBox { Width=360 };
        readonly CheckBox alerts = new CheckBox { Text="Target alert: notification, sound, and taskbar highlight", Checked=true, AutoSize=true };
        readonly Label encounterStatus = new Label { Text="Encounter handling off — PRO must stay visible and focused.", AutoSize=true, MaximumSize=new Size(570,0) };
        readonly NotifyIcon tray = new NotifyIcon { Icon=SystemIcons.Application,Text="Movement Controller" };
        HashSet<string> desiredNames;
        bool scanning;
        readonly RunAwayKey runKey = new RunAwayKey(Native.Send);
        bool escapeHeld { get { return runKey.Held; } }
        double nextScan, waitSince;
        string lastScreenText="";
        string lastError;
        readonly EncounterReadGrace readGrace=new EncounterReadGrace();
        readonly BattleVisual visual=new BattleVisual();
        readonly VisualSceneGate scene=new VisualSceneGate();
        readonly ReadyEscapePolicy readyEscape=new ReadyEscapePolicy();
        Point? trackedRun,trackedItems;
        int trackedWidth,trackedHeight,trackedGoldX,trackedHeaderY;
        ControlReady loggedReady=ControlReady.Unknown;
        string verifiedHeaderSignature,verifiedHeaderName;
        Rectangle? verifiedHeadingArea;
        void ResetControls() {trackedRun=trackedItems=null;readyEscape.Reset();loggedReady=ControlReady.Unknown;verifiedHeaderSignature=verifiedHeaderName=null;verifiedHeadingArea=null;managedWaiting=false;}
        bool SameControls(Bitmap image,ChromeReading chrome) {
            return trackedRun.HasValue && trackedItems.HasValue && image.Width==trackedWidth && image.Height==trackedHeight && Math.Abs(chrome.GoldX-trackedGoldX)<=8 && Math.Abs(chrome.Header.Top-trackedHeaderY)<=8;
        }
        void WatchControls(Bitmap image,ChromeReading chrome) {
            if(catchFlow.Active || !trackedRun.HasValue)return;
            if(!SameControls(image,chrome)) {ResetControls();stableCount=0;stableName="";nextScan=0;return;}
            var state=BattleControlReady.Read(image,trackedRun.Value,trackedItems.Value);
            readyEscape.Observe(state,clock.Elapsed.TotalSeconds);
            if(state!=loggedReady) {Activity("CONTROLS: "+state);loggedReady=state;}
            if(controller.Running && !scanning && stableCount>=2 && verifiedHeaderName==stableName && verifiedHeaderSignature!=null && clickFallback.Checked && readyEscape.Due(clock.Elapsed.TotalSeconds) && !escapeHeld && output.Ready() && !output.Busy()) {
                if(verifiedHeadingArea.HasValue && VerifiedHeader.Signature(image,verifiedHeadingArea.Value)==verifiedHeaderSignature) {
                    if(!EncounterRecognition.CloseToTarget(stableName,desiredNames))ClickReadyButton(stableName,trackedRun.Value,true);
                }
            }
        }
        void ClickReadyButton(string name,Point point,bool reusedHeader) {
            if(catchFlow.Active || EncounterRecognition.CloseToTarget(name,desiredNames))throw new InvalidOperationException("Run blocked for protected/catching encounter.");
            Native.ClickClient(output.Target,output.Process,point);
            escapeAttempts++;waitSince=clock.Elapsed.TotalSeconds;readyEscape.Submitted(waitSince);
            Activity("RUN: ready-control button clicked for "+name+"; "+Math.Round((waitSince-readyEscape.ReadyAt)*1000)+"ms after ready observed; accepted="+readyEscape.Accepted+(reusedHeader ? "; exact verified heading reused" : ""));
            LogFirstEscape();
        }
        VisualDecision visualDecision=VisualDecision.ValidateWorld;
        ChromeKind lastChrome=ChromeKind.Transition;
        double nextProbe, nextVisualStats, visualMs, clearStarted, battleStarted;
        int visualProbes, worldOcrCalls, battleOcrCalls, visualBattles, visualResumes;
        bool firstEscapeLogged;
        void SaveVisualStats() {
            if(visualProbes==0)return;
            try {
                string stats=String.Format(System.Globalization.CultureInfo.InvariantCulture,"{{\"visual_probes\":{0},\"probe_mean_ms\":{1:0.000},\"startup_or_refocus_world_ocr_calls\":{2},\"battle_ocr_calls\":{3},\"battle_appearances\":{4},\"visual_resumes\":{5}",visualProbes,visualMs/visualProbes,worldOcrCalls,battleOcrCalls,visualBattles,visualResumes);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,statsName),stats+",\"repel_prompt_ocr_calls\":"+repelReads+",\"repel_requests\":"+repelClicks+"}");
            } catch(IOException) {} catch(UnauthorizedAccessException) {}
        }
        readonly string activityPath;
        string lastReadingSummary="";
        void Activity(string message) {
            try {
                if(File.Exists(activityPath) && new FileInfo(activityPath).Length>131072) File.WriteAllText(activityPath,"Log restarted at size limit.\n");
                File.AppendAllText(activityPath,DateTimeOffset.Now.ToString("o")+"  "+message+Environment.NewLine);
            } catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
        readonly Button screenReading = new Button {Text="Show last screen reading",AutoSize=true};
        readonly Button checkEncounter = new Button {Text="Check encounter (no keys)",AutoSize=true};
        bool previewPending;
        double previewAt;
        int generation, stableCount, escapeAttempts;
        string stableName="";
        readonly CheckBox external = new CheckBox { Text="Enable keyboard input for an authorized test window", AutoSize=true };
        readonly CheckBox background = new CheckBox { Text="Keep moving when I click another window (experimental)", AutoSize=true };
        readonly CheckBox autoFocus = new CheckBox { Text="Return focus to the selected window automatically", AutoSize=true };
        readonly Label backgroundNote = new Label { Text="Auto-return takes focus from other apps. F8 stops; F10 is emergency stop.\nA minimized/closed target or a blocked focus request stops the run.", AutoSize=true, MaximumSize=new Size(570,0) };
        readonly Label target = new Label { Text="No target selected", AutoSize=true };
        readonly Label state = new Label { AutoSize=true, Font=new Font("Segoe UI",12,FontStyle.Bold) };
        readonly Label detail = new Label { AutoSize=true };
        readonly Button toggle = new Button { Text="Start / Stop (F8)", AutoSize=true };
        readonly Button capture = new Button { Text="Refresh window list", AutoSize=true };
        readonly ComboBox targets = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=570 };
        readonly Button reset = new Button { Text="Reset emergency stop", AutoSize=true };
        readonly Panel practice = new Panel { Width=570, Height=130, BackColor=Color.FromArgb(235,242,246) };
        readonly ListBox log = new ListBox { Width=570, Height=95, IntegralHeight=false };
        bool hotkeys, closing;
        double captureAt = -1, nextHotkeyRetry;
        bool pendingStart;
        float x=280, y=65;
        string lastStatus="", lastLoggedStatus="";
        public App(string managedSession=null) {
            managedMode=managedSession!=null;
            activityPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,managedMode ? "activity-"+managedSession+".log" : "activity.log");
            statsName=managedMode ? "visual-stats-"+managedSession+".json" : "visual-stats.json";
            output.LeaseRequired=managedMode;
            controller = new Controller(output);
            output.RunKey=runKey;
            Text="Movement Controller v2.1.5 — testing prototype";
            ClientSize=new Size(630,900); MinimumSize=new Size(650,780);
            Font=new Font("Segoe UI",10); StartPosition=FormStartPosition.CenterScreen;
            var flow=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new Padding(22), AutoScroll=true };
            Controls.Add(flow);
            flow.Controls.Add(new Label { Text="Back-and-forth movement", Font=new Font(Font.FontFamily,16,FontStyle.Bold), AutoSize=true });
            var emergency=new Button { Text="EMERGENCY STOP (F10)", BackColor=Color.Firebrick, ForeColor=Color.White, AutoSize=true, UseVisualStyleBackColor=false };
            if(!managedMode) {flow.Controls.Add(Row(toggle,emergency));flow.Controls.Add(reset);}
            else MinimumSize=Size.Empty;
            if(!managedMode) {
                var dual=new Button {Text="Two-window hunting mode",AutoSize=true};flow.Controls.Add(dual);
                dual.Click+=delegate {Guard(delegate {
                    StopAll("Stopped for two-window mode");timer.Stop();Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);hotkeys=false;Hide();
                    try {using(var shared=new DualApp())shared.ShowDialog(this);}
                    finally {Show();RegisterShortcuts();timer.Start();}
                });};
            }
            flow.Controls.Add(state); flow.Controls.Add(detail);
            state.MaximumSize=new Size(570,0); detail.MaximumSize=new Size(570,0);
            flow.Controls.Add(new Label { Text=managedMode ? "Shared F8 starts/stops both. F10 stops all input.\nEach profile keeps its own target, Repel and movement settings." : "F8: start / stop     F10: emergency stop (global)\nPractice mode is the default. No game input is sent in practice mode.", AutoSize=true, Margin=new Padding(3,8,3,12) });
            foreach (var c in new ComboBox[] {first,second}) {
                c.DropDownStyle=ComboBoxStyle.DropDownList; c.Width=85;
                c.Items.AddRange(new object[] { Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.W, Keys.A, Keys.S, Keys.D });
            }
            first.SelectedIndex=0; second.SelectedIndex=1;
            flow.Controls.Add(Row("First key",first,"Second key",second));
            flow.Controls.Add(Row("Hold (sec)",hold,"Gap (sec)",gap));
            flow.Controls.Add(Row("Run limit (sec)",limit));
            flow.Controls.Add(untilStop);
            flow.Controls.Add(continueRepel);
            flow.Controls.Add(encounters);
            flow.Controls.Add(new Label {Text="Run Away key: Right Shift (pressed briefly, then released)",AutoSize=true});
            flow.Controls.Add(clickFallback);
            flow.Controls.Add(Row("Target names",targetNames));
            InitializeCatchUi(flow);
            flow.Controls.Add(alerts); flow.Controls.Add(encounterStatus); flow.Controls.Add(Row(checkEncounter,screenReading));
            checkEncounter.Click += delegate { Guard(delegate {
                if(!output.Available()) throw new InvalidOperationException("Select the visible game window first.");
                StopAll("Read-only encounter check queued");
                previewPending=true; previewAt=clock.Elapsed.TotalSeconds+3;
                Native.SetForegroundWindow(output.Target);
            }); };
            screenReading.Click += delegate {
                Guard(delegate { StopAll("Stopped for screen-reading review"); });
                MessageBox.Show(this,lastScreenText.Length==0 ? "No screen reading yet." : lastScreenText,"Last recognized screen text",MessageBoxButtons.OK,MessageBoxIcon.Information);
            };
            untilStop.CheckedChanged += delegate { RefreshState(); };
            continueRepel.CheckedChanged += delegate {repelPolicy.Reset();Activity("REPEL OPTION: "+(continueRepel.Checked ? "enabled" : "disabled"));};
            flow.Controls.Add(external);if(!managedMode) {flow.Controls.Add(autoFocus);flow.Controls.Add(backgroundNote);}
            flow.Controls.Add(capture); flow.Controls.Add(targets); flow.Controls.Add(target);
            flow.Controls.Add(new Label { Text="PRO prohibits unauthorized bots and macros. Use live input only within\nexplicit staff-approved test scope. Selecting a window is not authorization.", AutoSize=true, ForeColor=Color.DarkRed });
            flow.Controls.Add(practice); flow.Controls.Add(log);
            toggle.Click += delegate { Toggle(); };
            emergency.Click += delegate { Guard(delegate { EmergencyAll(); }); };
            reset.Click += delegate { Guard(delegate { lastError=null; ReleaseEscape(); controller.Reset(); }); };
            capture.Click += delegate { RefreshWindows(); };
            targets.SelectedIndexChanged += delegate {
                var selected=targets.SelectedItem as WindowChoice;
                output.Target=selected==null ? IntPtr.Zero : selected.Handle;
                output.Process=selected==null ? 0 : selected.Process;
                target.Text=selected==null ? "Choose the authorized test window above." : "Selected: " + selected.Title;
                Activity(selected==null ? "Target cleared" : "Target selected: "+selected.Title+" ("+selected.Process+")");
            };
            practice.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.DrawString(output.External ? "Practice preview paused — external input enabled" : "LOCAL PRACTICE — simulated movement", Font, Brushes.SlateGray, 10, 8);
                e.Graphics.FillEllipse(Brushes.Teal,x-10,y-10,20,20);
            };
            external.CheckedChanged += delegate { if (!controller.Running) output.External=external.Checked; };
            controller.FocusRecoveryStarted += delegate {
                if(catchFlow.Active)catchFlow.Cancel("Stopped — focus changed during automatic catching");
                generation++; ReleaseEscape(); stableCount=0; stableName="";
                repelPolicy.Reset();
                readGrace.Reset();
                controller.Pause(true,clock.Elapsed.TotalSeconds);
                scene.Reset();visualDecision=VisualDecision.ValidateWorld;lastChrome=ChromeKind.Transition;nextProbe=0;
                ResetControls();
                nextScan=0; waitSince=clock.Elapsed.TotalSeconds;
            };
            background.CheckedChanged += delegate {
                if(!controller.Running && !controller.Held.HasValue && !escapeHeld) output.Background=background.Checked;
                if(background.Checked) {encounters.Checked=false;continueRepel.Checked=false;}
                encounterStatus.Text=background.Checked ? "Background movement only — encounter recognition and target alerts off." : "Encounter handling off — PRO must stay visible and focused.";
                RefreshState();
            };
            timer.Tick += delegate { Tick(); };if(!managedMode)timer.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                try { StopAll("Closed"); } catch(Exception ex) { e.Cancel=true; MessageBox.Show("Key release failed: " + ex.Message + "\nPress and release the movement key manually before closing."); return; }
                closing=true; timer.Stop(); tray.Dispose();
            };
            RefreshWindows(); RefreshState();
            Activity("Controller opened; no automatic start.");
        }
        public long Window {get{return output.Target.ToInt64();}}
        public bool Wanted {get{return managedArmed && (!managedStarted || controller.Running);}}
        public bool Available {get{return output.Available();}}
        public bool Reading {get{return scanning;}}
        public double ReadAllowance {get{return catchFlow.Active ? 2.5 : .8;}}
        public bool Waiting {get{return managedWaiting && managedReadinessObserved;}}
        public bool ManagedShiftHeld {get{return escapeHeld;}}
        public bool ManagedLatched {get{return controller.Latched;}}
        public void PrepareManaged(string protectedTarget,uint process,bool renewRepel) {
            EncounterRecognition.Targets(protectedTarget);WindowChoice match=null;int count=0;
            foreach(WindowChoice choice in targets.Items)if(choice.Process==process && choice.Title=="PROClient") {match=choice;count++;}
            if(count!=1)throw new InvalidOperationException("Select exactly one visible PRO window for process "+process);
            targets.SelectedItem=match;targetNames.Text=protectedTarget;hold.Value=.30m;gap.Value=.05m;
            untilStop.Checked=true;external.Checked=true;encounters.Checked=true;continueRepel.Checked=renewRepel;
            controller.Stop("Ready — shared controller starts with F8");RefreshState();
            Activity("PROFILE: protected="+protectedTarget+"; repel="+renewRepel+"; process="+process);
        }
        public void ValidateManaged() {
            if(catchDialogOpen)throw new InvalidOperationException("Close the catching settings dialog before starting.");
            if(!managedMode || scanning)throw new InvalidOperationException("Wait for profile recognition to finish.");
            if(controller.Latched)throw new InvalidOperationException("Reset the shared emergency stop before restarting.");
            if(!external.Checked || !output.Available())throw new InvalidOperationException("Enable input and select a restored game window in each profile.");
            ReadSettings().Validate();
            if(encounters.Checked)desiredNames=EncounterRecognition.Targets(targetNames.Text);
            ValidateCatch();
        }
        public void QueueManaged() {ValidateManaged();managedArmed=true;managedStarted=false;controller.Stop("Queued — waiting for shared focus");RefreshState();}
        public new void Enter() {
            output.LeaseGranted=true;managedReadinessObserved=false;
            try {if(!managedStarted) {StartRun();managedStarted=true;}controller.BeginTurn();nextProbe=0;RefreshState();}
            catch {output.LeaseGranted=false;throw;}
        }
        public new void Leave() {
            try {controller.YieldTurn(clock.Elapsed.TotalSeconds);ReleaseEscape();}
            finally {
                output.LeaseGranted=false;generation++;stableCount=0;stableName="";
                if(catchFlow.Active) {if(catchInputPending)StopCatch("Stopped — foreground turn ended before the catching action was delivered");else catchFlow.Suspend();}
                verifiedHeaderSignature=verifiedHeaderName=null;verifiedHeadingArea=null;
                managedResumePending=scene.InBattle;readyEscape.Observe(ControlReady.Unknown,clock.Elapsed.TotalSeconds);
                repelPolicy.Suspend();scene.Reset();visualDecision=VisualDecision.ValidateWorld;lastChrome=ChromeKind.Transition;nextProbe=nextScan=0;
                if(!controller.Running)managedArmed=false;RefreshState();
            }
        }
        public void Service() {
            if(!output.LeaseGranted)return;
            Tick();
            if(catchFlow.Active) {managedWaiting=catchFlow.WaitingAnimation && catchBusy;managedReadinessObserved=true;return;}
            if(visualDecision==VisualDecision.ReadBattle && trackedRun.HasValue && readyEscape.State==ControlReady.Busy)managedWaiting=managedWaiting || stableCount>=2;
            else if(readyEscape.State==ControlReady.Ready || lastChrome==ChromeKind.Clear)managedWaiting=false;
        }
        public void End(bool emergency,string reason) {
            try {if(emergency)EmergencyAll();else StopAll(reason);}
            finally {managedArmed=false;managedStarted=false;output.LeaseGranted=false;RefreshState();}
        }
        public void ResetManaged() {End(false,"Ready — both stopped");ReleaseEscape();controller.Reset();RefreshState();}
        public void CloseManaged() {End(false,"Closed profile");closing=true;timer.Stop();tray.Dispose();Dispose();}
        public void PreparePro(string protectedTarget,bool renewRepel=false) {
            EncounterRecognition.Targets(protectedTarget);
            WindowChoice match=null; int count=0;
            foreach(WindowChoice choice in targets.Items) {
                if(choice.Title=="PROClient") { match=choice; count++; }
            }
            if(count!=1) throw new InvalidOperationException("Open exactly one PROClient window before preparing automatic mode.");
            targets.SelectedItem=match; targetNames.Text=protectedTarget;
            hold.Value=.30m;gap.Value=.05m;
            untilStop.Checked=true; external.Checked=true; encounters.Checked=true; autoFocus.Checked=true;
            continueRepel.Checked=renewRepel;
            controller.Stop("Ready — external input armed; press F8 in PRO");
            encounterStatus.Text="Ready — visual watcher starts with F8; encounter OCR stays idle until a battle.";
            RefreshState();
            Activity("Automatic mode prepared; protected target: "+protectedTarget+". Waiting for F8 in PRO.");
        }
        void RefreshWindows() {
            if(controller.Running || pendingStart || controller.Held.HasValue) return;
            IntPtr previous=output.Target; targets.Items.Clear();
            output.Target=IntPtr.Zero; output.Process=0;
            Native.EnumWindows(delegate(IntPtr h, IntPtr data) {
                uint pid; Native.GetWindowThreadProcessId(h,out pid);
                string title=Native.Title(h);
                if(Native.IsWindowVisible(h) && pid!=(uint)Process.GetCurrentProcess().Id && !String.IsNullOrWhiteSpace(title)) {
                    var item=new WindowChoice {Handle=h,Process=pid,Title=title}; targets.Items.Add(item);
                    if(h==previous) targets.SelectedItem=item;
                }
                return true;
            },IntPtr.Zero);
            if(targets.SelectedIndex<0) target.Text="Choose the authorized test window above.";
        }
        static NumericUpDown Number(decimal value, decimal min, decimal max) { return new NumericUpDown { DecimalPlaces=2, Increment=0.05m, Minimum=min, Maximum=max, Value=value, Width=85 }; }
        static FlowLayoutPanel Row(params object[] parts) {
            var r=new FlowLayoutPanel { AutoSize=true, WrapContents=false, Margin=new Padding(0,3,0,3) };
            foreach(object p in parts) r.Controls.Add(p is Control ? (Control)p : new Label { Text=p.ToString(), AutoSize=true, Margin=new Padding(3,7,5,0) });
            return r;
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            RegisterShortcuts();
        }
        void RegisterShortcuts() {
            if(managedMode)return;
            if(hotkeys || !IsHandleCreated) return;
            bool a=Native.RegisterHotKey(Handle,1,0x4000,(uint)Keys.F8);
            bool b=Native.RegisterHotKey(Handle,2,0x4000,(uint)Keys.F10);
            hotkeys=a && b;
            if(!hotkeys) { Native.UnregisterHotKey(Handle,1); Native.UnregisterHotKey(Handle,2); }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); RefreshState(); }
        protected override void OnHandleDestroyed(EventArgs e) {
            Native.UnregisterHotKey(Handle,1); Native.UnregisterHotKey(Handle,2); hotkeys=false; base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message m) {
            if(Native.ActivateMessage!=0 && m.Msg==(int)Native.ActivateMessage && !closing) {
                Guard(delegate { StopAll("Stopped — controller reopened"); });
                if(WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal;
                Show(); Activate(); return;
            }
            if(m.Msg==0x312 && !closing) {
                if(m.WParam.ToInt32()==1) Toggle(true);
                if(m.WParam.ToInt32()==2) Guard(delegate { EmergencyAll(); });
            }
            base.WndProc(ref m);
        }
        void Toggle() { Toggle(false); }
        void Toggle(bool shortcut) {
            Guard(delegate {
                if(catchDialogOpen)return;
                lastError=null;
                if(previewPending) { StopAll("Read-only check cancelled"); return; }
                if(pendingStart) { pendingStart=false; controller.Stop("Start cancelled"); return; }
                if(controller.Running) { StopAll("Stopped by user"); return; }
                if(controller.Latched) throw new InvalidOperationException("Reset the emergency stop first.");
                RegisterShortcuts();
                if(!hotkeys) throw new InvalidOperationException("F8 / F10 are being used by another app or an older controller. Close that copy. This window retries automatically; you do not need to reopen it.");
                ReadSettings().Validate();
                if(background.Checked && (encounters.Checked || continueRepel.Checked)) throw new InvalidOperationException("Encounter and Repel handling require visible foreground input.");
                if(continueRepel.Checked && !output.External)throw new InvalidOperationException("Continue Repel needs keyboard input enabled for a selected, visible PRO window. Use one Repel manually and enable PRO's Repel prompt first.");
                if(encounters.Checked) {
                    if(!output.External) throw new InvalidOperationException("Encounter handling needs a selected game window and keyboard input enabled. Leave it off for local practice.");
                    desiredNames=EncounterRecognition.Targets(targetNames.Text);
                }
                ValidateCatch();
                if(output.External && output.Target==IntPtr.Zero) throw new InvalidOperationException("Choose the authorized test window from the window list first. Use Refresh if it is missing.");
                if(output.External && !output.Background && !shortcut) { pendingStart=true; captureAt=clock.Elapsed.TotalSeconds+3; return; }
                StartRun();
            });
        }
        void StartRun() {
            ValidateCatch();catchFlow.Reset();nextCatchScan=0;catchBusy=false;catchWorld.Reset();
            generation++; stableName=""; stableCount=0; escapeAttempts=0;
            managedResumePending=managedBattleContinuity=managedWaiting=managedReadinessObserved=false;
            repelPolicy.Reset();repelReads=repelClicks=0;
            readGrace.Reset();
            scene.Reset();visualDecision=VisualDecision.ValidateWorld;lastChrome=ChromeKind.Transition;nextProbe=0;
            ResetControls();
            visualProbes=worldOcrCalls=battleOcrCalls=visualBattles=visualResumes=0;visualMs=0;nextVisualStats=0;
            nextScan=0; waitSince=clock.Elapsed.TotalSeconds;
            var settings=ReadSettings();
            if(settings.AutoFocus && !output.Ready() && !output.Busy()) output.RequestFocus();
            controller.Start(settings,clock.Elapsed.TotalSeconds,WatchGameScene());
        }
        bool WatchGameScene() {return encounters.Checked || continueRepel.Checked || output.External && Native.Title(output.Target)=="PROClient";}
        void ReleaseEscape() { runKey.Release(); }
        void StopAll(string reason) { lastError=null; pendingStart=false; previewPending=false;catchPreviewPending=false; generation++;repelPolicy.Reset();if(catchFlow.Active)catchFlow.Cancel(reason); try { controller.Stop(reason); } finally { ReleaseEscape(); SaveVisualStats(); } }
        void EmergencyAll() { lastError=null; pendingStart=false; previewPending=false;catchPreviewPending=false; generation++;repelPolicy.Reset();if(catchFlow.Active)catchFlow.Cancel("Emergency stop"); try { controller.Emergency(); } finally { ReleaseEscape(); SaveVisualStats(); } }
        Settings ReadSettings() { return new Settings { First=(Keys)first.SelectedItem, Second=(Keys)second.SelectedItem, Hold=(double)hold.Value, Gap=(double)gap.Value, Limit=(double)limit.Value, UntilStopped=untilStop.Checked, AutoFocus=!managedMode && autoFocus.Checked && output.External && !output.Background }; }
        void Guard(Action action) {
            try { action(); }
            catch(Exception ex) {
                pendingStart=false;
                if(controller.Running || controller.Held.HasValue || escapeHeld) { try { EmergencyAll(); } catch { } }
                log.Items.Insert(0,"ERROR: " + ex.Message);
                Activity("ERROR: "+ex.Message);
                lastError=ex.Message;
            }
            RefreshState();
        }
        void Tick() {
            if(closing) return;
            if(previewPending && clock.Elapsed.TotalSeconds>=previewAt) { previewPending=false;bool forCatch=catchPreviewPending;catchPreviewPending=false;if(forCatch)ScanCatch(true);else ScanEncounter(true); }
            if(!hotkeys && !controller.Running && !pendingStart && clock.Elapsed.TotalSeconds>=nextHotkeyRetry) {
                nextHotkeyRetry=clock.Elapsed.TotalSeconds+1; RegisterShortcuts();
            }
            if(pendingStart && clock.Elapsed.TotalSeconds>=captureAt) {
                pendingStart=false;
                Guard(delegate { StartRun(); });
            }
            if(escapeHeld && (!controller.Running || clock.Elapsed.TotalSeconds>=runKey.ReleaseAt)) Guard(delegate { ReleaseEscape(); });
            Guard(delegate { controller.Tick(clock.Elapsed.TotalSeconds); });
            if(controller.Running && catchFlow.Phase==CatchPhase.Stopped) {Guard(delegate {StopAll(catchFlow.Status);});}
            if(controller.Running && !controller.RecoveringFocus && output.Ready() && WatchGameScene()) {
                double now=clock.Elapsed.TotalSeconds;
                if(catchFlow.Active) {
                    controller.Pause(true,now);ReleaseEscape();
                    if(!scanning && now>=nextCatchScan)ScanCatch();
                    RefreshState();return;
                }
                if(now>=nextProbe) { nextProbe=now+.08; Guard(ProbeScene); }
                bool waitingForDialogue=managedMode && managedWaiting && readyEscape.State==ControlReady.Busy;
                bool needName=encounters.Checked && !waitingForDialogue && !repelPolicy.Active && visualDecision==VisualDecision.ReadBattle && (stableCount<2 || !trackedRun.HasValue || readyEscape.Due(now) || readyEscape.StopRequired(now));
                if(controller.Running && !scanning && now>=nextScan && needName) ScanEncounter();
                if(now>=nextVisualStats) {nextVisualStats=now+5;SaveVisualStats();}
            }
            if(output.PracticeKey.HasValue) {
                Keys k=output.PracticeKey.Value;
                if(k==Keys.Left || k==Keys.A) x-=1.5f;
                if(k==Keys.Right || k==Keys.D) x+=1.5f;
                if(k==Keys.Up || k==Keys.W) y-=1.5f;
                if(k==Keys.Down || k==Keys.S) y+=1.5f;
                x=Math.Max(12,Math.Min(practice.Width-12,x)); y=Math.Max(40,Math.Min(practice.Height-12,y));
            }
            practice.Invalidate();
        }
        bool WatchRepel(Bitmap image,ChromeReading chrome) {
            bool wasActive=repelPolicy.Active;int requests=repelPolicy.Requests;
            var bounds=repelVisual.Find(image);
            if(!continueRepel.Checked) {
                repelPolicy.Reset();
                if(!bounds.HasValue)return false;
                controller.Pause(true,clock.Elapsed.TotalSeconds);ReleaseEscape();
                visualDecision=scene.Observe(ChromeKind.Transition,clock.Elapsed.TotalSeconds);lastChrome=ChromeKind.Transition;
                encounterStatus.Text="Repel expired — renewal is OFF. Dismiss the prompt manually; no item input sent.";
                return true;
            }
            repelPolicy.Observe(bounds,clock.Elapsed.TotalSeconds);
            if(!repelPolicy.Active) {
                if(wasActive && requests>0)Activity("REPEL: renewal dialog closed; visual world check resumes");
                return false;
            }
            controller.Pause(true,clock.Elapsed.TotalSeconds);ReleaseEscape();
            visualDecision=scene.Observe(ChromeKind.Transition,clock.Elapsed.TotalSeconds);lastChrome=ChromeKind.Transition;
            ResetControls();stableName="";stableCount=0;
            if(chrome.Kind==ChromeKind.Battle) {StopAll("Stopped — Repel prompt overlaps a battle; check manually");return true;}
            if(repelPolicy.Expired(clock.Elapsed.TotalSeconds)) {StopAll("Stopped — Repel prompt was not confirmed or did not close; check stock manually");return true;}
            encounterStatus.Text="Repel prompt detected — movement paused; verifying renewal.";
            if(!wasActive)Activity("REPEL: possible renewal dialog; movement released");
            if(!scanning && repelPolicy.NeedsRead(clock.Elapsed.TotalSeconds))ScanRepel(repelPolicy.Bounds);
            return true;
        }
        internal static string RepelDisabledTest() {
            using(var app=new App())using(var frame=new Bitmap(640,480))using(var g=Graphics.FromImage(frame)) {
                app.timer.Stop();app.continueRepel.Checked=false;app.controller.Start(new Settings {UntilStopped=true},0);
                g.Clear(Color.FromArgb(32,78,45));using(var title=new SolidBrush(Color.FromArgb(78,74,82)))using(var body=new SolidBrush(Color.FromArgb(40,35,50))) {g.FillRectangle(title,190,170,215,25);g.FillRectangle(body,190,195,215,73);g.FillRectangle(Brushes.Red,286,268,60,22);g.FillRectangle(Brushes.LimeGreen,346,268,60,22);}g.Flush();
                if(!app.WatchRepel(frame,new ChromeReading {Kind=ChromeKind.Clear}) || app.visualDecision!=VisualDecision.Pause || app.repelPolicy.Requests!=0 || app.scanning)throw new Exception("Unchecked Repel must pause on the dialog without scheduling OCR or item input");
                app.StopAll("Test complete");
            }
            return "PASS: unchecked Repel releases movement and pauses on renewal dialog without scheduling item input";
        }
        async void ScanRepel(Rectangle bounds) {
            scanning=true;int run=generation,epoch=repelPolicy.Epoch;
            double capturedAt=clock.Elapsed.TotalSeconds;
            repelPolicy.ReadingStarted(capturedAt);
            try {
                RepelPromptReading reading;string signature;
                using(var image=Native.CaptureClient(output.Target)) {
                    if(repelVisual.Find(image)!=bounds)return;
                    signature=VerifiedHeader.Signature(image,RepelPromptVisual.IdentityArea(bounds));repelReads++;
                    reading=await LocalOcr.ReadRepel(image,bounds);
                    lastScreenText="[Repel prompt]\n"+LocalOcr.LastRepelText;
                }
                if(closing || run!=generation || !controller.Running || epoch!=repelPolicy.Epoch || !repelPolicy.Visible || !continueRepel.Checked)return;
                double now=clock.Elapsed.TotalSeconds;
                if(now-capturedAt>1 || !output.Ready() || output.Busy()) {repelPolicy.Confirm(null,now);return;}
                if(!repelPolicy.Confirm(reading,now))return;
                using(var fresh=Native.CaptureClient(output.Target)) {
                    if(visual.Detect(fresh).Kind==ChromeKind.Battle || repelVisual.Find(fresh)!=bounds || VerifiedHeader.Signature(fresh,RepelPromptVisual.IdentityArea(bounds))!=signature) {Activity("REPEL: fresh dialog pixels changed; waiting for a stable read");repelPolicy.Confirm(null,clock.Elapsed.TotalSeconds);return;}
                }
                if(!continueRepel.Checked || !controller.Running || run!=generation || epoch!=repelPolicy.Epoch || !output.Ready() || output.Busy() || clock.Elapsed.TotalSeconds-capturedAt>1)return;
                Native.ClickClient(output.Target,output.Process,reading.Yes);
                repelPolicy.Clicked(clock.Elapsed.TotalSeconds);repelClicks++;
                Activity("REPEL: Yes requested for "+reading.Kind+"; request="+repelPolicy.Requests);
                encounterStatus.Text="Repel renewal requested — waiting for the prompt to close.";
            } catch(Exception ex) {
                if(!closing && run==generation) {
                    if(controller.Running && autoFocus.Checked && !output.Foreground())Guard(delegate {controller.Tick(clock.Elapsed.TotalSeconds);});
                    else {Guard(delegate {StopAll("Stopped — Repel renewal check failed; no further item input");});encounterStatus.Text="Repel check failed: "+ex.Message;}
                }
            } finally {scanning=false;if(!closing)RefreshState();}
        }
        void ProbeScene() {
            double began=clock.Elapsed.TotalSeconds;
            ChromeReading chrome;
            using(var image=Native.CaptureClient(output.Target)) {
                chrome=visual.Detect(image);
                if(WatchRepel(image,chrome)) {visualProbes++;visualMs+=(clock.Elapsed.TotalSeconds-began)*1000;return;}
                if(encounters.Checked && chrome.Kind==ChromeKind.Battle && (scene.InBattle || managedResumePending)) {
                    WatchControls(image,chrome);
                    if(managedMode && trackedRun.HasValue)managedReadinessObserved=true;
                }
            }
            visualProbes++;visualMs+=(clock.Elapsed.TotalSeconds-began)*1000;
            var previous=visualDecision;
            bool enteringBattle=chrome.Kind==ChromeKind.Battle && !scene.InBattle;
            if(chrome.Kind==ChromeKind.Clear && lastChrome!=ChromeKind.Clear)clearStarted=clock.Elapsed.TotalSeconds;
            lastChrome=chrome.Kind;
            visualDecision=scene.Observe(chrome.Kind,clock.Elapsed.TotalSeconds);
            if(enteringBattle) {
                if(!managedMode || !managedBattleContinuity) {visualBattles++;battleStarted=clock.Elapsed.TotalSeconds;firstEscapeLogged=false;}
                managedBattleContinuity=true;
                stableCount=0;stableName="";if(!managedResumePending)readGrace.Reset();nextScan=0;
                if(!managedResumePending)ResetControls();managedResumePending=false;
                Activity("VISUAL: battle panel appeared; "+(encounters.Checked ? "name OCR enabled" : "movement paused; encounter handling off"));
            }
            if(visualDecision==VisualDecision.Move) {
                managedResumePending=managedBattleContinuity=false;
                bool resume=controller.Paused;
                controller.Pause(false,clock.Elapsed.TotalSeconds);
                if(resume) {
                    visualResumes++;escapeAttempts=0;stableCount=0;stableName="";
                    ResetControls();
                    Activity("RESUME: visual world confirmed in "+Math.Round((clock.Elapsed.TotalSeconds-clearStarted)*1000)+"ms; no exit OCR");
                    controller.Tick(clock.Elapsed.TotalSeconds);
                }
                encounterStatus.Text="Moving — visual watcher armed; encounter OCR idle."+(continueRepel.Checked ? " Continue Repel on." : "");
            } else {
                controller.Pause(true,clock.Elapsed.TotalSeconds);
                if(visualDecision==VisualDecision.ValidateWorld)encounterStatus.Text="Starting/refocusing — validating the world once.";
                else if(visualDecision==VisualDecision.Pause)encounterStatus.Text="Scene transition — input paused; OCR idle.";
            }
            if(previous!=visualDecision && !enteringBattle)Activity("VISUAL: "+visualDecision);
        }
        void RefreshState() {
            state.Text=previewPending ? "Reading in " + Math.Max(0,Math.Ceiling(previewAt-clock.Elapsed.TotalSeconds)) + "s — switch to PRO (no keys)" : pendingStart ? "Starting in " + Math.Max(0,Math.Ceiling(captureAt-clock.Elapsed.TotalSeconds)) + "s — switch to selected window" : controller.Status;
            if(lastError!=null && !controller.Running && !pendingStart && !previewPending) state.Text="Stopped — " + lastError;
            state.ForeColor=controller.Latched ? Color.Firebrick : Color.DarkSlateGray;
            detail.Text=managedMode ? (managedArmed ? (output.LeaseGranted ? "Shared input — this window has focus" : "Shared input — waiting for this window's turn") : "Two-window profile — input starts only from the shared controls") : hotkeys ? (output.External ? (autoFocus.Checked ? "EXTERNAL INPUT — auto-return requested; Windows may refuse" : "EXTERNAL INPUT — focus loss stops the run") : "PRACTICE — no Windows key events") : "F8/F10 busy — close another controller or shortcut app. Retrying automatically.";
            bool edit=!managedArmed && !controller.Running && !controller.Held.HasValue && !pendingStart && !escapeHeld && !previewPending && !scanning;
            foreach(Control c in new Control[] {first,second,hold,gap,external,capture,targets,untilStop,background,autoFocus,targetNames}) c.Enabled=edit;
            continueRepel.Enabled=edit && !background.Checked;
            encounters.Enabled=edit && !background.Checked;
            clickFallback.Enabled=edit && !background.Checked;
            autoCatch.Enabled=edit && !background.Checked;
            catchSettings.Enabled=edit;
            checkCatch.Enabled=edit && !managedMode;
            alerts.Enabled=!background.Checked;
            screenReading.Enabled=edit && lastScreenText.Length>0;
            checkEncounter.Enabled=edit && !managedMode;
            limit.Enabled=edit && !untilStop.Checked;
            toggle.Text=previewPending ? "Cancel reading (F8)" : pendingStart ? "Cancel start (F8)" : controller.Running ? "Stop (F8)" : "Start (F8)";
            reset.Enabled=controller.Latched; toggle.Enabled=!controller.Latched || controller.Running || pendingStart;
            if(lastStatus!=controller.Status) { log.Items.Insert(0,DateTime.Now.ToString("HH:mm:ss") + "  " + controller.Status); lastStatus=controller.Status; }
            string activityStatus=controller.Status.StartsWith("Running",StringComparison.Ordinal) ? "Running — movement active" : controller.Status;
            if(activityStatus!=lastLoggedStatus) { Activity(activityStatus); lastLoggedStatus=activityStatus; }
            while(log.Items.Count>40) log.Items.RemoveAt(log.Items.Count-1);
        }
        async void ScanEncounter(bool previewOnly=false) {
            scanning=true; int run=generation, visualEpoch=scene.Epoch; double capturedAt=clock.Elapsed.TotalSeconds;
            nextScan=capturedAt+.08;
            try {
                string text;RunButtonCandidate candidate=null;ChromeReading frameChrome=null;
                int frameWidth=0,frameHeight=0;bool capturedReady=false;string headerSignature=null;Rectangle? headingArea=null;ChromeReading previewScene=null;
                using(var image=Native.CaptureClient(output.Target)) {
                    if(previewOnly) {previewScene=visual.Detect(image);frameWidth=image.Width;frameHeight=image.Height;text=await LocalOcr.ReadGame(image,true);}
                    else if(visualDecision==VisualDecision.ReadBattle) {
                        var chrome=visual.Detect(image);
                        frameChrome=chrome;frameWidth=image.Width;frameHeight=image.Height;
                        if(chrome.Kind!=ChromeKind.Battle)return;
                        battleOcrCalls++;
                        bool reuse=SameControls(image,chrome);
                        candidate=await LocalOcr.ReadBattle(image,chrome.Header,stableCount>=1 && !reuse,chrome.BattleBox.HasValue ? (Rectangle?)BattleRegions.Menu(chrome,image.Size) : null);
                        headingArea=candidate.IdentityBounds ?? chrome.Header;
                        headerSignature=VerifiedHeader.Signature(image,headingArea.Value);
                        if(reuse) {candidate.ClientPoint=trackedRun;candidate.ItemsPoint=trackedItems;}
                        if(candidate.ClientPoint.HasValue && candidate.ItemsPoint.HasValue)capturedReady=BattleControlReady.Read(image,candidate.ClientPoint.Value,candidate.ItemsPoint.Value)==ControlReady.Ready;
                        text="[Encounter header]\n"+candidate.Header+"\n[Battle controls]\n"+candidate.Controls+"\n[Full screen]\n";
                } else return;
                }
                if(closing || run!=generation || (!previewOnly && !controller.Running)) return;
                if(!previewOnly && (visualEpoch!=scene.Epoch || (candidate!=null && lastChrome!=ChromeKind.Battle)))return;
                if(clock.Elapsed.TotalSeconds-capturedAt>2.5) { StopAll("Stopped — recognition too slow; check manually"); return; }
                if(!output.Foreground() || !output.Available() || (!previewOnly && !output.Ready())) {
                    if(autoFocus.Checked && !output.Foreground()) controller.Tick(clock.Elapsed.TotalSeconds);
                    else StopAll("Stopped — target lost focus during reading");
                    return;
                }
                if(candidate!=null && candidate.ClientPoint.HasValue && candidate.ItemsPoint.HasValue && !trackedRun.HasValue) {
                    trackedRun=candidate.ClientPoint;trackedItems=candidate.ItemsPoint;trackedWidth=frameWidth;trackedHeight=frameHeight;trackedGoldX=frameChrome.GoldX;trackedHeaderY=frameChrome.Header.Top;
                    using(var fresh=Native.CaptureClient(output.Target)) {
                        var current=visual.Detect(fresh);
                        if(current.Kind!=ChromeKind.Battle || !SameControls(fresh,current)) {ResetControls();return;}
                        WatchControls(fresh,current);
                    }
                }
                lastScreenText=text;
                var reading=EncounterRecognition.Parse(text);
                if(candidate!=null) {reading.Battle=true;reading.Overworld=false;}
                string summary="Reading: "+(reading.Battle ? "battle" : reading.Overworld ? "overworld" : "unclear")+"; species="+(reading.Name ?? "unknown")+"; buttons="+reading.Commands;
                if(summary!=lastReadingSummary) { Activity(summary+"; OCR="+Math.Round((clock.Elapsed.TotalSeconds-capturedAt)*1000)+"ms"); lastReadingSummary=summary; }
                if(candidate!=null && reading.Name==null)Activity("UNREADABLE HEADER: "+candidate.Header.Replace('\n',' ')+"; area="+frameChrome.Header+"; box="+frameChrome.BattleBox);
                if(previewOnly) {
                    string sceneCheck="scene="+previewScene.Kind+"; HUD rows="+previewScene.HudRows+"; sprites="+previewScene.ColorRows+"; client="+frameWidth+"x"+frameHeight;
                    Activity("SCENE CHECK: "+sceneCheck+"; "+visual.HudDiagnostic);
                    encounterStatus.Text="Read-only: " + sceneCheck + "; opponent: " + (reading.Name ?? "unreadable") + ". No keys sent.";
                    log.Items.Insert(0,encounterStatus.Text);
                } else ApplyReading(reading,candidate,capturedAt,capturedReady,headerSignature,headingArea);
            } catch(Exception ex) {
                if(!closing && run==generation) {
                    if(controller.Running && autoFocus.Checked && !output.Foreground()) {
                        Guard(delegate { controller.Tick(clock.Elapsed.TotalSeconds); });
                        encounterStatus.Text="Reading paused while returning focus.";
                    } else {
                        Guard(delegate { StopAll("Stopped — encounter reading failed"); });
                        encounterStatus.Text="Reading failed: " + ex.Message;
                    }
                }
            } finally { scanning=false; if(!closing) RefreshState(); }
        }
        void StopForTarget(string name) {
            bool exact=desiredNames.Contains(name);
            StopAll(exact ? "TARGET FOUND — " + name : "Stopped — name resembles a target; check manually");
            if(alerts.Checked) {
                tray.Visible=true; tray.BalloonTipTitle=exact ? "Target Pokémon found" : "Check this encounter";
                tray.BalloonTipText=name+" — movement stopped. Return to PRO.";
                tray.ShowBalloonTip(10000); Native.Flash(TopLevelControl==null ? Handle : TopLevelControl.Handle); System.Media.SystemSounds.Exclamation.Play();
            }
        }
        void ApplyReading(EncounterReading reading,RunButtonCandidate candidate=null,double capturedAt=0,bool capturedReady=false,string headerSignature=null,Rectangle? headingArea=null) {
            double now=clock.Elapsed.TotalSeconds;
            if(reading.Battle) {
                controller.Pause(true,now);
                encounterStatus.Text="Battle detected — movement paused. Read: " + (reading.Name ?? "unreadable");
                if(reading.Name==null || reading.Ambiguous) {
                    stableCount=0; stableName="";
                    encounterStatus.Text="Reading opponent — movement paused; retrying automatically. No escape sent.";
                    if(readGrace.Expired(now)) StopAll("Stopped — opponent unreadable for 10 seconds; no escape sent");
                    return;
                }
                readGrace.Reset();
                if(EncounterRecognition.CloseToTarget(reading.Name,desiredNames)) {
                    if(autoCatch.Checked && desiredNames.Contains(reading.Name)) {
                        if(reading.Name==stableName)stableCount++;else {stableName=reading.Name;stableCount=1;}
                        if(stableCount<2) {encounterStatus.Text="Target seen — confirming exact name before catching.";return;}
                        BeginCatch(reading.Name);return;
                    }
                    StopForTarget(reading.Name);
                    return;
                }
                if(reading.Name==stableName) stableCount++; else { stableName=reading.Name; stableCount=1; }
                if(stableCount<2) { waitSince=now; return; }
                if(headerSignature!=null && now-capturedAt<=1) {verifiedHeaderSignature=headerSignature;verifiedHeaderName=reading.Name;verifiedHeadingArea=headingArea;}
                if(trackedRun.HasValue) {
                    if(readyEscape.StopRequired(now)) {StopAll("Stopped — ready-control escape attempts exhausted");return;}
                    if(!capturedReady || !readyEscape.Due(now)) {encounterStatus.Text="Non-target confirmed — waiting for ready battle controls.";return;}
                } else if(readyEscape.KeyboardLimit(now)) {StopAll("Stopped — keyboard escape requests were not confirmed");return;}
                // Two exact cropped VS/Wild species readings identify the encounter.
                // Small icon labels can be unreadable even while the run shortcut works.
                bool shiftDue=!escapeHeld && (trackedRun.HasValue ? readyEscape.Due(now) : readyEscape.KeyboardDue(now));
                if(!shiftDue)return;
                if(!output.Ready()) { StopAll("Stopped — target lost focus"); return; }
                if(output.Busy())return;
                if(candidate!=null && clickFallback.Checked && candidate.ClientPoint.HasValue && now-capturedAt<=1 && RunButtonMatch.AllowsOpponent(stableName,reading,desiredNames)) {
                    ClickReadyButton(reading.Name,candidate.ClientPoint.Value,false);return;
                }
                if(!shiftDue) return;
                runKey.Press(now);
                readyEscape.Submitted(now);
                LogFirstEscape();
                Activity("RUN: Right Shift requested for "+reading.Name);
                escapeAttempts++; waitSince=now;
                encounterStatus.Text="Run requested for " + reading.Name + " (Right Shift). Waiting for overworld confirmation.";
            } else {
                controller.Pause(true,now);
                encounterStatus.Text="Battle reading unavailable — movement paused; visual watcher remains active.";
            }
        }
        void LogFirstEscape() {
            if(firstEscapeLogged)return;firstEscapeLogged=true;
            Activity("FIRST_ESCAPE: "+Math.Round((clock.Elapsed.TotalSeconds-battleStarted)*1000)+"ms after visual battle detection");
        }
        public string SmokeReport() {
            return "First=" + first.SelectedItem + "; Second=" + second.SelectedItem + "; GlobalHotkeys=" + hotkeys + "; StartEnabled=" + toggle.Enabled + "; ContinueRepel="+continueRepel.Checked+"; AutoCatch="+autoCatch.Checked+"; CatchSettings="+catchSettings.Enabled+"; Status=" + detail.Text;
        }
    }
    static class Program {
        [STAThread] static int Main(string[] args) {
            Native.SetProcessDPIAware();
            if(args.Length>0 && args[0]=="--self-test") return Tests.Run(args.Length>1 ? args[1] : "test-results.txt");
            if(args.Length>1 && args[0]=="--background-test") return Tests.BackgroundTest(args[1]);
            if(args.Length>1 && args[0]=="--ocr-test") return Tests.OcrTest(args[1]);
            if(args.Length>2 && args[0]=="--repel-image-test") return Tests.RepelImageTest(args[1],args[2],args.Length==4 && args[3]=="--expect-reject");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>1 && args[0]=="--catch-ui-smoke") {
                using(var dialog=new CatchSettingsDialog(new CatchConfig(),"mimikyu, ralts")) {dialog.Show();Application.DoEvents();using(var bitmap=new Bitmap(dialog.Width,dialog.Height)) {dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}dialog.Close();}
                return 0;
            }
            if(args.Length>1 && args[0]=="--ui-smoke") {
                using(var app=new App()) {
                    app.Show(); Application.DoEvents();
                    File.WriteAllText(args[1]+".txt", app.SmokeReport());
                    using(var bitmap=new Bitmap(app.Width,app.Height)) { app.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(args[1]); }
                    app.Close();
                }
                return 0;
            }
            if(args.Length>1 && args[0]=="--dual-ui-smoke") {
                using(var dual=new DualApp()) {dual.Show();Application.DoEvents();File.WriteAllText(args[1]+".txt",dual.SmokeReport());using(var bitmap=new Bitmap(dual.Width,dual.Height)) {dual.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}dual.Close();}
                return 0;
            }
            bool firstInstance;
            using(var instance=new System.Threading.Mutex(true,@"Local\MovementPrototype.Controller",out firstInstance)) {
                if(!firstInstance) {
                    if(Native.ActivateMessage!=0) Native.PostMessage(new IntPtr(0xffff),Native.ActivateMessage,IntPtr.Zero,IntPtr.Zero);
                    return 0;
                }
                try {
                    if(args.Length>0 && (args[0]=="--dual" || args[0]=="--prepare-dual")) {
                        var dual=new DualApp();
                        if(args[0]=="--prepare-dual") {if(args.Length!=5)throw new ArgumentException("Use --prepare-dual <target1> <target2> <process1> <process2>");dual.Prepare(args[1],args[2],UInt32.Parse(args[3]),UInt32.Parse(args[4]));}
                        Application.Run(dual);
                    } else {
                        var app=new App();
                        if(args.Length>=2 && args[0]=="--prepare-pro") app.PreparePro(args[1],args.Length==3 && args[2]=="--continue-repel");
                        Application.Run(app);
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
            return 0;
        }
    }
    static class Tests {
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public Native.POINT point; public uint privateData; }
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG message,IntPtr window,uint min,uint max,uint remove);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG message);
        static void Drain(Receiver target,Receiver decoy) {
            foreach(var receiver in new Receiver[] {target,decoy}) {
                MSG message;
                while(PeekMessage(out message,receiver.Handle,0x100,0x109,1)) DispatchMessage(ref message);
            }
        }
        sealed class Receiver : NativeWindow, IDisposable {
            public List<string> Events=new List<string>();
            public List<uint> Flags=new List<uint>();
            public Receiver(string title) { CreateHandle(new CreateParams { Caption=title }); }
            protected override void WndProc(ref Message m) {
                if(m.Msg==0x100 || m.Msg==0x101) {
                    Events.Add((m.Msg==0x100 ? "down:" : "up:") + (Keys)m.WParam.ToInt32());
                    Flags.Add(unchecked((uint)m.LParam.ToInt64()));
                }
                base.WndProc(ref m);
            }
            public void Dispose() { DestroyHandle(); }
        }
        sealed class Fake : IOutput {
            public bool Focus=true, FailDown, FailUp;
            public List<string> Events=new List<string>();
            public bool Ready() { return Focus; }
            public void Down(Keys k) { Events.Add("down:"+k); if(FailDown) throw new Exception("send failed"); }
            public void Up(Keys k) { Events.Add("up:"+k); if(FailUp) throw new Exception("release failed"); }
        }
        sealed class FakeFocus : IFocusOutput {
            public bool Focus=true, Exists=true, InputBusy, Grant=true;
            public int Requests;
            public List<string> Events=new List<string>();
            public bool Ready() { return Exists && Focus && !InputBusy; }
            public bool Available() { return Exists; }
            public bool Foreground() { return Focus; }
            public bool Busy() { return InputBusy; }
            public void RequestFocus() { Requests++; Events.Add("request"); if(Grant) Focus=true; }
            public void Down(Keys key) { Check(Ready(),"No key-down without confirmed focus"); Events.Add("down:"+key); }
            public void Up(Keys key) { Events.Add("up:"+key); }
        }
        static List<string> PacingIntegrationTests() {
            var f=new Fake(); var c=new Controller(f); var p=new EncounterPacing();
            c.Start(new Settings {UntilStopped=true,Hold=1,Gap=.15},0,true);
            c.Pause(p.PauseFor(false,true,.1),.1); c.Tick(.1);
            Check(f.Events.Count==0,"Startup waits for fresh world confirmation");
            c.Pause(p.PauseFor(false,true,.35),.35); c.Tick(.35);
            Check(c.Held==Keys.Left && f.Events.Count==1,"Confirmed world starts movement without extra delay");
            c.Pause(p.PauseFor(false,false,.6),.6); c.Tick(.6);
            Check(!c.Paused && c.Held==Keys.Left && f.Events.Count==1,"A brief OCR miss must not release the held movement key");
            c.Pause(p.PauseFor(true,false,.7),.7); c.Tick(.7);
            Check(c.Paused && c.Held==null && f.Events[1]=="up:Left","Battle evidence releases movement immediately");
            c.Pause(p.PauseFor(false,false,1),1); c.Tick(1);
            Check(c.Paused && f.Events.Count==2,"Battle exit cannot reuse pre-battle world confidence");
            c.Pause(p.PauseFor(false,true,2),2); c.Tick(2);
            c.Pause(p.PauseFor(false,false,2.1),2.1); c.Tick(2.1);
            Check(f.Events.Count==2,"One post-battle world reading sends no movement");
            c.Pause(p.PauseFor(false,true,2.25),2.25); c.Tick(2.25);
            Check(!c.Paused && c.Held==Keys.Left && f.Events.Count==3,"Movement resumes after the second recent world reading");
            c.Emergency(); c.Tick(9);
            Check(c.Latched && !c.Running && c.Held==null && f.Events.Count==4,"Emergency stop still releases and prevents further movement");
            return new List<string> { "PASS: real controller preserves held movement through OCR misses, releases on battle, resumes promptly after escape, and emergency-stops" };
        }
        static List<string> RecoveryIntegrationTests() {
            var results=new List<string>();
            var f=new FakeFocus(); var c=new Controller(f);
            var s=new Settings {AutoFocus=true,Hold=0.5,Gap=0.1,Limit=2};
            c.Start(s,0); f.Focus=false; c.Tick(.1);
            Check(c.Running && c.RecoveringFocus && c.Held==null && f.Requests==0,"focus loss releases before recovery");
            c.Tick(.46); Check(f.Requests==1 && f.Events[1]=="up:Left" && f.Events[2]=="request","release precedes request");
            c.Tick(.47); c.Tick(.63); Check(c.Running && !c.RecoveringFocus && c.Held==null,"stable focus recovered without immediate key-down");
            c.Tick(.74); Check(c.Held==Keys.Left,"movement resumes only after restored focus and released gap");
            c.Tick(2); Check(!c.Running && c.Held==null,"time limit not reset by focus recovery");
            results.Add("PASS: auto-return releases before requesting focus, waits for stable focus and a gap, and preserves the original time limit");
            f=new FakeFocus(); c=new Controller(f); c.Start(new Settings {AutoFocus=true,UntilStopped=true},0);
            f.Focus=false; c.Tick(.1); c.Emergency(); c.Tick(10);
            Check(c.Latched && !c.Running && !c.RecoveringFocus && f.Requests==0 && c.Held==null,"emergency cancels queued focus recovery");
            results.Add("PASS: emergency stop cancels focus recovery and prevents later activation attempts");
            f=new FakeFocus {Grant=false}; c=new Controller(f); c.Start(new Settings {AutoFocus=true,UntilStopped=true},0);
            f.Focus=false; c.Tick(.1); c.Tick(.46); c.Tick(.97); c.Tick(1.48); c.Tick(2.7);
            Check(f.Requests==3 && !c.Running && c.Held==null,"blocked focus bounded and no movement resumed");
            results.Add("PASS: denied focus stops after bounded retries without sending another key-down");
            f=new FakeFocus(); c=new Controller(f); c.Start(new Settings {AutoFocus=true,UntilStopped=true},0);
            f.Exists=false; f.Focus=false; c.Tick(.1);
            Check(!c.Running && f.Requests==0 && c.Held==null,"closed or minimized target stops without refocus");
            results.Add("PASS: unavailable targets stop without restoration or retry");
            f=new FakeFocus(); c=new Controller(f); c.Start(new Settings {AutoFocus=true,UntilStopped=true},0);
            f.InputBusy=true; c.Tick(.1);
            Check(!c.Running && f.Requests==0 && c.Held==null,"foreground modifier safety preserved");
            f=new FakeFocus(); c=new Controller(f); c.Start(new Settings {AutoFocus=true,UntilStopped=true},0,true);
            f.Focus=false; c.Tick(.1); c.Tick(.46); c.Tick(.47); c.Tick(.63); c.Tick(.8);
            Check(c.Running && c.Paused && c.Held==null,"encounter pause survives focus recovery");
            c.Stop("test complete");
            results.Add("PASS: modifier safety and encounter pause remain active with auto-return enabled");
            return results;
        }
        static void Check(bool ok, string label) { if(!ok) throw new Exception(label); }
        public static int BackgroundTest(string path) {
            var lines=new List<string>();
            try {
                IntPtr before=Native.GetForegroundWindow();
                using(var target=new Receiver("Movement controller local receiver")) using(var decoy=new Receiver("Movement controller decoy")) {
                    var output=new Output { External=true,Background=true,Target=target.Handle,Process=(uint)Process.GetCurrentProcess().Id };
                    Check(output.Ready(),"background ready without foreground focus");
                    var c=new Controller(output); var s=new Settings {Hold=0.5,Gap=0.1,UntilStopped=true};
                    c.Start(s,0); Drain(target,decoy); c.Tick(0.5); Drain(target,decoy); c.Tick(0.6); Drain(target,decoy); c.Emergency(); Drain(target,decoy);
                    Check(String.Join(",",target.Events)=="down:Left,up:Left,down:Right,up:Right","target-only alternating movement and emergency release; observed: " + String.Join(",",target.Events));
                    Check(decoy.Events.Count==0,"decoy must not receive messages");
                    Check(Native.GetForegroundWindow()==before,"background test must not steal focus");
                    Check((target.Flags[0] & 0x01000000u)!=0 && (target.Flags[1] & 0xC1000000u)==0xC1000000u,"scan and release flags");
                    lines.Add("PASS: background alternation and emergency key-up reach only the selected local receiver; focus unchanged");
                    output.Background=false; Check(!output.Ready(),"foreground mode still rejects inactive target"); output.Background=true;
                    c.Reset(); c.Start(s,1); Drain(target,decoy); output.Process=0; c.Tick(1.1); Drain(target,decoy);
                    Check(!c.Running && c.Held==null && target.Events.Count==5,"changed target identity stops without sending to reused handle");
                    lines.Add("PASS: foreground safety is preserved; mismatched target identity stops background output");
                    target.Dispose(); bool rejected=false; try { Native.PostKey(output.Target,(uint)Process.GetCurrentProcess().Id,Keys.Left,false); } catch(InvalidOperationException) { rejected=true; }
                    Check(rejected,"closed target must reject key-down");
                    lines.Add("PASS: closed target rejects background input");
                }
                lines.Add("These tests do not establish PRO compatibility. No game received input.");
                File.WriteAllLines(path,lines); return 0;
            } catch(Exception ex) { lines.Add("FAIL: "+ex); File.WriteAllLines(path,lines); return 1; }
        }
        public static int Run(string path) {
            var results=new List<string>();
            try {
                var f=new Fake(); var c=new Controller(f); var s=new Settings { Hold=0.5, Gap=0.1, Limit=2 };
                c.Start(s,0); Check(c.Held==Keys.Left,"first key"); c.Tick(0.5); Check(c.Held==null,"release before gap"); c.Tick(0.6); Check(c.Held==Keys.Right,"reverse"); c.Tick(1.1); c.Tick(1.21); Check(c.Held==Keys.Left,"repeat");
                results.Add("PASS: alternating keys with release and gap");
                c.Tick(2); Check(!c.Running && c.Held==null,"limit"); results.Add("PASS: run limit releases held key");
                c.Start(s,3); f.Focus=false; c.Tick(3.01); Check(!c.Running && c.Held==null,"focus"); results.Add("PASS: focus loss stops and releases");
                bool rejected=false; try { c.Start(s,4); } catch(InvalidOperationException) { rejected=true; } Check(rejected && !c.Latched && !c.Running,"focus setup rejection must not latch emergency");
                f.Focus=true; c.Start(s,4); c.Emergency(); rejected=false; try { c.Start(s,5); } catch(InvalidOperationException) { rejected=true; }
                Check(rejected && c.Held==null,"emergency latch"); c.Reset(); c.Start(s,6); c.Stop("test"); Check(!c.Running && c.Held==null,"stop"); results.Add("PASS: emergency latch, explicit reset, ordinary stop");
                f.FailDown=true; try { c.Start(s,7); } catch(Exception) { } Check(c.Latched && !c.Running && c.Held==null,"down failure cleanup"); results.Add("PASS: failed key-down triggers release and latches stop");
                f.FailDown=false; c.Reset(); c.Start(s,8); f.FailUp=true; try { c.Emergency(); } catch(Exception) { } Check(c.Held.HasValue && c.Latched && !c.Running,"retain release"); f.FailUp=false; c.Reset(); Check(c.Held==null,"retry release"); results.Add("PASS: failed key-up retains pending key for retry");
                foreach(double bad in new double[] {Double.NaN, Double.PositiveInfinity, 0, 6}) { rejected=false; try { new Settings {Hold=bad}.Validate(); } catch(ArgumentException) {rejected=true;} Check(rejected,"invalid duration"); }
                rejected=false; try { new Settings {First=Keys.F10}.Validate(); } catch(ArgumentException) {rejected=true;} Check(rejected,"reserved key"); results.Add("PASS: invalid duration and non-movement key rejection");
                f=new Fake(); c=new Controller(f); c.Start(s,0); c.Tick(20); Check(f.Events.Count==2 && !c.Running,"delayed tick"); results.Add("PASS: delayed tick stops without replaying inputs");
                f=new Fake(); c=new Controller(f); c.Start(new Settings {UntilStopped=true},0); c.Tick(86400); Check(c.Running,"unlimited beyond one day"); c.Tick(86401); c.Stop("manual"); Check(!c.Running && c.Held==null,"unlimited manual stop"); results.Add("PASS: unlimited mode survives elapsed day and stops manually");
                f=new Fake(); c=new Controller(f); c.Start(new Settings {UntilStopped=true},0,true); Check(f.Events.Count==0 && c.Paused,"start paused sends no movement"); c.Pause(false,1); c.Tick(1); Check(c.Held.HasValue,"resume"); c.Pause(true,1.1); Check(c.Held==null,"pause releases"); c.Emergency(); Check(!c.Running && c.Latched,"paused emergency"); results.Add("PASS: encounter checking starts without a key and pause releases movement");
                f=new Fake(); c=new Controller(f); c.Start(new Settings {UntilStopped=true},0); f.Focus=false; c.Tick(500); Check(!c.Running && c.Held==null,"unlimited focus loss"); results.Add("PASS: focus-loss safety remains active in unlimited mode");
                var desired=EncounterRecognition.Targets("Pikachu, Ralts");
                var parsed=EncounterRecognition.Parse("Advay0 VS. Wild Seedot\nFight\nItems\nLocal Trade");
                Check(parsed.Battle && parsed.Commands && !parsed.Overworld && parsed.Name=="seedot","wild header");
                Check(!EncounterRecognition.CloseToTarget("seedot",desired) && EncounterRecognition.CloseToTarget("pikachu",desired) && EncounterRecognition.CloseToTarget("pikach",desired),"target preservation");
                Check(EncounterRecognition.Parse("VS. Wild P1kachu\nFight\nItems").Name==null,"unreadable species");
                Check(EncounterRecognition.Parse("VS. Wild Pikachu\nVS. Wild Ralts\nFight Items").Ambiguous,"conflicting names");
                Check(!EncounterRecognition.Parse("Fight Items Local Trade").Overworld,"battle priority");
                Check(EncounterRecognition.Parse("Local Trade Guild").Overworld,"world marker");
                rejected=false; try { EncounterRecognition.Targets(""); } catch(ArgumentException) {rejected=true;} Check(rejected,"empty target");
                results.Add("PASS: exact species parsing, target preservation, near-target guard, ambiguous name rejection, and screen classification");
                parsed=EncounterRecognition.Parse("[Encounter header]\nAdvay0 VS.\nWild Seedot\n[Battle controls]\nFight\nItems\n[Full screen]\nLocal Trade");
                Check(parsed.Battle && parsed.Commands && parsed.Name=="seedot" && !parsed.Overworld,"split cropped battle header");
                parsed=EncounterRecognition.Parse("[Encounter header]\nVS.\nWild S33dot\n[Battle controls]\n\n[Full screen]\nLocal Trade");
                Check(parsed.Battle && parsed.Name==null && !parsed.Overworld,"unclear battle must not become overworld");
                parsed=EncounterRecognition.Parse("[Encounter header]\n\n[Battle controls]\n\n[Full screen]\nLocal Trade\nVS. Wild Seedot Fight Items");
                Check(!parsed.Battle && parsed.Name==null && !parsed.Commands,"chat cannot authorize flee");
                results.Add("PASS: cropped split headers parse; unclear battle pauses; full-screen chat cannot authorize fleeing");
                parsed=EncounterRecognition.Parse("[Encounter header]\nAdvayO VS. Wild Seedot\n[Battle controls]\ntems\nRun\n[Full screen]\nLocal Trade");
                Check(parsed.Battle && parsed.Commands && parsed.Name=="seedot","live Seedot OCR and Run/Items pair");
                parsed=EncounterRecognition.Parse("[Encounter header]\nVS. Wild Mimikyu\n[Battle controls]\ntems Run\n[Full screen]\nLocal Trade");
                Check(parsed.Commands && EncounterRecognition.CloseToTarget(parsed.Name,EncounterRecognition.Targets("Mimikyu")),"target protected with Run button recognition");
                results.Add("PASS: observed Seedot Run/Items OCR is accepted only in button crop; target encounters remain protected");
                var grace=new EncounterReadGrace();
                Check(!grace.Expired(100) && !grace.Expired(109.99) && grace.Expired(110),"unreadable encounter gets ten seconds of retries");
                grace.Reset(); Check(!grace.Expired(200) && !grace.Expired(209),"readable encounter resets retry deadline");
                results.Add("PASS: transient unreadable encounters can recover; persistent unreadability reaches a bounded deadline");
                parsed=EncounterRecognition.Parse("[Encounter header]\nAdvayO VS. Wild Sneasel\nNearbyPlayer\n[Battle controls]\nItems Run\n[Full screen]\nLocal Trade");
                Check(parsed.Name=="sneasel" && parsed.Commands,"unrelated player line must not become part of species");
                parsed=EncounterRecognition.Parse("[Encounter header]\nAdvayO VS.\nWild Sneasel\nNearbyPlayer\n[Battle controls]\nItems Run\n[Full screen]\nLocal Trade");
                Check(parsed.Name=="sneasel" && parsed.Commands,"join only split battle heading");
                results.Add("PASS: live Sneasel heading excludes nearby player lines, including split VS/Wild text");
                parsed=EncounterRecognition.Parse("[Encounter header]\nAdvayO VS\n26\n• Wild Seedot\n[Battle controls]\nPokemon\n[Full screen]\nLocal Trade");
                Check(parsed.Battle && parsed.Name=="seedot" && !parsed.Commands,"level text between VS and Wild must not hide species");
                parsed=EncounterRecognition.Parse("[Encounter header]\n26\nWild Seedot\n[Battle controls]\nItems Run\n[Full screen]\nLocal Trade");
                Check(parsed.Name==null,"standalone Wild label without VS must not authorize flee");
                results.Add("PASS: cropped VS/Wild identity survives intervening level text; missing VS cannot confirm species");
                results.AddRange(FocusRecoveryTests.Run());
                results.AddRange(RecoveryIntegrationTests());
                results.AddRange(RunAwayKeyTests.Run());
                results.AddRange(EncounterPacingTests.Run());
                results.AddRange(PacingIntegrationTests());
                results.AddRange(RunButtonMatch.Tests());
                results.AddRange(VisualSceneGate.Tests());
                results.AddRange(ReadyEscapePolicy.Tests());
                results.AddRange(DualScheduler.Tests());
                results.AddRange(CatchTests.Run());
                var leaseOutput=new Output(delegate(int key) {return false;}) {LeaseRequired=true};
                bool leaseRejected=false;try {leaseOutput.Down(Keys.Left);}catch(InvalidOperationException) {leaseRejected=true;}
                Check(leaseRejected && !leaseOutput.Ready() && !leaseOutput.PracticeKey.HasValue,"Inactive shared profile must not send movement");
                leaseOutput.LeaseGranted=true;leaseOutput.Down(Keys.Left);leaseOutput.LeaseGranted=false;leaseOutput.Up(Keys.Left);
                Check(!leaseOutput.Ready() && !leaseOutput.PracticeKey.HasValue,"Revoked lease still permits cleanup, never new key-down");
                results.Add("PASS: inactive shared profiles reject key-down, while revoked leases retain key-release cleanup");
                var turnOutput=new Fake();var turnController=new Controller(turnOutput);
                turnController.Start(new Settings {UntilStopped=true,Hold=5},0,true);turnController.BeginTurn();turnController.Pause(false,0);turnController.Tick(0);
                turnController.YieldTurn(.1);turnController.BeginTurn();turnController.Pause(false,.2);turnController.Tick(.2);
                Check(turnController.Held==Keys.Right,"Short turns must alternate even when a hold is interrupted");turnController.YieldTurn(.3);
                turnController.BeginTurn();turnController.Pause(false,.4);turnController.Tick(.4);Check(turnController.Held==Keys.Left,"Interrupted shared holds must remain balanced across turns");turnController.Emergency();
                results.Add("PASS: short shared turns alternate their starting direction even when configured holds are interrupted");
                results.AddRange(RepelPromptMatch.Tests());
                results.Add(App.RepelDisabledTest());
                results.AddRange(RepelRenewalPolicy.Tests());
                bool leftShift=false, controlDown=false, rightShift=false;
                var runShift=new RunAwayKey(delegate(Keys key,bool up) { rightShift=!up; });
                var shiftOutput=new Output(delegate(int key) { return key==0x10 ? leftShift || rightShift : key==0x11 ? controlDown : key==0xA0 ? leftShift : key==0xA1 ? rightShift : false; });
                shiftOutput.RunKey=runShift;
                rightShift=true; Check(shiftOutput.ModifiersDown(),"Manual right Shift must still block movement"); rightShift=false;
                runShift.Press(0); Check(!shiftOutput.ModifiersDown() && !shiftOutput.Busy(),"Owned run Shift must not trigger self-stop");
                controlDown=true; Check(shiftOutput.ModifiersDown(),"Ctrl must still block during run Shift"); controlDown=false;
                leftShift=true; Check(shiftOutput.ModifiersDown(),"Manual left Shift must still block during run Shift"); leftShift=false;
                runShift.Release(); rightShift=true; Check(shiftOutput.ModifiersDown(),"Right Shift safety must resume after release");
                results.Add("PASS: owned Right Shift avoids self-stop; manual Right Shift, Left Shift and other modifiers retain safety checks");
                File.WriteAllLines(path,results); return 0;
            } catch(Exception ex) { results.Add("FAIL: "+ex); File.WriteAllLines(path,results); return 1; }
        }
        public static int RepelImageTest(string imagePath,string path,bool expectReject=false) {
            try {
                using(var loaded=Image.FromFile(imagePath))using(var bitmap=new Bitmap(loaded)) {
                    var area=new RepelPromptVisual().Find(bitmap);
                    Check(area.HasValue,"Repel prompt geometry in supplied image");
                    var read=LocalOcr.ReadRepel(bitmap,area.Value).GetAwaiter().GetResult();
                    if(expectReject) {
                        Check(read==null,"Obscured/ambiguous prompt must not authorize a click");
                        File.WriteAllText(path,"PASS: supplied screenshot with a cursor obscuring prompt text is detected but rejected by exact wording checks. No input sent.");
                    } else {
                        Check(read!=null && area.Value.Contains(read.Yes),"Exact Repel prompt and unique Yes button");
                        File.WriteAllText(path,"PASS: supplied Repel screenshot triggers a cropped read and exact renewal/Yes recognition. No input sent.");
                    }
                }
                return 0;
            } catch(Exception ex) {File.WriteAllText(path,"FAIL: "+ex);return 1;}
        }
        public static int OcrTest(string path) {
            try {
                using(var frame=new Bitmap(640,480))using(var g=Graphics.FromImage(frame))using(var titleFont=new Font("Segoe UI",10,FontStyle.Bold))using(var bodyFont=new Font("Segoe UI",8))using(var buttonFont=new Font("Segoe UI",8,FontStyle.Bold))using(var titleBrush=new SolidBrush(Color.FromArgb(78,74,82)))using(var bodyBrush=new SolidBrush(Color.FromArgb(40,35,50))) {
                    g.Clear(Color.FromArgb(32,78,45));g.FillRectangle(titleBrush,180,170,267,27);g.FillRectangle(bodyBrush,180,197,267,88);
                    g.FillRectangle(Brushes.Red,294,285,74,24);g.FillRectangle(Brushes.LimeGreen,368,285,74,24);
                    g.DrawString("Repel Expired",titleFont,Brushes.LightGray,245,173);g.DrawString("Would you like to use another Max Repel?",bodyFont,Brushes.White,190,225);
                    g.DrawString("No",buttonFont,Brushes.White,316,287);g.DrawString("Yes",buttonFont,Brushes.White,390,287);g.Flush();
                    var area=new RepelPromptVisual().Find(frame);Check(area.HasValue,"Synthetic renewal modal trigger");
                    var read=LocalOcr.ReadRepel(frame,area.Value).GetAwaiter().GetResult();
                    Check(read!=null && read.Kind=="Max Repel" && read.Yes.X>345 && read.Yes.Y>267,"Cropped renewal OCR finds exact wording and Yes; OCR="+LocalOcr.LastRepelText);
                }
                using(var frame=new Bitmap(640,480))using(var g=Graphics.FromImage(frame))using(var font=new Font("Segoe UI",14,FontStyle.Bold))using(var bar=new SolidBrush(Color.FromArgb(40,40,42))) {
                    g.Clear(Color.FromArgb(32,78,45));g.FillRectangle(bar,0,50,640,30);g.FillRectangle(Brushes.Black,480,80,160,240);
                    g.DrawString("Advay0",font,Brushes.White,80,52);g.DrawString("VS.",font,Brushes.Gold,205,52);g.DrawString("Wild Seedot",font,Brushes.White,250,52);
                    g.DrawString("Items",font,Brushes.White,500,350);g.DrawString("Run",font,Brushes.White,590,350);
                    var chrome=new BattleVisual().Detect(frame);
                    Check(chrome.Kind==ChromeKind.Battle,"visual battle fixture");
                    var snapshot=LocalOcr.ReadBattle(frame,chrome.Header,true).GetAwaiter().GetResult();
                    var read=EncounterRecognition.Parse("[Encounter header]\n"+snapshot.Header+"\n[Battle controls]\n"+snapshot.Controls+"\n[Full screen]\n");
                    Check(read.Name=="seedot" && snapshot.ClientPoint.HasValue,"detected thin header and direct Run OCR preserve identity");
                }
                using(var bitmap=new Bitmap(1100,260)) {
                    using(var g=Graphics.FromImage(bitmap)) using(var font=new Font("Segoe UI",26,FontStyle.Bold)) {
                        g.Clear(Color.White); g.DrawString("Advay0 VS. Wild Seedot\nFight       Items\nLocal       Trade",font,Brushes.Black,20,20);
                    }
                    string text=LocalOcr.Read(bitmap).GetAwaiter().GetResult();
                    var reading=EncounterRecognition.Parse(text);
                    Check(reading.Name=="seedot" && reading.Battle && reading.Commands,"OCR synthetic battle recognition");
                    using(var game=new Bitmap(1100,700)) {
                        using(var g=Graphics.FromImage(game)) using(var font=new Font("Segoe UI",14,FontStyle.Bold)) {
                            g.Clear(Color.Black);
                            g.DrawString("Advay0 VS. Wild Seedot",font,Brushes.White,180,70);
                            g.DrawString("Items",font,Brushes.White,830,500);
                            g.DrawString("Run",font,Brushes.White,1010,500);
                            g.DrawString("Local Trade",font,Brushes.White,20,650);
                        }
                        var cropped=EncounterRecognition.Parse(LocalOcr.ReadGame(game).GetAwaiter().GetResult());
                        Check(cropped.Battle && cropped.Commands && cropped.Name=="seedot","enlarged game-region OCR");
                        var quick=EncounterRecognition.Parse(LocalOcr.ReadGame(game,false).GetAwaiter().GetResult());
                        Check(quick.Battle && quick.Name=="seedot" && !quick.Commands,"fast two-region OCR preserves exact battle identity");
                        var button=LocalOcr.FindRunButton(game).GetAwaiter().GetResult();
                        Check(button.ClientPoint.HasValue && button.ClientPoint.Value.X>990 && button.ClientPoint.Value.Y>480 && button.Header.IndexOf("Seedot",StringComparison.OrdinalIgnoreCase)>=0,"visible Run locator and fresh header OCR");
                        using(var g=Graphics.FromImage(game)) using(var font=new Font("Segoe UI",14,FontStyle.Bold)) {
                            g.Clear(Color.Black); g.DrawString("Battles Trade All Guild Local",font,Brushes.White,520,450);
                        }
                        var world=EncounterRecognition.Parse(LocalOcr.ReadGame(game).GetAwaiter().GetResult());
                        Check(world.Overworld && !world.Battle,"enlarged world-tab OCR");
                        var quickWorld=EncounterRecognition.Parse(LocalOcr.ReadGame(game,false).GetAwaiter().GetResult());
                        Check(quickWorld.Overworld && !quickWorld.Battle,"fast two-region OCR confirms overworld");
                    }
                    File.WriteAllText(path,"PASS: Windows OCR reads an exact Max Repel renewal and its Yes button; synthetic wild header and battle commands also pass.\n"+text+"\n"+CatchTests.Ocr()); return 0;
                }
            } catch(Exception ex) { File.WriteAllText(path,"FAIL: "+ex); return 1; }
        }
    }
}
