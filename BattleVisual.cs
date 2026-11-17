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
        public Rectangle? BattleBox;
    }
    public sealed class BattleVisual {
        byte[] pixels;
        int width,height,stride;
        int hudRight,hudFirst,hudSpacing,hudWidth,hudHeight;
        public string HudDiagnostic {get;private set;}
        bool GrayDark(int x,int y) {
            int i=y*stride+x*4, b=pixels[i],g=pixels[i+1],r=pixels[i+2];
            return Math.Max(r,Math.Max(g,b))<=85 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=12;
        }
        bool Black(int x,int y,int maximum) {
            int i=y*stride+x*4;
            return pixels[i]<=maximum && pixels[i+1]<=maximum && pixels[i+2]<=maximum;
        }
        bool HeaderGray(int x,int y) {
            int i=y*stride+x*4,b=pixels[i],g=pixels[i+1],r=pixels[i+2];int maximum=Math.Max(r,Math.Max(g,b)),minimum=Math.Min(r,Math.Min(g,b));
            return maximum>=36 && maximum<=105 && maximum-minimum<=15;
        }
        bool HudGray(int x,int y) {
            int i=y*stride+x*4,b=pixels[i],g=pixels[i+1],r=pixels[i+2];int maximum=Math.Max(r,Math.Max(g,b)),minimum=Math.Min(r,Math.Min(g,b));
            return maximum>=18 && maximum<=105 && maximum-minimum<=32;
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
        ChromeReading FloatingPanel() {
            int step=Math.Max(2,height/180),band=Math.Max(6,height/45);
            for(int y=Math.Max(3,height/60);y<(int)(height*.72);y+=step) {
                int run=0,longest=0;
                for(int gx=0;gx<width;gx+=3) {run=HeaderGray(gx,y) ? run+3 : 0;longest=Math.Max(longest,run);}
                if(longest<width*.44)continue;
                for(int x=(int)(width*.15);x<(int)(width*.78);x+=3) {
                    int count=0,sum=0;
                    for(int yy=Math.Max(0,y-band);yy<Math.Min(height,y+band);yy+=2)for(int xx=x;xx<Math.Min(width,x+24);xx+=2)if(Gold(xx,yy)) {count++;sum+=xx;}
                    if(count<Math.Max(8,width/100) || !HeaderGray(sum/count,y))continue;
                    int center=sum/count,left=center,right=center,miss=0;
                    for(int xx=center;xx>=0;xx--) {miss=HeaderGray(xx,y) ? 0 : miss+1;if(miss>=4) {left=xx+4;break;}left=xx;}
                    miss=0;for(int xx=center;xx<width;xx++) {miss=HeaderGray(xx,y) ? 0 : miss+1;if(miss>=4) {right=xx-3;break;}right=xx+1;}
                    int panelWidth=right-left;
                    if(panelWidth<width*.45 || panelWidth>width*.92)continue;
                    int edge=right-Math.Max(6,panelWidth/70),top=y,bottom=y;
                    while(top>0 && HeaderGray(edge,top-1))top--;
                    while(bottom<height-1 && HeaderGray(edge,bottom+1))bottom++;
                    if(bottom-top<height*.015 || bottom-top>height*.09)continue;
                    int dark=0,total=0;
                    for(int yy=bottom+Math.Max(5,panelWidth/60);yy<Math.Min(height,bottom+(int)(panelWidth*.30));yy+=Math.Max(3,height/60))
                        for(int xx=left+(int)(panelWidth*.77);xx<left+(int)(panelWidth*.97);xx+=Math.Max(3,width/90)) {total++;if(PanelBlack(xx,yy))dark++;}
                    // A matching header without its black log panel is uncertain, never a clear world.
                    bool log=total>0 && (double)dark/total>=.72;
                    var box=new Rectangle(left,top,panelWidth,Math.Min(height-top,bottom-top+(int)(panelWidth*.53)));
                    return new ChromeReading {Kind=log ? ChromeKind.Battle : ChromeKind.Transition,GoldX=center,BattleBox=box,Header=new Rectangle(left,Math.Max(0,top-2),panelWidth,Math.Min(height-top,bottom-top+5))};
                }
            }
            return null;
        }
        ChromeReading LegacyWorldHud() {
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
            return new ChromeReading {Kind=rows>=3 && colored>=2 ? ChromeKind.Clear : ChromeKind.Transition,HudRows=rows,ColorRows=colored};
        }
        bool HudRow(int right,int center,out bool sprite) {
            int gray=0,total=0,color=0,middle=0,middleTotal=0;
            int half=Math.Max(4,right/11);
            if(center-half<0 || center+half>=height) {sprite=false;return false;}
            for(int y=center-half;y<=center+half;y+=3)for(int x=(int)(right*.46);x<(int)(right*.94);x+=3) {total++;if(HudGray(x,y))gray++;}
            for(int y=center-2;y<=center+2;y+=2)for(int x=(int)(right*.46);x<(int)(right*.94);x+=3) {middleTotal++;if(HudGray(x,y))middle++;}
            for(int y=center-(int)(right*.16);y<center+(int)(right*.16);y+=3)for(int x=(int)(right*.04);x<(int)(right*.34);x+=3) {
                if(y<0 || y>=height)continue;int i=y*stride+x*4,b=pixels[i],g=pixels[i+1],r=pixels[i+2];int max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b));if(max>100 && max-min>35)color++;
            }
            sprite=color>=3;return total>0 && middleTotal>0 && (double)gray/total>=.50 && (double)middle/middleTotal>=.40;
        }
        ChromeReading WorldHud() {
            if(hudRight>0 && hudWidth==width && hudHeight==height) {
                int rows=0,colors=0;for(int n=0;n<6;n++) {bool sprite;if(HudRow(hudRight,hudFirst+n*hudSpacing,out sprite)) {rows++;if(sprite)colors++;}}
                if(rows>=3 && colors>=2)return new ChromeReading {Kind=ChromeKind.Clear,HudRows=rows,ColorRows=colors};
            }
            // Recover the UI's card width from repeated gray nameplate right edges.
            // Card spacing follows UI scale, independently of the game-window height.
            int maximum=Math.Min(300,(int)(width*.35));var votes=new int[maximum+2];
            for(int y=4;y<Math.Min(height*.18,150);y+=2) {
                int began=-1;
                for(int x=10;x<=maximum;x++) {
                    bool gray=x<maximum && HudGray(x,y);
                    if(gray && began<0)began=x;
                    else if(!gray && began>=0) {if(x-began>=22 && x>=65)votes[x/4*4]++;began=-1;}
                }
            }
            var edges=new List<int>();for(int i=64;i<maximum;i+=4)if(votes[i]>=2)edges.Add(i);
            edges.Sort((a,b)=>votes[b].CompareTo(votes[a]));if(edges.Count>5)edges.RemoveRange(5,edges.Count-5);
            HudDiagnostic="edges="+String.Join(",",edges);
            ChromeReading best=LegacyWorldHud();
            if(best.Kind==ChromeKind.Clear)return best;
            foreach(int edge in edges)for(int spacing=(int)(edge*.32);spacing<=edge*.45;spacing+=2)for(int first=(int)(edge*.15);first<=edge*.39;first+=3) {
                int rows=0,sprites=0;
                for(int n=0;n<6;n++) {bool sprite;bool gray=HudRow(edge,first+n*spacing,out sprite);if(!gray)break;rows++;if(sprite)sprites++;}
                if(rows>=3 && sprites>=2) {hudRight=edge;hudFirst=first;hudSpacing=spacing;hudWidth=width;hudHeight=height;HudDiagnostic+="; matched="+edge+","+first+","+spacing;return new ChromeReading {Kind=ChromeKind.Clear,HudRows=rows,ColorRows=sprites};}
                if(rows>best.HudRows)best=new ChromeReading {Kind=ChromeKind.Transition,HudRows=rows,ColorRows=sprites};
            }
            return best;
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
            var floating=FloatingPanel();if(floating!=null)return floating;
            if(PanelFraction((int)(height*.06))>.92) return new ChromeReading {Kind=ChromeKind.Transition};
            return WorldHud();
        }
    }
    public static class BattleRegions {
        static Rectangle Clip(Rectangle area,Size image) {area.Intersect(new Rectangle(Point.Empty,image));return area;}
        public static Rectangle Menu(ChromeReading chrome,Size image) {
            if(!chrome.BattleBox.HasValue)return new Rectangle((int)(image.Width*.72),(int)(image.Height*.13),(int)(image.Width*.28),(int)(image.Height*.80));
            var b=chrome.BattleBox.Value;return Clip(Rectangle.FromLTRB(b.Left+(int)(b.Width*.73),chrome.Header.Bottom,b.Right,b.Bottom+(int)(b.Width*.05)),image);
        }
        public static Rectangle Own(ChromeReading chrome,Size image) {
            if(!chrome.BattleBox.HasValue)return new Rectangle((int)(image.Width*.40),(int)(image.Height*.45),(int)(image.Width*.33),(int)(image.Height*.34));
            var b=chrome.BattleBox.Value;return Clip(new Rectangle(b.Left+(int)(b.Width*.49),b.Top+(int)(b.Width*.36),(int)(b.Width*.24),(int)(b.Width*.16)),image);
        }
        public static Rectangle Log(ChromeReading chrome,Size image) {
            if(!chrome.BattleBox.HasValue)return new Rectangle(0,(int)(image.Height*.70),(int)(image.Width*.63),(int)(image.Height*.29));
            var b=chrome.BattleBox.Value;return Clip(new Rectangle(b.Left+(int)(b.Width*.74)+3,chrome.Header.Bottom+2,(int)(b.Width*.24)-3,(int)(b.Width*.30)),image);
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
                g.Clear(Color.FromArgb(32,78,45));DrawHud(g,3);
                if(detector.Detect(image).Kind!=ChromeKind.Clear)throw new Exception("Observed three-member party must confirm the world");
                g.Clear(Color.FromArgb(32,78,45));DrawHud(g,2);
                if(detector.Detect(image).Kind!=ChromeKind.Transition)throw new Exception("Insufficient HUD evidence must remain paused");
                g.Clear(Color.FromArgb(32,78,45));DrawHud(g);
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
                g.Clear(Color.FromArgb(32,78,45));DrawHud(g);
                using(var fill=new SolidBrush(Color.FromArgb(55,55,59)))g.FillRectangle(fill,85,60,440,22);
                g.FillRectangle(Brushes.Black,415,82,110,145);g.DrawString("VS.",font,Brushes.Gold,235,61);
                var floating=detector.Detect(image);
                if(floating.Kind!=ChromeKind.Battle || !floating.BattleBox.HasValue || floating.BattleBox.Value.Right>535)throw new Exception("Inset battle panel must override visible party/world HUD: "+floating.Kind+"; "+floating.BattleBox);
                g.FillRectangle(Brushes.White,415,82,110,145);
                if(detector.Detect(image).Kind!=ChromeKind.Transition)throw new Exception("Inset battle header with an uncertain menu must never authorize walking");
            }
            foreach(var size in new Size[] {new Size(1150,864),new Size(1150,600),new Size(1600,900)})using(var image=new Bitmap(size.Width,size.Height))using(var g=Graphics.FromImage(image))using(var fill=new SolidBrush(Color.FromArgb(45,45,49))) {
                g.Clear(Color.FromArgb(32,78,45));
                for(int n=0;n<4;n++) {int cy=38+n*56;g.FillRectangle(fill,48,cy-22,96,44);g.FillEllipse(Brushes.OrangeRed,8,cy-20,35,40);g.FillRectangle(Brushes.LimeGreen,55,cy+14,80,3);}g.Flush();
                if(detector.Detect(image).Kind!=ChromeKind.Clear)throw new Exception("Four-member fixed-scale HUD must be recognized independently of window height: "+size);
                if(detector.Detect(image).Kind!=ChromeKind.Clear)throw new Exception("Cached adaptive HUD layout must remain usable");
                g.Clear(Color.FromArgb(32,78,45));for(int n=0;n<2;n++) {int cy=38+n*56;g.FillRectangle(fill,48,cy-22,96,44);g.FillEllipse(Brushes.OrangeRed,8,cy-20,35,40);}g.Flush();
                if(detector.Detect(image).Kind!=ChromeKind.Transition)throw new Exception("A cached HUD must not trust missing party evidence");
            }
            return new List<string> {"PASS: visual watcher distinguishes battle chrome, world/HUD decoration, dark world and blank/login backgrounds","PASS: walking and startup use visual HUD confirmation without OCR; battle arms recognition; exit resumes after 160ms; focus resets trust","PASS: no opponent OCR runs while walking, during uncertain transitions, or while an accepted escape is settling","PASS: inset battle geometry overrides visible world HUD; uncertain battle menus pause rather than authorize movement","PASS: fixed-scale four-member HUD works across resized game windows; cached layout still rejects missing party evidence"};
        }
        static void DrawHud(Graphics g,int count=6) {
            using(var fill=new SolidBrush(Color.FromArgb(45,45,45)))for(int n=0;n<count;n++) {int cy=(int)(480*(.065+n*.095));g.FillRectangle(fill,40,cy-14,75,28);g.FillEllipse(Brushes.OrangeRed,10,cy-12,25,24);}
        }
    }
}
