using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

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
        static readonly Queue<OcrEngine> engines=new Queue<OcrEngine>();
        static OcrEngine AcquireEngine() {
            lock(engines)if(engines.Count>0)return engines.Dequeue();
            var engine=OcrEngine.TryCreateFromUserProfileLanguages();
            if(engine==null)throw new InvalidOperationException("Windows OCR is unavailable. Install a supported OCR language or disable encounter handling.");
            return engine;
        }
        static Bitmap EnlargeRectangle(Bitmap image,Rectangle area) {
            area.Intersect(new Rectangle(0,0,image.Width,image.Height));
            if(area.Width<1 || area.Height<1)throw new InvalidOperationException("Battle heading is outside the game window.");
            var result=new Bitmap(area.Width*3,area.Height*3);
            using(var g=Graphics.FromImage(result)) {g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(image,new Rectangle(0,0,result.Width,result.Height),area,GraphicsUnit.Pixel);}
            return result;
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
        public static Bitmap RepelOcrImage(Bitmap image,Rectangle area,bool keepGold=false) {
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
                            byte value=(byte)((min>=140 && max-min<=90 || gold) ? 0 : 255);
                            row[x]=row[x+1]=row[x+2]=value;row[x+3]=255;
                        }
                        System.Runtime.InteropServices.Marshal.Copy(row,0,pointer,row.Length);
                    }
                } finally {mask.UnlockBits(bits);}
                return EnlargeRectangle(mask,new Rectangle(Point.Empty,mask.Size));
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
            using(var header=headingBounds.HasValue ? EnlargeRectangle(image,headingBounds.Value) : EnlargeRegion(image,.14,0,.72,.28))
            using(var buttons=menuBounds.HasValue ? EnlargeRectangle(image,menuBounds.Value) : EnlargeRegion(image,.72,.50,.28,.43)) {
                var heading=Recognize(header); var recognition=Recognize(buttons);
                await Task.WhenAll(heading,recognition);
                var words=new List<ButtonWord>(); var lines=new List<string>();
                foreach(var line in recognition.Result.Lines) {
                    lines.Add(line.Text);
                    foreach(var word in line.Words) {
                        var r=word.BoundingRect;
                        words.Add(new ButtonWord(word.Text,(float)r.X,(float)r.Y,(float)r.Width,(float)r.Height));
                    }
                }
                var point=RunButtonMatch.Locate(words,buttons.Width,buttons.Height);
                var items=point.HasValue ? RunButtonMatch.ItemsAnchor(words,point.Value,buttons.Width,buttons.Height) : null;
                return new RunButtonCandidate {
                    Header=TextOf(heading.Result), Controls=String.Join("\n",lines),IdentityBounds=CaptionBounds(heading.Result,headingBounds ?? new Rectangle((int)(image.Width*.14),0,(int)(image.Width*.72),(int)(image.Height*.28)),image.Size),
                    ClientPoint=point.HasValue ? (Point?)new Point((menuBounds.HasValue ? menuBounds.Value.Left : (int)(image.Width*.72))+ (int)(point.Value.X/3),(menuBounds.HasValue ? menuBounds.Value.Top : (int)(image.Height*.50))+(int)(point.Value.Y/3)) : null,
                    ItemsPoint=items.HasValue ? (Point?)new Point((menuBounds.HasValue ? menuBounds.Value.Left : (int)(image.Width*.72))+(int)(items.Value.X/3),(menuBounds.HasValue ? menuBounds.Value.Top : (int)(image.Height*.50))+(int)(items.Value.Y/3)) : null
                };
            }
        }
        public static async Task<string> Read(Bitmap image) {
            var result=await Recognize(image);
            var lines=new List<string>();
            foreach(var line in result.Lines) lines.Add(line.Text);
            return String.Join("\n",lines);
        }
        public static async Task<CatchTextRegion> ReadCatchRegion(Bitmap image,Rectangle area,bool contrast=false,bool pixelScale=false) {
            area.Intersect(new Rectangle(Point.Empty,image.Size));if(area.Width<1 || area.Height<1)throw new InvalidOperationException("Catching region is outside the client.");
            double factor=Math.Min(pixelScale ? 6 : 3,(double)OcrEngine.MaxImageDimension/Math.Max(area.Width,area.Height));
            using(var crop=contrast ? RepelOcrImage(image,area) : new Bitmap(Math.Max(1,(int)(area.Width*factor)),Math.Max(1,(int)(area.Height*factor)))) {
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
        public static async Task<CatchFrame> ReadCatch(Bitmap image,ChromeReading chrome,CatchConfig config,string species,string move,int slot,long id,double at,bool partyNeeded=true,bool selectingBall=false,Rectangle? verifiedCaption=null) {
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
            var regions=await Task.WhenAll(
                verifiedCaption.HasValue
                    ? Task.FromResult(new CatchTextRegion {Text="VS. Wild "+species})
                    : ReadCatchRegion(image,headerArea),
                ReadCatchRegion(image,menuArea,selectingBall || !String.IsNullOrEmpty(move)),
                selectingBall ? empty : ReadCatchRegion(image,ownArea),
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
            if(!selectingBall && !String.IsNullOrWhiteSpace(config.Catcher) && CatchVision.OwnCaption(regions[2],config.Catcher,image.Size).IsEmpty) {
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
            CatchVision.AddNavigation(frame,regions[1],image);
            frame.HeaderSeal=verifiedCaption ?? CatchVision.WildCaption(regions[0],image.Size,headerArea);frame.OwnSeal=CatchVision.OwnCaption(regions[2],config.Catcher,image.Size);
            if(frame.OwnSeal.IsEmpty && !selectingBall)frame.OwnSeal=ownArea;
            var moveDetails=regions[1].Rows.FindAll(r=>Regex.IsMatch(r.Text.Trim(),@"^[0-9x× /'.,:]+$",RegexOptions.IgnoreCase));
            frame.ReadDetail=String.Join(" | ",moveDetails.ConvertAll(r=>r.Text))+" [Active caption] "+regions[2].Text.Replace('\n',' ');
            foreach(var kind in frame.Controls.Keys)frame.InputSeals[kind]=CatchVision.InputSeal(image,frame,kind);
            return frame;
        }
        static async Task<OcrResult> Recognize(Bitmap image) {
            var engine=AcquireEngine();
            try {
            using(var png=new MemoryStream()) {
                int maximum=(int)OcrEngine.MaxImageDimension;
                if(image.Width>maximum || image.Height>maximum) {
                    double ratio=Math.Min((double)maximum/image.Width,(double)maximum/image.Height);
                    using(var resized=new Bitmap(Math.Max(1,(int)(image.Width*ratio)),Math.Max(1,(int)(image.Height*ratio)))) {
                        using(var g=Graphics.FromImage(resized)) g.DrawImage(image,0,0,resized.Width,resized.Height);
                        resized.Save(png,System.Drawing.Imaging.ImageFormat.Png);
                    }
                } else image.Save(png,System.Drawing.Imaging.ImageFormat.Png);
                using(var stream=new InMemoryRandomAccessStream()) {
                    using(var writer=new DataWriter(stream.GetOutputStreamAt(0))) {
                        writer.WriteBytes(png.ToArray()); await System.WindowsRuntimeSystemExtensions.AsTask<uint>(writer.StoreAsync()); await System.WindowsRuntimeSystemExtensions.AsTask<bool>(writer.FlushAsync()); writer.DetachStream();
                    }
                    stream.Seek(0);
                    var decoder=await System.WindowsRuntimeSystemExtensions.AsTask<BitmapDecoder>(BitmapDecoder.CreateAsync(stream));
                    using(var pixels=await System.WindowsRuntimeSystemExtensions.AsTask<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied))) {
                        return await System.WindowsRuntimeSystemExtensions.AsTask<OcrResult>(engine.RecognizeAsync(pixels));
                    }
                }
            }
            } finally {lock(engines)engines.Enqueue(engine);}
        }
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
