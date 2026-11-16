using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MovementPrototype {
    public static class VerifiedHeader {
        public static string Signature(Bitmap image,Rectangle area) {
            using(var crop=image.Clone(area,PixelFormat.Format32bppArgb)) {
                var data=crop.LockBits(new Rectangle(0,0,crop.Width,crop.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try {
                    byte[] bytes=new byte[crop.Width*crop.Height*4];
                    for(int y=0;y<crop.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),bytes,y*crop.Width*4,crop.Width*4);
                    using(var hash=SHA256.Create())return crop.Width+"x"+crop.Height+":"+Convert.ToBase64String(hash.ComputeHash(bytes));
                } finally {crop.UnlockBits(data);}
            }
        }
    }
    public enum ControlReady { Unknown, Busy, Ready }
    public static class BattleControlReady {
        public static ControlReady Read(Bitmap frame,Point run,Point items) {
            var a=Read(frame,run);var b=Read(frame,items);
            if(a==ControlReady.Busy && b==ControlReady.Busy)return ControlReady.Busy;
            if(a!=ControlReady.Unknown && b==ControlReady.Ready)return ControlReady.Ready;
            return ControlReady.Unknown;
        }
        public static ControlReady Read(Bitmap frame,Point run) {
            int left=Math.Max(0,run.X-Math.Max(10,frame.Width/40)),right=Math.Min(frame.Width,run.X+Math.Max(10,frame.Width/40));
            int top=Math.Max(0,run.Y-(int)(frame.Height*.065)),bottom=Math.Min(frame.Height,run.Y-Math.Max(4,frame.Height/80));
            if(bottom<=top || right<=left)return ControlReady.Unknown;
            int red=0,gray=0,blue=0,total=0;
            for(int y=top;y<bottom;y+=2)for(int x=left;x<right;x+=2) {
                var c=frame.GetPixel(x,y);total++;
                if(c.R>110 && c.R>c.G*1.65 && c.R>c.B*1.45)red++;
                if(c.B>120 && c.B>c.R*1.5 && c.B>c.G*1.15)blue++;
                int maximum=Math.Max(c.R,Math.Max(c.G,c.B)),minimum=Math.Min(c.R,Math.Min(c.G,c.B));
                if(minimum>65 && maximum-minimum<45)gray++;
            }
            if(red>=Math.Max(8,total*.04))return ControlReady.Busy;
            if(blue>=Math.Max(8,total*.03) && red<Math.Max(5,total*.02))return ControlReady.Ready;
            if(gray>=Math.Max(8,total*.03) && red<Math.Max(5,total*.02))return ControlReady.Ready;
            return ControlReady.Unknown;
        }
    }
    public sealed class ReadyEscapePolicy {
        public ControlReady State {get;private set;}
        public int Requests {get;private set;}
        public int Accepted {get;private set;}
        public double ReadyAt {get;private set;}
        double began,next;
        bool pending;
        public void Reset() {State=ControlReady.Unknown;Requests=Accepted=0;pending=false;began=next=ReadyAt=0;}
        public void Observe(ControlReady state,double now) {
            if(state==ControlReady.Busy && pending) {Accepted++;pending=false;next=0;}
            if(state==ControlReady.Ready && State!=ControlReady.Ready) {ReadyAt=now;next=pending ? Math.Max(next,now) : now;}
            State=state;
        }
        public bool Due(double now) {return State==ControlReady.Ready && now>=next && !StopRequired(now);}
        public bool KeyboardDue(double now) {return now>=next && !KeyboardLimit(now);}
        public bool KeyboardLimit(double now) {return Requests>=40 || (Requests>0 && now-began>=15);}
        public bool StopRequired(double now) {return State==ControlReady.Ready && (Accepted>=3 || Requests>=40 || (Requests>0 && now-began>=15));}
        public void Submitted(double now) {
            if(Requests==0)began=now;
            Requests++;pending=true;next=now+.20;
        }
        public static List<string> Tests() {
            var p=new ReadyEscapePolicy();p.Reset();p.Observe(ControlReady.Busy,0);
            if(p.Due(8))throw new Exception("Dialogue/busy controls must not receive Run input");
            p.Observe(ControlReady.Ready,8.01);if(!p.Due(8.01))throw new Exception("Ready transition must not wait for a fixed cooldown");
            p.Submitted(8.01);if(p.Due(8.10) || !p.Due(8.22) || p.Accepted!=0)throw new Exception("Ignored early requests must retry promptly without consuming accepted attempts");
            p.Submitted(8.22);p.Observe(ControlReady.Busy,8.25);
            if(p.Accepted!=1 || p.Due(12.0))throw new Exception("Acknowledged action must wait for ready controls or world exit");
            p.Observe(ControlReady.Ready,12.01);if(!p.Due(12.01))throw new Exception("Failed escape returning to ready must retry immediately");
            p.Submitted(12.01);p.Observe(ControlReady.Busy,12.02);p.Observe(ControlReady.Ready,12.2);p.Submitted(12.2);p.Observe(ControlReady.Busy,12.3);p.Observe(ControlReady.Ready,13);
            if(!p.StopRequired(13) || p.Due(13))throw new Exception("Three acknowledged failed attempts retain the stop limit");
            p.Reset();p.Observe(ControlReady.Ready,20);p.Submitted(20);if(!p.StopRequired(35.1))throw new Exception("Unacknowledged requests must be time bounded");
            p.Reset();if(p.Due(100) || p.Requests!=0)throw new Exception("New battle/reset must clear old readiness");
            if(!p.KeyboardDue(100))throw new Exception("Keyboard fallback must not inherit a four-second cooldown");
            p.Submitted(100);if(p.KeyboardDue(100.1) || !p.KeyboardDue(100.21) || !p.KeyboardLimit(115.1))throw new Exception("Unlocated-control fallback must be paced and time bounded");
            using(var image=new Bitmap(640,480))using(var g=Graphics.FromImage(image)) {
                var point=new Point(600,400);g.Clear(Color.Black);
                if(BattleControlReady.Read(image,point)!=ControlReady.Unknown)throw new Exception("An absent button must not be ready");
                g.FillRectangle(Brushes.LightGray,584,369,32,26);if(BattleControlReady.Read(image,point)!=ControlReady.Ready)throw new Exception("Enabled gray controls must be recognized");
                g.FillRectangle(Brushes.Red,584,369,32,26);if(BattleControlReady.Read(image,point)!=ControlReady.Busy)throw new Exception("Busy red controls must be recognized");
                g.FillRectangle(Brushes.Blue,584,369,32,26);if(BattleControlReady.Read(image,point)!=ControlReady.Ready)throw new Exception("Blue hover highlight must remain ready");
                var area=new Rectangle(20,20,200,40);string signature=VerifiedHeader.Signature(image,area);
                if(signature!=VerifiedHeader.Signature(image,area))throw new Exception("Unchanged verified header should match exactly");
                image.SetPixel(50,30,Color.White);if(signature==VerifiedHeader.Signature(image,area))throw new Exception("Even one changed heading pixel must require fresh recognition");
            }
            return new List<string> {"PASS: Run follows ready controls immediately; dialogue/busy states block input; ignored clicks retry without the old four-second wait","PASS: accepted failures and ignored-request time are bounded; reset and unknown controls block action","PASS: visual Run control separates enabled gray/blue hover, busy red and absent buttons","PASS: verified header reuse requires exact pixel identity; a changed heading forces new OCR"};
        }
    }
}
