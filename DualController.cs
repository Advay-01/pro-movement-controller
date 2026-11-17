using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MovementPrototype {
    public interface ISharedHunt {
        long Window {get;}
        bool Wanted {get;}
        bool Available {get;}
        bool Reading {get;}
        bool Waiting {get;}
        double ReadAllowance {get;}
        void Enter();
        void Leave();
        void Service();
        void End(bool emergency,string reason);
    }
    public interface ISharedDesktop {
        long Foreground {get;}
        bool Busy {get;}
        void Focus(long window);
    }
    // One lease, one foreground target, and no focus change before key release.
    public sealed class DualScheduler {
        readonly ISharedHunt[] hunts;
        readonly ISharedDesktop desktop;
        int candidate,attempts;
        double previous,began,nextRequest,readySince,sliceEnd,slice;
        double? busySince;
        bool observing;
        public int Active {get;private set;}
        public int Grants {get;private set;}
        public bool Running {get;private set;}
        public string Status {get;private set;}
        public DualScheduler(ISharedHunt[] sessions,ISharedDesktop input) {
            if(sessions==null || sessions.Length!=2 || input==null)throw new ArgumentException("Two sessions and a desktop are required.");
            hunts=sessions;desktop=input;Active=-1;Status="Ready — both stopped";
        }
        public void Start(double now,double seconds) {
            if(Double.IsNaN(now) || Double.IsInfinity(now) || Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds<.4 || seconds>5)throw new ArgumentException("Switch interval must be 0.40–5 seconds.");
            if(hunts[0].Window==0 || hunts[1].Window==0 || hunts[0].Window==hunts[1].Window)throw new ArgumentException("Select two different visible game windows.");
            foreach(var hunt in hunts)if(!hunt.Wanted || !hunt.Available)throw new InvalidOperationException("Both selected windows must be available before starting.");
            slice=seconds;previous=now;Grants=0;Running=true;Active=-1;busySince=null;Begin(0,now);
        }
        void Begin(int index,double now) {candidate=index;began=now;nextRequest=now;attempts=0;observing=false;Status="Switching to window "+(index+1);}
        int Next(int from) {for(int n=1;n<=2;n++) {int i=(from+n)%2;if(hunts[i].Wanted && hunts[i].Available)return i;}return -1;}
        void Relinquish() {if(Active<0)return;int old=Active;hunts[old].Leave();Active=-1;}
        public void Stop(bool emergency,string reason) {
            Running=false;Active=-1;observing=false;Status=reason;
            Exception failure=null;
            foreach(var hunt in hunts)try {hunt.End(emergency,reason);}catch(Exception ex) {failure=ex;}
            if(failure!=null)throw failure;
        }
        public void Step(double now) {
            if(!Running)return;
            if(Double.IsNaN(now) || Double.IsInfinity(now) || now<previous || now-previous>3) {Stop(true,"Stopped — shared controller timing was interrupted");return;}
            previous=now;
            foreach(var hunt in hunts)if(hunt.Wanted && !hunt.Available)hunt.End(false,"Stopped — selected window closed, changed or minimized");
            if(desktop.Busy) {
                Relinquish();observing=false;
                if(!busySince.HasValue)busySince=now;
                if(now-busySince.Value>=2.5)Stop(false,"Stopped — manual mouse/modifier input remained active");
                else Status="Paused — release mouse buttons and modifiers";
                return;
            }
            if(busySince.HasValue) {busySince=null;Begin(candidate,now);}
            if(Active>=0) {
                int old=Active;
                if(!hunts[old].Wanted || !hunts[old].Available) {
                    Relinquish();int next=Next(old);
                    if(next<0) {Running=false;Status="Both hunts stopped — review the profile messages";return;}
                    Begin(next,now);return;
                }
                if(desktop.Foreground!=hunts[old].Window) {Relinquish();Begin(old,now);return;}
                hunts[old].Service();
                if(!hunts[old].Wanted || (hunts[old].Waiting && !hunts[old].Reading) || (now>=sliceEnd && (!hunts[old].Reading || now>=sliceEnd+hunts[old].ReadAllowance))) {
                    int next=Next(old);
                    if(next==old && hunts[old].Wanted) {sliceEnd=now+slice;return;}
                    bool bothWaiting=next>=0 && hunts[old].Waiting && hunts[next].Waiting;
                    Relinquish();
                    if(next<0) {Running=false;Status="Both hunts stopped — review the profile messages";return;}
                    Begin(next,now);
                    if(bothWaiting)nextRequest=now+.25;
                }
                return;
            }
            if(!hunts[candidate].Wanted || !hunts[candidate].Available) {
                int next=Next(candidate);
                if(next<0) {Running=false;Status="Both hunts stopped — review the profile messages";return;}
                Begin(next,now);
            }
            if(now-began>=2.5) {Stop(false,"Stopped — Windows did not confirm the requested focus");return;}
            if(desktop.Foreground==hunts[candidate].Window) {
                if(!observing) {readySince=now;observing=true;}
                if(now-readySince>=.06) {
                    hunts[candidate].Enter();Active=candidate;Grants++;sliceEnd=now+slice;
                    Status="Window "+(candidate+1)+" active — coordinated switching";
                }
                return;
            }
            observing=false;
            if(now>=nextRequest && attempts<3) {attempts++;nextRequest=now+.40;desktop.Focus(hunts[candidate].Window);}
        }
        sealed class FakeDesktop : ISharedDesktop {
            public long Window=1;public bool Input,Allow=true;public List<string> Events=new List<string>();
            public long Foreground {get{return Window;}}public bool Busy {get{return Input;}}
            public void Focus(long window) {Events.Add("focus:"+window);if(Allow)Window=window;}
        }
        sealed class FakeHunt : ISharedHunt {
            readonly FakeDesktop desktop;public long Id;public bool Enabled=true,Exists=true,Lease,Pending=false,InBattle=false;public int Moves,Epoch;public double Allowance=.8;
            public FakeHunt(FakeDesktop d,long id) {desktop=d;Id=id;}
            public long Window {get{return Id;}}public bool Wanted {get{return Enabled;}}public bool Available {get{return Exists;}}public bool Reading {get{return Pending;}}public bool Waiting {get{return InBattle;}}
            public double ReadAllowance {get{return Allowance;}}
            public void Enter() {if(desktop.Window!=Id)throw new Exception("Lease granted without focus");Lease=true;desktop.Events.Add("enter:"+Id);}
            public void Leave() {desktop.Events.Add("release:"+Id);Lease=false;Epoch++;}
            public void Service() {if(!Lease || desktop.Window!=Id)throw new Exception("Input reached the wrong foreground window");Moves++;}
            public void End(bool emergency,string reason) {Leave();Enabled=false;}
        }
        public static List<string> Tests() {
            var d=new FakeDesktop();var a=new FakeHunt(d,1);var b=new FakeHunt(d,2);var s=new DualScheduler(new ISharedHunt[] {a,b},d);
            s.Start(0,.4);s.Step(0);if(s.Active!=-1)throw new Exception("Focus must be observed across ticks");s.Step(.07);s.Step(.1);s.Step(.49);s.Step(.50);s.Step(.51);s.Step(.58);s.Step(.60);
            if(a.Moves<1 || b.Moves<1 || a.Lease || !b.Lease)throw new Exception("Both profiles need exclusive, fair service");
            if(d.Events.IndexOf("release:1")>d.Events.IndexOf("focus:2"))throw new Exception("Release must precede focus handoff");
            int epoch=b.Epoch;d.Window=9;s.Step(.61);if(b.Lease || b.Epoch==epoch)throw new Exception("Unexpected focus must revoke input and stale work");
            d.Input=true;int calls=d.Events.Count;s.Step(.62);s.Step(.9);if(d.Events.Count!=calls)throw new Exception("Manual input must block focus requests");s.Step(3.13);if(s.Running || a.Enabled || b.Enabled)throw new Exception("Busy input must reach a bounded stop");
            d=new FakeDesktop {Window=9,Allow=false};a=new FakeHunt(d,1);b=new FakeHunt(d,2);s=new DualScheduler(new ISharedHunt[] {a,b},d);s.Start(0,.4);
            foreach(double t in new double[] {0,.4,.8,1.2,2,2.51})s.Step(t);
            if(s.Running || a.Moves+b.Moves!=0 || d.Events.FindAll(e=>e.StartsWith("focus:")).Count!=3)throw new Exception("Denied focus must send no keys and stop after bounded attempts");
            d=new FakeDesktop();a=new FakeHunt(d,1);b=new FakeHunt(d,2);s=new DualScheduler(new ISharedHunt[] {a,b},d);s.Start(0,.4);s.Step(0);s.Step(.07);a.Enabled=false;s.Step(.1);s.Step(.11);s.Step(.12);s.Step(.20);s.Step(.3);s.Step(.8);
            if(s.Active!=1 || a.Lease || !b.Lease || b.Moves<1)throw new Exception("A completed target must leave the other hunt active");
            s.Stop(true,"emergency");calls=d.Events.Count;s.Step(.9);if(s.Running || a.Lease || b.Lease || calls!=d.Events.Count)throw new Exception("Emergency stop must prevent later input/focus requests");
            d=new FakeDesktop();a=new FakeHunt(d,1);b=new FakeHunt(d,1);s=new DualScheduler(new ISharedHunt[] {a,b},d);bool rejected=false;try {s.Start(0,.4);}catch(ArgumentException) {rejected=true;}if(!rejected)throw new Exception("Duplicate targets must be rejected");
            d=new FakeDesktop();a=new FakeHunt(d,1) {InBattle=true,Pending=true};b=new FakeHunt(d,2);s=new DualScheduler(new ISharedHunt[] {a,b},d);s.Start(0,5);s.Step(0);s.Step(.07);s.Step(.10);
            if(s.Active!=0)throw new Exception("Finish current recognition before yielding");a.Pending=false;s.Step(.11);s.Step(.12);s.Step(.13);s.Step(.21);s.Step(.22);
            if(s.Active!=1 || b.Moves<1)throw new Exception("Walking partner must get the turn while battle dialogue is busy");s.Stop(false,"done");
            d=new FakeDesktop();a=new FakeHunt(d,1) {Pending=true,Allowance=2.5};b=new FakeHunt(d,2);s=new DualScheduler(new ISharedHunt[] {a,b},d);s.Start(0,.4);s.Step(0);s.Step(.07);s.Step(1.4);
            if(s.Active!=0)throw new Exception("Catch screen revalidation needs a bounded longer read allowance");s.Step(3.0);if(s.Active!=-1 || a.Lease)throw new Exception("Catching reads must still relinquish after the bounded allowance");s.Stop(false,"done");
            return new List<string> {"PASS: dual hunts receive exclusive foreground leases, fair turns, and key release before focus changes","PASS: unexpected focus and manual input revoke stale work; busy input and denied focus stop within bounded attempts","PASS: a completed hunt leaves the other active; global emergency stop cancels both; duplicate windows are rejected","PASS: busy battle dialogue yields its turn to the other hunt immediately after current recognition completes","PASS: catch screen revalidation receives a longer bounded turn allowance and cannot hold foreground indefinitely"};
        }
    }
    sealed class SharedDesktop : ISharedDesktop {
        readonly App[] hunts;
        public SharedDesktop(App[] sessions) {hunts=sessions;}
        public long Foreground {get{return Native.GetForegroundWindow().ToInt64();}}
        public bool Busy {get {
            foreach(int key in new int[] {1,2,4,5,6,0xA0,0x11,0x12,0x5B,0x5C})if((Native.GetAsyncKeyState(key)&0x8000)!=0)return true;
            bool owned=false;foreach(var hunt in hunts)owned|=hunt.ManagedShiftHeld;
            return (Native.GetAsyncKeyState(0xA1)&0x8000)!=0 && !owned;
        }}
        public void Focus(long window) {Native.SetForegroundWindow(new IntPtr(window));}
    }
    sealed class DualApp : Form {
        readonly App[] hunts=new App[] {new App("window-1"),new App("window-2")};
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly Timer timer=new Timer {Interval=20};
        readonly NumericUpDown interval=new NumericUpDown {DecimalPlaces=2,Minimum=.4m,Maximum=5,Increment=.1m,Value=.8m,Width=75};
        readonly Button toggle=new Button {Text="Start both (F8)",AutoSize=true};
        readonly Button reset=new Button {Text="Reset emergency stop",AutoSize=true};
        readonly Label status=new Label {Text="Ready — both stopped",AutoSize=true};
        readonly DualScheduler scheduler;
        bool hotkeys,latched,closing,pending;double startAt,nextHotkeys;string lastState="";
        readonly string logPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dual-activity.log");
        public DualApp() {
            Text="Movement Controller v2.1.3 — two hunts";Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterScreen;
            var area=Screen.PrimaryScreen.WorkingArea;ClientSize=new Size(Math.Min(1320,area.Width-40),Math.Min(980,area.Height-70));MinimumSize=new Size(950,650);
            scheduler=new DualScheduler(new ISharedHunt[] {hunts[0],hunts[1]},new SharedDesktop(hunts));
            var top=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(10),WrapContents=true};
            var emergency=new Button {Text="STOP ALL (F10)",AutoSize=true,BackColor=Color.Firebrick,ForeColor=Color.White,UseVisualStyleBackColor=false};
            top.Controls.Add(toggle);top.Controls.Add(emergency);top.Controls.Add(reset);top.Controls.Add(new Label {Text="Turn (sec)",AutoSize=true,Margin=new Padding(14,8,3,3)});top.Controls.Add(interval);top.Controls.Add(status);
            var profiles=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            for(int i=0;i<2;i++) {var panel=new Panel {Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle};var title=new Label {Text="WINDOW "+(i+1),Dock=DockStyle.Top,Height=28,Font=new Font(Font,FontStyle.Bold)};hunts[i].TopLevel=false;hunts[i].FormBorderStyle=FormBorderStyle.None;hunts[i].Dock=DockStyle.Fill;panel.Controls.Add(hunts[i]);panel.Controls.Add(title);profiles.Controls.Add(panel,i,0);hunts[i].Show();}
            Controls.Add(profiles);Controls.Add(top);
            toggle.Click+=delegate {Guard(delegate {Toggle(false);});};emergency.Click+=delegate {Guard(delegate {Stop(true,"EMERGENCY STOP — both stopped");});};reset.Click+=delegate {Guard(delegate {ResetAll();});};
            timer.Tick+=delegate {Guard(Step);};timer.Start();
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                try {Stop(false,"Closed");foreach(var hunt in hunts)hunt.CloseManaged();closing=true;timer.Stop();}
                catch(Exception ex) {e.Cancel=true;MessageBox.Show(this,"Input release failed: "+ex.Message+"\nRelease the movement keys manually before closing.");}
            };
        }
        public void Prepare(string firstTarget,string secondTarget,uint firstPid,uint secondPid) {hunts[0].PrepareManaged(firstTarget,firstPid,false);hunts[1].PrepareManaged(secondTarget,secondPid,true);}
        void Log(string message) {try {if(File.Exists(logPath) && new FileInfo(logPath).Length>131072)File.WriteAllText(logPath,"");File.AppendAllText(logPath,DateTimeOffset.Now.ToString("o")+"  "+message+Environment.NewLine);}catch(IOException) {}catch(UnauthorizedAccessException) {}}
        protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);Register();}
        void Register() {if(hotkeys || !IsHandleCreated)return;bool a=Native.RegisterHotKey(Handle,1,0x4000,(uint)Keys.F8),b=Native.RegisterHotKey(Handle,2,0x4000,(uint)Keys.F10);hotkeys=a&&b;if(!hotkeys) {Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);}}
        protected override void OnHandleDestroyed(EventArgs e) {Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);base.OnHandleDestroyed(e);}
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x312 && !closing) {if(m.WParam.ToInt32()==1)Guard(delegate {Toggle(true);});if(m.WParam.ToInt32()==2)Guard(delegate {Stop(true,"EMERGENCY STOP — both stopped");});}
            if(Native.ActivateMessage!=0 && m.Msg==(int)Native.ActivateMessage && !closing) {Guard(delegate {Stop(false,"Stopped — controller reopened");});if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Show();Activate();}
            base.WndProc(ref m);
        }
        void Toggle(bool shortcut) {
            if(pending || scheduler.Running) {Stop(false,"Stopped by user — both hunts");return;}
            if(latched)throw new InvalidOperationException("Reset the emergency stop first.");
            Register();if(!hotkeys)throw new InvalidOperationException("F8/F10 are in use; close the other controller first.");
            foreach(var hunt in hunts)hunt.ValidateManaged();
            if(hunts[0].Window==hunts[1].Window)throw new InvalidOperationException("Choose different game windows for the two profiles.");
            if(!shortcut) {pending=true;startAt=clock.Elapsed.TotalSeconds+3;status.Text="Starting in 3 seconds";return;}
            Start();
        }
        void Start() {pending=false;foreach(var hunt in hunts)hunt.QueueManaged();try {scheduler.Start(clock.Elapsed.TotalSeconds,(double)interval.Value);}catch {foreach(var hunt in hunts)hunt.End(false,"Start cancelled");throw;}Log("START: windows="+hunts[0].Window+","+hunts[1].Window+"; turn="+interval.Value);}
        void Stop(bool emergency,string reason) {pending=false;latched|=emergency;scheduler.Stop(emergency,reason);Log(reason);RefreshControls();}
        void ResetAll() {foreach(var hunt in hunts)hunt.ResetManaged();latched=false;status.Text="Ready — both stopped";RefreshControls();}
        void Guard(Action action) {try {action();}catch(Exception ex) {try {Stop(true,"Stopped — shared controller error");}catch {}status.Text=ex.Message;Log("ERROR: "+ex.Message);}RefreshControls();}
        void Step() {
            if(closing)return;double now=clock.Elapsed.TotalSeconds;
            if(!hotkeys && now>=nextHotkeys) {nextHotkeys=now+1;Register();}
            if(pending) {status.Text="Starting in "+Math.Max(0,Math.Ceiling(startAt-now))+" seconds";if(now>=startAt)Start();}
            if(scheduler.Running) {scheduler.Step(now);status.Text=scheduler.Status;}
            if(lastState!=scheduler.Status) {lastState=scheduler.Status;Log(scheduler.Status);}
        }
        void RefreshControls() {interval.Enabled=!scheduler.Running&&!pending;toggle.Enabled=!latched&&hotkeys;toggle.Text=scheduler.Running||pending ? "Stop both (F8)" : "Start both (F8)";reset.Enabled=!scheduler.Running && (latched || hunts[0].ManagedLatched || hunts[1].ManagedLatched);}
        public string SmokeReport() {return "SharedHotkeys="+hotkeys+"; Running="+scheduler.Running+"; Turn="+interval.Value+"; "+hunts[0].SmokeReport()+" | "+hunts[1].SmokeReport();}
    }
}
