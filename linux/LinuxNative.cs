using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MovementPrototype {
    static class Native {
        const string X11="libX11.so.6", XTest="libXtst.so.6";
        const int Mapped=2, AnyModifier=1<<15, AsyncGrab=1;
        static IntPtr display, root;
        static int xError;
        static readonly Dictionary<string,IntPtr> atoms=new Dictionary<string,IntPtr>();
        static readonly Dictionary<int,HotKey> hotkeys=new Dictionary<int,HotKey>();
        static readonly HashSet<int> heldHotkeys=new HashSet<int>();
        static readonly byte[] keymap=new byte[32];
        static readonly ErrorHandler onXError=delegate(IntPtr d,IntPtr e) {xError=Marshal.ReadByte(e,32);return 0;};
        delegate int ErrorHandler(IntPtr d,IntPtr e);
        public delegate bool EnumWindowProc(IntPtr window,IntPtr data);
        public static readonly uint ActivateMessage=0;
        public struct RECT {public int Left,Top,Right,Bottom;}
        public struct POINT {public int X,Y;}
        sealed class HotKey {public IntPtr Owner;public int Code;}

        [StructLayout(LayoutKind.Sequential)]
        struct WindowAttributes {
            public int X,Y,Width,Height,Border,Depth;
            public IntPtr Visual,Root;
            public int Class,BitGravity,WinGravity,BackingStore;
            public UIntPtr BackingPlanes,BackingPixel;
            public int SaveUnder;
            public IntPtr Colormap;
            public int MapInstalled,MapState;
            public IntPtr AllEventMasks,YourEventMask,DoNotPropagateMask;
            public int OverrideRedirect;
            public IntPtr Screen;
        }
        [StructLayout(LayoutKind.Explicit,Size=192)]
        struct KeyEvent {
            [FieldOffset(0)] public int Type;
            [FieldOffset(84)] public int Code;
        }

        [DllImport(X11)] static extern int XInitThreads();
        [DllImport(X11)] static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(X11)] static extern IntPtr XDefaultRootWindow(IntPtr d);
        [DllImport(X11)] static extern IntPtr XSetErrorHandler(ErrorHandler handler);
        [DllImport(X11)] static extern IntPtr XInternAtom(IntPtr d,string name,int onlyIfExists);
        [DllImport(X11)] static extern int XGetWindowAttributes(IntPtr d,IntPtr w,out WindowAttributes attributes);
        [DllImport(X11)] static extern int XGetWindowProperty(IntPtr d,IntPtr w,IntPtr property,IntPtr offset,IntPtr length,int del,IntPtr type,
            out IntPtr actual,out int format,out UIntPtr count,out UIntPtr remaining,out IntPtr value);
        [DllImport(X11)] static extern int XFree(IntPtr value);
        [DllImport(X11)] static extern int XGetInputFocus(IntPtr d,out IntPtr w,out int revert);
        [DllImport(X11)] static extern int XQueryTree(IntPtr d,IntPtr w,out IntPtr root,out IntPtr parent,out IntPtr children,out uint count);
        [DllImport(X11)] static extern int XFetchName(IntPtr d,IntPtr w,out IntPtr name);
        [DllImport(X11)] static extern int XTranslateCoordinates(IntPtr d,IntPtr src,IntPtr dst,int x,int y,out int rx,out int ry,out IntPtr child);
        [DllImport(X11)] static extern IntPtr XStringToKeysym(string name);
        [DllImport(X11)] static extern byte XKeysymToKeycode(IntPtr d,IntPtr symbol);
        [DllImport(X11)] static extern int XQueryKeymap(IntPtr d,[Out] byte[] state);
        [DllImport(X11)] static extern int XQueryPointer(IntPtr d,IntPtr w,out IntPtr root,out IntPtr child,
            out int rootX,out int rootY,out int x,out int y,out uint mask);
        [DllImport(X11)] static extern int XGrabKey(IntPtr d,int code,uint modifiers,IntPtr w,int ownerEvents,int pointerMode,int keyboardMode);
        [DllImport(X11)] static extern int XUngrabKey(IntPtr d,int code,uint modifiers,IntPtr w);
        [DllImport(X11)] static extern int XPending(IntPtr d);
        [DllImport(X11)] static extern int XNextEvent(IntPtr d,out KeyEvent e);
        [DllImport(X11)] static extern int XSync(IntPtr d,int discard);
        [DllImport(X11)] static extern int XkbSetDetectableAutoRepeat(IntPtr d,int enable,out int supported);
        [DllImport(XTest)] static extern int XTestFakeKeyEvent(IntPtr d,uint code,int press,UIntPtr delay);
        [DllImport(XTest)] static extern int XTestFakeButtonEvent(IntPtr d,uint button,int press,UIntPtr delay);
        [DllImport(XTest)] static extern int XTestFakeMotionEvent(IntPtr d,int screen,int x,int y,UIntPtr delay);

        public static bool SetProcessDPIAware() {return XInitThreads()!=0;}
        static bool Open() {
            if(display!=IntPtr.Zero)return true;
            display=XOpenDisplay(IntPtr.Zero);
            if(display==IntPtr.Zero)return false;
            root=XDefaultRootWindow(display);
            XSetErrorHandler(onXError);
            int supported;
            XkbSetDetectableAutoRepeat(display,1,out supported);
            return true;
        }
        static IntPtr Atom(string name) {
            IntPtr atom;
            if(!atoms.TryGetValue(name,out atom))atoms[name]=atom=XInternAtom(display,name,0);
            return atom;
        }
        static IntPtr Property(IntPtr w,string name,out int format,out int count) {
            IntPtr type,ptr;
            UIntPtr items,remaining;
            int status=XGetWindowProperty(display,w,Atom(name),IntPtr.Zero,new IntPtr(4096),0,IntPtr.Zero,
                out type,out format,out items,out remaining,out ptr);
            count=status==0 ? (int)items.ToUInt64() : 0;
            return ptr;
        }
        static IntPtr NumberProperty(IntPtr w,string name) {
            int format,count;
            var p=Property(w,name,out format,out count);
            try {return p!=IntPtr.Zero && format==32 && count>0 ? Marshal.ReadIntPtr(p) : IntPtr.Zero;}
            finally {if(p!=IntPtr.Zero)XFree(p);}
        }
        public static uint GetWindowThreadProcessId(IntPtr w,out uint pid) {
            pid=Open() && w.ToInt64()>1 ? (uint)NumberProperty(w,"_NET_WM_PID").ToInt64() : 0;
            return pid;
        }
        static IntPtr Parent(IntPtr w) {
            IntPtr unused,parent,children;
            uint count;
            int ok=XQueryTree(display,w,out unused,out parent,out children,out count);
            if(children!=IntPtr.Zero)XFree(children);
            return ok!=0 ? parent : IntPtr.Zero;
        }
        static bool Contains(IntPtr ancestor,IntPtr w) {
            for(int i=0;i<24 && w.ToInt64()>1;i++,w=Parent(w))
                if(w==ancestor)return true;
            return false;
        }
        public static IntPtr GetForegroundWindow() {
            if(!Open())return IntPtr.Zero;
            IntPtr focused;
            int revert;
            XGetInputFocus(display,out focused,out revert);
            var active=NumberProperty(root,"_NET_ACTIVE_WINDOW");
            if(active.ToInt64()>1 && Contains(active,focused))return active;
            for(int i=0;i<24 && focused.ToInt64()>1 && focused!=root;i++,focused=Parent(focused))
                if(NumberProperty(focused,"_NET_WM_PID")!=IntPtr.Zero)return focused;
            return IntPtr.Zero;
        }
        public static bool IsWindow(IntPtr w) {WindowAttributes a;return Open() && w.ToInt64()>1 && XGetWindowAttributes(display,w,out a)!=0;}
        public static bool IsWindowVisible(IntPtr w) {
            WindowAttributes a;
            return Open() && w.ToInt64()>1 && XGetWindowAttributes(display,w,out a)!=0 && a.MapState==Mapped;
        }
        public static bool IsIconic(IntPtr w) {return !IsWindowVisible(w);}
        public static bool EnumWindows(EnumWindowProc callback,IntPtr data) {
            if(!Open())return false;
            int format,count;
            var p=Property(root,"_NET_CLIENT_LIST",out format,out count);
            try {
                if(p!=IntPtr.Zero && format==32)
                    for(int i=0;i<count;i++)if(!callback(Marshal.ReadIntPtr(p,i*IntPtr.Size),data))break;
            } finally {if(p!=IntPtr.Zero)XFree(p);}
            return true;
        }
        public static string Title(IntPtr w) {
            if(!Open() || w.ToInt64()<=1)return "";
            int format,count;
            var p=Property(w,"_NET_WM_NAME",out format,out count);
            try {
                if(p!=IntPtr.Zero && format==8 && count>0) {
                    var bytes=new byte[count];
                    Marshal.Copy(p,bytes,0,count);
                    return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                }
            } finally {if(p!=IntPtr.Zero)XFree(p);}
            if(XFetchName(display,w,out p)==0 || p==IntPtr.Zero)return "";
            try {return Marshal.PtrToStringAnsi(p) ?? "";} finally {XFree(p);}
        }
        public static bool SetForegroundWindow(IntPtr w) {
            if(!IsWindowVisible(w))return false;
            using(var proc=Process.Start(new ProcessStartInfo("wmctrl","-ia 0x"+w.ToInt64().ToString("x")) {UseShellExecute=false})) {
                if(!proc.WaitForExit(500)) {proc.Kill();return false;}
                return proc.ExitCode==0;
            }
        }
        public static void Flash(IntPtr w) {}
        public static bool PostMessage(IntPtr w,uint message,IntPtr key,IntPtr data) {return false;}
        public static uint KeyMessageData(Keys key,bool up) {
            uint flags=1;
            if(key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down)flags|=1u<<24;
            if(up)flags|=(1u<<30)|(1u<<31);
            return flags;
        }
        public static void PostKey(IntPtr w,uint pid,Keys key,bool up) {
            throw new InvalidOperationException("Background input is unavailable on Linux. Keep the selected window visible and focused.");
        }
        static string KeyName(int key) {
            switch(key) {
                case 0x10:case 0xa0:return "Shift_L";
                case 0xa1:return "Shift_R";
                case 0x11:return "Control_L";
                case 0x12:return "Alt_L";
                case 0x5b:return "Super_L";
                case 0x5c:return "Super_R";
            }
            string name=((Keys)key).ToString();
            return name.Length==1 ? name.ToLowerInvariant() : name;
        }
        static int KeyCode(int key) {return XKeysymToKeycode(display,XStringToKeysym(KeyName(key)));}
        public static short GetAsyncKeyState(int key) {
            if(!Open())return 0;
            if(key==1 || key==2 || key==4 || key==5 || key==6) {
                IntPtr r,c;int rx,ry,x,y;uint mask;
                int button=key==1 ? 1 : key==2 ? 3 : key==4 ? 2 : key;
                return XQueryPointer(display,root,out r,out c,out rx,out ry,out x,out y,out mask)!=0 &&
                    (mask & (1u<<(button+7)))!=0 ? unchecked((short)0x8000) : (short)0;
            }
            XQueryKeymap(display,keymap);
            int code=KeyCode(key);
            bool down=code!=0 && (keymap[code/8]&(1<<(code%8)))!=0;
            if(!down && (key==0x10 || key==0x11 || key==0x12)) {
                var right=key==0x10 ? "Shift_R" : key==0x11 ? "Control_R" : "Alt_R";
                code=XKeysymToKeycode(display,XStringToKeysym(right));
                down=code!=0 && (keymap[code/8]&(1<<(code%8)))!=0;
            }
            return down ? unchecked((short)0x8000) : (short)0;
        }
        public static void Send(Keys key,bool up) {
            if(!Open())throw new InvalidOperationException("No X11 display is available.");
            int code=KeyCode((int)key);
            if(code==0 || XTestFakeKeyEvent(display,(uint)code,up ? 0 : 1,UIntPtr.Zero)==0)
                throw new InvalidOperationException("X11 rejected the key event.");
            XSync(display,0);
        }
        public static bool RegisterHotKey(IntPtr owner,int id,uint modifiers,uint key) {
            if(!Open() || hotkeys.ContainsKey(id))return false;
            int code=KeyCode((int)key);
            if(code==0)return false;
            xError=0;
            XGrabKey(display,code,AnyModifier,root,0,AsyncGrab,AsyncGrab);
            XSync(display,0);
            if(xError!=0)return false;
            hotkeys[id]=new HotKey {Owner=owner,Code=code};
            return true;
        }
        public static bool UnregisterHotKey(IntPtr owner,int id) {
            HotKey key;
            if(!hotkeys.TryGetValue(id,out key) || key.Owner!=owner)return false;
            XUngrabKey(display,key.Code,AnyModifier,root);
            XSync(display,0);
            hotkeys.Remove(id);heldHotkeys.Remove(key.Code);
            return true;
        }
        public static List<int> PollHotKeys(IntPtr owner) {
            var events=new List<int>();
            if(!Open())return events;
            KeyEvent e;
            while(XPending(display)>0) {
                XNextEvent(display,out e);
                if(e.Type==3)heldHotkeys.Remove(e.Code);
                if(e.Type!=2 || !heldHotkeys.Add(e.Code))continue;
                foreach(var key in hotkeys)
                    if(key.Value.Owner==owner && key.Value.Code==e.Code)events.Add(key.Key);
            }
            return events;
        }
        public static Bitmap CaptureClient(IntPtr w) {
            WindowAttributes a;
            int x,y;
            IntPtr child;
            if(!Open() || GetForegroundWindow()!=w || XGetWindowAttributes(display,w,out a)==0 || a.MapState!=Mapped ||
                XTranslateCoordinates(display,w,root,0,0,out x,out y,out child)==0 || a.Width<100 || a.Height<100)
                throw new InvalidOperationException("Keep the selected game visible and focused for recognition.");
            var bitmap=new Bitmap(a.Width,a.Height);
            try {using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(x,y,0,0,bitmap.Size);return bitmap;}
            catch {bitmap.Dispose();throw;}
        }
        public static double ClickClient(IntPtr w,uint expectedProcess,Point point,Stopwatch clock=null) {
            WindowAttributes a;
            uint pid;
            int x,y;
            IntPtr child;
            if(GetWindowThreadProcessId(w,out pid)==0 || pid!=expectedProcess || GetForegroundWindow()!=w ||
                XGetWindowAttributes(display,w,out a)==0 || a.MapState!=Mapped ||
                point.X<0 || point.Y<0 || point.X>=a.Width || point.Y>=a.Height ||
                XTranslateCoordinates(display,w,root,point.X,point.Y,out x,out y,out child)==0)
                throw new InvalidOperationException("The selected battle window changed before the click.");
            if(XTestFakeMotionEvent(display,-1,x,y,UIntPtr.Zero)==0)
                throw new InvalidOperationException("X11 rejected pointer movement.");
            XSync(display,0);
            IntPtr r,hit;int rx,ry,wx,wy;uint buttons;
            if(GetForegroundWindow()!=w || XQueryPointer(display,root,out r,out hit,out rx,out ry,out wx,out wy,out buttons)==0 ||
                !Contains(hit,w) || (buttons&0x700)!=0)
                throw new InvalidOperationException("The selected click is covered, unfocused, or a mouse button is held.");
            if(XTestFakeButtonEvent(display,1,1,UIntPtr.Zero)==0)
                throw new InvalidOperationException("X11 rejected mouse-down.");
            XSync(display,0);
            double at=clock==null ? 0 : clock.Elapsed.TotalSeconds;
            try {System.Threading.Thread.Sleep(60);}
            finally {
                if(XTestFakeButtonEvent(display,1,0,UIntPtr.Zero)==0)
                    throw new InvalidOperationException("X11 rejected mouse-up.");
                XSync(display,0);
            }
            return at;
        }
    }
}
