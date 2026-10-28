using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MessageBox = AsnLabeler.AppDialogs;

namespace AsnLabeler
{
    sealed class SubstituteCardData
    {
        public string Title = "", Asn = "", Location = "", Initials = "";
        public bool Blank, Permanent;
        public DateTime? Taken, Due;
    }

    static class SubstituteCardRenderer
    {
        static readonly System.Drawing.Text.PrivateFontCollection officeFonts = new System.Drawing.Text.PrivateFontCollection();
        static readonly System.Drawing.Text.PrivateFontCollection officeBoldFonts = new System.Drawing.Text.PrivateFontCollection();
        static readonly Lazy<FontFamily> aptos = new Lazy<FontFamily>(FindAptos);
        internal static FontFamily TestFont;

        static ushort ReadU16(BinaryReader reader)
        { return (ushort)((reader.ReadByte()<<8)|reader.ReadByte()); }
        static uint ReadU32(BinaryReader reader)
        { return ((uint)ReadU16(reader)<<16)|ReadU16(reader); }

        static string FontName(string file, int nameId)
        {
            // Read the TrueType name table, not hashed Office cache filenames.
            using(var reader=new BinaryReader(File.OpenRead(file))) {
                reader.BaseStream.Position=4;
                int tables=ReadU16(reader); reader.BaseStream.Position=12;
                for(int i=0;i<tables;i++) {
                    string tag=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                    ReadU32(reader); uint offset=ReadU32(reader); ReadU32(reader);
                    if(tag!="name") continue;
                    reader.BaseStream.Position=offset; ReadU16(reader);
                    int count=ReadU16(reader), strings=ReadU16(reader);
                    for(int n=0;n<count;n++) {
                        int platform=ReadU16(reader); ReadU16(reader); int language=ReadU16(reader);
                        int name=ReadU16(reader), length=ReadU16(reader), start=ReadU16(reader);
                        if(platform==3 && language==0x409 && name==nameId) {
                            reader.BaseStream.Position=offset+strings+start;
                            return System.Text.Encoding.BigEndianUnicode.GetString(reader.ReadBytes(length));
                        }
                    }
                    break;
                }
            }
            return "";
        }

        static FontFamily FindAptos()
        {
            using(var installed = new System.Drawing.Text.InstalledFontCollection())
                if(installed.Families.Any(f => f.Name == "Aptos")) return new FontFamily("Aptos");
            // Office cloud fonts are local user assets, not redistributed with the app.
            string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","FontCache");
            if(Directory.Exists(cache)) foreach(string version in Directory.GetDirectories(cache))
            {
                string directory=Path.Combine(version,"CloudFonts","Aptos");
                if(!Directory.Exists(directory)) continue;
                foreach(string face in new[] {"Regular","Bold"})
                    foreach(string file in Directory.GetFiles(directory,"*.ttf"))
                        try {
                            if(FontName(file,1)!="Aptos" || FontName(file,2)!=face) continue;
                            (face=="Bold" ? officeBoldFonts : officeFonts).AddFontFile(file); break;
                        } catch(ArgumentException) {} catch(IOException) {} catch(UnauthorizedAccessException) {}
                if(officeFonts.Families.Any(f => f.Name=="Aptos")) break;
            }
            return officeFonts.Families.FirstOrDefault(f => f.Name == "Aptos");
        }

        internal static bool HasOriginalFont { get { return aptos.Value != null; } }

        static Font CardFont(float points, FontStyle style)
        {
            var family=TestFont ?? aptos.Value;
            if(family==null) throw new InvalidOperationException(Loc.T("Für die Originalvorlage wird Aptos benötigt. Bitte die Schrift installieren und die App neu starten."));
            if(TestFont==null && style==FontStyle.Bold && officeBoldFonts.Families.Length>0) family=officeBoldFonts.Families[0];
            return new Font(family,points,style,GraphicsUnit.Point);
        }

        // Logical coordinates are hundredths of an inch, matching PrintDocument.
        internal const int Width = 827, Height = 1169;
        internal static readonly Rectangle Content = new Rectangle(98, 106, 650, 1020);

        internal static Bitmap Preview(SubstituteCardData data)
        {
            var bitmap = new Bitmap(Width, Height);
            bitmap.SetResolution(100,100);
            try { using (var g = Graphics.FromImage(bitmap)) Draw(g, data); return bitmap; }
            catch { bitmap.Dispose(); throw; }
        }

        static void Text(Graphics g, string text, float points, FontStyle style, RectangleF area)
        {
            using (var font = CardFont(points,style))
            using (var format = new StringFormat { Trimming = StringTrimming.None })
            {
                var size = g.MeasureString(text, font, new SizeF(area.Width, 10000), format);
                if (size.Height > area.Height + 1)
                    throw new InvalidOperationException(Loc.T("Text zu lang für die A4-Karte. Bitte kürzen."));
                g.DrawString(text, font, Brushes.Black, area, format);
            }
        }

        static float Field(Graphics g, string caption, string value, float y)
        {
            string text = Loc.T(caption) + ": " + (value ?? "");
            using (var font = CardFont(14,FontStyle.Regular))
            {
                float height = Math.Max(36, g.MeasureString(text,font,631).Height + 8);
                if(y + height > 1065) throw new InvalidOperationException(Loc.T("Text zu lang für die A4-Karte. Bitte kürzen."));
                Text(g,text,14,FontStyle.Regular,new RectangleF(98,y,631,height));
                return y + height;
            }
        }

        internal static void Draw(Graphics g, SubstituteCardData data)
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var label = new Rectangle(656,106,91,91);
            using(var fill = new SolidBrush(Color.FromArgb(242,242,242))) g.FillRectangle(fill,label);
            using (var pen = new Pen(Color.FromArgb(0,32,48),2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawRectangle(pen, label);
            using(var font = CardFont(9,FontStyle.Regular))
            using(var format = new StringFormat { Alignment=StringAlignment.Center, LineAlignment=StringAlignment.Center })
                g.DrawString(Loc.T("ASN-Label hier einkleben"),font,Brushes.Gray,new RectangleF(667,116,69,71),format);
            using(var font = CardFont(24,FontStyle.Bold))
            using(var format = new StringFormat { Alignment=StringAlignment.Center })
                g.DrawString("— " + Loc.T("Dokument ausgelagert") + " —",font,Brushes.Black,new RectangleF(65,504,697,60),format);
            float y = Field(g, "Dokumententitel", data.Blank ? "" : data.Title,614);
            y = Field(g, "ASN", data.Blank ? "" : data.Asn,y);
            y = Field(g, "Aufbewahrungsort", data.Blank ? "" : data.Location,y);
            Text(g, Loc.T("Dauer der Auslagerung") + ":",14,FontStyle.Regular,new RectangleF(98,y,221,36));
            for (int i = 0; i < 2; i++)
            {
                int x = 321 + i * 133;
                g.DrawRectangle(Pens.Black, x, y+5, 15, 15);
                if (!data.Blank && data.Permanent == (i == 1))
                { g.DrawLine(Pens.Black, x+3,y+12,x+6,y+16); g.DrawLine(Pens.Black,x+6,y+16,x+12,y+8); }
                Text(g, Loc.T(i == 0 ? "temporär" : "dauerhaft"),14,FontStyle.Regular,new RectangleF(x+23,y,110,36));
            }
            y += 36;
            var culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.Language=="en" ? "en-GB" : "de-DE");
            if(!data.Blank && data.Taken.HasValue) y=Field(g,"Entnahme",data.Taken.Value.ToString("d",culture),y);
            if(!data.Blank && !data.Permanent && data.Due.HasValue) y=Field(g,"Geplante Rückgabe",data.Due.Value.ToString("d",culture),y);
            Field(g,"Handzeichen",data.Blank ? "" : data.Initials,y);
            using(var font = CardFont(11,FontStyle.Regular))
                g.DrawString("Version 1.3.2 | Oktober 2026",font,Brushes.Gray,new PointF(98,1099));
        }

        internal static bool IsA4(PaperSize paper)
        { return paper != null && Math.Abs(paper.Width - Width) <= 3 && Math.Abs(paper.Height - Height) <= 3; }

        internal static void Print(PrintDocument document, SubstituteCardData data)
        {
            document.DocumentName = Loc.T("Stellvertreterkarte");
            document.PrintController = new StandardPrintController();
            document.OriginAtMargins = false;
            document.PrintPage += (sender, e) => {
                if (!IsA4(e.PageSettings.PaperSize) || e.PageSettings.Landscape || !e.PageSettings.PrintableArea.Contains(Content))
                    throw new InvalidOperationException(Loc.T("Bitte einen A4-Drucker im Hochformat mit ausreichend Druckfläche auswählen."));
                var state = e.Graphics.Save();
                try {
                    e.Graphics.PageUnit = GraphicsUnit.Display;
                    e.Graphics.TranslateTransform(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY);
                    Draw(e.Graphics, data); e.HasMorePages = false;
                } finally { e.Graphics.Restore(state); }
            };
            document.Print();
        }
    }

    sealed class SubstituteCardForm : Form
    {
        readonly AppConfig config;
        readonly CopyDocument source;
        readonly Func<IWin32Window, Task<bool>> printLabel;
        readonly TextBox title = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill };
        readonly TextBox location = new TextBox { Multiline = true, MaxLength = 240, Dock = DockStyle.Fill };
        readonly TextBox initials = new TextBox { MaxLength = 80, Dock = DockStyle.Fill };
        readonly ComboBox duration = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly DateTimePicker taken = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill };
        readonly DateTimePicker due = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill };
        readonly CheckBox copy = new CheckBox { Text = Loc.T("Sekundärlabel mitdrucken"), AutoSize = true, Enabled = false };
        readonly PictureBox preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        readonly Button print = new Button { Text = Loc.T("A4 drucken…"), AutoSize = true, BackColor = Color.FromArgb(18,103,82), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Padding = new Padding(12,8,12,8) };
        readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        readonly TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        bool busy, submitted, valid, detecting;
        bool labelReady;

        internal SubstituteCardForm(AppConfig config, CopyDocument source = null, Func<IWin32Window, Task<bool>> printLabel = null, bool testMode = false)
        {
            this.config = config; this.source = source; this.printLabel = printLabel;
            Text = Loc.T(source == null ? "Blanko-Stellvertreterkarte" : "Stellvertreterkarte") + (config.DemoMode ? " · Demo" : "");
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(900,650);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = config.DemoMode ? Color.FromArgb(255,251,235) : Color.FromArgb(246,247,249);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,50)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,65));
            root.Controls.Add(fields,0,0); root.Controls.Add(preview,1,0);
            title.Text = source == null ? "" : source.documentTitle;
            duration.Items.AddRange(new object[] {Loc.T("temporär"), Loc.T("dauerhaft")}); duration.SelectedIndex = 0;
            AddField("Dokumententitel", title, 65);
            AddField("ASN", new Label { Text = source == null ? "—" : source.barcodeText, Dock = DockStyle.Fill },28);
            AddField("Aufbewahrungsort", location,65); AddField("Dauer der Auslagerung",duration,30);
            AddField("Entnahme",taken,30); AddField("Geplante Rückgabe",due,30); AddField("Handzeichen",initials,30);
            fields.Enabled = source != null;
            root.Controls.Add(copy,0,1); root.SetColumnSpan(copy,2);
            root.Controls.Add(status,0,2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            actions.Controls.Add(print); root.Controls.Add(actions,1,2); Controls.Add(root);
            copy.Visible = source != null && printLabel != null;
            float scale;
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            ClientSize = new Size((int)(900*scale),(int)(650*scale));
            MinimumSize = SizeFromClientSize(new Size((int)(800*scale),(int)(650*scale)));
            root.Padding = new Padding((int)(20*scale));
            foreach (var panel in new[] {root,fields})
                foreach (RowStyle row in panel.RowStyles) if(row.SizeType == SizeType.Absolute) row.Height *= scale;
            print.Padding = new Padding((int)(12*scale),(int)(8*scale),(int)(12*scale),(int)(8*scale));
            foreach (var field in new[] {title,location,initials}) field.TextChanged += delegate { RefreshPreview(); };
            duration.SelectedIndexChanged += delegate {
                int row = fields.GetRow(due);
                bool visible = duration.SelectedIndex == 0;
                due.Visible = visible; fields.GetControlFromPosition(0,row-1).Visible = visible;
                fields.RowStyles[row-1].Height = visible ? 23*scale : 0;
                fields.RowStyles[row].Height = visible ? 30*scale : 0;
                RefreshPreview();
            };
            taken.ValueChanged += delegate { RefreshPreview(); }; due.ValueChanged += delegate { RefreshPreview(); };
            print.Click += async delegate { await PrintCard(); };
            FormClosing += (sender,e) => { if (busy) e.Cancel = true; };
            Shown += async delegate { if (!testMode && copy.Visible) await DetectLabelPrinter(); };
            RefreshPreview();
        }

        void AddField(string caption, Control control, int height)
        {
            int row = fields.RowCount; fields.RowCount += 2;
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute,23)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            fields.Controls.Add(new Label { Text = Loc.T(caption), Dock = DockStyle.Fill },0,row);
            fields.Controls.Add(control,0,row+1);
        }

        SubstituteCardData Data()
        {
            if (source != null && duration.SelectedIndex == 0 && taken.Checked && due.Checked && due.Value.Date < taken.Value.Date)
                throw new InvalidOperationException(Loc.T("Die Rückgabe darf nicht vor der Entnahme liegen."));
            return new SubstituteCardData { Blank = source == null, Title = source == null ? "" : source.documentTitle, Asn = source == null ? "" : source.barcodeText,
                Location = location.Text, Initials = initials.Text, Permanent = duration.SelectedIndex == 1,
                Taken = taken.Checked ? (DateTime?)taken.Value.Date : null, Due = due.Checked && duration.SelectedIndex == 0 ? (DateTime?)due.Value.Date : null };
        }

        void RefreshPreview()
        {
            var old = preview.Image; preview.Image = null; if (old != null) old.Dispose();
            try { preview.Image = SubstituteCardRenderer.Preview(Data()); valid = true; status.Text = ""; }
            catch (Exception ex) { valid = false; status.Text = ex.Message; }
            print.Enabled = valid && !busy && !submitted && !detecting;
        }

        async Task DetectLabelPrinter()
        {
            detecting = true; print.Enabled = false;
            try {
                bool ready = config.DemoMode || await Task.Run(() => {
                    var choice = PrinterBackends.Select(PrinterBackends.DiscoverBrother(),config.PrinterName);
                    using (var printer = new BpacPrinterSession(choice,config.UsePfRightAligned)) return printer.ReadStatus().CanPrint;
                });
                if (!IsDisposed && !busy && !submitted) copy.Checked = copy.Enabled = labelReady = ready;
            } catch { if (!IsDisposed) copy.Enabled = copy.Checked = false; }
            finally { detecting = false; if (!IsDisposed) print.Enabled = valid && !busy && !submitted; }
        }

        static bool SelectPrinter(PrintDocument document, IWin32Window owner)
        {
            document.DefaultPageSettings.PaperSize = new PaperSize("A4",SubstituteCardRenderer.Width,SubstituteCardRenderer.Height);
            using(var dialog = new PrintDialog { Document=document, UseEXDialog=true, AllowSomePages=false, AllowSelection=false, AllowPrintToFile=false })
                if(dialog.ShowDialog(owner)!=DialogResult.OK) return false;
            var paper=document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(SubstituteCardRenderer.IsA4);
            if(paper==null || document.PrinterSettings.PrinterName.IndexOf("QL-",StringComparison.OrdinalIgnoreCase)>=0)
                throw new InvalidOperationException(Loc.T("Bitte einen A4-Drucker auswählen, keinen Labeldrucker."));
            document.DefaultPageSettings.PaperSize=paper; document.DefaultPageSettings.Landscape=false;
            document.PrinterSettings.Copies=1; document.PrinterSettings.Duplex=Duplex.Simplex;
            return true;
        }

        internal static async Task PrintBlank(IWin32Window owner, bool demo)
        {
            bool sent=false;
            try {
                using(var document=new PrintDocument()) {
                    if(!demo && !SelectPrinter(document,owner)) return;
                    sent=true;
                    if(demo) await Task.Delay(1200);
                    else await Task.Run(() => SubstituteCardRenderer.Print(document,new SubstituteCardData {Blank=true}));
                }
                MessageBox.Show(owner,(demo ? "Demo: " : "")+Loc.T("A4-Auftrag übergeben. Bitte Ausdruck prüfen."),Loc.T("Blanko-Stellvertreterkarte"),MessageBoxButtons.OK,MessageBoxIcon.Information);
            } catch(Exception ex) {
                MessageBox.Show(owner,Loc.T(sent ? "A4-Druckstatus unklar. Vor erneutem Druck Warteschlange prüfen." : "Kein A4-Auftrag gestartet.")+"\r\n"+ex.Message,Loc.T("Blanko-Stellvertreterkarte"),MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        }

        async Task PrintCard()
        {
            if (busy || submitted || !valid || detecting) return;
            busy = true; print.Enabled = false; fields.Enabled = copy.Enabled = false;
            bool sendStarted = false;
            try {
                var data = Data();
                if (source != null && !config.DemoMode) await Task.Run(() => CopyLabelForm.VerifyDocument(source, new ReservationClient(config).CopyDocuments(asn:source.asnNumber)));
                using (var document = new PrintDocument())
                {
                    if (!config.DemoMode && !SelectPrinter(document,this)) return;
                    status.Text = Loc.T("A4-Druckauftrag wird übergeben…");
                    sendStarted = true;
                    if (config.DemoMode) await Task.Delay(1200);
                    else await Task.Run(() => SubstituteCardRenderer.Print(document,data));
                }
                submitted = true;
                status.Text = Loc.T("A4-Auftrag übergeben. Bitte Ausdruck prüfen.");
                if (copy.Checked && printLabel != null)
                {
                    copy.Checked = false;
                    bool success = await printLabel(this);
                    status.Text = Loc.T(success ? "A4-Auftrag übergeben; Sekundärlabel gedruckt." : "A4-Auftrag übergeben; Sekundärlabel nicht bestätigt. Bitte prüfen.");
                }
            }
            catch (Exception ex)
            {
                bool cardQueued = submitted;
                if (sendStarted) submitted = true; // Never resend an ambiguous spool job automatically.
                status.Text = Loc.T(cardQueued ? "A4-Auftrag übergeben; Sekundärlabel nicht bestätigt. Bitte prüfen." : sendStarted ? "A4-Druckstatus unklar. Vor erneutem Druck Warteschlange prüfen." : "Kein A4-Auftrag gestartet.");
                MessageBox.Show(this,status.Text + "\r\n" + ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
            finally { busy = false; fields.Enabled = source != null && !submitted; copy.Enabled = !submitted && labelReady; print.Enabled = valid && !submitted; }
        }

        internal static IEnumerable<string> Validate(string path = null)
        {
            // CI without Office can exercise flow/layout, but must not claim Aptos visual acceptance.
            if(!SubstituteCardRenderer.HasOriginalFont) {
                if(path!=null) throw new InvalidOperationException("Aptos is required for reference rendering.");
                Console.WriteLine("Card flow tests use a test font; Aptos visual acceptance unavailable on this host.");
                SubstituteCardRenderer.TestFont=FontFamily.GenericSansSerif;
            }
            try {
            var failures = new List<string>();
            foreach (bool blank in new[] {false,true})
            {
                using (var bitmap = SubstituteCardRenderer.Preview(new SubstituteCardData { Blank=blank, Title="Geburtsurkunde / Birth certificate", Asn="ASN00018",Location="Notariat – Original zur Beglaubigung", Taken=new DateTime(2026,10,27), Due=new DateTime(2026,11,3), Initials="OM" }))
                    if (path != null) bitmap.Save(Path.Combine(Path.GetDirectoryName(path),"card-"+(blank ? "blank" : "filled")+"-"+Loc.Language+".png"));
            }
            if (SubstituteCardRenderer.IsA4(new PaperSize("Label",91,91)) || !SubstituteCardRenderer.IsA4(new PaperSize("A4",827,1169))) failures.Add("A4 media validation failed.");
            try { using (var bitmap = SubstituteCardRenderer.Preview(new SubstituteCardData {Title=new string('W',10000)})) {} failures.Add("Overflowing card accepted."); } catch (InvalidOperationException) {}
            using (var form = new SubstituteCardForm(new AppConfig {DemoMode=true},new CopyDocument {documentTitle="Geburtsurkunde",barcodeText="ASN00018"},null,true))
            {
                form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-20000,-20000); form.Show();
                if (!form.valid || form.copy.Visible) failures.Add("Card after label must not print another label.");
                form.title.Text="Changed";
                if(!form.title.ReadOnly || form.Data().Title!="Geburtsurkunde") failures.Add("Paperless title must be immutable.");
                form.due.Checked=true; form.duration.SelectedIndex=1;
                if(form.due.Visible || form.Data().Due.HasValue) failures.Add("Permanent removal must not have a return date.");
                form.duration.SelectedIndex=0;
                form.taken.Value=new DateTime(2026,11,3); form.taken.Checked=true; form.due.Value=new DateTime(2026,10,27); form.due.Checked=true; form.RefreshPreview();
                if (form.valid) failures.Add("Invalid return date accepted.");
                form.due.Checked=false; form.RefreshPreview(); Application.DoEvents();
                if(path!=null) using(var image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(Path.Combine(Path.GetDirectoryName(path),"card-form-"+Loc.Language+".png")); }
                foreach (Control field in form.fields.Controls)
                    if (field.Bottom > form.fields.ClientSize.Height || field.Height < field.Font.Height) failures.Add("Card field clipped at current DPI.");
            }
            if (path == null) foreach (bool includeLabel in new[] {true,false})
            {
                int labels = 0;
                using (var form = new SubstituteCardForm(new AppConfig {DemoMode=true},new CopyDocument {documentTitle="Test",barcodeText="ASN00001"},owner => { labels++; return Task.FromResult(false); },true))
                {
                    form.copy.Checked=includeLabel;
                    var task=form.PrintCard();
                    if(form.print.Enabled || form.fields.Enabled) failures.Add("Card controls not locked during submission.");
                    var wait=System.Diagnostics.Stopwatch.StartNew();
                    while(!task.IsCompleted && wait.ElapsedMilliseconds<5000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    task.GetAwaiter().GetResult();
                    form.PrintCard().GetAwaiter().GetResult();
                    if(!form.submitted || labels!=(includeLabel ? 1 : 0) || form.print.Enabled) failures.Add("Card retry duplicated a job or ignored label opt-out.");
                }
            }
            return failures;
            } finally { SubstituteCardRenderer.TestFont=null; }
        }

        protected override void Dispose(bool disposing)
        { if(disposing && preview.Image!=null) preview.Image.Dispose(); base.Dispose(disposing); }
    }
}
