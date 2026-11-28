using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace MovementPrototype {
    public sealed class ButtonWord {
        public string Text;
        public RectangleF Bounds;
        public ButtonWord(string text,float x,float y,float width,float height) { Text=text; Bounds=new RectangleF(x,y,width,height); }
        public PointF Center { get { return new PointF(Bounds.Left+Bounds.Width/2,Bounds.Top+Bounds.Height/2); } }
    }
    public sealed class RunButtonCandidate {
        public string Header, Controls;
        public Point? ClientPoint;
        public Point? ItemsPoint;
        public Rectangle? IdentityBounds;
    }
    public static class RunButtonMatch {
        public static bool IsSubmenu(string text) {
            return Regex.IsMatch(text ?? "",@"\bChoose\s*(Item|Attack|Move|Pok[e\u00e9]mon)\b",RegexOptions.IgnoreCase);
        }
        public static bool FindControls(CatchTextRegion text,Rectangle area,out Point? run,out Point? items) {
            run=items=null;
            if(IsSubmenu(text.Text))return false;
            var words=new List<ButtonWord>();
            foreach(var row in text.Rows)foreach(var word in row.Words)
                words.Add(new ButtonWord(word.Text,word.Bounds.X-area.Left,word.Bounds.Y-area.Top,word.Bounds.Width,word.Bounds.Height));
            var runCenter=Locate(words,area.Width,area.Height);
            if(!runCenter.HasValue)return false;
            var itemCenter=ItemsAnchor(words,runCenter.Value,area.Width,area.Height);
            if(!itemCenter.HasValue)return false;
            run=Point.Round(new PointF(runCenter.Value.X+area.Left,runCenter.Value.Y+area.Top));
            items=Point.Round(new PointF(itemCenter.Value.X+area.Left,itemCenter.Value.Y+area.Top));
            return true;
        }
        public static PointF? ItemsAnchor(IList<ButtonWord> words,PointF run,int width,int height) {
            foreach(var word in words) {
                if(!String.Equals(word.Text,"Items",StringComparison.OrdinalIgnoreCase) && !String.Equals(word.Text,"tems",StringComparison.OrdinalIgnoreCase))continue;
                var p=word.Center;
                if(run.X-p.X>=width*.2 && Math.Abs(run.Y-p.Y)<=Math.Max(12,height*.09))return p;
            }
            return null;
        }
        public static bool AllowsOpponent(string expected,EncounterReading fresh,HashSet<string> targets) {
            return fresh!=null && fresh.Battle && !fresh.Ambiguous && fresh.Name!=null && fresh.Name==expected && !EncounterRecognition.CloseToTarget(fresh.Name,targets);
        }
        public static PointF? Locate(IList<ButtonWord> words,int width,int height) {
            PointF? selected=null;
            foreach(var run in words) {
                if(!String.Equals(run.Text,"Run",StringComparison.OrdinalIgnoreCase)) continue;
                var point=run.Center;
                if(point.X<width*.5 || point.X>=width || point.Y<height*.25 || point.Y>=height) continue;
                bool rowAnchor=false;
                foreach(var item in words) {
                    if(!String.Equals(item.Text,"Items",StringComparison.OrdinalIgnoreCase) && !String.Equals(item.Text,"tems",StringComparison.OrdinalIgnoreCase)) continue;
                    var itemPoint=item.Center;
                    if(point.X-itemPoint.X>=width*.2 && Math.Abs(point.Y-itemPoint.Y)<=Math.Max(12,height*.09)) rowAnchor=true;
                }
                if(!rowAnchor) continue;
                if(selected.HasValue) return null;
                selected=point;
            }
            if(selected.HasValue)return selected;
            // A blue hover sprite can obscure the Run word. Recover its grid position
            // only when the other three menu captions establish both rows and columns.
            foreach(var items in words) {
                if(!String.Equals(items.Text,"Items",StringComparison.OrdinalIgnoreCase) && !String.Equals(items.Text,"tems",StringComparison.OrdinalIgnoreCase))continue;
                foreach(var fight in words) {
                    if(!String.Equals(fight.Text,"Fight",StringComparison.OrdinalIgnoreCase))continue;
                    if(Math.Abs(items.Center.X-fight.Center.X)>width*.12 || items.Center.Y-fight.Center.Y<height*.08)continue;
                    foreach(var pokemon in words) {
                        string label=pokemon.Text.ToLowerInvariant().Replace("é","e");
                        if(label!="pokemon" && label!="okemon")continue;
                        if(pokemon.Center.X-fight.Center.X<width*.20 || Math.Abs(pokemon.Center.Y-fight.Center.Y)>Math.Max(12,height*.09))continue;
                        var inferred=new PointF(pokemon.Center.X,items.Center.Y);
                        if(inferred.X<width*.5 || inferred.X>=width || inferred.Y<height*.25 || inferred.Y>=height)continue;
                        if(selected.HasValue && Math.Abs(selected.Value.X-inferred.X)+Math.Abs(selected.Value.Y-inferred.Y)>5)return null;
                        selected=inferred;
                    }
                }
            }
            return selected;
        }
        public static List<string> Tests() {
            var words=new List<ButtonWord> {new ButtonWord("Items",10,190,60,20),new ButtonWord("Run",230,190,40,20)};
            if(!Locate(words,300,300).HasValue) throw new Exception("Aligned Run/Items row must be recognized");
            words.RemoveAt(0); if(Locate(words,300,300).HasValue) throw new Exception("Isolated Run text must not authorize clicking");
            words.Add(new ButtonWord("Items",10,20,60,20)); if(Locate(words,300,300).HasValue) throw new Exception("Mismatched button rows must be rejected");
            words[1]=new ButtonWord("tems",10,190,60,20);
            words.Add(new ButtonWord("Run",260,190,40,20)); if(Locate(words,300,300).HasValue) throw new Exception("Ambiguous Run candidates must be rejected");
            var grid=new List<ButtonWord> {new ButtonWord("Fight",20,100,50,20),new ButtonWord("Pokemon",210,100,80,20),new ButtonWord("Items",20,190,50,20)};
            if(!Locate(grid,300,300).HasValue)throw new Exception("Three aligned menu labels must recover a hover-obscured Run position");
            grid.RemoveAt(0);if(Locate(grid,300,300).HasValue)throw new Exception("Incomplete menu grids must not authorize inferred Run positions");
            var menu=new Rectangle(720,150,280,450);
            var row=new CatchTextRow();
            row.Words.Add(new ButtonWord("Items",730,563,20,10));
            row.Words.Add(new ButtonWord("Run",930,563,20,10));
            var region=new CatchTextRegion {Text="Items Run",Rows=new List<CatchTextRow> {row}};
            Point? r,i;
            if(!FindControls(region,menu,out r,out i) || r!=new Point(940,568) || i!=new Point(740,568))
                throw new Exception("Bottom-row Run and Items in a floating battle must use client coordinates");
            region.Text+="\nChoose Item";
            if(FindControls(region,menu,out r,out i) || r.HasValue || i.HasValue)
                throw new Exception("Underlying labels in an open item submenu cannot authorize clicking Run");
            var targets=EncounterRecognition.Targets("Mimikyu");
            if(!AllowsOpponent("seedot",new EncounterReading {Battle=true,Name="seedot"},targets)) throw new Exception("Fresh non-target should pass identity gate");
            if(AllowsOpponent("mimikyu",new EncounterReading {Battle=true,Name="mimikyu"},targets) || AllowsOpponent("mimiky",new EncounterReading {Battle=true,Name="mimiky"},targets)) throw new Exception("Exact and near targets must never authorize clicking");
            if(AllowsOpponent("seedot",new EncounterReading {Battle=true,Name="arbok"},targets) || AllowsOpponent("seedot",new EncounterReading {Battle=true,Name="seedot",Ambiguous=true},targets) || AllowsOpponent("seedot",new EncounterReading {Battle=false,Name="seedot"},targets) || AllowsOpponent("seedot",new EncounterReading {Battle=true},targets)) throw new Exception("Changed, ambiguous, absent or non-battle identities must be rejected");
            return new List<string> {
                "PASS: Run click requires one right-side Run label and an aligned Items anchor; isolated, misaligned and ambiguous text is rejected",
                "PASS: visible Run clicks reject exact/near targets, changed opponents, unknown names, ambiguous reads and non-battle screens"
            };
        }
    }
}
