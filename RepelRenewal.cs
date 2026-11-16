using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MovementPrototype {
    // A cheap trigger, never permission to click. OCR must verify the entire dialog.
    public sealed class RepelPromptVisual {
        public static Rectangle IdentityArea(Rectangle bounds) {
            int inset=Math.Max(5,bounds.Width/30),top=Math.Max(6,bounds.Height/14);
            return new Rectangle(bounds.Left+inset,bounds.Top+top,bounds.Width-inset*2,Math.Max(1,(int)(bounds.Height*.75)-top));
        }
        byte[] pixels;
        int width,height,stride;
        bool Green(int x,int y) {
            int i=y*stride+x*4,b=pixels[i],g=pixels[i+1],r=pixels[i+2];
            return g>90 && g>r*1.5 && g>b*1.3;
        }
        Color Pixel(int x,int y) {
            int i=y*stride+x*4;return Color.FromArgb(pixels[i+2],pixels[i+1],pixels[i]);
        }
        bool Gray(Color c) {
            int hi=Math.Max(c.R,Math.Max(c.G,c.B)),lo=Math.Min(c.R,Math.Min(c.G,c.B));
            return lo>=40 && hi<=155 && hi-lo<40;
        }
        public Rectangle? Find(Bitmap image) {
            width=image.Width;height=image.Height;stride=width*4;
            if(width<100 || height<90)return null;
            if(pixels==null || pixels.Length!=stride*height)pixels=new byte[stride*height];
            var bits=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),pixels,y*stride,stride);}
            finally {image.UnlockBits(bits);}
            for(int y=height/8;y<height*.96;y+=2)for(int x=0;x<width;) {
                if(!Green(x,y)) {x++;continue;}
                int left=x;while(x<width && Green(x,y))x++;
                int span=x-left;
                if(span<Math.Max(14,width*.025) || span>width*.45)continue;
                int headerY=y-(int)(span*1.40),bodyY=y-(int)(span*.65);
                int modalLeft=x-(int)(span*3.55),modalTop=y-(int)(span*1.65);
                if(headerY<0 || bodyY<0 || modalLeft<-8 || modalTop<-8)continue;
                int red=0;
                foreach(double f in new double[] {.2,.5,.8}) {
                    int rx=left-(int)(span*f);
                    if(rx<0)continue;
                    var c=Pixel(rx,y);
                    if(c.R>65 && c.R>c.G*1.6 && c.R>c.B*1.5)red++;
                }
                if(red<3)continue;
                int lx=Math.Max(0,modalLeft+(int)(span*.12)),rx2=Math.Min(width-1,x-(int)(span*.12));
                var a=Pixel(lx,headerY);var b=Pixel(rx2,headerY);
                if(!Gray(a) || !Gray(b) || Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B)>45)continue;
                a=Pixel(lx,bodyY);b=Pixel(rx2,bodyY);
                if(Math.Max(a.R,Math.Max(a.G,a.B))>95 || Math.Max(b.R,Math.Max(b.G,b.B))>95)continue;
                var area=Rectangle.FromLTRB(Math.Max(0,modalLeft-3),Math.Max(0,modalTop-3),Math.Min(width,x+4),Math.Min(height,y+(int)(span*.43)+3));
                if(area.Width>=80 && area.Height>=60)return area;
            }
            return null;
        }
    }
    public sealed class RepelPromptReading {
        public string Kind;
        public Point Yes;
    }
    public static class RepelPromptMatch {
        public static RepelPromptReading Parse(string text,IList<ButtonWord> words,int width,int height) {
            string folded=Regex.Replace(text.ToLowerInvariant(),"[^a-z]","");
            var match=Regex.Match(folded,@"^repelexpiredwouldyouliketouseanother(max|super)?repel(?:noyes|yesno)$");
            if(!match.Success)return null;
            ButtonWord yes=null,no=null;
            foreach(var word in words) {
                if(String.Equals(word.Text,"Yes",StringComparison.OrdinalIgnoreCase)) {if(yes!=null)return null;yes=word;}
                if(String.Equals(word.Text,"No",StringComparison.OrdinalIgnoreCase)) {if(no!=null)return null;no=word;}
            }
            if(yes==null || no==null)return null;
            var p=yes.Center;var q=no.Center;
            if(p.X<width*.64 || p.X>=width || p.Y<height*.76 || p.Y>=height || q.X<width*.30 || q.X>=p.X-width*.10 || Math.Abs(p.Y-q.Y)>Math.Max(6,height*.06))return null;
            return new RepelPromptReading {Kind=match.Groups[1].Value=="" ? "Repel" : match.Groups[1].Value=="max" ? "Max Repel" : "Super Repel",Yes=new Point((int)p.X,(int)p.Y)};
        }
        public static List<string> Tests() {
            var words=new List<ButtonWord> {new ButtonWord("No",125,122,25,16),new ButtonWord("Yes",210,122,25,16)};
            foreach(string kind in new string[] {"Repel","Max Repel","Super Repel"})
                if(Parse("Repel Expired\nWould you like to use another "+kind+"?\nNo Yes",words,267,152)==null)throw new Exception("Renewal prompt rejected: "+kind);
            foreach(string text in new string[] {"Release Pokemon\nWould you like to release this Pokemon? No Yes","Repel Expired\nWould you like to buy another Max Repel? No Yes","Repel Expired\nWould you like to use another Escape Rope? No Yes","Chat says Repel Expired Would you like to use another Max Repel? No Yes","Repel Expired Would you like to use another Max Repel? No Yes extra"})
                if(Parse(text,words,267,152)!=null)throw new Exception("Unrelated/extra dialog text must not authorize item use");
            words.Add(new ButtonWord("Yes",220,122,25,16));
            if(Parse("Repel Expired Would you like to use another Max Repel? No Yes",words,267,152)!=null)throw new Exception("Ambiguous Yes must be rejected");
            words.RemoveAt(2);words[0]=new ButtonWord("No",125,12,25,16);
            if(Parse("Repel Expired Would you like to use another Max Repel? No Yes",words,267,152)!=null)throw new Exception("Button row mismatch must be rejected");
            using(var frame=new Bitmap(640,480))using(var g=Graphics.FromImage(frame)) {
                var detector=new RepelPromptVisual();g.Clear(Color.ForestGreen);
                if(detector.Find(frame).HasValue)throw new Exception("Walking must not trigger prompt OCR");
                g.Clear(Color.FromArgb(32,78,45));
                using(var title=new SolidBrush(Color.FromArgb(78,74,82)))using(var body=new SolidBrush(Color.FromArgb(40,35,50))) {
                    g.FillRectangle(title,190,170,215,25);g.FillRectangle(body,190,195,215,73);
                    g.FillRectangle(Brushes.Red,286,268,60,22);g.FillRectangle(Brushes.LimeGreen,346,268,60,22);
                }
                g.Flush();if(!detector.Find(frame).HasValue)throw new Exception("Paired prompt controls and title/body should trigger a cropped read");
                var area=detector.Find(frame).Value;var identity=RepelPromptVisual.IdentityArea(area);
                string signature=VerifiedHeader.Signature(frame,identity);
                frame.SetPixel(area.Left,area.Bottom-1,Color.White);
                if(VerifiedHeader.Signature(frame,identity)!=signature)throw new Exception("Animated scenery outside the opaque prompt must not invalidate identity");
                frame.SetPixel(identity.Left+10,identity.Top+10,Color.White);
                if(VerifiedHeader.Signature(frame,identity)==signature)throw new Exception("Changed prompt pixels must require another fresh read");
            }
            return new List<string> {"PASS: Repel renewal accepts only the exact expired/use-another prompt for Repel, Super Repel or Max Repel","PASS: purchases, releases, unrelated items, chat/extra text and ambiguous/misaligned buttons never authorize Repel clicks","PASS: cheap dialog geometry triggers a cropped read; ordinary world colors trigger no prompt OCR"};
        }
    }
    public sealed class RepelRenewalPolicy {
        public bool Active {get;private set;}
        public bool Visible {get;private set;}
        public int Epoch {get;private set;}
        public int Requests {get;private set;}
        public Rectangle Bounds {get;private set;}
        int confirmations,absentFrames;
        string kind;
        Point point;
        double began,nextRead,lastRead,lastClick;
        double? absentSince;
        public void Reset() {Active=Visible=false;confirmations=absentFrames=Requests=0;kind=null;began=nextRead=lastRead=lastClick=0;absentSince=null;Epoch++;}
        public void Observe(Rectangle? bounds,double now) {
            if(!bounds.HasValue) {
                Visible=false;
                if(!Active)return;
                if(!absentSince.HasValue)absentSince=now;
                if(++absentFrames>=2 && now-absentSince.Value>=.10)Reset();
                return;
            }
            if(!Active || bounds.Value!=Bounds) {Reset();Active=true;Bounds=bounds.Value;began=now;}
            Visible=true;absentSince=null;absentFrames=0;
        }
        public bool NeedsRead(double now) {return Visible && Requests<2 && now>=nextRead && !Expired(now);}
        public void ReadingStarted(double now) {nextRead=now+.25;}
        public bool Confirm(RepelPromptReading reading,double now) {
            if(!Visible || reading==null || Expired(now)) {confirmations=0;kind=null;return false;}
            if(kind!=reading.Kind || point!=reading.Yes || now-lastRead>1.5)confirmations=0;
            kind=reading.Kind;point=reading.Yes;lastRead=now;confirmations++;
            return confirmations>=2 && Requests<2 && (Requests==0 || now-lastClick>=1);
        }
        public void Clicked(double now) {Requests++;lastClick=now;nextRead=now+1;confirmations=0;}
        public bool Expired(double now) {return Active && (now-began>=10 || (Requests>=2 && now-lastClick>=2));}
        public static List<string> Tests() {
            var p=new RepelRenewalPolicy();var area=new Rectangle(100,100,267,152);
            var read=new RepelPromptReading {Kind="Max Repel",Yes=new Point(225,135)};
            if(p.NeedsRead(0) || p.Confirm(read,0))throw new Exception("Absent prompts cannot send input");
            p.Observe(area,1);if(!p.NeedsRead(1) || p.Confirm(read,1.1) || !p.Confirm(read,1.3))throw new Exception("Two fresh same-prompt reads are required");
            p.Clicked(1.3);if(p.NeedsRead(1.5))throw new Exception("Do not hammer a submitted prompt");
            p.Observe(null,1.4);if(p.NeedsRead(1.4) || p.Confirm(read,1.4))throw new Exception("Disappeared dialog cannot be clicked from stale OCR");
            int epoch=p.Epoch;p.Observe(null,1.6);if(p.Active || p.Epoch==epoch)throw new Exception("Confirmed disappearance must invalidate pending OCR");
            p.Observe(area,2);if(p.Confirm(read,2.1) || !p.Confirm(read,2.3))throw new Exception("New prompt requires new readings");
            p.Clicked(2.3);if(p.Confirm(read,3.4) || !p.Confirm(read,3.6))throw new Exception("Ignored click retry needs two fresh confirmations");
            p.Clicked(3.6);if(!p.Expired(5.7) || p.NeedsRead(5.7) || p.Confirm(read,5.7))throw new Exception("Ignored clicks must reach a bounded stop");
            p.Reset();p.Observe(area,10);p.Confirm(read,10.1);p.Reset();p.Observe(area,11);if(p.Confirm(read,11.1))throw new Exception("Stop/focus reset must discard old confirmation");
            p.Observe(new Rectangle(101,100,267,152),11.2);if(p.Confirm(read,11.3))throw new Exception("Changed geometry must discard old confirmation");
            if(!p.Expired(21.3) || p.Confirm(read,21.3))throw new Exception("Unrecognized modal must not remain pending forever or accept late OCR");
            return new List<string> {"PASS: Repel requires two recent matching prompt reads and blocks absent/stale evidence","PASS: dialog disappearance, changed geometry and stop/focus reset invalidate pending renewal","PASS: ignored Repel clicks retry only with fresh reads and stop after a bounded deadline"};
        }
    }
}
