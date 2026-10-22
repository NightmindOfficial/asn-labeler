using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MessageBox = AsnLabeler.AppDialogs;

namespace AsnLabeler
{
    sealed class CopyDocument
    {
        public int documentId { get; set; }
        public int asnNumber { get; set; }
        public string barcodeText { get; set; }
        public string documentTitle { get; set; }
        public string status { get; set; }
        public int secondaryLabelCount { get; set; }
    }

    // A receipt retry only reports completion; it never sends another print job.
    static class CopyReceipts
    {
        static readonly object Gate = new object();
        static string Folder(string server, string key)
        {
            string identity = server.Trim().TrimEnd('/') + "\n" + key;
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsnLabeler", "CopyReceipts", hash);
        }

        public static string Prepare(AppConfig config, string id)
        {
            lock (Gate)
            {
                string dir = Folder(config.ServerUrl, config.ApiKey); Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, id + ".prepared");
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.WriteByte(0); stream.Flush(true); }
                return path;
            }
        }

        public static void Complete(string path)
        {
            lock (Gate) File.Move(path, Path.ChangeExtension(path, ".confirmed"));
        }

        public static void Flush(AppConfig config, ReservationClient client)
        {
            if (config.DemoMode) return;
            lock (Gate)
            {
                // Freeze the endpoint and identity while Settings may change on the UI thread.
                string server = config.ServerUrl, key = config.ApiKey;
                var endpoint = new ReservationClient(new AppConfig { ServerUrl = server }, key);
                FlushFolder(Folder(server, key), endpoint.ConfirmCopy);
            }
        }

        internal static void FlushFolder(string dir, Action<string> confirm)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string path in Directory.GetFiles(dir, "*.confirmed"))
            {
                confirm(Path.GetFileNameWithoutExtension(path));
                File.Delete(path);
            }
        }

        public static string Tooltip(int count)
        { return Loc.T("Zu diesem Dokument wurden {0} Sekundärlabels erstellt.").Replace("{0}", count.ToString()); }
    }

    sealed class CopyDocumentList
    {
        public CopyDocument[] items { get; set; }
        public bool hasMore { get; set; }
    }

    sealed class CopyLabelForm : Form
    {
        readonly AppConfig config;
        readonly ReservationClient client;
        readonly DataGridView grid = new DataGridView();
        readonly PictureBox preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        readonly Label previewTitle = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly Label pageLabel = new Label { AutoSize = true, Margin = new Padding(10) };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };
        readonly Button print = new Button { Text = Loc.T("Sekundärlabel drucken"), AutoSize = true, Height = 40, Padding = new Padding(8), BackColor = Color.FromArgb(18, 103, 82), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        readonly Button previous = new MaterialIconButton("\ue5cb") { AccessibleName = Loc.T("Vorherige Seite"), Size = new Size(36, 36) };
        readonly Button next = new MaterialIconButton("\ue5cc") { AccessibleName = Loc.T("Nächste Seite"), Size = new Size(36, 36) };
        readonly Button refresh = new MaterialIconButton("\ue5d5") { AccessibleName = Loc.T("Dokumente aktualisieren"), Size = new Size(36, 36) };
        readonly Timer timer = new Timer();
        readonly ToolTip tips = new ToolTip();
        MediaInfo media = MediaInfo.DefaultDk11221();
        bool busy, hasMore;
        int page = 1;

        public CopyLabelForm(AppConfig config, bool testMode = false)
        {
            this.config = config;
            client = new ReservationClient(config);
            Text = Loc.T("Sekundärlabel") + (config.DemoMode ? " · Demo" : "");
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(840, 560);
            MinimumSize = new Size(820, 580);
            BackColor = config.DemoMode ? Color.FromArgb(255, 251, 235) : Color.FromArgb(246, 247, 249);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));
            var title = new Label { Text = Loc.T("Dokument in Paperless auswählen"), Font = new Font("Segoe UI", 16, FontStyle.Bold), Dock = DockStyle.Fill };
            layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 2);
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ASN", FillWeight = 30, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("Dokumenttitel"), FillWeight = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("Kopien"), FillWeight = 15, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.SelectionChanged += delegate { UpdatePreview(); UpdateControls(); };
            layout.Controls.Add(grid, 0, 1);
            var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12, 0, 0, 0) };
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewLayout.Controls.Add(previewTitle, 0, 0); previewLayout.Controls.Add(preview, 0, 1);
            layout.Controls.Add(previewLayout, 1, 1);
            var paging = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            paging.Controls.AddRange(new Control[] { previous, pageLabel, next, refresh });
            layout.Controls.Add(paging, 0, 2);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 0, 0) };
            commands.Controls.Add(print); layout.Controls.Add(commands, 1, 2);
            layout.Controls.Add(status, 0, 3); layout.SetColumnSpan(status, 2);
            layout.Controls.Add(progress, 0, 4); layout.SetColumnSpan(progress, 2);
            Controls.Add(layout);
            float scale;
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            ClientSize = new Size((int)(840 * scale), (int)(560 * scale));
            MinimumSize = SizeFromClientSize(new Size((int)(800 * scale), (int)(540 * scale)));
            layout.Padding = new Padding((int)(20 * scale));
            foreach (RowStyle row in layout.RowStyles) if (row.SizeType == SizeType.Absolute) row.Height *= scale;
            previewLayout.RowStyles[0].Height = 40 * scale;
            previewLayout.Padding = new Padding((int)(12 * scale), 0, 0, 0);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = (int)(32 * scale);
            grid.RowTemplate.Height = (int)(28 * scale);
            foreach (var button in new[] { previous, next, refresh }) button.Size = new Size((int)(36 * scale), (int)(36 * scale));
            print.Height = (int)(40 * scale);
            print.Padding = new Padding((int)(8 * scale));
            tips.SetToolTip(previous, Loc.T("Vorherige Seite")); tips.SetToolTip(next, Loc.T("Nächste Seite")); tips.SetToolTip(refresh, Loc.T("Dokumente aktualisieren"));
            previous.Click += async delegate { if (!busy && page > 1) { page--; await LoadDocuments(); } };
            next.Click += async delegate { if (!busy && hasMore) { page++; await LoadDocuments(); } };
            refresh.Click += async delegate { await LoadDocuments(); };
            print.Click += async delegate { await PrintCopy(); };
            timer.Interval = config.RefreshMilliseconds;
            timer.Tick += async delegate { if (ContainsFocus && !busy) await LoadDocuments(); };
            Shown += async delegate { if (!testMode) { await LoadDocuments(); timer.Start(); } };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            UpdateControls();
        }

        CopyDocument Selected { get { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as CopyDocument; } }

        static CopyDocumentList DemoDocuments()
        {
            return new CopyDocumentList { items = new[] {
                new CopyDocument { documentId = 1, asnNumber = 1, barcodeText = "ASN00001", documentTitle = Loc.T("Demo · Urkunde"), status = "imported" },
                new CopyDocument { documentId = 2, asnNumber = 2, barcodeText = "ASN00002", documentTitle = Loc.T("Demo · Vertrag"), status = "imported" }
            }.Reverse().ToArray() };
        }

        void UpdateControls()
        {
            grid.Enabled = refresh.Enabled = !busy;
            previous.Enabled = !busy && page > 1; next.Enabled = !busy && hasMore;
            print.Enabled = !busy && Selected != null && Selected.status == "imported";
            progress.Visible = busy;
            pageLabel.Text = Loc.T("Seite ") + page;
        }

        void UpdatePreview()
        {
            var old = preview.Image; preview.Image = null; if (old != null) old.Dispose();
            previewTitle.Text = Loc.T("Vorschau · ") + media.WidthMm + " × " + media.PageLengthMm + " mm";
            if (Selected == null) return;
            try { preview.Image = LabelRenderer.Render(Selected.barcodeText, media, config.UsePfRightAligned, true); }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        async Task LoadDocuments()
        {
            if (busy) return;
            busy = true; UpdateControls();
            try
            {
                var selected = Selected;
                var data = config.DemoMode ? DemoDocuments() : await Task.Run(() => { CopyReceipts.Flush(config, client); return client.CopyDocuments(page); });
                if (data.items == null) throw new InvalidOperationException(Loc.T("Keine gültige Dokumentliste erhalten. Service 1.3.2 benötigt."));
                grid.Rows.Clear(); hasMore = data.hasMore;
                foreach (var item in data.items)
                {
                    if (item.status != "imported") continue;
                    if (config.DemoMode) item.secondaryLabelCount = config.DemoCopyCounts.ContainsKey(item.documentId.ToString()) ? config.DemoCopyCounts[item.documentId.ToString()] : 0;
                    int row = grid.Rows.Add(item.barcodeText, item.documentTitle, item.secondaryLabelCount); grid.Rows[row].Tag = item;
                    grid.Rows[row].Cells[2].ToolTipText = CopyReceipts.Tooltip(item.secondaryLabelCount);
                    if (selected != null && selected.documentId == item.documentId) grid.CurrentCell = grid.Rows[row].Cells[0];
                }
                UpdatePreview();
                status.Text = grid.Rows.Count == 0 ? Loc.T("Keine Dokumente mit ASN in Paperless auf dieser Seite.") : Loc.T("Nur in Paperless erfasste Dokumente.");
            }
            catch (Exception ex) { grid.Rows.Clear(); hasMore = false; status.Text = ex.Message; }
            finally { busy = false; UpdateControls(); }
        }

        internal static void VerifyDocument(CopyDocument selected, CopyDocumentList current)
        {
            if (selected == null || selected.status != "imported" || selected.asnNumber < 1 || current == null || current.items == null || current.items.Length != 1 ||
                current.items[0].status != "imported" || current.items[0].documentId != selected.documentId ||
                current.items[0].asnNumber != selected.asnNumber || current.items[0].barcodeText != selected.barcodeText)
                throw new InvalidOperationException(Loc.T("Dokument oder ASN wurde in Paperless geändert. Bitte neu auswählen; kein Druck gestartet."));
        }

        async Task PrintCopy()
        {
            var selected = Selected;
            if (busy || selected == null || !print.Enabled) return;
            busy = true; timer.Stop(); UpdateControls();
            bool started = false;
            bool completed = false;
            bool receiptSaved = false;
            string receipt = null;
            string printId = Guid.NewGuid().ToString("N");
            try
            {
                status.Text = Loc.T("Dokument und Drucker werden geprüft…");
                PrinterChoice choice = null;
                if (!config.DemoMode)
                    media = await Task.Run(() => {
                        VerifyDocument(selected, client.CopyDocuments(asn: selected.asnNumber));
                        choice = PrinterBackends.Select(PrinterBackends.DiscoverBrother(), config.PrinterName);
                        using (var printer = new BpacPrinterSession(choice, config.UsePfRightAligned))
                        {
                            var result = printer.ReadStatus();
                            if (!result.CanPrint) throw new InvalidOperationException(result.BlockReason);
                            return result.Media;
                        }
                    });
                UpdatePreview();
                if (preview.Image == null) throw new InvalidOperationException(Loc.T("Das Sekundärlabel passt nicht scanbar auf die eingelegte Rolle."));
                if (MessageBox.Show(this, selected.barcodeText + " · " + selected.documentTitle + Loc.T("\r\nEin zusätzliches Label mit ! -COPY- ! drucken?"),
                    Loc.T("Sekundärlabel bestätigen"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                status.Text = config.DemoMode ? Loc.T("Demo · Sekundärlabel wird gedruckt…") : Loc.T("Sekundärlabel wird gedruckt…");
                if (config.DemoMode) await Task.Delay(3000);
                else await Task.Run(() => BpacPrinterSession.PrintSecondary(choice, selected.barcodeText, config.UsePfRightAligned, media, () => {
                    VerifyDocument(selected, client.CopyDocuments(asn: selected.asnNumber));
                    client.PrepareCopy(printId, selected);
                    receipt = CopyReceipts.Prepare(config, printId);
                    started = true;
                }));
                completed = true;
                if (config.DemoMode)
                {
                    config.DemoCopyCounts[selected.documentId.ToString()] = selected.secondaryLabelCount + 1;
                    config.Save();
                }
                else
                {
                    await Task.Run(() => {
                        CopyReceipts.Complete(receipt); receiptSaved = true;
                        CopyReceipts.Flush(config, client);
                    });
                }
                selected.secondaryLabelCount++;
                grid.CurrentRow.Cells[2].Value = selected.secondaryLabelCount;
                grid.CurrentRow.Cells[2].ToolTipText = CopyReceipts.Tooltip(selected.secondaryLabelCount);
                status.Text = (config.DemoMode ? "Demo · " : "") + selected.barcodeText + Loc.T(" · Sekundärlabel gedruckt. ASN-Zähler unverändert.");
            }
            catch (Exception ex)
            {
                string message = (completed ? Loc.T("Label erfolgreich gedruckt, Zähler noch nicht synchronisiert. Nicht erneut drucken. Gespeicherte Bestätigungen werden automatisch erneut übertragen.\r\n") : started ? Loc.T("Druck nicht bestätigt. Bitte Label und Warteschlange prüfen; nicht ungeprüft erneut drucken.\r\n") : Loc.T("Kein Druckauftrag gestartet.\r\n")) + ex.Message;
                if (completed && !config.DemoMode && !receiptSaved)
                    message = Loc.T("Label erfolgreich gedruckt, Bestätigung konnte lokal nicht gespeichert werden. Nicht erneut drucken. Kopienzähler muss geprüft werden.\r\n") + ex.Message;
                status.Text = message;
                MessageBox.Show(this, message, Loc.T("Sekundärlabel"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { busy = false; UpdateControls(); timer.Start(); }
        }

        internal static IEnumerable<string> Validate(string snapshotPath = null)
        {
            var failures = new List<string>();
            string journal = Path.Combine(Environment.CurrentDirectory, "asn-copy-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(journal);
            try
            {
                string prepared = Path.Combine(journal, "copy.prepared"); File.WriteAllText(prepared, "");
                int confirmations = 0;
                CopyReceipts.FlushFolder(journal, id => confirmations++);
                if (confirmations != 0) failures.Add("Unconfirmed copy receipt was sent.");
                CopyReceipts.Complete(prepared);
                try { CopyReceipts.FlushFolder(journal, id => { throw new IOException("offline"); }); }
                catch (IOException) { }
                if (!File.Exists(Path.ChangeExtension(prepared, ".confirmed"))) failures.Add("Offline copy receipt lost.");
                CopyReceipts.FlushFolder(journal, id => confirmations++);
                CopyReceipts.FlushFolder(journal, id => confirmations++);
                if (confirmations != 1) failures.Add("Copy receipt was not retried exactly once.");
            }
            finally { foreach (string file in Directory.GetFiles(journal)) File.Delete(file); Directory.Delete(journal); }
            var settings = new AppConfig();
            var snapshot = new SettingsSnapshot(settings, "test-key");
            settings.RefreshSeconds = 60; settings.ShowCompleted = true;
            if (snapshot.ServerChanged(settings, "test-key") || snapshot.PrinterChanged(settings)) failures.Add("Display changes request a server/printer refresh.");
            settings.PrinterName = "Other printer";
            if (snapshot.ServerChanged(settings, "test-key") || !snapshot.PrinterChanged(settings)) failures.Add("Printer changes refresh the server.");
            if (!snapshot.ServerChanged(settings, "new-key")) failures.Add("API key change missed.");
            settings.ServerUrl += "/";
            if (snapshot.ServerChanged(settings, "test-key")) failures.Add("Trailing slash requests an unnecessary refresh.");
            settings.ServerUrl = "https://other.test";
            if (!snapshot.ServerChanged(settings, "test-key")) failures.Add("Server change missed.");
            ReservationClient.ValidateCompatibility(new Health { ok = true }); // Legacy additive API.
            ReservationClient.ValidateCompatibility(new Health { ok = true, apiVersion = 1, capabilities = new[] { "secondary-label-tracking-v1" } }, true);
            foreach (var health in new[] { new Health { ok = true, apiVersion = 2 }, new Health { ok = true, minimumClientVersion = "99.0" }, new Health { ok = true, capabilities = new string[0] } })
            {
                try { ReservationClient.ValidateCompatibility(health, true); failures.Add("Incompatible secondary-label server accepted."); }
                catch (InvalidOperationException) { }
            }
            var selected = DemoDocuments().items[0];
            VerifyDocument(selected, new CopyDocumentList { items = new[] { selected } });
            foreach (var invalid in new[] { new CopyDocumentList { items = new CopyDocument[0] },
                new CopyDocumentList { items = new[] { new CopyDocument { status = "pending" } } },
                new CopyDocumentList { items = new[] { new CopyDocument { status = "imported", asnNumber = selected.asnNumber, barcodeText = selected.barcodeText, documentId = 99 } } } })
            {
                try { VerifyDocument(selected, invalid); failures.Add("Copy accepted an unverified document."); }
                catch (InvalidOperationException) { }
            }
            using (var form = new CopyLabelForm(new AppConfig { DemoMode = true }, true))
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false; form.Show();
                foreach (var item in DemoDocuments().items) { int row = form.grid.Rows.Add(item.barcodeText, item.documentTitle, item.documentId == 2 ? 3 : 0); form.grid.Rows[row].Tag = item; }
                form.UpdatePreview(); form.UpdateControls(); Application.DoEvents();
                if (!form.print.Enabled) failures.Add("Copy selection does not enable printing.");
                form.busy = true; form.UpdateControls();
                if (form.print.Enabled || form.grid.Enabled) failures.Add("Copy controls remain active while printing.");
                form.busy = false; form.UpdateControls();
                if (snapshotPath != null) using (var bitmap = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshotPath), Path.GetFileNameWithoutExtension(snapshotPath) + "-copy.png"));
                }
            }
            return failures;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); tips.Dispose(); if (preview.Image != null) preview.Image.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
