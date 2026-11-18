using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MovementPrototype {
    public sealed class CatchTextRow {
        public string Text;public RectangleF Bounds;
        public List<ButtonWord> Words=new List<ButtonWord>();
    }
    public sealed class CatchTextRegion {
        public string Text="";public List<CatchTextRow> Rows=new List<CatchTextRow>();
    }
    public static class CatchVision {
        public static void AddNavigation(CatchFrame frame,CatchTextRegion menu,Bitmap image) {
            if(!frame.Wild)return;var fight=Unique(menu,"Fight");var items=Unique(menu,"Items");var run=Unique(menu,"Run");
            if(fight==null || items==null || run==null)return;
            var f=Center(fight.Bounds);var i=Center(items.Bounds);var r=Center(run.Bounds);
            if(Math.Abs(f.X-i.X)>image.Width*.035 || r.X-i.X<image.Width*.025 || Math.Abs(r.Y-i.Y)>image.Height*.025)return;
            frame.Ready=BattleControlReady.Read(image,r,i)==ControlReady.Ready;
            if(frame.Ready) {Put(frame,CatchActionKind.OpenFight,Control(fight,"Fight"));Put(frame,CatchActionKind.OpenBag,Control(items,"Items"));}
        }
        public static string InputSeal(Bitmap image,CatchFrame frame,CatchActionKind kind) {
            var control=frame.Control(kind);if(control==null)return null;
            var area=control.Bounds.IsEmpty ? new Rectangle(control.Point.X-25,control.Point.Y-12,50,24) : control.Bounds;area.Intersect(new Rectangle(Point.Empty,image.Size));
            var catcher=frame.OwnSeal.IsEmpty ? "" : VerifiedHeader.Signature(image,frame.OwnSeal);
            return VerifiedHeader.Signature(image,frame.HeaderSeal)+catcher+VerifiedHeader.Signature(image,area);
        }
        static string Fold(string text) {return EncounterRecognition.Normalize((text ?? "").Replace('é','e').Replace('É','E'));}
        static bool Has(string text,string name) {return !String.IsNullOrWhiteSpace(name) && Regex.IsMatch(text ?? "",@"(?<![A-Za-z])"+Regex.Escape(name)+@"(?![A-Za-z])",RegexOptions.IgnoreCase);}
        static CatchTextRow Unique(CatchTextRegion region,string label) {
            var found=new List<CatchTextRow>();
            foreach(var row in region.Rows) {
                if(!Has(row.Text,label) && !Fold(row.Text).Contains(Fold(label)))continue;
                if(row.Words.Count==0) {if(Fold(row.Text)==Fold(label))found.Add(row);continue;}
                for(int first=0;first<row.Words.Count;first++) {
                    string phrase="";RectangleF bounds=RectangleF.Empty;
                    for(int last=first;last<Math.Min(first+4,row.Words.Count);last++) {
                        phrase+=(last==first ? "" : " ")+row.Words[last].Text;bounds=last==first ? row.Words[last].Bounds : RectangleF.Union(bounds,row.Words[last].Bounds);
                        if(Fold(phrase)==Fold(label)) {found.Add(new CatchTextRow {Text=label,Bounds=bounds});break;}
                    }
                }
            }
            return found.Count==1 ? found[0] : null;
        }
        static Point Center(RectangleF r) {return new Point((int)(r.Left+r.Width/2),(int)(r.Top+r.Height/2));}
        static int Quantity(string text,bool pp=false,bool bare=false) {
            if(pp)return MovePp(text);
            int number;if(bare && Regex.IsMatch(text ?? "",@"^\s*\d{1,5}\s*$") && Int32.TryParse(text.Trim(),out number))return number;
            var match=Regex.Match(text ?? "",pp ? @"(?:\bPP\s*[:=]?\s*)?(\d+)\s*/\s*\d+" : @"(?:[x×]\s*|\bquantity\s*[:=]?\s*)(\d+)\b",RegexOptions.IgnoreCase);
            int value;return match.Success && Int32.TryParse(match.Groups[1].Value,out value) ? value : -1;
        }
        // Pixel-font '/' is sometimes read as '1' or omitted. Accept a compact
        // counter only when it has one possible current/total pair (0..99).
        public static int MovePp(string text) {
            var normal=Regex.Match(text ?? "",@"(?:\bPP\s*[:=]?\s*)?(\d{1,2})\s*[/\\'’.,:]\s*(\d{1,2})(?!\d)",RegexOptions.IgnoreCase);
            if(normal.Success) {int current=Int32.Parse(normal.Groups[1].Value),total=Int32.Parse(normal.Groups[2].Value);return total>0 && current<=total ? current : -1;}
            string compact=Regex.Replace(text ?? "",@"\s","");if(!Regex.IsMatch(compact,@"^\d{2,5}$"))return -1;
            var pairs=new HashSet<string>();int remaining=-1;
            for(int left=1;left<=2 && left<compact.Length;left++)for(int separator=0;separator<=1;separator++) {
                int right=left+separator;if(right>=compact.Length || compact.Length-right>2 || separator==1 && compact[left]!='1')continue;
                string a=compact.Substring(0,left),b=compact.Substring(right);if(a.Length>1 && a[0]=='0' || b.Length>1 && b[0]=='0')continue;
                int current=Int32.Parse(a),total=Int32.Parse(b);if(total<1 || current>total)continue;
                pairs.Add(current+"/"+total);remaining=current;
            }
            return pairs.Count==1 ? remaining : -1;
        }
        static CatchControl Control(CatchTextRow row,string name,int slot=0,int remaining=-1,CatchTextRow counter=null) {
            if(row==null)return null;var bounds=Rectangle.Inflate(Rectangle.Ceiling(row.Bounds),2,2);
            if(counter!=null)bounds=Rectangle.Union(bounds,Rectangle.Inflate(Rectangle.Ceiling(counter.Bounds),2,2));
            return new CatchControl {Name=name,Point=Center(row.Bounds),Bounds=bounds,Slot=slot,Remaining=remaining};
        }
        static void Put(CatchFrame frame,CatchActionKind kind,CatchControl control) {if(control!=null)frame.Controls[kind]=control;}
        static CatchTextRow Near(CatchTextRegion menu,CatchTextRow label,Func<string,bool> predicate) {
            var rows=menu.Rows.FindAll(r=>predicate(r.Text) && Math.Abs(r.Bounds.Top-label.Bounds.Top)<70 && Math.Abs(r.Bounds.Left-label.Bounds.Left)<180);
            rows.Sort((a,b)=>(Math.Abs(a.Bounds.Top-label.Bounds.Top)+Math.Abs(a.Bounds.Left-label.Bounds.Left)).CompareTo(Math.Abs(b.Bounds.Top-label.Bounds.Top)+Math.Abs(b.Bounds.Left-label.Bounds.Left)));
            return rows.Count>0 ? rows[0] : null;
        }
        static CatchTextRow Stock(CatchTextRegion region,CatchTextRow label) {
            var rows=region.Rows.FindAll(r=>Quantity(r.Text,false,true)>=0 && Math.Abs(r.Bounds.Left-label.Bounds.Left)<=Math.Max(14,label.Bounds.Height) && r.Bounds.Top>=label.Bounds.Top-3 && r.Bounds.Top-label.Bounds.Bottom<=Math.Max(24,label.Bounds.Height*2.5));
            if(rows.Count==0)return null;
            // Retries can read the same counter at slightly different bounds.
            // Accept agreement only if the reads overlap vertically.
            var first=rows[0];
            int count=Quantity(first.Text,false,true);
            return rows.All(r=>Quantity(r.Text,false,true)==count &&
                r.Bounds.Top<first.Bounds.Bottom && first.Bounds.Top<r.Bounds.Bottom) ? first : null;
        }
        static readonly List<string> Species=LoadSpecies();
        static List<string> LoadSpecies() {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"pokemon-names.txt");
            return File.Exists(path) ? File.ReadAllLines(path).Where(x=>x.Trim().Length>0).OrderByDescending(x=>x.Length).ToList() : new List<string>();
        }
        static string SpeciesIn(string text) {foreach(string name in Species)if(Has(text,name))return name;return null;}
        static bool OneEdit(string a,string b) {
            if(Math.Abs(a.Length-b.Length)>1)return false;int i=0,j=0,edits=0;
            while(i<a.Length && j<b.Length) {if(a[i]==b[j]) {i++;j++;continue;}if(++edits>1)return false;if(a.Length>=b.Length)i++;if(b.Length>=a.Length)j++;}
            return edits+(a.Length-i)+(b.Length-j)<=1;
        }
        static bool OwnName(string text,string wanted) {
            if(Has(text,wanted))return true;string desired=Fold(wanted);if(desired.Length<5)return false;
            foreach(Match word in Regex.Matches(text ?? "",@"[A-Za-z]+")) {
                string token=Fold(word.Value);if(Species.Any(n=>Fold(n)==token))continue;
                var matches=Species.Where(n=>OneEdit(Fold(n),token) ||
                    Fold(n).Replace("rn","m")==token.Replace("rn","m")).Select(Fold).Distinct().ToList();
                if(matches.Count==1 && matches[0]==desired)return true;
            }return false;
        }
        public static Rectangle OwnCaption(CatchTextRegion own,string wanted,Size size) {
            Rectangle result=Rectangle.Empty;
            foreach(var row in own.Rows) {
                foreach(var word in row.Words)if(OwnName(word.Text,wanted)) {
                    var box=Rectangle.Inflate(Rectangle.Ceiling(word.Bounds),2,2);result=result.IsEmpty ? box : Rectangle.Union(result,box);
                }
            }
            // Windows OCR sometimes splits the observed Smeargle caption into
            // "S rr-leargle". Require both adjacent words in the active caption.
            if(result.IsEmpty && Fold(wanted)=="smeargle")foreach(var row in own.Rows) {
                for(int i=0;i+1<row.Words.Count;i++) {
                    var first=row.Words[i];var second=row.Words[i+1];
                    if(Fold(first.Text+second.Text)=="srrleargle" && second.Bounds.Left-first.Bounds.Right<=first.Bounds.Height)
                        result=Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Union(first.Bounds,second.Bounds)),2,2);
                }
            }
            result.Intersect(new Rectangle(Point.Empty,size));return result;
        }
        public static Rectangle WildCaption(CatchTextRegion header,Size size,Rectangle fallback) {
            var rows=header.Rows.Where(r=>Regex.IsMatch(r.Text,@"\bWild\b",RegexOptions.IgnoreCase)).ToList();
            if(rows.Count!=1)return fallback;
            var box=Rectangle.Inflate(Rectangle.Ceiling(rows[0].Bounds),2,2);box.Intersect(new Rectangle(Point.Empty,size));return box;
        }
        public static CatchFrame Preview(CatchTextRegion region,string expected,long id,double at) {
            var frame=new CatchFrame {Id=id,CapturedAt=at,View=CatchView.World};
            var marker=Unique(region,"PREVIEW");var species=String.IsNullOrEmpty(expected) ? null : Unique(region,expected);
            if(marker!=null)frame.View=CatchView.Unknown;
            if(marker==null || species==null || Math.Abs(marker.Bounds.Top-species.Bounds.Top)>Math.Max(8,marker.Bounds.Height))return frame;
            frame.Success=true;frame.Opponent=expected;frame.View=CatchView.Success;
            frame.Message=frame.MessageKey=expected+" capture PREVIEW";
            frame.HeaderSeal=Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Union(marker.Bounds,species.Bounds)),2,2);frame.OwnSeal=frame.HeaderSeal;
            var close=Unique(region,"X");
            if(close!=null && close.Bounds.Left>marker.Bounds.Right && Math.Abs(close.Bounds.Top-marker.Bounds.Top)<=Math.Max(8,marker.Bounds.Height))Put(frame,CatchActionKind.CloseSummary,Control(close,"Captured Pokémon preview"));
            return frame;
        }
        public static Rectangle? StockArea(CatchTextRegion bag,string ball,Size image,bool compact=false) {
            var label=Unique(bag,ball);if(label==null)return null;
            var stock=Stock(bag,label);if(stock!=null)return null;
            // In battle, the count is directly below the ball name. Keep the
            // retry above the next item so its count cannot be mistaken for ours.
            var bounds=Rectangle.FromLTRB((int)label.Bounds.Left-5,
                (int)Math.Floor(label.Bounds.Bottom)+(compact ? -1 : 1),
                (int)Math.Ceiling(label.Bounds.Left+Math.Max(compact ? 48 : 90,label.Bounds.Width)),
                (int)Math.Ceiling(label.Bounds.Bottom+Math.Max(compact ? 17 : 28,label.Bounds.Height*(compact ? 1.5 : 2.5))));
            bounds.Intersect(new Rectangle(Point.Empty,image));return bounds.Width>0 && bounds.Height>0 ? (Rectangle?)bounds : null;
        }
        static List<CatchTextRow> HudParty(CatchTextRegion hud,CatchConfig config) {
            return hud.Rows.Where(r=>SpeciesIn(r.Text)!=null || Has(r.Text,config.Catcher) || Has(r.Text,config.ExpectedLead)).OrderBy(r=>r.Bounds.Top).ToList();
        }
        public static string LeadName(CatchTextRegion hud,CatchConfig config,Size image) {
            var rows=HudParty(hud,config);
            return !String.IsNullOrWhiteSpace(config.ExpectedLead) && rows.Count>0 && FirstHudRow(rows[0],image) && Has(rows[0].Text,config.ExpectedLead) ? config.ExpectedLead : null;
        }
        static bool FirstHudRow(CatchTextRow row,Size image) {return row.Bounds.Top<image.Height*.09 && row.Bounds.Top<=Math.Max(20,row.Bounds.Height*4);}
        public static Point PartyIcon(Rectangle box,int slot) {
            int index=slot-1;return new Point(box.Left+(int)(box.Width*(.805+.11*(index%2))),box.Top+(int)(box.Width*(.115+.085*(index/2))));
        }
        static bool IconVisible(Bitmap image,Rectangle box,int slot) {
            var point=PartyIcon(box,slot);int radius=Math.Max(8,(int)(box.Width*.029)),colored=0,dark=0,total=0;
            if(point.X-radius<0 || point.Y-radius<0 || point.X+radius>=image.Width || point.Y+radius>=image.Height)return false;
            for(int y=point.Y-radius;y<=point.Y+radius;y+=2)for(int x=point.X-radius;x<=point.X+radius;x+=2) {
                var c=image.GetPixel(x,y);int maximum=Math.Max(c.R,Math.Max(c.G,c.B)),minimum=Math.Min(c.R,Math.Min(c.G,c.B));total++;if(maximum>=80 && maximum-minimum>=25)colored++;if(maximum<=40 && maximum-minimum<=12)dark++;
            }
            return colored>=12 && dark>=total*.18;
        }
        static bool ReadIconParty(CatchFrame frame,CatchTextRegion menu,CatchTextRegion hud,ChromeReading chrome,Bitmap image,CatchConfig config) {
            if(Unique(menu,"Choose Pokemon")==null && Unique(menu,"Choose Pokémon")==null)return false;
            frame.View=CatchView.Party;
            if(!chrome.BattleBox.HasValue)return true;
            var rows=HudParty(hud,config);if(rows.Count<1 || rows.Count>6 || !FirstHudRow(rows[0],image.Size))return true;
            int icons=0;bool empty=false;for(int slot=1;slot<=6;slot++) {if(IconVisible(image,chrome.BattleBox.Value,slot)) {if(empty)return true;icons++;}else empty=true;}
            if(icons!=rows.Count || config.PartySlot<1 || config.PartySlot>rows.Count)return true;
            for(int i=1;i<rows.Count;i++)if(rows[i].Bounds.Top-rows[i-1].Bounds.Top<image.Height*.025 || rows[i].Bounds.Top-rows[i-1].Bounds.Top>image.Height*.12)return true;
            if(Has(rows[config.PartySlot-1].Text,config.Catcher))Put(frame,CatchActionKind.ChooseCatcher,new CatchControl {Name=config.Catcher,Slot=config.PartySlot,Point=PartyIcon(chrome.BattleBox.Value,config.PartySlot)});
            return true;
        }
        public static bool CaptureMessage(string message,string expected) {
            if(String.IsNullOrEmpty(expected))return false;
            string target=Regex.Escape(expected);
            return Regex.IsMatch(message ?? "",@"(?:^|\n)\s*(?:(?:Gotcha|Success|Succo)[!.,]?\s+)?(?:(?:Wild\s+)?"+target+@"\s+(?:was|has been)\s+(?:successfully\s+)?caught[!.]?|You\s+(?:have\s+)?caught\s+(?:a\s+)?"+target+@"[!.]?)\s*(?:$|\n)",RegexOptions.IgnoreCase);
        }
        public static CatchFrame Parse(CatchTextRegion header,CatchTextRegion menu,CatchTextRegion own,CatchTextRegion log,CatchTextRegion lead,ChromeReading chrome,Bitmap image,CatchConfig config,string expected,string wantedMove,int wantedSlot,long id,double capturedAt,CatchTextRegion bag=null) {
            var frame=new CatchFrame {Id=id,CapturedAt=capturedAt};
            var reading=EncounterRecognition.Parse("[Encounter header]\n"+header.Text+"\n[Battle controls]\n\n[Full screen]\n");
            frame.Opponent=reading.Name;frame.Wild=reading.Battle && !reading.Ambiguous && reading.Name!=null;
            frame.ActivePokemon=OwnName(own.Text,config.Catcher) || !OwnCaption(own,config.Catcher,image.Size).IsEmpty ? config.Catcher : null;
            var hudParty=HudParty(lead,config);
            frame.LeadPokemon=!String.IsNullOrWhiteSpace(config.ExpectedLead) && hudParty.Count>0 && FirstHudRow(hudParty[0],image.Size) && Has(hudParty[0].Text,config.ExpectedLead) ? config.ExpectedLead : null;
            frame.Message=log.Text;frame.MessageKey=log.Text;
            bool trainer=Regex.IsMatch(header.Text ?? "",@"\bVS\b",RegexOptions.IgnoreCase) && !Regex.IsMatch(header.Text ?? "",@"\bWild\b",RegexOptions.IgnoreCase);
            bool success=!trainer && CaptureMessage(log.Text,expected);
            if(success) {frame.Success=true;frame.Opponent=expected;frame.View=CatchView.Success;}
            if(Regex.IsMatch(log.Text ?? "",@"\b(fainted|no pp left|not enough pp|doesn.t affect|had no effect|no effect on|no .+balls left|can.t use that|cannot use that|attack missed|ran away|fled from battle)\b",RegexOptions.IgnoreCase)) {frame.Failure=true;frame.FailureReason="battle reported a failed action, fainting or escape";}
            // The backpack can overlay the battle without hiding the command buttons.
            var battleBall=Unique(menu,config.Ball);
            if(frame.Wild && battleBall!=null && Unique(menu,"Choose Item")!=null) {
                var quantity=Stock(menu,battleBall);int stock=quantity==null ? -1 : Quantity(quantity.Text,false,true);
                var item=Control(battleBall,config.Ball,0,stock,quantity);
                item.ListedBattleItem=true;
                frame.View=CatchView.BallList;Put(frame,CatchActionKind.UseBall,item);return frame;
            }
            var ballRegion=bag ?? menu;var ball=Unique(ballRegion,config.Ball);
            if(frame.Wild && ball!=null && Unique(ballRegion,"Balls")!=null) {
                var quantity=Stock(ballRegion,ball);int stock=quantity==null ? -1 : Quantity(quantity.Text,false,true);
                frame.View=CatchView.BallList;Put(frame,CatchActionKind.UseBall,Control(ball,config.Ball,0,stock,quantity));return frame;
            }
            if(frame.Wild && ReadIconParty(frame,menu,lead,chrome,image,config))return frame;
            if(frame.Wild) {
            var attackTitle=Unique(menu,"Choose Attack") ?? Unique(menu,"Choose Move");
            bool attackMenu=attackTitle!=null;
            var explicitPp=menu.Rows.FindAll(r=>Regex.IsMatch(r.Text,@"\d\s*/\s*\d") && Quantity(r.Text,true)>=0);
            var wantedLabel=String.IsNullOrEmpty(wantedMove) ? null : Unique(menu,wantedMove);
            if(attackMenu || explicitPp.Count>=2 && (wantedLabel!=null || explicitPp.Count==4)) {
                var ppRows=menu.Rows.FindAll(r=>(attackTitle==null || r.Bounds.Bottom<=attackTitle.Bounds.Top+2)
                    && (Quantity(r.Text,true)>=0 || attackMenu && Regex.IsMatch(r.Text.Trim(),@"^\d{2,5}$")));
                frame.View=CatchView.Moves;
                var name=wantedLabel;
                if(name!=null) {
                    var pp=Near(new CatchTextRegion {Rows=ppRows},name,t=>Quantity(t,true)>=0);
                    if(pp!=null) {
                        // PP anchors establish each slot independently of text length.
                        var anchors=ppRows.OrderBy(r=>r.Bounds.Top).ThenBy(r=>r.Bounds.Left).ToList();
                        if(anchors.Count==4) {
                            var upper=anchors.Where(r=>r.Bounds.Top-anchors[0].Bounds.Top<=Math.Max(18,image.Height*.035)).OrderBy(r=>r.Bounds.Left).ToList();
                            var lower=anchors.Except(upper).OrderBy(r=>r.Bounds.Left).ToList();
                            bool grid=upper.Count==2 && lower.Count==2 && Math.Abs(lower[0].Bounds.Top-lower[1].Bounds.Top)<=Math.Max(18,image.Height*.035) && upper[1].Bounds.Left-upper[0].Bounds.Left>image.Width*.05;
                            if(grid)anchors=upper.Concat(lower).ToList();
                        }
                        int slot=anchors.IndexOf(pp)+1;
                        if(anchors.Count==4 && slot==wantedSlot)Put(frame,CatchActionKind.ChooseMove,Control(name,wantedMove,slot,Quantity(pp.Text,true),pp));
                    }
                }
                return frame;
            }
            var partyRows=menu.Rows.Where(r=>SpeciesIn(r.Text)!=null || Has(r.Text,config.Catcher)).OrderBy(r=>r.Bounds.Top).ThenBy(r=>r.Bounds.Left).ToList();
            if(partyRows.Count>=2 && partyRows.Count<=6 && (Has(menu.Text,"Back") || Has(menu.Text,"Switch") || Has(menu.Text,"Choose"))) {
                frame.View=CatchView.Party;
                if(config.PartySlot<=partyRows.Count) {
                    var row=partyRows[config.PartySlot-1];
                    if(Has(row.Text,config.Catcher))Put(frame,CatchActionKind.ChooseCatcher,Control(row,config.Catcher,config.PartySlot));
                }
                return frame;
            }
            }
            var fight=Unique(menu,"Fight");var pokemon=Unique(menu,"Pokemon") ?? Unique(menu,"Pokémon") ?? Unique(menu,"P kemon") ?? Unique(menu,"okemon");var items=Unique(menu,"Items");var run=Unique(menu,"Run");
            if(frame.Wild && fight!=null && pokemon!=null && items!=null && run!=null) {
                var f=Center(fight.Bounds);var p=Center(pokemon.Bounds);var i=Center(items.Bounds);var r=Center(run.Bounds);
                if(p.X<=f.X+image.Width*.025 || i.Y<=f.Y+image.Height*.025 || Math.Abs(f.X-i.X)>image.Width*.035 || Math.Abs(p.X-r.X)>image.Width*.035 || Math.Abs(f.Y-p.Y)>image.Height*.025 || Math.Abs(i.Y-r.Y)>image.Height*.025)return frame;
                var state=BattleControlReady.Read(image,Center(run.Bounds),Center(items.Bounds));
                frame.Ready=state==ControlReady.Ready;frame.View=frame.Ready ? CatchView.Commands : state==ControlReady.Busy ? CatchView.Busy : CatchView.Unknown;
                Put(frame,CatchActionKind.OpenFight,Control(fight,"Fight"));Put(frame,CatchActionKind.OpenParty,Control(pokemon,"Pokemon"));Put(frame,CatchActionKind.OpenBag,Control(items,"Items"));
                return frame;
            }
            if(success) {
                var close=Unique(menu,"OK") ?? Unique(menu,"Close");Put(frame,CatchActionKind.CloseSummary,Control(close,"Catch summary"));return frame;
            }
            if(chrome.Kind==ChromeKind.Clear && !frame.Wild) {frame.View=CatchView.World;return frame;}
            if(!frame.Wild)return frame;
            var pocket=Unique(menu,"Poke Balls") ?? Unique(menu,"Poké Balls") ?? Unique(menu,"Pokeballs");
            if(pocket!=null) {frame.View=CatchView.Bag;Put(frame,CatchActionKind.OpenBalls,Control(pocket,"Poke Balls"));return frame;}
            return frame;
        }
        public static string Describe(CatchFrame frame) {
            string text="[Catching diagnostic — no stored chat]\nView: "+frame.View+"\nWild: "+frame.Wild+"\nOpponent: "+(frame.Opponent ?? "unreadable")+"\nActive catcher: "+(frame.ActivePokemon ?? "unreadable")+"\nReady: "+frame.Ready+"\nCapture confirmed: "+frame.Success+"\nFailure: "+frame.Failure;
            foreach(var pair in frame.Controls)text+="\n"+pair.Key+": "+pair.Value.Name+"; slot="+pair.Value.Slot+"; remaining="+pair.Value.Remaining;
            return text;
        }
        public static bool Reconfirms(CatchAction action,CatchFrame before,CatchFrame fresh) {
            if(action==null || before==null || fresh==null || fresh.View!=before.View || fresh.Failure)return false;
            if(action.Kind==CatchActionKind.CloseSummary) {if(!fresh.Success)return false;}
            else if(!before.Wild || !fresh.Wild || Fold(before.Opponent)!=Fold(fresh.Opponent) || before.Ready!=fresh.Ready || Fold(before.ActivePokemon)!=Fold(fresh.ActivePokemon))return false;
            var a=before.Control(action.Kind);var b=fresh.Control(action.Kind);
            return a!=null && b!=null && Fold(a.Name)==Fold(b.Name) && a.Slot==b.Slot && a.Remaining==b.Remaining && Math.Abs(a.Point.X-b.Point.X)<=8 && Math.Abs(a.Point.Y-b.Point.Y)<=8;
        }
    }
}
