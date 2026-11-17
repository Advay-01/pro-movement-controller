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
        static int Quantity(string text,bool pp=false) {
            var match=Regex.Match(text ?? "",pp ? @"(?:\bPP\s*[:=]?\s*)?(\d+)\s*/\s*\d+" : @"(?:[x×]\s*|\bquantity\s*[:=]?\s*)(\d+)\b",RegexOptions.IgnoreCase);
            int value;return match.Success && Int32.TryParse(match.Groups[1].Value,out value) ? value : -1;
        }
        static CatchControl Control(CatchTextRow row,string name,int slot=0,int remaining=-1) {return row==null ? null : new CatchControl {Name=name,Point=Center(row.Bounds),Slot=slot,Remaining=remaining};}
        static void Put(CatchFrame frame,CatchActionKind kind,CatchControl control) {if(control!=null)frame.Controls[kind]=control;}
        static CatchTextRow Near(CatchTextRegion menu,CatchTextRow label,Func<string,bool> predicate) {
            var rows=menu.Rows.FindAll(r=>predicate(r.Text) && Math.Abs(r.Bounds.Top-label.Bounds.Top)<70 && Math.Abs(r.Bounds.Left-label.Bounds.Left)<180);
            rows.Sort((a,b)=>(Math.Abs(a.Bounds.Top-label.Bounds.Top)+Math.Abs(a.Bounds.Left-label.Bounds.Left)).CompareTo(Math.Abs(b.Bounds.Top-label.Bounds.Top)+Math.Abs(b.Bounds.Left-label.Bounds.Left)));
            return rows.Count>0 ? rows[0] : null;
        }
        static CatchTextRow Stock(CatchTextRegion region,CatchTextRow label) {
            var rows=region.Rows.FindAll(r=>Quantity(r.Text)>=0 && Math.Abs(r.Bounds.Left-label.Bounds.Left)<=Math.Max(14,label.Bounds.Height) && r.Bounds.Top>=label.Bounds.Top-3 && r.Bounds.Top-label.Bounds.Bottom<=Math.Max(24,label.Bounds.Height*2.5));
            return rows.Count==1 ? rows[0] : null;
        }
        static readonly List<string> Species=LoadSpecies();
        static List<string> LoadSpecies() {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"pokemon-names.txt");
            return File.Exists(path) ? File.ReadAllLines(path).Where(x=>x.Trim().Length>0).OrderByDescending(x=>x.Length).ToList() : new List<string>();
        }
        static string SpeciesIn(string text) {foreach(string name in Species)if(Has(text,name))return name;return null;}
        public static Rectangle? StockArea(CatchTextRegion bag,string ball,Size image) {
            var label=Unique(bag,ball);if(label==null)return null;
            var stock=Stock(bag,label);if(stock!=null)return null;
            var bounds=Rectangle.FromLTRB((int)label.Bounds.Left-5,(int)label.Bounds.Bottom+1,(int)(label.Bounds.Left+Math.Max(90,label.Bounds.Width)),(int)(label.Bounds.Bottom+Math.Max(28,label.Bounds.Height*2.5)));
            bounds.Intersect(new Rectangle(Point.Empty,image));return bounds.Width>0 && bounds.Height>0 ? (Rectangle?)bounds : null;
        }
        static List<CatchTextRow> HudParty(CatchTextRegion hud,CatchConfig config) {
            return hud.Rows.Where(r=>SpeciesIn(r.Text)!=null || Has(r.Text,config.Catcher) || Has(r.Text,config.ExpectedLead)).OrderBy(r=>r.Bounds.Top).ToList();
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
        public static CatchFrame Parse(CatchTextRegion header,CatchTextRegion menu,CatchTextRegion own,CatchTextRegion log,CatchTextRegion lead,ChromeReading chrome,Bitmap image,CatchConfig config,string expected,string wantedMove,int wantedSlot,long id,double capturedAt,CatchTextRegion bag=null) {
            var frame=new CatchFrame {Id=id,CapturedAt=capturedAt};
            var reading=EncounterRecognition.Parse("[Encounter header]\n"+header.Text+"\n[Battle controls]\n\n[Full screen]\n");
            frame.Opponent=reading.Name;frame.Wild=reading.Battle && !reading.Ambiguous && reading.Name!=null;
            frame.ActivePokemon=Has(own.Text,config.Catcher) ? config.Catcher : null;
            var hudParty=HudParty(lead,config);
            frame.LeadPokemon=!String.IsNullOrWhiteSpace(config.ExpectedLead) && hudParty.Count>0 && FirstHudRow(hudParty[0],image.Size) && Has(hudParty[0].Text,config.ExpectedLead) ? config.ExpectedLead : null;
            frame.Message=log.Text;frame.MessageKey=log.Text;
            string target=Regex.Escape(expected ?? "");
            bool trainer=Regex.IsMatch(header.Text ?? "",@"\bVS\b",RegexOptions.IgnoreCase) && !Regex.IsMatch(header.Text ?? "",@"\bWild\b",RegexOptions.IgnoreCase);
            bool success=!trainer && !String.IsNullOrEmpty(expected) && Regex.IsMatch(log.Text ?? "",@"(?:^|\n)\s*(?:Gotcha[!.,]?\s*)?(?:(?:Wild\s+)?"+target+@"\s+(?:was|has been)\s+caught[!.]?|You\s+(?:have\s+)?caught\s+(?:a\s+)?"+target+@"[!.]?)\s*(?:$|\n)",RegexOptions.IgnoreCase);
            if(success) {frame.Success=true;frame.Opponent=expected;frame.View=CatchView.Success;}
            if(Regex.IsMatch(log.Text ?? "",@"\b(fainted|no pp left|not enough pp|doesn.t affect|had no effect|no effect on|no .+balls left|can.t use that|cannot use that|attack missed|ran away|fled from battle)\b",RegexOptions.IgnoreCase)) {frame.Failure=true;frame.FailureReason="battle reported a failed action, fainting or escape";}
            // The backpack can overlay the battle without hiding the command buttons.
            var ballRegion=bag ?? menu;var ball=Unique(ballRegion,config.Ball);
            if(frame.Wild && ball!=null && Unique(ballRegion,"Balls")!=null) {
                var quantity=Stock(ballRegion,ball);int stock=quantity==null ? -1 : Quantity(quantity.Text);
                frame.View=CatchView.BallList;Put(frame,CatchActionKind.UseBall,Control(ball,config.Ball,0,stock));return frame;
            }
            if(frame.Wild && ReadIconParty(frame,menu,lead,chrome,image,config))return frame;
            if(frame.Wild) {
            var ppRows=menu.Rows.FindAll(r=>Quantity(r.Text,true)>=0);
            if(ppRows.Count>=1 || Unique(menu,"Choose Move")!=null) {
                frame.View=CatchView.Moves;
                var name=String.IsNullOrEmpty(wantedMove) ? null : Unique(menu,wantedMove);
                if(name!=null) {
                    var pp=Quantity(name.Text,true)>=0 ? name : Near(menu,name,t=>Quantity(t,true)>=0);
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
                        if(anchors.Count==4 && slot==wantedSlot)Put(frame,CatchActionKind.ChooseMove,Control(name,wantedMove,slot,Quantity(pp.Text,true)));
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
