using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MovementPrototype {
    public enum ChromeKind { Clear, Battle, Transition }
    public sealed class ChromeReading {
        public ChromeKind Kind;
        public Rectangle Header;
        public int HudRows,ColorRows;
        public int GoldX;
    }
    public sealed class BattleVisual {
        byte[] pixels;
        int width,height,stride;
        bool GrayDark(int x,int y) {
            int i=y*stride+x*4, b=pixels[i],g=pixels[i+1],r=pixels[i+2];
            return Math.Max(r,Math.Max(g,b))<=85 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=12;
        }
        bool Black(int x,int y,int maximum) {
            int i=y*stride+x*4;
            return pixels[i]<=maximum && pixels[i+1]<=maximum && pixels[i+2]<=maximum;
        }
        bool Gold(int x,int y) {
            int i=y*stride+x*4,b=pixels[i],g=pixels[i+1],r=pixels[i+2];
            return r>=155 && g>=95 && g<=235 && b<110 && r>g*1.05 && g>b*1.5;
        }
        bool PanelBlack(int x,int y) {
            int i=y*stride+x*4,b=pixels[i],g=pixels[i+1],r=pixels[i+2];
            return Math.Max(r,Math.Max(g,b))<=32 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=5;
        }
        double PanelFraction(int row) {
            int dark=0,total=0;
            for(int y=row+(int)(height*.08);y<Math.Min(height,row+(int)(height*.46));y+=Math.Max(2,height/30))
                for(int x=(int)(width*.82);x<(int)(width*.97);x+=Math.Max(2,width/40)) { total++; if(PanelBlack(x,y))dark++; }
            return total==0 ? 0 : (double)dark/total;
        }
        ChromeReading WorldHud() {
            int rows=0,colored=0;
            for(int n=0;n<6;n++) {
                int cy=(int)(height*(.065+n*.095)),gray=0,count=0,colors=0;
                for(int dy=-height/70;dy<=height/70;dy+=Math.Max(1,height/140))
                    for(int x=(int)(width*.07);x<(int)(width*.17);x+=Math.Max(1,width/90)) {count++;if(GrayDark(x,cy+dy))gray++;}
                if(count>0 && (double)gray/count>=.45)rows++;
                for(int y=cy-height/35;y<cy+height/35;y+=2)for(int x=(int)(width*.015);x<(int)(width*.06);x+=2) {
                    int i=y*stride+x*4,b=pixels[i],g=pixels[i+1],r=pixels[i+2];
                    if(Math.Max(r,Math.Max(g,b))>100 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))>35)colors++;
                }
                if(colors>=3)colored++;
            }
            return new ChromeReading {Kind=rows>=4 && colored>=2 ? ChromeKind.Clear : ChromeKind.Transition,HudRows=rows,ColorRows=colored};
        }
        public ChromeReading Detect(Bitmap image) {
            width=image.Width; height=image.Height; stride=width*4;
            if(pixels==null || pixels.Length!=stride*height) pixels=new byte[stride*height];
            var data=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                if(data.Stride==stride) Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                else for(int y=0;y<height;y++) Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),pixels,y*stride,stride);
            } finally { image.UnlockBits(data); }
            int black=0,samples=0;
            for(int y=height/8;y<height;y+=Math.Max(1,height/12)) for(int x=width/12;x<width;x+=Math.Max(1,width/16)) {samples++;if(Black(x,y,9))black++;}
            if(samples>0 && (double)black/samples>.94) return new ChromeReading {Kind=ChromeKind.Transition};
            for(int y=Math.Max(2,height/60);y<(int)(height*.30);y+=Math.Max(2,height/180)) {
                int gray=0,total=0;
                for(int x=(int)(width*.06);x<(int)(width*.97);x+=Math.Max(2,width/70)) {total++;if(GrayDark(x,y))gray++;}
                if(total==0 || (double)gray/total<.67) continue;
                int gold=0,minY=height,maxY=0,sumX=0;
                int band=Math.Max(5,height/45);
                for(int gy=Math.Max(0,y-band);gy<Math.Min(height,y+band);gy+=2)
                    for(int x=(int)(width*.20);x<(int)(width*.62);x+=2) if(Gold(x,gy)) {gold++;sumX+=x;minY=Math.Min(minY,gy);maxY=Math.Max(maxY,gy);}
                if(gold<Math.Max(6,width/100) || PanelFraction(y)<.75) continue;
                // The first matching row may touch only the top of the gold letters.
                // Include the full caption line, including white species-name descenders.
                int top=Math.Max(0,y-band), bottom=Math.Min(height,y+Math.Max(28,(int)(height*.075)));
                return new ChromeReading {Kind=ChromeKind.Battle,GoldX=sumX/gold,Header=new Rectangle((int)(width*.10),top,(int)(width*.86),Math.Max(1,bottom-top))};
            }
            if(PanelFraction((int)(height*.06))>.92) return new ChromeReading {Kind=ChromeKind.Transition};
            return WorldHud();
        }
    }
    public enum VisualDecision { ValidateWorld, Move, Pause, ReadBattle }
    public sealed class VisualSceneGate {
        public static bool NeedsOpponentRead(VisualDecision decision,int confirmed,double now,double nextAction) {
            return decision==VisualDecision.ReadBattle && (confirmed<2 || now>=nextAction);
        }
        bool battle, interrupted;
        double? clearBegan;
        int clearFrames;
        public bool InBattle {get{return battle;}}
        public int Epoch {get;private set;}
        public void Reset() {battle=false;interrupted=true;clearBegan=null;clearFrames=0;Epoch++;}
        public void ConfirmWorld() {battle=false;interrupted=false;clearBegan=null;clearFrames=0;}
        public VisualDecision Observe(ChromeKind kind,double now) {
            if(kind==ChromeKind.Battle) {
                if(!battle)Epoch++;
                battle=true;interrupted=true;clearBegan=null;clearFrames=0;
                return VisualDecision.ReadBattle;
            }
            if(kind==ChromeKind.Transition) {interrupted=true;clearBegan=null;clearFrames=0;return VisualDecision.Pause;}
            if(battle || interrupted) {
                if(!clearBegan.HasValue)clearBegan=now;
                clearFrames++;
                if(clearFrames<2 || now-clearBegan.Value<.10)return VisualDecision.Pause;
                if(battle)Epoch++;
                battle=false;interrupted=false;clearBegan=null;clearFrames=0;
            }
            return VisualDecision.Move;
        }
        public static List<string> Tests() {
            var gate=new VisualSceneGate();gate.Reset();
            if(gate.Observe(ChromeKind.Clear,0)!=VisualDecision.Pause || gate.Observe(ChromeKind.Clear,.16)!=VisualDecision.Move)throw new Exception("Startup needs stable visual HUD evidence");
            gate.ConfirmWorld();
            if(gate.Observe(ChromeKind.Clear,1)!=VisualDecision.Move)throw new Exception("Confirmed walking must not request OCR");
            int epoch=gate.Epoch;
            if(gate.Observe(ChromeKind.Battle,2)!=VisualDecision.ReadBattle || gate.Epoch==epoch)throw new Exception("Battle appearance must pause and arm OCR");
            if(gate.Observe(ChromeKind.Clear,3)!=VisualDecision.Pause || gate.Observe(ChromeKind.Clear,3.08)!=VisualDecision.Pause || gate.Observe(ChromeKind.Clear,3.16)!=VisualDecision.Move)throw new Exception("Visual exit should resume after 160ms without OCR");
            if(gate.Observe(ChromeKind.Transition,4)!=VisualDecision.Pause)throw new Exception("Black/transition frames must remain paused");
            gate.Reset();if(gate.Observe(ChromeKind.Clear,5)!=VisualDecision.Pause)throw new Exception("Focus reset must invalidate world trust");
            if(NeedsOpponentRead(VisualDecision.Move,0,1,0) || NeedsOpponentRead(VisualDecision.Pause,0,1,0) || NeedsOpponentRead(VisualDecision.ReadBattle,2,1,3) || !NeedsOpponentRead(VisualDecision.ReadBattle,0,1,3) || !NeedsOpponentRead(VisualDecision.ReadBattle,2,3,3))throw new Exception("OCR must run only for unresolved battle identity or a due retry");
            var detector=new BattleVisual();
            using(var image=new Bitmap(640,480))using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",16,FontStyle.Bold)) {
                g.Clear(Color.FromArgb(32,78,45));
                if(detector.Detect(image).Kind!=ChromeKind.Transition)throw new Exception("A colored background without the game HUD must remain paused");
                DrawHud(g);
                if(detector.Detect(image).Kind!=ChromeKind.Clear)throw new Exception("Visible game HUD must confirm world without OCR");
                g.FillRectangle(Brushes.DimGray,150,0,200,35);g.DrawString("VS",font,Brushes.Gold,200,5);
                if(detector.Detect(image).Kind!=ChromeKind.Clear)throw new Exception("Gold HUD decoration alone must not trigger");
                using(var bar=new SolidBrush(Color.FromArgb(40,40,42)))foreach(int top in new int[] {6,50,85}) {
                    g.Clear(Color.FromArgb(32,78,45));g.FillRectangle(bar,0,top,640,30);
                    g.FillRectangle(Brushes.Black,480,top+30,160,240);g.DrawString("VS.",font,Brushes.Gold,205,top+2);
                    if(detector.Detect(image).Kind!=ChromeKind.Battle)throw new Exception("Battle chrome must trigger across observed panel positions");
                }
                g.Clear(Color.FromArgb(10,27,17));
                DrawHud(g);
                if(detector.Detect(image).Kind!=ChromeKind.Clear)throw new Exception("Dark colored world must not be mistaken for a battle");
                g.Clear(Color.Black);if(detector.Detect(image).Kind!=ChromeKind.Transition)throw new Exception("Blank transitions must pause without OCR");
            }
            return new List<string> {"PASS: visual watcher distinguishes battle chrome, world/HUD decoration, dark world and blank/login backgrounds","PASS: walking and startup use visual HUD confirmation without OCR; battle arms recognition; exit resumes after 160ms; focus resets trust","PASS: no opponent OCR runs while walking, during uncertain transitions, or while an accepted escape is settling"};
        }
        static void DrawHud(Graphics g) {
            using(var fill=new SolidBrush(Color.FromArgb(45,45,45)))for(int n=0;n<6;n++) {int cy=(int)(480*(.065+n*.095));g.FillRectangle(fill,40,cy-14,75,28);g.FillEllipse(Brushes.OrangeRed,10,cy-12,25,24);}
        }
    }
}
