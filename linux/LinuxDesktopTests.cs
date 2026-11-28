using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MovementPrototype {
    static class LinuxDesktopTests {
        static void Check(bool ok,string reason) {
            if(!ok)throw new InvalidOperationException(reason);
        }
        static void PumpUntil(Func<bool> predicate) {
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<1800) {
                Application.DoEvents();
                if(predicate())return;
                Thread.Sleep(10);
            }
            throw new InvalidOperationException("The expected X11 event was not received.");
        }
        static IntPtr Find(string title) {
            IntPtr result=IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr w,IntPtr data) {
                if(Native.Title(w)!=title)return true;
                result=w;return false;
            },IntPtr.Zero);
            return result;
        }
        static void SetFixturePid(IntPtr window) {
            var args="-id 0x"+window.ToInt64().ToString("x")+" -f _NET_WM_PID 32c -set _NET_WM_PID "+Process.GetCurrentProcess().Id;
            using(var process=Process.Start(new ProcessStartInfo("xprop",args) {UseShellExecute=false})) {
                Check(process.WaitForExit(1000) && process.ExitCode==0,"Could not mark the local test window with its PID");
            }
        }
        public static int Run(string report) {
            var results=new List<string>();
            if(Environment.GetEnvironmentVariable("MOVEMENT_ISOLATED_DESKTOP_TEST")!="1") {
                File.WriteAllText(report,"FAIL: run the X11 input test inside the isolated test harness.");
                return 1;
            }
            try {
                using(var target=new Form {Text="Movement test receiver",StartPosition=FormStartPosition.Manual,
                    Location=new Point(80,80),ClientSize=new Size(330,220),BackColor=Color.OrangeRed,KeyPreview=true})
                using(var decoy=new Form {Text="Movement test alternate",StartPosition=FormStartPosition.Manual,
                    Location=new Point(500,80),ClientSize=new Size(280,220)}) {
                    int downs=0,ups=0,clicks=0;
                    target.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.KeyCode==Keys.Left)downs++;};
                    target.KeyUp+=delegate(object sender,KeyEventArgs e) {if(e.KeyCode==Keys.Left)ups++;};
                    target.MouseDown+=delegate {clicks++;};
                    target.Show();decoy.Show();
                    IntPtr window=IntPtr.Zero,other=IntPtr.Zero;
                    PumpUntil(delegate {window=Find(target.Text);other=Find(decoy.Text);return window!=IntPtr.Zero && other!=IntPtr.Zero;});
                    SetFixturePid(window);
                    SetFixturePid(other);
                    uint pid;
                    Check(Native.GetWindowThreadProcessId(window,out pid)==(uint)Process.GetCurrentProcess().Id,"X11 window PID must match");
                    Check(Native.SetForegroundWindow(window),"X11 window activation must succeed");
                    PumpUntil(()=>Native.GetForegroundWindow()==window);
                    using(var image=Native.CaptureClient(window)) {
                        Check(image.Width==330 && image.Height==220,"Captured size must match client area");
                        Check(image.GetPixel(70,70).ToArgb()==Color.OrangeRed.ToArgb(),"Capture must contain client pixels");
                    }
                    results.Add("PASS: X11 finds the correct window, focuses it, and captures its client area");
                    Native.Send(Keys.Left,false);Native.Send(Keys.Left,true);
                    PumpUntil(()=>downs==1 && ups==1);
                    Native.ClickClient(window,pid,new Point(70,70));
                    PumpUntil(()=>clicks==1);
                    Check(Native.SetForegroundWindow(other),"Alternate window focus");
                    PumpUntil(()=>Native.GetForegroundWindow()==other);
                    bool refused=false;
                    try {Native.ClickClient(window,pid,new Point(70,70));}
                    catch(InvalidOperationException) {refused=true;}
                    Check(refused && clicks==1,"Input must refuse an unfocused target");
                    results.Add("PASS: XTest movement and clicks are delivered, and unfocused clicks are refused");
                    Check(Native.RegisterHotKey(target.Handle,1,0,(uint)Keys.F8),"F8 registration");
                    Check(Native.RegisterHotKey(target.Handle,2,0,(uint)Keys.F10),"F10 registration");
                    try {
                        var keys=new List<int>();
                        Native.Send(Keys.F8,false);Native.Send(Keys.F8,true);
                        Native.Send(Keys.F10,false);Native.Send(Keys.F10,true);
                        PumpUntil(delegate {keys.AddRange(Native.PollHotKeys(target.Handle));return keys.Count==2;});
                        Check(keys[0]==1 && keys[1]==2,"Hotkey identities and ordering");
                    } finally {
                        Native.UnregisterHotKey(target.Handle,1);
                        Native.UnregisterHotKey(target.Handle,2);
                    }
                    results.Add("PASS: global F8 and F10 work while another X11 window has focus");
                }
                File.WriteAllLines(report,results);return 0;
            } catch(Exception ex) {
                results.Add("FAIL: "+ex);File.WriteAllLines(report,results);return 1;
            }
        }
    }
}
