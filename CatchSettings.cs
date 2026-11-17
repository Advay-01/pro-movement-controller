using System;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MovementPrototype {
    public static class CatchSettingsFile {
        public static CatchConfig Load(string path) {
            if(!File.Exists(path))return new CatchConfig();
            if(new FileInfo(path).Length>32768)throw new InvalidOperationException("Catching settings file is too large.");
            var settings=new JavaScriptSerializer().Deserialize<CatchConfig>(File.ReadAllText(path));
            if(settings==null)throw new InvalidOperationException("Catching settings are empty.");settings.Validate(false);return settings;
        }
        public static void Save(string path,CatchConfig settings) {
            settings.Validate(false);File.WriteAllText(path,new JavaScriptSerializer().Serialize(settings));
        }
    }
    public sealed class CatchSettingsDialog : Form {
        readonly TextBox catcher=new TextBox {Width=165,MaxLength=32},lead=new TextBox {Width=165,MaxLength=32};
        readonly NumericUpDown party=new NumericUpDown {Minimum=1,Maximum=6,Width=50};
        readonly NumericUpDown maximum=new NumericUpDown {Minimum=1,Maximum=100,Value=10,Width=65};
        readonly ComboBox ball=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=155};
        readonly CheckBox resume=new CheckBox {Text="Resume hunting after a confirmed catch",Checked=true,AutoSize=true};
        readonly CheckBox verified=new CheckBox {Text="I have tested this catching sequence for my protected species",AutoSize=true};
        readonly DataGridView moves=new DataGridView {Width=570,Height=150,AllowUserToAddRows=false,AllowUserToDeleteRows=true,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersWidth=35,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
        bool loading=true;
        public CatchConfig Settings {get;private set;}
        static FlowLayoutPanel Row(params object[] controls) {
            var row=new FlowLayoutPanel {AutoSize=true,WrapContents=false};
            foreach(var value in controls)row.Controls.Add(value is Control ? (Control)value : new Label {Text=value.ToString(),AutoSize=true,Margin=new Padding(3,7,4,0)});
            return row;
        }
        public CatchSettingsDialog(CatchConfig initial,string targets="") {
            Text="Auto-catch settings — this profile";Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(630,720);MinimumSize=new Size(640,600);
            var flow=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(18)};
            var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=52,Padding=new Padding(18,6,8,6)};Controls.Add(flow);Controls.Add(footer);
            flow.Controls.Add(new Label {Text="Configure your catcher and ordered battle steps",AutoSize=true,Font=new Font(Font,FontStyle.Bold)});
            flow.Controls.Add(new Label {Text="Protected targets: "+(targets.Length==0 ? "enter target names in the profile first" : targets),AutoSize=true,MaximumSize=new Size(570,0)});
            flow.Controls.Add(Row("Catcher's displayed name",catcher,"Party slot",party));
            flow.Controls.Add(Row("Ball",ball,"Maximum balls per target",maximum));
            flow.Controls.Add(new Label {Text="In PRO, leave the backpack on its Balls tab with your ball visible.\nUnknown menus or unreadable PP/stock stop catching for manual attention.",AutoSize=true,MaximumSize=new Size(570,0)});
            flow.Controls.Add(Row("Leading Pokémon after catch (optional)",lead));
            flow.Controls.Add(new Label {Text="Steps run in order. Slot is the move's current position (1–4).\nFalse Swipe uses are bounded; a visual HP bar cannot prove exactly 1 HP.",AutoSize=true,MaximumSize=new Size(570,0)});
            var nameColumn=new DataGridViewComboBoxColumn {Name="Move",HeaderText="Move / preparation",FillWeight=180};nameColumn.Items.AddRange(CatchConfig.AllowedMoves);
            moves.Columns.Add(nameColumn);moves.Columns.Add(new DataGridViewTextBoxColumn {Name="Slot",HeaderText="Move slot",FillWeight=75});moves.Columns.Add(new DataGridViewTextBoxColumn {Name="Uses",HeaderText="Uses",FillWeight=60});flow.Controls.Add(moves);
            Button add=new Button {Text="Add step",AutoSize=true},remove=new Button {Text="Remove step",AutoSize=true},up=new Button {Text="Move up",AutoSize=true},down=new Button {Text="Move down",AutoSize=true};
            flow.Controls.Add(Row(add,remove,up,down));flow.Controls.Add(resume);
            flow.Controls.Add(new Label {Text="For Mimikyu, verify the Ghost/Disguise preparation sequence first.\nFor escape-capable targets, include your tested trapping/preparation steps.",AutoSize=true,MaximumSize=new Size(570,0)});
            flow.Controls.Add(verified);
            Button save=new Button {Text="Save settings",AutoSize=true},cancel=new Button {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};footer.Controls.Add(save);footer.Controls.Add(cancel);CancelButton=cancel;
            ball.Items.AddRange(CatchConfig.Balls);catcher.Text=initial.Catcher;party.Value=Math.Max(1,Math.Min(6,initial.PartySlot));maximum.Value=Math.Max(1,Math.Min(100,initial.MaximumBalls));ball.SelectedItem=initial.Ball;if(ball.SelectedIndex<0)ball.SelectedIndex=2;lead.Text=initial.ExpectedLead;resume.Checked=initial.Resume;
            if(initial.Moves!=null)foreach(var move in initial.Moves)moves.Rows.Add(move.Name,move.Slot,move.Uses);
            verified.Checked=initial.VerifiedRecipe;
            add.Click+=delegate {if(moves.Rows.Count<8)moves.Rows.Add("False Swipe",1,1);};
            remove.Click+=delegate {if(moves.CurrentRow!=null)moves.Rows.Remove(moves.CurrentRow);};
            up.Click+=delegate {Shift(-1);};down.Click+=delegate {Shift(1);};
            moves.CellValueChanged+=delegate {if(!loading)verified.Checked=false;};moves.RowsRemoved+=delegate {if(!loading)verified.Checked=false;};moves.RowsAdded+=delegate {if(!loading)verified.Checked=false;};
            catcher.TextChanged+=delegate {if(!loading)verified.Checked=false;};party.ValueChanged+=delegate {if(!loading)verified.Checked=false;};
            save.Click+=delegate {
                try {
                    moves.EndEdit();var result=new CatchConfig {Catcher=catcher.Text.Trim(),PartySlot=(int)party.Value,Ball=(string)ball.SelectedItem,MaximumBalls=(int)maximum.Value,ExpectedLead=lead.Text.Trim(),Resume=resume.Checked,VerifiedRecipe=verified.Checked};result.Moves.Clear();
                    foreach(DataGridViewRow row in moves.Rows)result.Moves.Add(new CatchMove {Name=Convert.ToString(row.Cells[0].Value),Slot=Convert.ToInt32(row.Cells[1].Value),Uses=Convert.ToInt32(row.Cells[2].Value)});
                    result.ProtectedTargets=targets;result.Validate(false);Settings=result;DialogResult=DialogResult.OK;Close();
                }catch(Exception ex) {MessageBox.Show(this,ex.Message,"Check catching settings",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            };
            loading=false;
        }
        void Shift(int direction) {
            if(moves.CurrentRow==null)return;int old=moves.CurrentRow.Index,next=old+direction;
            if(next<0 || next>=moves.Rows.Count)return;
            for(int cell=0;cell<3;cell++) {object temp=moves.Rows[old].Cells[cell].Value;moves.Rows[old].Cells[cell].Value=moves.Rows[next].Cells[cell].Value;moves.Rows[next].Cells[cell].Value=temp;}
            moves.CurrentCell=moves.Rows[next].Cells[0];verified.Checked=false;
        }
    }
}
