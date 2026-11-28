using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
#if !LINUX
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using System.Runtime.InteropServices.WindowsRuntime;
#endif

namespace MovementPrototype {
    public sealed class EncounterReadGrace {
        double? began;
        public void Reset() { began=null; }
        public bool Expired(double now) {
            if(!began.HasValue) began=now;
            return now-began.Value>=10;
        }
    }
    public static class LocalOcr {
        public static string LastRepelText {get;private set;}
#if !LINUX
        static readonly Queue<OcrEngine> engines=new Queue<OcrEngine>();
        static OcrEngine AcquireEngine() {
            lock(engines)if(engines.Count>0)return engines.Dequeue();
            var engine=OcrEngine.TryCreateFromUserProfileLanguages();
            if(engine==null)throw new InvalidOperationException("Windows OCR is unavailable. Install a supported OCR language or disable encounter handling.");
            return engine;
        }
#endif
        static Bitmap EnlargeRectangle(Bitmap image,Rectangle area,int scale=3) {
            area.Intersect(new Rectangle(0,0,image.Width,image.Height));
            if(area.Width<1 || area.Height<1)throw new InvalidOperationException("Battle heading is outside the game window.");
            var result=new Bitmap(area.Width*scale,area.Height*scale);
            using(var g=Graphics.FromImage(result)) {g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(image,new Rectangle(0,0,result.Width,result.Height),area,GraphicsUnit.Pixel);}
            return result;
        }
        public static async Task<bool> ReadRareEncounterNotice(Bitmap image) {
            // The encounter popup is in the game viewport, above the chat feed.
            var viewport=new Rectangle(0,0,image.Width,Math.Max(1,image.Height*4/5));
            double scale=Math.Max(1,Math.Min(2,1440.0/viewport.Width));
            var notice=await ReadCatchRegion(image,viewport,scale:scale);
            if(EncounterRecognition.RareEncounterNotice(notice.Text))return true;
#if LINUX
            var center=Rectangle.FromLTRB(image.Width/7,image.Height/7,image.Width*6/7,image.Height*4/5);
            var clean=await ReadCatchRegion(image,center,contrast:true,scale:2);
            return EncounterRecognition.RareEncounterNotice(clean.Text);
#else
            return false;
#endif
        }
        public static Rectangle EncounterFeedArea(Size size) {
            int left=(int)(size.Width*.57),top=(int)(size.Height*.72);
            return Rectangle.FromLTRB(left,top,size.Width,(int)(size.Height*.97));
        }
        public static HashSet<ulong> EncounterFeedHighlights(Bitmap image) {
            // Compare event/shiny glyphs by line, independent of vertical scroll.
            var area=Rectangle.FromLTRB((int)(image.Width*.63),(int)(image.Height*.83),image.Width,(int)(image.Height*.97));
            var lines=new HashSet<ulong>();
            using(var crop=image.Clone(area,System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
                var bits=crop.LockBits(new Rectangle(Point.Empty,crop.Size),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try {
                    var row=new byte[crop.Width*4];
                    const ulong basis=14695981039346656037UL,prime=1099511628211UL;
                    ulong line=basis;
                    int rows=0,first=0,timeRows=0;
                    int timeWidth=Math.Max(22,(int)(crop.Width*.27));
                    int highlightStart=(int)(crop.Width*.22),maxTextHeight=Math.Max(12,image.Height/48);
                    for(int y=0;y<crop.Height;y++) {
                        Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),row,0,row.Length);
                        ulong glyphs=basis;
                        int count=0,timeInk=0;
                        for(int x=0;x<crop.Width;x++) {
                            int b=row[x*4],g=row[x*4+1],r=row[x*4+2];
                            bool red=r>=110 && r>g*1.7 && r>b*1.5;
                            bool purple=r>=120 && b>=140 && r>g*1.15 && b>g*1.15;
                            if(x<timeWidth && r>=147 && g>=147 && b>=147)timeInk++;
                            if(x>=highlightStart && (red || purple)) {
                                glyphs=unchecked((glyphs^(uint)(x*2+(purple ? 1 : 0)))*prime);
                                count++;
                            }
                        }
                        if(count>=3) {
                            if(rows++==0)first=y;
                            line=unchecked((line^glyphs)*prime);
                            if(timeInk>=3)timeRows++;
                        } else {
                            if(rows>=2 && rows<=maxTextHeight && timeRows>=2 && first>0)lines.Add(line);
                            rows=timeRows=0;line=basis;
                        }
                    }
                    if(rows>=2 && rows<=maxTextHeight && timeRows>=2 && first>0)lines.Add(line);
                } finally {crop.UnlockBits(bits);}
            }
            return lines;
        }
        public static async Task<string> ReadRareEncounterFeed(Bitmap image,Rectangle area) {
            var reads=await Task.WhenAll(ReadCatchRegion(image,area,scale:2.5),
                ReadCatchRegion(image,area,contrast:true,scale:4,encounterFeed:true));
            var lines=new List<string>();
            foreach(var feed in reads) {
                foreach(var row in feed.Rows) {
                    string line=row.Text;
                    if(Regex.IsMatch(line,@"^[A-Za-z0-9_]{2,24}\s+encountered\s+an?\s+",RegexOptions.IgnoreCase)) {
                        var timeArea=Rectangle.FromLTRB(area.Left,Math.Max(area.Top,(int)row.Bounds.Top-4),
                            (int)row.Bounds.Left,Math.Min(area.Bottom,(int)Math.Ceiling(row.Bounds.Bottom)+3));
                        if(timeArea.Width>=24 && timeArea.Height>0) {
                            var times=await Task.WhenAll(ReadCatchRegion(image,timeArea,pixelScale:true,scale:5),
                                ReadCatchRegion(image,timeArea,contrast:true,scale:4));
                            foreach(var time in times) {
                                var digits=Regex.Replace(time.Text,@"[^0-9]","");
                                if(digits.Length!=6)continue;
                                line="["+digits.Insert(4,":").Insert(2,":")+"] "+line;
                                break;
                            }
                        }
                    }
                    lines.Add(line);
                }
            }
            return String.Join("\n",lines);
        }
        public static async Task<RunButtonCandidate> ReadBattle(Bitmap image,Rectangle heading,bool locateButton,Rectangle? menuBounds=null) {
            RunButtonCandidate candidate;
            if(locateButton)candidate=await FindRunButton(image,heading,menuBounds);
            else using(var crop=EnlargeRectangle(image,heading)) {
                var result=await Recognize(crop);candidate=new RunButtonCandidate {Header=TextOf(result),Controls="",IdentityBounds=CaptionBounds(result,heading,image.Size)};
            }
            if(EncounterRecognition.Parse("[Encounter header]\n"+candidate.Header+"\n[Battle controls]\n\n[Full screen]\n").Name!=null)return candidate;
            using(var mask=RepelOcrImage(image,heading,true)) {
                var result=await Recognize(mask);string text=TextOf(result);
                if(EncounterRecognition.Parse("[Encounter header]\n"+text+"\n[Battle controls]\n\n[Full screen]\n").Name!=null) {candidate.Header=text;candidate.IdentityBounds=CaptionBounds(result,heading,image.Size);}
            }
            if(EncounterRecognition.Parse("[Encounter header]\n"+candidate.Header+"\n[Battle controls]\n\n[Full screen]\n").Name==null) {
                var sharp=await ReadCatchRegion(image,heading,false,true);
                if(EncounterRecognition.Parse("[Encounter header]\n"+sharp.Text+"\n[Battle controls]\n\n[Full screen]\n").Name!=null)candidate.Header=sharp.Text;
            }
            return candidate;
        }
        public static Bitmap RepelOcrImage(Bitmap image,Rectangle area,bool keepGold=false,int scale=3,bool encounterFeed=false) {
            using(var mask=image.Clone(area,System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
                var bits=mask.LockBits(new Rectangle(Point.Empty,mask.Size),System.Drawing.Imaging.ImageLockMode.ReadWrite,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try {
                    byte[] row=new byte[mask.Width*4];
                    for(int y=0;y<mask.Height;y++) {
                        var pointer=IntPtr.Add(bits.Scan0,y*bits.Stride);
                        System.Runtime.InteropServices.Marshal.Copy(pointer,row,0,row.Length);
                        for(int x=0;x<row.Length;x+=4) {
                            int min=Math.Min(row[x],Math.Min(row[x+1],row[x+2])),max=Math.Max(row[x],Math.Max(row[x+1],row[x+2]));
                            bool gold=keepGold && row[x+2]>=155 && row[x+1]>=95 && row[x+1]<=235 && row[x]<110 && row[x+2]>row[x+1]*1.05;
                            int blue=row[x],green=row[x+1],red=row[x+2];
                            if(encounterFeed) {
                                // Event names are dark red against the blue chat background.
                                if(red>=75 && red>green*1.65 && red>blue*1.38) {
                                    byte bright=(byte)Math.Min(255,(red-55)*3);
                                    row[x]=row[x+1]=row[x+2]=bright;
                                }
                                continue;
                            }
                            byte value=(byte)((min>=140 && max-min<=90 || gold) ? 0 : 255);
                            row[x]=row[x+1]=row[x+2]=value;row[x+3]=255;
                        }
                        System.Runtime.InteropServices.Marshal.Copy(row,0,pointer,row.Length);
                    }
                } finally {mask.UnlockBits(bits);}
                return EnlargeRectangle(mask,new Rectangle(Point.Empty,mask.Size),scale);
            }
        }
        public static async Task<RepelPromptReading> ReadRepel(Bitmap image,Rectangle area) {
            int buttonTop=(int)(area.Height*.76);
            using(var header=EnlargeRectangle(image,new Rectangle(area.X,area.Y,area.Width,buttonTop)))
            using(var buttons=RepelOcrImage(image,new Rectangle(area.X,area.Y+buttonTop,area.Width,area.Height-buttonTop))) {
                var text=Recognize(header);var controls=Recognize(buttons);await Task.WhenAll(text,controls);
                var words=new List<ButtonWord>();
                foreach(var line in controls.Result.Lines)foreach(var word in line.Words) {
                    var r=word.BoundingRect;words.Add(new ButtonWord(word.Text,(float)r.X,(float)r.Y+buttonTop*3,(float)r.Width,(float)r.Height));
                }
                LastRepelText=TextOf(text.Result)+"\n"+TextOf(controls.Result);
                var reading=RepelPromptMatch.Parse(LastRepelText,words,area.Width*3,area.Height*3);
                if(reading!=null)reading.Yes=new Point(area.Left+reading.Yes.X/3,area.Top+reading.Yes.Y/3);
                return reading;
            }
        }
        static string TextOf(OcrResult result) {var lines=new List<string>();foreach(var line in result.Lines)lines.Add(line.Text);return String.Join("\n",lines);}
        static Rectangle? CaptionBounds(OcrResult result,Rectangle source,Size frame) {
            RectangleF bounds=RectangleF.Empty;bool found=false;
            foreach(var line in result.Lines) {
                if(!Regex.IsMatch(line.Text,@"\bWild\b",RegexOptions.IgnoreCase))continue;
                foreach(var word in line.Words) {
                    var r=word.BoundingRect;var rect=new RectangleF((float)r.X,(float)r.Y,(float)r.Width,(float)r.Height);
                    bounds=found ? RectangleF.Union(bounds,rect) : rect;found=true;
                }
            }
            if(!found)return null;
            var area=Rectangle.FromLTRB(source.Left+(int)Math.Floor(bounds.Left/3)-2,source.Top+(int)Math.Floor(bounds.Top/3)-2,source.Left+(int)Math.Ceiling(bounds.Right/3)+2,source.Top+(int)Math.Ceiling(bounds.Bottom/3)+2);
            area.Intersect(new Rectangle(Point.Empty,frame));return area.Width>0 && area.Height>0 ? (Rectangle?)area : null;
        }
        static Bitmap EnlargeRegion(Bitmap image, double x, double y, double width, double height) {
            var area=new Rectangle((int)(image.Width*x),(int)(image.Height*y),Math.Max(1,(int)(image.Width*width)),Math.Max(1,(int)(image.Height*height)));
            var enlarged=new Bitmap(area.Width*3,area.Height*3);
            using(var graphics=Graphics.FromImage(enlarged)) {
                graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image,new Rectangle(0,0,enlarged.Width,enlarged.Height),area,GraphicsUnit.Pixel);
            }
            return enlarged;
        }
        public static async Task<string> ReadGame(Bitmap image, bool detailed=true) {
            using(var header=EnlargeRegion(image,.14,0,.72,.28))
            using(var tabs=EnlargeRegion(image,.45,.57,.55,.15)) {
                if(!detailed) {
                    var quick=await Task.WhenAll(Read(header),Read(tabs));
                    return "[Encounter header]\n"+quick[0]+"\n[Battle controls]\n\n[World tabs]\n"+quick[1]+"\n[Full screen]\n";
                }
                using(var buttons=EnlargeRegion(image,.72,.50,.28,.43)) {
                    var readings=await Task.WhenAll(Read(header),Read(buttons),Read(tabs),Read(image));
                    return "[Encounter header]\n"+readings[0]+"\n[Battle controls]\n"+readings[1]+"\n[World tabs]\n"+readings[2]+"\n[Full screen]\n"+readings[3];
                }
            }
        }
        public static async Task<RunButtonCandidate> FindRunButton(Bitmap image,Rectangle? headingBounds=null,Rectangle? menuBounds=null) {
            var menu=menuBounds ?? new Rectangle((int)(image.Width*.72),(int)(image.Height*.50),(int)(image.Width*.28),(int)(image.Height*.43));
            using(var header=headingBounds.HasValue ? EnlargeRectangle(image,headingBounds.Value) : EnlargeRegion(image,.14,0,.72,.28)) {
                var heading=Recognize(header);
                var commands=ReadCatchRegion(image,menu);
                await Task.WhenAll(heading,commands);
                var result=commands.Result;
                Point? run,items;
                if(!RunButtonMatch.FindControls(result,menu,out run,out items) &&
                    !RunButtonMatch.IsSubmenu(result.Text)) {
                    var alternate=await ReadCatchRegion(image,menu,true);
                    if(RunButtonMatch.FindControls(alternate,menu,out run,out items))result=alternate;
                }
                return new RunButtonCandidate {
                    Header=TextOf(heading.Result),Controls=result.Text,
                    IdentityBounds=CaptionBounds(heading.Result,headingBounds ?? new Rectangle((int)(image.Width*.14),0,(int)(image.Width*.72),(int)(image.Height*.28)),image.Size),
                    ClientPoint=run,ItemsPoint=items
                };
            }
        }
        public static async Task<string> Read(Bitmap image) {
            var result=await Recognize(image);
            var lines=new List<string>();
            foreach(var line in result.Lines) lines.Add(line.Text);
            return String.Join("\n",lines);
        }
        public static async Task<CatchTextRegion> ReadCatchRegion(Bitmap image,Rectangle area,bool contrast=false,bool pixelScale=false,double scale=0,bool encounterFeed=false) {
            area.Intersect(new Rectangle(Point.Empty,image.Size));if(area.Width<1 || area.Height<1)throw new InvalidOperationException("Catching region is outside the client.");
            double factor=Math.Min(scale>0 ? scale : pixelScale ? 6 : 3,(double)OcrEngine.MaxImageDimension/Math.Max(area.Width,area.Height));
            using(var crop=contrast ? RepelOcrImage(image,area,scale:Math.Max(1,(int)factor),encounterFeed:encounterFeed) : new Bitmap(Math.Max(1,(int)(area.Width*factor)),Math.Max(1,(int)(area.Height*factor)))) {
                if(!contrast)using(var graphics=Graphics.FromImage(crop)) {graphics.InterpolationMode=pixelScale ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;graphics.DrawImage(image,new Rectangle(Point.Empty,crop.Size),area,GraphicsUnit.Pixel);}
                float sx=(float)area.Width/crop.Width,sy=(float)area.Height/crop.Height;
                var result=await Recognize(crop);var region=new CatchTextRegion {Text=TextOf(result)};
                foreach(var line in result.Lines) {
                    RectangleF bounds=RectangleF.Empty;bool first=true;var row=new CatchTextRow {Text=line.Text};
                    foreach(var word in line.Words) {var r=word.BoundingRect;var b=new RectangleF(area.Left+(float)r.X*sx,area.Top+(float)r.Y*sy,(float)r.Width*sx,(float)r.Height*sy);bounds=first ? b : RectangleF.Union(bounds,b);first=false;row.Words.Add(new ButtonWord(word.Text,b.X,b.Y,b.Width,b.Height));}
                    if(!first) {row.Bounds=bounds;region.Rows.Add(row);}
                }
                return region;
            }
        }
        public static async Task<int> ReadMoveCounter(Bitmap image,Rectangle area) {
            var sharp=ReadCatchRegion(image,area,pixelScale:true,scale:8);
            var contrast=ReadCatchRegion(image,area,contrast:true,scale:8);
            await Task.WhenAll(sharp,contrast);
            int sharpPp=CatchVision.ReadExplicitPp(sharp.Result.Text);
            int contrastPp=CatchVision.ReadExplicitPp(contrast.Result.Text);
            if(sharpPp>=0 && contrastPp>=0 && sharpPp!=contrastPp)return -1;
            if(sharpPp>=0 || contrastPp>=0)return Math.Max(sharpPp,contrastPp);
            int compact=CatchVision.MovePp(sharp.Result.Text);
            return compact==CatchVision.MovePp(contrast.Result.Text) ? compact : -1;
        }
        static async Task<int> ReadCompactMoveCounter(Bitmap image,Rectangle area) {
            int value=await ReadMoveCounter(image,area);
            if(value>=0)return value;
            var tight=Rectangle.FromLTRB(Math.Max(area.Left,area.Right-45),area.Top+3,area.Right,area.Bottom-2);
            var reads=await Task.WhenAll(ReadCatchRegion(image,tight,scale:5),
                ReadCatchRegion(image,tight,pixelScale:true,scale:10),
                ReadCatchRegion(image,tight,contrast:true,scale:6));
            var counts=new Dictionary<int,int>();
            foreach(var read in reads) {
                int pp=CatchVision.ReadExplicitPp(read.Text);
                if(pp<0)continue;
                int count;counts.TryGetValue(pp,out count);
                if(count>0)return pp;
                counts[pp]=1;
            }
            foreach(var key in counts.Keys)if(counts.Count==1)return key;
            return -1;
        }
        public static async Task<CatchFrame> ReadCatch(Bitmap image,ChromeReading chrome,CatchConfig config,string species,string move,int slot,long id,double at,bool partyNeeded=true,bool selectingBall=false,Rectangle? verifiedCaption=null,Rectangle? verifiedOwn=null,bool waitingForBall=false) {
            if(chrome.Kind==ChromeKind.Clear && !chrome.BattleBox.HasValue) {
                var area=new Rectangle((int)(image.Width*.60),(int)(image.Height*.32),(int)(image.Width*.35),(int)(image.Height*.19));
                var preview=await ReadCatchRegion(image,area);var world=CatchVision.Preview(preview,species,id,at);world.ReadDetail=preview.Text;
                if(!String.IsNullOrWhiteSpace(config.ExpectedLead)) {
                    var lead=await ReadCatchRegion(image,new Rectangle(0,0,(int)(image.Width*.15),(int)(image.Height*.14)));
                    world.LeadPokemon=CatchVision.LeadName(lead,config,image.Size);
                }
                foreach(var kind in world.Controls.Keys)world.InputSeals[kind]=CatchVision.InputSeal(image,world,kind);
                return world;
            }
            var headerArea=chrome.Kind==ChromeKind.Battle || chrome.BattleBox.HasValue ? chrome.Header : new Rectangle(0,0,image.Width,(int)(image.Height*.30));
            var menuArea=BattleRegions.Menu(chrome,image.Size);
            var bagArea=new Rectangle((int)(image.Width*.20),(int)(image.Height*.18),(int)(image.Width*.58),(int)(image.Height*.48));
            var ownArea=BattleRegions.Own(chrome,image.Size);
            var logArea=BattleRegions.Log(chrome,image.Size);
            var leadArea=new Rectangle(0,0,(int)(image.Width*.15),(int)(image.Height*.53));
            var empty=Task.FromResult(new CatchTextRegion());
            bool readOwn=!selectingBall && !waitingForBall;
            var regions=await Task.WhenAll(
                verifiedCaption.HasValue
                    ? Task.FromResult(new CatchTextRegion {Text="VS. Wild "+species})
                    : ReadCatchRegion(image,headerArea),
                ReadCatchRegion(image,menuArea,selectingBall || !String.IsNullOrEmpty(move)),
                !readOwn || verifiedOwn.HasValue ? empty : ReadCatchRegion(image,ownArea),
                selectingBall ? empty : ReadCatchRegion(image,logArea),
                partyNeeded ? ReadCatchRegion(image,leadArea) : empty,
                chrome.BattleBox.HasValue ? empty : ReadCatchRegion(image,bagArea),
                selectingBall ? empty : ReadCatchRegion(image,BattleRegions.BottomLog(chrome,image.Size)));
            regions[3].Text+="\n"+regions[6].Text;
            if(EncounterRecognition.Parse("[Encounter header]\n"+regions[0].Text+"\n[Battle controls]\n\n[Full screen]\n").Name==null) {
                var sharp=await ReadCatchRegion(image,headerArea,false,true);
                if(EncounterRecognition.Parse("[Encounter header]\n"+sharp.Text+"\n[Battle controls]\n\n[Full screen]\n").Name!=null)regions[0]=sharp;
                else regions[0]=await ReadCatchRegion(image,headerArea,true);
            }
            if(Regex.IsMatch(regions[1].Text,@"Choose\s*Attack",RegexOptions.IgnoreCase)) {
                foreach(var row in regions[1].Rows)row.Text=Regex.Replace(row.Text,@"(\d{1,2})\s*[/\\'’.,:]\s*(\d{1,2})","$1/$2");
            }
            bool compactItems=Regex.IsMatch(regions[1].Text,@"Choose\s*Item",RegexOptions.IgnoreCase);
            // The read-only check uses high contrast and can see counts that
            // disappear in the normal read once the move sequence is complete.
            if(!selectingBall && compactItems && CatchVision.StockArea(regions[1],config.Ball,image.Size,true).HasValue) {
                var itemContrast=await ReadCatchRegion(image,menuArea,true);
                if(Regex.IsMatch(itemContrast.Text,@"Choose\s*Item",RegexOptions.IgnoreCase))
                    regions[1]=itemContrast;
            }
            var stockRegion=compactItems ? regions[1] : regions[5];
            var stockArea=selectingBall && compactItems ? null : CatchVision.StockArea(stockRegion,config.Ball,image.Size,compactItems);
            if(stockArea.HasValue) {
                var count=await ReadCatchRegion(image,stockArea.Value);
                stockRegion.Rows.AddRange(count.Rows);
                stockRegion.Text+="\n"+count.Text;
                if(compactItems && CatchVision.StockArea(stockRegion,config.Ball,image.Size,true).HasValue) {
                    var sharpCount=await ReadCatchRegion(image,stockArea.Value,false,true);
                    stockRegion.Rows.AddRange(sharpCount.Rows);
                    stockRegion.Text+="\n"+sharpCount.Text;
                }
                if(compactItems && CatchVision.StockArea(stockRegion,config.Ball,image.Size,true).HasValue) {
                    var contrastCount=await ReadCatchRegion(image,stockArea.Value,true);
                    stockRegion.Rows.AddRange(contrastCount.Rows);
                    stockRegion.Text+="\n"+contrastCount.Text;
                }
            }
            if(readOwn && !verifiedOwn.HasValue && !String.IsNullOrWhiteSpace(config.Catcher) && CatchVision.OwnCaption(regions[2],config.Catcher,image.Size).IsEmpty) {
                var caption=BattleRegions.OwnNameplate(image,ownArea);
                var fallback=await ReadCatchRegion(image,caption ?? ownArea);
                if(CatchVision.OwnCaption(fallback,config.Catcher,image.Size).IsEmpty) {
                    var fromRight=BattleRegions.OwnNameplate(image,ownArea,true);
                    if(fromRight.HasValue && fromRight!=caption) {
                        var anchored=await ReadCatchRegion(image,fromRight.Value);
                        if(!CatchVision.OwnCaption(anchored,config.Catcher,image.Size).IsEmpty)fallback=anchored;
                    }
                }
                if(CatchVision.OwnCaption(fallback,config.Catcher,image.Size).IsEmpty && caption.HasValue)
                    fallback=await ReadCatchRegion(image,caption.Value,false,true);
                if(CatchVision.OwnCaption(fallback,config.Catcher,image.Size).IsEmpty)
                    fallback=await ReadCatchRegion(image,caption ?? ownArea,true);
                if(CatchVision.OwnCaption(fallback,config.Catcher,image.Size).IsEmpty && caption.HasValue)
                    fallback=await ReadCatchRegion(image,ownArea,true);
                regions[2].Text+="\n"+fallback.Text;
                regions[2].Rows.AddRange(fallback.Rows);
            }
            var frame=CatchVision.Parse(regions[0],regions[1],regions[2],regions[3],regions[4],chrome,image,config,species,move,slot,id,at,regions[5]);
            if(frame.Wild && (frame.View==CatchView.Unknown ||
                frame.View==CatchView.Moves && !String.IsNullOrEmpty(move) && frame.Control(CatchActionKind.ChooseMove)==null)) {
                var sharpMenu=await ReadCatchRegion(image,menuArea,pixelScale:true);
                var alternate=CatchVision.Parse(regions[0],sharpMenu,regions[2],regions[3],regions[4],chrome,image,config,species,move,slot,id,at,regions[5]);
                CatchVision.AddNavigation(alternate,sharpMenu,image,menuArea);
                if(frame.View==CatchView.Unknown && alternate.View!=CatchView.Unknown ||
                    alternate.View==CatchView.Moves && alternate.Control(CatchActionKind.ChooseMove)!=null) {
                    frame=alternate;
                    regions[1]=sharpMenu;
                }
            }
            if(frame.Wild && !frame.Failure && frame.Control(CatchActionKind.UseBall)==null) {
                var list=CatchVision.CompactItemList(regions[1],chrome,image.Size);
                if(list.HasValue && list.Value.Width>0 && list.Value.Height>0) {
                    var items=await ReadCatchRegion(image,list.Value,pixelScale:true);
                    var ball=CatchVision.CompactListedBall(items,config.Ball);
                    if(ball==null) {
                        items=await ReadCatchRegion(image,list.Value,scale:5);
                        ball=CatchVision.CompactListedBall(items,config.Ball);
                    }
                    if(ball!=null) {
                        frame.View=CatchView.BallList;
                        frame.Controls[CatchActionKind.UseBall]=ball;
                    }
                }
            }
            string ppRetry="";
            if(frame.Wild && !frame.Failure && frame.View==CatchView.Moves && frame.Control(CatchActionKind.ChooseMove)==null) {
                var counterArea=CatchVision.MoveCounterArea(regions[1],move,slot,image.Size);
                if(counterArea.HasValue) {
                    int pp=await ReadMoveCounter(image,counterArea.Value);
                    ppRetry=" [Isolated "+move+" PP] "+(pp>=0 ? pp.ToString() : "unreadable");
                    var control=CatchVision.MoveWithVerifiedPp(regions[1],move,slot,counterArea.Value,pp);
                    if(control!=null)frame.Controls[CatchActionKind.ChooseMove]=control;
                }
            }
            if(frame.Wild && !frame.Failure && frame.View==CatchView.Moves && frame.Control(CatchActionKind.ChooseMove)==null &&
                !String.IsNullOrEmpty(move)) {
                var card=CatchVision.CompactMoveCard(regions[1],chrome,slot,image.Size);
                if(card.HasValue) {
                    var rect=card.Value;
                    var nameArea=Rectangle.FromLTRB(rect.Left,rect.Top,rect.Left+rect.Width*3/4,rect.Top+rect.Height/2+1);
                    var ppArea=Rectangle.FromLTRB(rect.Left+rect.Width/2,rect.Top+rect.Height/3,rect.Right,rect.Bottom);
                    var name=ReadCatchRegion(image,nameArea,pixelScale:true,scale:7);
                    int visiblePp=CatchVision.CompactMovePp(regions[1],ppArea);
                    var pp=visiblePp>=0 ? Task.FromResult(visiblePp) : ReadCompactMoveCounter(image,ppArea);
                    await Task.WhenAll(name,pp);
                    int usablePp=pp.Result>=0 ? pp.Result : CatchVision.CompactMovePp(regions[1],ppArea,true);
                    var control=CatchVision.CompactMoveControl(name.Result,move,slot,ppArea,usablePp);
                    if(control==null && usablePp>=0) {
                        var contrastName=await ReadCatchRegion(image,nameArea,contrast:true,scale:7);
                        control=CatchVision.CompactMoveControl(contrastName,move,slot,ppArea,usablePp);
                    }
                    ppRetry+=" [Compact card "+slot+"] "+name.Result.Text.Replace('\n',' ')+" PP="+(usablePp>=0 && pp.Result<0 ? "available; count unreadable" : pp.Result.ToString());
                    if(control!=null)frame.Controls[CatchActionKind.ChooseMove]=control;
                }
            }
            if(verifiedOwn.HasValue && readOwn)frame.ActivePokemon=config.Catcher;
            CatchVision.AddNavigation(frame,regions[1],image,menuArea);
            frame.HeaderSeal=verifiedCaption ?? CatchVision.WildCaption(regions[0],image.Size,headerArea);frame.OwnSeal=readOwn ? verifiedOwn ?? CatchVision.OwnCaption(regions[2],config.Catcher,image.Size) : Rectangle.Empty;
            if(frame.OwnSeal.IsEmpty && frame.ActivePokemon!=null)frame.OwnSeal=ownArea;
            var moveDetails=regions[1].Rows.FindAll(r=>Regex.IsMatch(r.Text.Trim(),@"^[0-9x× /'.,:]+$",RegexOptions.IgnoreCase));
            frame.ReadDetail=String.Join(" | ",moveDetails.ConvertAll(r=>r.Text))+ppRetry+" [Active caption] "+regions[2].Text.Replace('\n',' ');
            if(frame.View==CatchView.Unknown || frame.View==CatchView.Moves && frame.Control(CatchActionKind.ChooseMove)==null)
                frame.ReadDetail+=" [Battle controls] "+regions[1].Text.Replace('\n',' ')+" [Battle box] "+chrome.BattleBox;
            foreach(var kind in frame.Controls.Keys)frame.InputSeals[kind]=CatchVision.InputSeal(image,frame,kind);
            return frame;
        }
#if LINUX
        static Task<OcrResult> Recognize(Bitmap image) {
            return Task.Run(()=>LinuxOcr.Recognize(image));
        }
#else
        static async Task<OcrResult> Recognize(Bitmap image) {
            int maximum=(int)OcrEngine.MaxImageDimension;
            if(image.Width>maximum || image.Height>maximum) {
                double ratio=Math.Min((double)maximum/image.Width,(double)maximum/image.Height);
                using(var resized=new Bitmap(Math.Max(1,(int)(image.Width*ratio)),Math.Max(1,(int)(image.Height*ratio)))) {
                    using(var g=Graphics.FromImage(resized))g.DrawImage(image,0,0,resized.Width,resized.Height);
                    return await Recognize(resized);
                }
            }
            var engine=AcquireEngine();
            try {
                var data=image.LockBits(new Rectangle(Point.Empty,image.Size),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                byte[] bytes;
                try {
                    int rowSize=image.Width*4;
                    bytes=new byte[rowSize*image.Height];
                    if(data.Stride==rowSize)Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                    else for(int y=0;y<image.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),bytes,y*rowSize,rowSize);
                } finally {image.UnlockBits(data);}
                using(var pixels=SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(),BitmapPixelFormat.Bgra8,image.Width,image.Height,BitmapAlphaMode.Ignore))
                    return await System.WindowsRuntimeSystemExtensions.AsTask<OcrResult>(engine.RecognizeAsync(pixels));
            } finally {lock(engines)engines.Enqueue(engine);}
        }
#endif
    }
    public sealed class EncounterReading {
        public string Name;
        public bool Battle, Commands, Overworld, Ambiguous;
    }
    public static class EncounterRecognition {
        static readonly HashSet<string> Species=LoadSpecies();
        static HashSet<string> LoadSpecies() {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"pokemon-names.txt");
            if(!File.Exists(path)) return new HashSet<string>();
            var set=new HashSet<string>();
            foreach(string line in File.ReadAllLines(path)) if(line.Trim().Length>0) set.Add(Normalize(line));
            return set;
        }
        public static string Normalize(string name) { return Regex.Replace(name.ToLowerInvariant(),"[^a-z0-9]",""); }
        public static HashSet<string> Targets(string text) {
            var names=new HashSet<string>();
            foreach(string raw in text.Split(new char[] {',',';','\n'},StringSplitOptions.RemoveEmptyEntries)) {
                string n=Normalize(raw.Trim());
                if(!Species.Contains(n)) throw new ArgumentException("Unknown target Pokémon: " + raw.Trim() + ". Use a full species name.");
                names.Add(n);
            }
            if(names.Count==0) throw new ArgumentException("Enter at least one target Pokémon before enabling encounter handling.");
            return names;
        }
        public static bool RareEncounterNotice(string text) {
            return Regex.IsMatch(text ?? "",
                @"(?:^|\n)[ \t]*You\s*(?:have\s+|['\u2019]?ve\s+)encountered\s+(?:a\s+)?(?:rare\s+(?:form|encounter)|shiny\s+Pok[e\u00e9]mon)[!?. \t]*(?=\r?$|\r?\n)|(?:^|\n)[ \t]*You\s+got\s+a\s+rare\s+encounter[!?. \t]*(?=\r?$|\r?\n)",
                RegexOptions.IgnoreCase);
        }
        public static string RareEncounterAnnouncement(string caption,string feed,string species,DateTime battleStart,DateTime capturedAt) {
            if(String.IsNullOrEmpty(species))return null;
            var title=Regex.Match(caption ?? "",@"(?im)^\s*([A-Za-z0-9_]{2,24})\s+VS\.?\s+Wild\b");
            if(!title.Success)return null;
            string player=title.Groups[1].Value.ToLowerInvariant().Replace('0','o');
            foreach(Match row in Regex.Matches(feed ?? "",
                @"(?im)^\s*[\[\(]?(\d{2}:\d{2}:\d{2})[\]\)]\s+([A-Za-z0-9_]{2,24})\s+encountered\s+an?\s+([A-Za-z][A-Za-z0-9 .'-]+)\.\s*$")) {
                string speaker=row.Groups[2].Value.ToLowerInvariant().Replace('0','o');
                if(speaker!=player)continue;
                TimeSpan time;
                if(!TimeSpan.TryParseExact(row.Groups[1].Value,@"hh\:mm\:ss",
                    System.Globalization.CultureInfo.InvariantCulture,out time))continue;
                var announced=capturedAt.Date+time;
                if(announced>capturedAt.AddSeconds(1))announced=announced.AddDays(-1);
                if(announced<battleStart.AddSeconds(-2))continue;
                string[] words=row.Groups[3].Value.Split(new[] {' '},StringSplitOptions.RemoveEmptyEntries);
                for(int i=1;i<words.Length;i++)
                    if(Normalize(String.Join(" ",words,i,words.Length-i).Replace("\u00e6","ee").Replace("\u00c6","ee"))==species)
                        return announced.ToString("yyyy-MM-dd HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture)+" "+player+" "+species;
            }
            return null;
        }
        public static EncounterReading Parse(string text) {
            var result=new EncounterReading();
            bool scoped=text.StartsWith("[Encounter header]\n",StringComparison.Ordinal);
            string header=text, commands=text;
            if(scoped) {
                int controlsAt=text.IndexOf("\n[Battle controls]\n",StringComparison.Ordinal), fullAt=text.IndexOf("\n[Full screen]\n",StringComparison.Ordinal);
                if(controlsAt<0 || fullAt<controlsAt) return result;
                header=text.Substring("[Encounter header]\n".Length,controlsAt-"[Encounter header]\n".Length);
                int tabsAt=text.IndexOf("\n[World tabs]\n",controlsAt,StringComparison.Ordinal);
                int controlsEnd=tabsAt>controlsAt && tabsAt<fullAt ? tabsAt : fullAt;
                commands=text.Substring(controlsAt+"\n[Battle controls]\n".Length,controlsEnd-controlsAt-"\n[Battle controls]\n".Length);
                // Join a split VS./Wild heading without appending unrelated player-name lines.
                header=Regex.Replace(header,@"\bVS\.?[ \t]*\r?\n[ \t]*(?=Wild\b)","VS. ",RegexOptions.IgnoreCase);
            }
            bool fightAndItems=Regex.IsMatch(commands,@"\bFight\b",RegexOptions.IgnoreCase) && Regex.IsMatch(commands,@"\bItems\b",RegexOptions.IgnoreCase);
            // The icon-style I is omitted by OCR on the observed live PRO screen.
            bool runAndItems=scoped && Regex.IsMatch(commands,@"\bRun\b",RegexOptions.IgnoreCase) && Regex.IsMatch(commands,@"\bI?tems\b",RegexOptions.IgnoreCase);
            result.Commands=fightAndItems || runAndItems;
            result.Battle=result.Commands || Regex.IsMatch(header,@"\bVS\.?\s*Wild\b|\bYou have run away\b|\bcouldn.t (?:get away|escape)\b",RegexOptions.IgnoreCase);
            if(scoped && Regex.IsMatch(header,@"\bVS\b|\bWild\b",RegexOptions.IgnoreCase)) result.Battle=true;
            var found=new HashSet<string>();
            bool scopedVersus=scoped && Regex.IsMatch(header,@"\bVS\b",RegexOptions.IgnoreCase);
            foreach(string line in header.Split('\n')) {
                var match=Regex.Match(line,scopedVersus ? @"\bWild\s+(.+)" : @"\bVS\.?\s*Wild\s+(.+)",RegexOptions.IgnoreCase);
                if(!match.Success) continue;
                string suffix=match.Groups[1].Value.Trim();
                // Only accept an exact known species, apart from a trailing level label.
                suffix=Regex.Replace(suffix,@"\s+(?:Lv\.?|Level)\s*\d+.*$","",RegexOptions.IgnoreCase);
                string candidate=Normalize(suffix);
                if(Species.Contains(candidate)) found.Add(candidate); else result.Ambiguous=true;
            }
            if(found.Count==1 && !result.Ambiguous) foreach(string name in found) result.Name=name;
            if(found.Count>1) result.Ambiguous=true;
            int markers=0;
            foreach(string label in new string[] {"Local","Trade","Guild","Other","All"})
                if(Regex.IsMatch(text,@"\b"+label+@"\b",RegexOptions.IgnoreCase)) markers++;
            result.Overworld=!result.Battle && markers>=2;
            return result;
        }
        public static bool CloseToTarget(string name, HashSet<string> targets) {
            foreach(string target in targets) {
                if(name==target) return true;
                int[,] d=new int[name.Length+1,target.Length+1];
                for(int i=0;i<=name.Length;i++) d[i,0]=i;
                for(int j=0;j<=target.Length;j++) d[0,j]=j;
                for(int i=1;i<=name.Length;i++) for(int j=1;j<=target.Length;j++)
                    d[i,j]=Math.Min(Math.Min(d[i-1,j]+1,d[i,j-1]+1),d[i-1,j-1]+(name[i-1]==target[j-1]?0:1));
                if(d[name.Length,target.Length]<=2) return true;
            }
            return false;
        }
    }
}
