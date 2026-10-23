using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using MessageBox = AsnLabeler.AppDialogs;

[assembly: System.Reflection.AssemblyVersion(AsnLabeler.AppRelease.NumericVersion)]
[assembly: System.Reflection.AssemblyFileVersion(AsnLabeler.AppRelease.NumericVersion)]

namespace AsnLabeler
{
    static class ThirdPartyLicenses
    {
        public static string Read()
        {
            var text = new StringBuilder("ZXing.Net 0.16.11 - Copyright Michael Jahn and ZXing contributors\r\nhttps://github.com/micjahn/ZXing.Net\r\nApache License 2.0; unmodified assembly\r\n\r\nMaterial Icons - Google\r\nhttps://github.com/google/material-design-icons\r\nApache License 2.0; unmodified font\r\n\r\n");
            text.AppendLine("Inno Setup 6.7.0 - Jordan Russell and Martijn Laan\r\nhttps://jrsoftware.org/\r\nUnmodified installer engine; separate Inno Setup License.\r\n");
            foreach (string name in new[] { "AsnLabeler.NOTICE", "AsnLabeler.LICENSE", "AsnLabeler.BpacPermission", "AsnLabeler.ThirdPartyNotices", "AsnLabeler.ZXing.LICENSE", "AsnLabeler.MaterialIcons.LICENSE", "AsnLabeler.InnoSetup.LICENSE" })
                using (var stream = typeof(ThirdPartyLicenses).Assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                    text.AppendLine(name).AppendLine(reader.ReadToEnd()).AppendLine();
            return text.ToString();
        }
    }

    static class AppDialogs
    {
        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        { return ShowCore(owner, text, title, buttons, icon, defaultButton); }

        public static DialogResult Show(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        { return Show(null, text, title, buttons, icon); }

        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        { return ShowCore(owner, text, title, buttons, icon, MessageBoxDefaultButton.Button1); }

        static DialogResult ShowCore(IWin32Window owner, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            using (var dialog = Create(text, title, buttons, icon, defaultButton)) return dialog.ShowDialog(owner);
        }

        internal static Form Create(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
                bool diagnostic = icon == MessageBoxIcon.Error || (icon == MessageBoxIcon.Warning && buttons == MessageBoxButtons.OK);
                var dialog = new Form();
                var body = new TextBox();
                var actions = new FlowLayoutPanel();
                var close = new Button();
                dialog.Text = diagnostic ? title + " | " + AppRelease.Version : title;
                dialog.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                dialog.Font = new Font("Segoe UI", 10);
                dialog.AutoScaleDimensions = new SizeF(96, 96);
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.ClientSize = new Size(600, 280);
                dialog.MinimumSize = new Size(420, 240);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.Padding = new Padding(16);
                dialog.BackColor = Color.White;
                body.Multiline = true;
                body.ReadOnly = true;
                body.ScrollBars = ScrollBars.Vertical;
                body.Text = text;
                body.Dock = DockStyle.Fill;
                body.BackColor = SystemColors.Window;
                actions.Dock = DockStyle.Bottom;
                actions.AutoSize = true;
                actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                actions.Height = 52;
                actions.Padding = new Padding(0, 10, 0, 0);
                actions.FlowDirection = FlowDirection.RightToLeft;
                close.Text = Loc.T("Schließen");
                close.Size = new Size(120, 34);
                close.AutoSize = true;
                close.MinimumSize = new Size(120, 34);
                close.DialogResult = DialogResult.OK;
                actions.Controls.Add(close);
                if (diagnostic)
                {
                    var copy = new Button { Name = "copyMessage", Text = Loc.T("Kopieren"), AutoSize = true, MinimumSize = new Size(160, 34) };
                    copy.Click += delegate
                    {
                        try { Clipboard.SetText("ASN Labeler " + AppRelease.Version + "\r\n" + title + "\r\n" + text); copy.Text = Loc.T("Kopiert"); }
                        catch (ExternalException) { copy.Text = Loc.T("Erneut kopieren"); }
                    };
                    actions.Controls.Add(copy);
                }
                var choices = new List<Button>();
                if (buttons != MessageBoxButtons.OK)
                {
                    close.Text = buttons == MessageBoxButtons.YesNo ? Loc.T("Nein") : Loc.T("Abbrechen");
                    close.DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.Cancel;
                    var positive = new Button { Text = buttons == MessageBoxButtons.OKCancel ? "OK" : Loc.T("Ja"), AutoSize = true, MinimumSize = new Size(120,34),
                        DialogResult = buttons == MessageBoxButtons.OKCancel ? DialogResult.OK : DialogResult.Yes };
                    actions.Controls.Add(positive); choices.Add(positive);
                    if (buttons == MessageBoxButtons.YesNoCancel) {
                        var negative = new Button { Text = Loc.T("Nein"), AutoSize = true, MinimumSize = new Size(120,34), DialogResult = DialogResult.No };
                        actions.Controls.Add(negative); choices.Add(negative);
                    }
                }
                choices.Add(close);
                if (diagnostic || text.Length > 2000) dialog.Controls.Add(body);
                else
                {
                    body.Dispose();
                    var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4, 8, 4, 8) };
                    var message = new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = Color.FromArgb(55, 65, 71) };
                    content.SizeChanged += delegate { message.MaximumSize = new Size(Math.Max(1, content.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth), 0); };
                    content.Controls.Add(message);
                    dialog.Controls.Add(content);
                    var heading = new Label { Text = title, Dock = DockStyle.Top, AutoSize = true, Font = new Font(dialog.Font, FontStyle.Bold), Padding = new Padding(4, 0, 4, 16) };
                    dialog.Controls.Add(heading);
                    close.BackColor = Color.FromArgb(0, 125, 98); close.ForeColor = Color.White;
                    close.FlatStyle = FlatStyle.Flat; close.FlatAppearance.BorderSize = 0;
                }
                dialog.Controls.Add(actions);
                dialog.AcceptButton = choices[Math.Min((int)defaultButton / 256, choices.Count - 1)];
                dialog.CancelButton = close;
                dialog.Load += delegate {
                    using (var graphics = dialog.CreateGraphics()) {
                        float scale = graphics.DpiX / 96f;
                        bool simple = !diagnostic && text.Length <= 2000;
                        int width = (int)((simple ? 520 : 600) * scale);
                        int textHeight = TextRenderer.MeasureText(text, dialog.Font, new Size(width - (int)(64 * scale), Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
                        int height = simple ? Math.Min((int)(400 * scale), Math.Max((int)(160 * scale), textHeight + (int)(115 * scale))) : (int)(280 * scale);
                        dialog.MinimumSize = new Size((int)(420 * scale), (int)((simple ? 160 : 240) * scale));
                        dialog.ClientSize = new Size(width, height);
                    }
                };
                return dialog;
        }

        internal static List<string> Validate(string snapshotPath = null)
        {
            var failures = new List<string>();
            foreach (var icon in new[] { MessageBoxIcon.Information, MessageBoxIcon.Question, MessageBoxIcon.Warning, MessageBoxIcon.Error })
            using (var form = Create(ClientUpdates.Text("Eine neue Version steht bereit. Einstellungen bleiben bei der Installation erhalten.", "A new version is available. Your settings will be preserved during installation."),
                ClientUpdates.Text("Update verfügbar", "Update available"), MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1))
            {
                bool diagnostic = icon == MessageBoxIcon.Error || icon == MessageBoxIcon.Warning;
                if ((form.Controls.Find("copyMessage", true).Length != 0) != diagnostic) failures.Add("Unexpected popup copy action.");
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false;
                form.Show(); Application.DoEvents();
                foreach (Control control in form.Controls)
                {
                    if (!form.ClientRectangle.Contains(control.Bounds)) failures.Add("Popup control overflow.");
                    foreach (Control other in form.Controls)
                        if (other != control && control.Bounds.IntersectsWith(other.Bounds)) failures.Add("Popup control overlap.");
                }
                if (snapshotPath != null && icon == MessageBoxIcon.Information)
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshotPath), Path.GetFileNameWithoutExtension(snapshotPath) + "-info.png"), ImageFormat.Png);
                    }
                form.Close();
            }
            return failures;
        }
    }

    static class Program
    {
        static Mutex singleInstanceMutex;
        static Mutex runningMutex;
        public static readonly int BringToFrontMessage = NativeMethods.RegisterWindowMessage("ASN_LABELER_BRING_TO_FRONT");

        [STAThread]
        static int Main(string[] args)
        {
            EmbeddedBarcodeLibrary.Initialize();
#if DEVELOPMENT
            ClientRuntime.DevelopmentMode = args.Contains("--development") || args.Contains("--self-test");
#endif
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            if (args.Length > 0 && args[0] == "--apply-update") return ClientUpdates.Apply(args);
            if (args.Length == 1 && args[0] == "--licenses")
            {
                MessageBox.Show(ThirdPartyLicenses.Read(), Loc.T("Open-Source-Lizenzen"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--self-test")
            {
                return SelfTest.Run();
            }
            if (args.Length == 2 && (args[0] == "--render-ui" || args[0] == "--render-ui-en"))
            {
                Loc.Language = args[0] == "--render-ui-en" ? "en" : "de";
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm.RenderDemoSnapshot(args[1]);
                SettingsForm.ValidateLayout(args[1]);
                DocumentStatusForm.RenderSnapshot(args[1]);
                CopyLabelForm.Validate(args[1]);
                UpdateForm.ValidateLayout(args[1]);
                AppDialogs.Validate(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--render-barcodes")
            {
                Directory.CreateDirectory(args[1]);
                foreach (string payload in new[] { "ASN00001", "ASN00123", "ASN99999", "ASN100000", "ASN1234567" })
                    foreach (var media in new[] { MediaInfo.DefaultDk11221(), MediaInfo.FromStatus(0x4A, 12, 0), MediaInfo.FromStatus(0x4A, 62, 0) })
                    {
                        using (var bitmap = LabelRenderer.Render(payload, media))
                            bitmap.Save(Path.Combine(args[1], payload + "-" + media.WidthMm + ".png"), ImageFormat.Png);
                        if (media.WidthMm == 23)
                            using (var bitmap = LabelRenderer.RenderCompatible(payload, media))
                                bitmap.Save(Path.Combine(args[1], payload + "-pf-native.png"), ImageFormat.Png);
                    }
                return 0;
            }

            Mutex setup;
            bool setupRunning;
            try { setupRunning = Mutex.TryOpenExisting("Global\\AsnLabelerSetup", System.Security.AccessControl.MutexRights.Synchronize, out setup); }
            catch (UnauthorizedAccessException) { setupRunning = true; setup = null; }
            if (!args.Contains("--installed") && setupRunning)
            {
                if (setup != null) setup.Dispose();
                MessageBox.Show("Setup is running. Please start ASN Labeler after installation finishes.", "ASN Labeler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            if (setup != null) setup.Dispose();
            bool created;
            singleInstanceMutex = new Mutex(true, "Local\\AsnLabelerBrotherQl800", out created);
            if (!created)
            {
                NativeMethods.PostMessage((IntPtr)0xffff, BringToFrontMessage, IntPtr.Zero, IntPtr.Zero);
                NativeMethods.TryBringExistingProcessToFront();
                return 0;
            }
            // Keep a shared presence handle alive across Windows sessions for Setup.
            var security = new System.Security.AccessControl.MutexSecurity();
            security.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                System.Security.AccessControl.MutexRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            runningMutex = new Mutex(false, "Global\\AsnLabelerRunning", out created, security);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, "ASN Labeler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            using (singleInstanceMutex)
            using (runningMutex)
            {
                bool restart;
                do { Loc.Language = AppConfig.Load().Language; using (var form = new MainForm()) { Application.Run(form); restart = form.RestartRequested; } } while (restart);
            }
            return 0;
        }
    }

    static class NativeMethods
    {
        public const int SW_RESTORE = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void TryBringExistingProcessToFront()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                string currentPath = current.MainModule.FileName;
                foreach (var process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id == current.Id) continue;
                    if (!SameExecutable(process, currentPath)) continue;

                    IntPtr handle = IntPtr.Zero;
                    for (int i = 0; i < 20; i++)
                    {
                        process.Refresh();
                        handle = process.MainWindowHandle;
                        if (handle != IntPtr.Zero) break;
                        Thread.Sleep(75);
                    }

                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        static bool SameExecutable(Process process, string currentPath)
        {
            try
            {
                return string.Equals(process.MainModule.FileName, currentPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }

    static class AsnNumber
    {
        public static int Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            string upper = value.Trim().ToUpperInvariant();
            if (upper.StartsWith("ASN")) upper = upper.Substring(3);
            int parsed;
            return int.TryParse(upper, out parsed) ? parsed : 0;
        }

        public static string Format(int number)
        {
            return "ASN" + Math.Max(1, number).ToString("00000");
        }
    }

    sealed class MainForm : Form
    {
        sealed class ServerView
        {
            public bool Ready, Attempted;
            public string Payload, Badge;
        }
        static ServerView languageRestartView;
        internal bool RestartRequested { get; private set; }
        readonly AppConfig config;
        readonly Label titleLabel = new Label();
        readonly Label subtitleLabel = new Label();
        readonly Label banner = new Label();
        readonly Label modeBadge = new Label();
        readonly Label serverStatus = new Label();
        readonly Label printerStatus = new Label();
        readonly Label lastPrinted = new Label();
        readonly Label nextAsn = new Label();
        readonly Label heroNextAsn = new Label();
        readonly Label printerDetail = new Label();
        readonly Label serverDetail = new Label();
        readonly Label previewDetail = new Label();
        readonly PictureBox preview = new PictureBox();
        readonly ProgressBar activity = new ProgressBar();
        readonly Button printButton = new ActionButton();
        readonly NumericUpDown printCount = new NumericUpDown { Minimum = 1, Maximum = 10, Value = 1, TextAlign = HorizontalAlignment.Center };
        readonly Panel countFrame = new Panel { BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        readonly CutSwitch cutSwitch = new CutSwitch { Text = "Einzeln schneiden", AccessibleName = "Einzeln schneiden" };
        readonly Label countLabel = new Label { Text = Loc.T("Anzahl") };
        readonly ToolTip actionTips = new ToolTip();
        int batchIndex;
        int batchTotal = 1;
        readonly Button settingsButton = new MaterialIconButton("\ue8b8");
        readonly ActionButton documentsButton = new ActionButton();
        bool badgeLoading;
        readonly Button testServerButton = new MaterialIconButton("\ue5d5");
        readonly Button testPrinterButton = new MaterialIconButton("\ue5d5");
        readonly Button refreshNextButton = new ActionButton();
        readonly Panel printerStep = new StepCardPanel();
        readonly Panel serverStep = new StepCardPanel();
        readonly Panel previewStep = new StepCardPanel();
        readonly Label printerBadge = new Label();
        readonly Label serverBadge = new Label();
        readonly Label previewBadge = new Label();
        readonly Label printerHeading = new Label();
        readonly Label serverHeading = new Label();
        readonly Label previewHeading = new Label();
        readonly Label printerSub = new Label();
        readonly Label serverSub = new Label();
        readonly Label previewSub = new Label();
        readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
        readonly ReservationClient client;
        readonly bool testMode;
        MediaInfo detectedMedia;
        int animationStep;
        bool isBusy;
        bool updateChecking;
        ClientRelease pendingUpdate;
        bool pendingBpacSetup;
        string notifiedMinimum;
        bool isRefreshingNext;
        bool printerReady;
        bool serverReady;
        bool nextReady;
        bool printerAttempted;
        bool serverAttempted;
        bool printerChecking;
        bool serverChecking;
        bool previewValid;
        string previewError;
        bool showBanner;

        public MainForm() : this(AppConfig.Load(), false) { }

        MainForm(AppConfig initialConfig, bool testMode)
        {
            Loc.Language = initialConfig.Language;
            SuspendLayout();
            this.testMode = testMode;
            Text = "Paperless ASN Labeler | " + AppRelease.Version;
            if (ClientRuntime.DevelopmentMode) Text += " | DEVELOPMENT";
            ClientSize = new Size(900, 660);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(243, 246, 250);
            AutoScroll = false;
            DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            config = initialConfig;
            client = new ReservationClient(config);

            titleLabel.Text = "ASN Labeler";
            titleLabel.Font = UiFont(24, FontStyle.Bold);

            subtitleLabel.Text = "paperless-ngx + Brother QL";
            subtitleLabel.ForeColor = Color.FromArgb(85, 91, 102);
            subtitleLabel.Font = UiFont(10, FontStyle.Regular);

            banner.BackColor = Color.FromArgb(230, 244, 255);
            banner.ForeColor = Color.FromArgb(20, 74, 124);
            banner.TextAlign = ContentAlignment.MiddleLeft;
            banner.Padding = new Padding(14, 0, 14, 0);
            banner.Font = UiFont(10, FontStyle.Regular);

            modeBadge.Text = Loc.T("DEMO-MODUS");
            modeBadge.TextAlign = ContentAlignment.MiddleCenter;
            modeBadge.Font = UiFont(9, FontStyle.Bold);
            modeBadge.BackColor = Color.FromArgb(251, 191, 36);
            modeBadge.ForeColor = Color.FromArgb(69, 26, 3);
            modeBadge.Visible = config.DemoMode;

            printButton.Text = Loc.T("Label drucken");
            printButton.Font = UiFont(12, FontStyle.Bold);
            printButton.BackColor = Color.FromArgb(0, 120, 83);
            printButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 102, 70);
            printButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 84, 58);
            printButton.ForeColor = Color.White;
            printButton.FlatStyle = FlatStyle.Flat;
            printButton.FlatAppearance.BorderSize = 0;
            printButton.SetBounds(670, 458, 170, 52);
            printButton.Click += delegate { PrintNext(); };
            AcceptButton = printButton;
            printCount.Font = UiFont(12, FontStyle.Regular);
            printCount.BorderStyle = BorderStyle.None;
            printCount.AccessibleName = "Anzahl Labels";
            printCount.ValueChanged += delegate { UpdateStepAvailability(); };
            actionTips.SetToolTip(printCount, Loc.T("1 bis 10 Labels mit jeweils eigener ASN"));

            settingsButton.AccessibleName = Loc.T("Einstellungen");
            actionTips.SetToolTip(settingsButton, settingsButton.AccessibleName);
            StyleSecondaryButton(settingsButton);
            settingsButton.Click += delegate { OpenSettings(); };
            documentsButton.Text = Loc.T("Dokumente");
            StyleSecondaryButton(documentsButton);
            documentsButton.TextAlign = ContentAlignment.MiddleLeft;
            documentsButton.Click += delegate
            {
                if (OperationRunning) return;
                pollTimer.Stop();
                try { using (var form = new DocumentStatusForm(client, config)) form.ShowDialog(this); }
                finally { pollTimer.Start(); RefreshDocumentBadge(); RefreshNextNumber(true); }
            };

            testPrinterButton.Text = "";
            testPrinterButton.AccessibleName = Loc.T("Drucker erneut prüfen");
            actionTips.SetToolTip(testPrinterButton, testPrinterButton.AccessibleName);
            StyleSecondaryButton(testPrinterButton);
            testPrinterButton.Click += delegate { StartPrinterCheckFromUi(); };

            testServerButton.Text = "";
            testServerButton.AccessibleName = Loc.T("Server und nächste ASN aktualisieren");
            actionTips.SetToolTip(testServerButton, testServerButton.AccessibleName);
            StyleSecondaryButton(testServerButton);
            testServerButton.Click += delegate { StartServerCheckFromUi(); };

            refreshNextButton.Text = Loc.T("Nummer aktualisieren");
            StyleSecondaryButton(refreshNextButton);
            refreshNextButton.Click += delegate { RefreshNextNumber(false); };

            heroNextAsn.Text = Loc.T("Wird geladen...");
            heroNextAsn.Font = PreferredMonoFont(22, FontStyle.Bold);
            heroNextAsn.ForeColor = Color.FromArgb(17, 24, 39);
            heroNextAsn.TextAlign = ContentAlignment.MiddleLeft;

            printerDetail.ForeColor = Color.FromArgb(75, 85, 99);
            printerDetail.Text = Loc.T("Prüft Erreichbarkeit und liest die eingelegte DK-Rolle.");
            printerDetail.AutoEllipsis = true;

            printerStatus.Font = UiFont(10, FontStyle.Bold);
            printerStatus.Text = Loc.T("Noch nicht geprüft");

            serverDetail.ForeColor = Color.FromArgb(75, 85, 99);
            serverDetail.Text = Loc.T("Prüft API und nächste ASN.");
            serverDetail.Font = UiFont(10, FontStyle.Regular);
            serverDetail.AutoEllipsis = true;

            serverStatus.Font = UiFont(10, FontStyle.Bold);
            serverStatus.Text = Loc.T("Noch nicht geprüft");

            previewDetail.ForeColor = Color.FromArgb(75, 85, 99);
            previewDetail.Text = Loc.T("Erst Drucker und Server prüfen.");
            previewDetail.Font = UiFont(10, FontStyle.Regular);
            previewDetail.AutoEllipsis = true;

            lastPrinted.Font = PreferredMonoFont(11, FontStyle.Bold);
            lastPrinted.AutoEllipsis = true;

            preview.BackColor = Color.White;
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.SizeMode = PictureBoxSizeMode.Zoom;

            ConfigureStepPanel(serverStep, serverBadge, serverHeading, serverSub, "1", Loc.T("Server"), Loc.T("Startet automatisch"), StartServerCheckFromUi);
            ConfigureStepPanel(printerStep, printerBadge, printerHeading, printerSub, "2", Loc.T("Drucker"), Loc.T("Wartet auf Server"), StartPrinterCheckFromUi);
            serverStep.TabIndex = 0;
            printerStep.TabIndex = 1;
            previewStep.TabIndex = 2;
            ConfigureStepPanel(previewStep, previewBadge, previewHeading, previewSub, "3", Loc.T("Vorschau"), Loc.T("Noch nicht bereit"), null);

            printerStep.Controls.Add(testPrinterButton);
            printerStep.Controls.Add(printerDetail);
            printerStep.Controls.Add(printerStatus);

            serverStep.Controls.Add(testServerButton);
            serverStep.Controls.Add(serverStatus);
            serverStep.Controls.Add(serverDetail);
            serverStep.Controls.Add(heroNextAsn);

            previewStep.Controls.Add(previewDetail);
            previewStep.Controls.Add(preview);
            previewStep.Controls.Add(printButton);
            countFrame.Controls.Add(printCount);
            previewStep.Controls.Add(countFrame);
            previewStep.Controls.Add(cutSwitch);
            previewStep.Controls.Add(countLabel);

            pollTimer.Interval = config.RefreshMilliseconds;
            pollTimer.Tick += delegate { OfferPendingUpdate(); if (IsDisposed || Disposing) return; RefreshDocumentBadge(); RefreshNextNumber(true); };
            pollTimer.Start();

            animationTimer.Interval = 60;
            animationTimer.Tick += delegate { AnimateBusy(); };

            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(banner);
            Controls.Add(modeBadge);
            Controls.Add(settingsButton);
            Controls.Add(documentsButton);
            Controls.Add(printerStep);
            Controls.Add(serverStep);
            Controls.Add(previewStep);
            activity.Style = ProgressBarStyle.Marquee;
            activity.MarqueeAnimationSpeed = 25;
            activity.Visible = false;
            Controls.Add(activity);

            RefreshDashboard("Starte automatisch mit Schritt 1.");
            Resize += delegate { LayoutMain(); };
            LayoutMain();
            ResumeLayout(false);
            Shown += delegate
            {
                LayoutMain();
                if (!testMode)
                {
                    if (config.LegacyPrinterMigrated)
                        MessageBox.Show(this, Loc.T("Die bisherige Druckeranbindung wurde auf Brother / b-PAC umgestellt. Der alte Gerätepfad bleibt gespeichert, wird aber nicht mehr benutzt. Bitte unter Einstellungen den Brother-Drucker auswählen und bei Bedarf b-PAC einrichten."),
                            "Druckeranbindung aktualisiert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    var restored = languageRestartView;
                    languageRestartView = null;
                    if (restored == null) { BeginAutoWorkflow(); RefreshDocumentBadge(); }
                    else
                    {
                        serverReady = nextReady = restored.Ready;
                        serverAttempted = restored.Attempted;
                        SetNextText(restored.Payload);
                        documentsButton.NotificationText = restored.Badge;
                        serverStatus.Text = Loc.T(serverReady ? "Verbunden" : "Nicht erreichbar");
                        serverDetail.Text = serverReady ? Loc.T("Nächste ASN geladen: ") + restored.Payload : Loc.T("Server nicht erreichbar oder API-Key ungültig.");
                        UpdateStepAvailability();
                        if (serverReady) CheckPrinter();
                    }
                }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (isBusy) e.Cancel = true; };
            Shown += delegate { if (!testMode) CheckUpdates(false); };
            Shown += delegate { if (!testMode) CheckInitialSetup(); };
        }

        async void CheckInitialSetup()
        {
            if (config.BpacSetupPrompted || config.DemoMode || ClientUpdates.InstallScope(Application.ExecutablePath) == null) return;
            bool available = await Task.Run(() => { try { BpacPrinterSession.Probe(); return true; } catch { return false; } });
            if (IsDisposed) return;
            config.BpacSetupPrompted = true; config.Save();
            pendingBpacSetup = !available;
        }

        async void CheckUpdates(bool manual)
        {
            if (testMode || updateChecking) return;
            if (ClientUpdates.InstallScope(Application.ExecutablePath) == null)
            {
                if (manual) MessageBox.Show(this, ClientUpdates.Text("Updates sind nach der Installation mit Setup verfügbar.", "Updates are available after installing with Setup."), "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            updateChecking = true;
            try
            {
                pendingUpdate = await Task.Run(() => ClientUpdates.Check(manual));
                if (IsDisposed) return;
                Version required;
                if (pendingUpdate != null && ClientRuntime.UpdateRequired &&
                    (!Version.TryParse(ClientRuntime.RequiredVersion, out required) || ClientUpdates.ParseVersion(pendingUpdate.VersionText) < required)) pendingUpdate = null;
                if (pendingUpdate == null && manual) MessageBox.Show(this, ClientRuntime.UpdateRequired
                    ? ClientUpdates.Text("Der Server verlangt mindestens Client ", "The server requires client ") + ClientRuntime.RequiredVersion + ClientUpdates.Text(". Noch kein passendes Update verfügbar. Bitte den Administrator kontaktieren.", " or later. No matching update is available yet. Contact your administrator.")
                    : ClientUpdates.Text("Die App ist aktuell.", "The app is up to date."), "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OfferPendingUpdate();
            }
            catch (Exception ex)
            {
                if (manual && !IsDisposed) MessageBox.Show(this, ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { updateChecking = false; }
        }

        void OfferPendingUpdate()
        {
            if (ClientRuntime.UpdateRequired && notifiedMinimum != ClientRuntime.RequiredVersion && !OperationRunning && !BpacPrinterSession.IsPrinting && ActiveForm == this && Enabled && !IsDisposed)
            {
                notifiedMinimum = ClientRuntime.RequiredVersion;
                UpdateStepAvailability();
                MessageBox.Show(this, ClientUpdates.Text("Dieser Server benötigt mindestens Client ", "This server requires client ") + notifiedMinimum + ClientUpdates.Text(". Bitte aktualisieren. Neue Druckaufträge sind gesperrt; Einstellungen und Diagnose bleiben verfügbar.", " or later. Please update. New print jobs are blocked; settings and diagnostics remain available."),
                    ClientUpdates.Text("Update erforderlich", "Update required"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                CheckUpdates(true);
                return;
            }
            if (pendingBpacSetup && !OperationRunning && ActiveForm == this && Enabled && !IsDisposed)
            {
                pendingBpacSetup = false;
                if (MessageBox.Show(this, ClientUpdates.Text("b-PAC 64 Bit fehlt oder ist nicht verfügbar. Jetzt die Druckereinrichtung öffnen?", "b-PAC 64-bit is missing or unavailable. Open printer setup now?"),
                    "Brother b-PAC", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) OpenSettings(true);
                return;
            }
            if (pendingUpdate == null || OperationRunning || BpacPrinterSession.IsPrinting || ActiveForm != this || !Enabled || IsDisposed) return;
            var release = pendingUpdate; pendingUpdate = null;
            pollTimer.Stop();
            using (var dialog = new UpdateForm(release))
            {
                dialog.ShowDialog(this);
                if (dialog.InstallRequested) { config.Save(); Close(); return; }
            }
            pollTimer.Start();
        }

        public static void RenderDemoSnapshot(string path)
        {
            using (var form = new MainForm(new AppConfig { Language = Loc.Language }, true))
            {
                form.Size = new Size(1180, 820);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.config.DemoMode = true;
                form.documentsButton.NotificationText = "99+";
                form.detectedMedia = MediaInfo.DefaultDk11221();
                form.printerAttempted = true;
                form.serverAttempted = true;
                form.printerReady = true;
                form.serverReady = true;
                form.nextReady = true;
                form.printerStatus.Text = Loc.T("Demo bereit");
                form.printerDetail.Text = Loc.T("Simulierter Brother QL-800 mit DK-11221.");
                form.serverStatus.Text = Loc.T("Demo bereit");
                form.serverDetail.Text = Loc.T("API wird im Demo-Modus nicht benutzt.");
                form.lastPrinted.Text = "Letzter Druck: ASN00006";
                form.RefreshDashboard(Loc.T("Demo-Modus: Bereit zum Drucken."));
                form.SetNextText("ASN00007");
                form.UpdatePreview("ASN00007");
                form.lastPrinted.Text = "Letzter Druck: ASN00006";
                form.LayoutMain();
                form.Show();
                Application.DoEvents();
                form.LayoutMain();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(path, ImageFormat.Png);
                }
                form.Size = form.MinimumSize;
                form.printCount.Value = 3;
                form.LayoutMain();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-minimum.png"), ImageFormat.Png);
                }
                form.SetBusy(true);
                form.SetBanner(Loc.T("Druckauftrag wird verarbeitet..."), true);
                Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-printing.png"), ImageFormat.Png);
                }
                form.SetBusy(false);
                form.config.DemoMode = false;
                form.printerReady = false;
                form.serverReady = false;
                form.nextReady = false;
                form.printerDetail.Text = Loc.T("Brother nicht erreichbar. USB-Verbindung und eingelegte Rolle prüfen.");
                form.RefreshDashboard(Loc.T("Drucker nicht erreichbar."));
                form.UpdateStepAvailability();
                form.LayoutMain();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-bpac.png"), ImageFormat.Png);
                }
            }
        }

        public static List<string> ValidateLayoutForTests()
        {
            var failures = new List<string>();
            using (var form = new MainForm(new AppConfig(), true))
            {
                form.ShowInTaskbar = false;
                form.Location = new Point(-20000, -20000);
                form.Show();
                form.Size = new Size(1180, 820);
                form.config.DemoMode = true;
                form.ApplyDemoReadyStateForTests();
                form.LayoutMain();
                form.AssertPanelChildrenInside("demo-ready-large", failures);
                string previousMinimum = ClientRuntime.RequiredVersion;
                try
                {
                    form.config.DemoMode = false;
                    ClientRuntime.RequiredVersion = "99.0.0";
                    form.UpdateStepAvailability();
                    if (form.printButton.Enabled || !form.settingsButton.Enabled || !form.documentsButton.Enabled)
                        failures.Add("Required update must block printing without blocking settings.");
                }
                finally { ClientRuntime.RequiredVersion = previousMinimum; form.config.DemoMode = true; form.UpdateStepAvailability(); }
                if (form.serverStep.Top >= form.printerStep.Top || form.printerStep.Top >= form.previewStep.Top ||
                    form.serverBadge.Text != "1" || form.printerBadge.Text != "2") failures.Add("Server-first order is incorrect.");
                form.printerReady = false;
                form.printerAttempted = true;
                form.UpdateStepAvailability();
                if (!form.heroNextAsn.Visible || !form.testServerButton.Enabled || form.printButton.Enabled)
                    failures.Add("Printer failure prevents server-only ASN lookup.");
                form.serverReady = false;
                form.nextReady = false;
                form.StartPrinterCheckFromUi();
                if (form.printerChecking) failures.Add("Printer accessed before server readiness.");
                form.RefreshNextNumber(true);
                if (!form.serverReady || !form.nextReady || form.printerChecking || !form.heroNextAsn.Visible)
                    failures.Add("Demo server refresh requires a printer.");
                form.ApplyDemoReadyStateForTests();

                form.Size = new Size(760, 620);
                form.LayoutMain();
                form.AssertPanelChildrenInside("demo-ready-small", failures);
                var readyCardBounds = form.previewStep.Bounds;
                form.SetBusy(true);
                form.SetBanner(Loc.T("Druckauftrag wird verarbeitet..."), true);
                form.LayoutMain();
                form.AssertPanelChildrenInside("demo-printing", failures);
                if (form.previewStep.Bounds != readyCardBounds || form.activity.Top < form.previewStep.Bottom || form.banner.Top < form.previewStep.Bottom)
                    failures.Add("Footer overlaps content or changes card positions during printing.");
                if (form.printButton.Enabled || form.settingsButton.Enabled || form.testPrinterButton.Enabled)
                    failures.Add("Printing does not lock all actions.");
                form.SetBusy(false);
                form.config.LastPrinted = "ASN00500";
                form.config.DemoLastPrinted = "ASN00007";
                if (form.DemoNextNumber() != 8) failures.Add("Demo counter uses real counter.");
                form.config.ResetDemoCounter();
                if (form.DemoNextNumber() != 1 || form.config.LastPrinted != "ASN00500") failures.Add("Demo reset failed or changed real counter.");
                form.SetNextText("ASN00001");
                var enter = new Message();
                form.ProcessCmdKey(ref enter, Keys.Enter);
                form.PrintNext();
                form.StartPrinterCheckFromUi();
                if (!form.isBusy || form.printerChecking) failures.Add("Concurrent action bypassed print lock.");
                var wait = Stopwatch.StartNew();
                while (form.isBusy && wait.ElapsedMilliseconds < 6000)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                if (form.isBusy || form.config.DemoLastPrinted != "ASN00001" || form.CurrentPreviewText() != "ASN00002")
                    failures.Add("Demo print cycle did not complete exactly once.");
                form.AssertPanelChildrenInside("after-demo-print", failures);
                var click = typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                foreach (Control target in new Control[] { form.previewStep, form.previewHeading, form.previewBadge, form.previewSub, form.preview })
                    click.Invoke(target, new object[] { EventArgs.Empty });
                if (form.isBusy) failures.Add("Preview click started a print.");
                if (form.printCount.Minimum != 1 || form.printCount.Maximum != 10 || form.printCount.Value != 1)
                    failures.Add("Quantity defaults or bounds are incorrect.");
                if (form.cutSwitch.Checked || form.cutSwitch.Visible) failures.Add("Cut switch defaults are incorrect.");
                form.printCount.Value = 10;
                if (!form.cutSwitch.Visible || form.countFrame.Height != form.printButton.Height || form.countFrame.Top != form.printButton.Top)
                    failures.Add("Quantity/switch alignment is incorrect.");
                click.Invoke(form.cutSwitch, new object[] { EventArgs.Empty });
                if (!form.cutSwitch.Checked) failures.Add("Cut switch does not turn on.");
                click.Invoke(form.cutSwitch, new object[] { EventArgs.Empty });
                if (form.cutSwitch.Checked) failures.Add("Cut switch does not turn off.");
                var titleBounds = form.titleLabel.Bounds;
                var subtitleBounds = form.subtitleLabel.Bounds;
                var footerBounds = form.banner.Bounds;
                bool transient = false;
                EventHandler moved = delegate { transient = true; };
                form.titleLabel.LocationChanged += moved;
                form.subtitleLabel.LocationChanged += moved;
                form.banner.LocationChanged += moved;
                for (int i = 0; i < 20; i++) form.UpdateStepAvailability();
                form.titleLabel.LocationChanged -= moved;
                form.subtitleLabel.LocationChanged -= moved;
                form.banner.LocationChanged -= moved;
                if (transient || form.titleLabel.Bounds != titleBounds || form.subtitleLabel.Bounds != subtitleBounds || form.banner.Bounds != footerBounds)
                    failures.Add("Refresh moves stable UI elements through intermediate positions.");
                if (form.testPrinterButton.Top != form.printerBadge.Top || form.testServerButton.Top != form.serverBadge.Top)
                    failures.Add("Reload buttons are not aligned with step badges.");
                form.printButton.PerformClick();
                if (form.printCount.Enabled || form.cutSwitch.Enabled || !form.isBusy) failures.Add("Batch controls are not locked.");
                wait.Restart();
                while (form.isBusy && wait.ElapsedMilliseconds < 6000) { Application.DoEvents(); Thread.Sleep(10); }
                if (form.isBusy || form.config.DemoLastPrinted != "ASN00011") failures.Add("Demo batch did not print 10 distinct labels.");
                form.AssertPanelChildrenInside("after-batch-print", failures);
                form.printerReady = true;
                form.serverReady = false;
                form.nextReady = false;
                form.serverChecking = true;
                form.UpdateStepAvailability();
                form.AssertPanelChildrenInside("server-checking", failures);
                form.serverChecking = false;
                form.serverAttempted = true;
                form.UpdateStepAvailability();
                form.AssertPanelChildrenInside("server-error", failures);
                string beforeEnter = form.config.DemoLastPrinted;
                form.ProcessCmdKey(ref enter, Keys.Enter);
                if (form.isBusy || form.config.DemoLastPrinted != beforeEnter) failures.Add("Enter started printing while disabled.");
            }

            using (var form = new MainForm(new AppConfig(), true))
            {
                form.ShowInTaskbar = false;
                form.Location = new Point(-20000, -20000);
                form.Show();
                form.Size = new Size(1180, 820);
                form.printerAttempted = true;
                form.printerReady = false;
                form.printerStatus.Text = Loc.T("Nicht bereit");
                form.printerDetail.Text = Loc.T("Kein direkt ansprechbarer Brother QL-800 gefunden. Öffne Einstellungen und nutze Brother QL-800 suchen.");
                form.UpdateStepAvailability();
                form.AssertPanelChildrenInside("printer-error", failures);
                form.Size = form.MinimumSize;
                form.LayoutMain();
                form.AssertPanelChildrenInside("printer-error-minimum", failures);
                form.SetBusy(true);
                form.SetBusy(false);
            }
            return failures;
        }

        void ApplyDemoReadyStateForTests()
        {
            documentsButton.NotificationText = "99+";
            detectedMedia = MediaInfo.DefaultDk11221();
            printerAttempted = true;
            serverAttempted = true;
            printerReady = true;
            serverReady = true;
            nextReady = true;
            printerStatus.Text = Loc.T("Demo bereit");
            printerDetail.Text = Loc.T("Simulierter Brother QL-800 mit DK-11221.");
            serverStatus.Text = Loc.T("Demo bereit");
            serverDetail.Text = Loc.T("API wird im Demo-Modus nicht benutzt.");
            SetNextText("ASN00007");
            lastPrinted.Text = "Letzter Druck: ASN00006";
            UpdatePreview("ASN00007");
            UpdateStepAvailability();
        }

        void AssertPanelChildrenInside(string scenario, List<string> failures)
        {
            foreach (Control label in new Control[] { titleLabel, subtitleLabel, modeBadge })
                foreach (Control button in new Control[] { documentsButton, settingsButton })
                    if (label.Visible && button.Visible && label.Bounds.IntersectsWith(button.Bounds))
                        failures.Add(scenario + ": header overlaps " + button.Text);
            foreach (Control control in Controls)
                if (control.Visible && !ClientRectangle.Contains(control.Bounds))
                    failures.Add(scenario + ": root control outside client " + control.Bounds);
            if (AutoScroll || VerticalScroll.Visible || HorizontalScroll.Visible) failures.Add(scenario + ": scrollbars visible");
            AssertChildrenInsidePanel(scenario, "printer", printerStep, failures);
            AssertChildrenInsidePanel(scenario, "server", serverStep, failures);
            AssertChildrenInsidePanel(scenario, "preview", previewStep, failures);
        }

        void AssertChildrenInsidePanel(string scenario, string panelName, Panel panel, List<string> failures)
        {
            foreach (Control child in panel.Controls)
            {
                if (!child.Visible) continue;
                if (child.Left < 0 || child.Top < 0 || child.Right > panel.ClientSize.Width || child.Bottom > panel.ClientSize.Height)
                {
                    failures.Add(scenario + ": " + panelName + "/" + child.Name + " out of bounds " + child.Bounds + " in " + panel.ClientSize);
                }
                foreach (Control other in panel.Controls)
                    if (other != child && other.Visible && child.Bounds.IntersectsWith(other.Bounds))
                        failures.Add(scenario + ": overlapping " + child.Text + " / " + other.Text);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.BringToFrontMessage)
            {
                BringThisWindowToFront();
                return;
            }
            base.WndProc(ref m);
        }

        void BringThisWindowToFront()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Show();
            Activate();
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(Handle);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && Enabled && printButton.Visible && printButton.Enabled)
            {
                printButton.PerformClick();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }


        void ConfigureStepPanel(Panel panel, Label badge, Label heading, Label sub, string number, string title, string subtitle, Action clickAction)
        {
            panel.BackColor = Color.White;
            panel.Cursor = clickAction == null ? Cursors.Default : Cursors.Hand;
            panel.Padding = new Padding(0);
            if (clickAction != null) panel.Click += delegate { clickAction(); };

            badge.Text = number;
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.Font = UiFont(12, FontStyle.Bold);
            badge.BackColor = Color.FromArgb(219, 234, 254);
            badge.ForeColor = Color.FromArgb(29, 78, 216);
            badge.Cursor = Cursors.Hand;
            if (clickAction != null) badge.Click += delegate { clickAction(); };

            heading.Text = title;
            heading.Font = UiFont(14, FontStyle.Bold);
            heading.Cursor = Cursors.Hand;
            if (clickAction != null) heading.Click += delegate { clickAction(); };

            sub.Text = subtitle;
            sub.ForeColor = Color.FromArgb(100, 108, 122);
            sub.Font = UiFont(10, FontStyle.Regular);
            sub.Cursor = Cursors.Hand;
            if (clickAction != null) sub.Click += delegate { clickAction(); };

            panel.Controls.Add(badge);
            panel.Controls.Add(heading);
            panel.Controls.Add(sub);
        }

        void StartPrinterCheckFromUi()
        {
            if (!OperationRunning)
            {
                CheckPrinter();
            }
        }

        void StartServerCheckFromUi()
        {
            if (OperationRunning) return;
            if (!serverChecking)
            {
                CheckServer();
            }
        }

        async void RefreshDocumentBadge()
        {
            if (badgeLoading || IsDisposed) return;
            badgeLoading = true;
            bool demo = config.DemoMode;
            try
            {
                int count = demo ? DocumentStatusForm.DemoStatus(config, 1).openCount : (await Task.Run(() => {
                    try { CopyReceipts.Flush(config, client); } catch { /* Keep receipts for the next regular poll. */ }
                    return client.DocumentStatus(1, false);
                })).openCount;
                if (IsDisposed || demo != config.DemoMode) return;
                documentsButton.NotificationText = count > 99 ? "99+" : count > 0 ? count.ToString() : null;
                documentsButton.AccessibleDescription = count + Loc.T(" offene Dokumente");
            }
            catch
            {
                if (!IsDisposed) { documentsButton.NotificationText = "?"; documentsButton.AccessibleDescription = Loc.T("Dokumentstatus nicht verfügbar"); }
            }
            finally
            {
                badgeLoading = false;
                if (!IsDisposed) documentsButton.Invalidate();
            }
        }

        float layoutScale = 1;
        bool layingOut;

        protected override CreateParams CreateParams
        {
            get
            {
                var value = base.CreateParams;
                // Buffer child HWNDs too (numeric input, labels and progress bar).
                value.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return value;
            }
        }

        void Place(Control control, int x, int y, int width, int height)
        {
            control.Bounds = ScaleBounds(new Rectangle(x, y, width, height), layoutScale);
        }

        void LayoutMain()
        {
            if (layingOut) return;
            layingOut = true;
            SuspendLayout();
            printerStep.SuspendLayout();
            serverStep.SuspendLayout();
            previewStep.SuspendLayout();
            try
            {
            // All geometry is expressed in logical pixels and scaled exactly once.
            float scale;
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            layoutScale = scale;
            var minimumClient = new Size((int)Math.Ceiling(800 * scale), (int)Math.Ceiling(620 * scale));
            var minimumWindow = SizeFromClientSize(minimumClient);
            if (MinimumSize != minimumWindow) MinimumSize = minimumWindow;
            int margin = 20;
            int width = (int)(ClientSize.Width / scale) - margin * 2;
            int contentRight = margin + width;

            Place(titleLabel, margin, 12, width - 360, 40);
            Place(subtitleLabel, margin + 2, 52, width - 326, 24);
            subtitleLabel.AutoEllipsis = true;
            int settingsWidth = 36;
            int badgeWidth = 152;
            Place(settingsButton, contentRight - settingsWidth, 28, settingsWidth, 36);
            Place(documentsButton, contentRight - settingsWidth - 152, 28, 140, 36);
            Place(modeBadge, contentRight - badgeWidth, 66, badgeWidth, 22);
            int footerY = (int)(ClientSize.Height / scale) - 52;
            Place(banner, margin, footerY, width, 38);
            banner.AutoEllipsis = true;
            Place(activity, margin, footerY + 42, width, 4);

            int y = 90;
            int gap = 10;
            bool serverComplete = serverReady && nextReady;
            int step1Height = 144;
            int step2Height = !serverComplete ? 76 : printerAttempted && !printerReady && !printerChecking ? 130 : 88;
            int step3Height = printerReady && serverReady && nextReady ? 176 : 76;

            LayoutStepCard(serverStep, margin, y, width, step1Height, true);
            LayoutServerStep(step1Height, width);

            y += step1Height + gap;
            LayoutStepCard(printerStep, margin, y, width, step2Height, serverComplete);
            LayoutPrinterStep(step2Height, width - (serverComplete ? 0 : 18));

            y += step2Height + gap;
            LayoutStepCard(previewStep, margin, y, width, step3Height, printerReady && serverReady && nextReady);
            LayoutPreviewStep(step3Height, width - (printerReady && serverReady && nextReady ? 0 : 18));
            AutoScrollMinSize = Size.Empty;
            }
            finally
            {
                previewStep.ResumeLayout(false);
                serverStep.ResumeLayout(false);
                printerStep.ResumeLayout(false);
                ResumeLayout(false);
                layingOut = false;
            }
        }

        static Rectangle ScaleBounds(Rectangle bounds, float scale)
        {
            return Rectangle.FromLTRB((int)Math.Round(bounds.Left * scale), (int)Math.Round(bounds.Top * scale),
                (int)Math.Round(bounds.Right * scale), (int)Math.Round(bounds.Bottom * scale));
        }

        void LayoutStepCard(Panel panel, int x, int y, int width, int height, bool enabled)
        {
            int inset = enabled ? 0 : 18;
            Place(panel, x + inset, y, width - inset, height);
        }

        void LayoutStepHeader(Label badge, Label heading, Label sub, int width)
        {
            Place(badge, 16, 16, 32, 32);
            Place(heading, 62, 10, width - 126, 26);
            Place(sub, 62, 38, width - 126, 24);
        }

        void LayoutPrinterStep(int height, int width)
        {
            LayoutStepHeader(printerBadge, printerHeading, printerSub, width);
            Place(testPrinterButton, width - 48, 16, 32, 32);
            Place(printerStatus, 62, 68, width - 270, 22);
            printerStatus.AutoEllipsis = true;
            Place(printerDetail, 62, 68, width - 94, 48);
        }

        void LayoutServerStep(int height, int width)
        {
            LayoutStepHeader(serverBadge, serverHeading, serverSub, width);
            Place(testServerButton, width - 48, 16, 32, 32);
            Place(refreshNextButton, 216, 102, 180, 32);
            Place(serverStatus, 62, 68, 124, 24);
            serverStatus.AutoEllipsis = true;
            Place(serverDetail, 62, 68, serverReady && nextReady ? width - 330 : width - 94, 52);
            Place(heroNextAsn, width - 244, 74, 228, 44);
            heroNextAsn.AutoEllipsis = true;
        }

        void LayoutPreviewStep(int height, int width)
        {
            LayoutStepHeader(previewBadge, previewHeading, previewSub, width);
            int previewX = width - 164;
            Place(preview, previewX, 16, 144, 144);
            Place(previewHeading, 62, 10, previewX - 78, 26);
            Place(previewSub, 62, 38, previewX - 78, 24);
            int textWidth = previewX - 82;
            Place(previewDetail, 62, 68, textWidth, 22);
            Place(lastPrinted, 62, 94, textWidth, 24);
            Place(printButton, 62, 114, 206, 44);
            Place(countLabel, 280, 88, 72, 23);
            Place(countFrame, 280, 114, 72, 44);
            printCount.SetBounds((int)(6 * layoutScale), Math.Max(0, (countFrame.ClientSize.Height - printCount.PreferredHeight) / 2),
                countFrame.ClientSize.Width - (int)(12 * layoutScale), printCount.PreferredHeight);
            Place(cutSwitch, 364, 114, 214, 44);
        }

        void StyleSecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 216, 226);
            button.BackColor = Color.FromArgb(250, 252, 255);
            button.ForeColor = Color.FromArgb(55, 65, 81);
            button.Font = UiFont(9, FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.UseVisualStyleBackColor = false;
        }


        void OpenSettings(bool printerSetup = false)
        {
            if (OperationRunning) return;
            string previousLanguage = config.Language;
            var previousSettings = new SettingsSnapshot(config);
            pollTimer.Stop();
            bool checkUpdates = false;
            using (var form = new SettingsForm(config))
            {
                if (printerSetup) form.SelectPrinterSetup();
                var result = form.ShowDialog(this);
                checkUpdates = form.CheckUpdatesRequested;
                if (result == DialogResult.OK || form.ResetPerformed)
                {
                    config.Save();
                    bool serverChanged = previousSettings.ServerChanged(config) || form.ResetPerformed;
                    bool printerChanged = previousSettings.PrinterChanged(config);
                    if (config.Language != previousLanguage)
                    {
                        if (!serverChanged) languageRestartView = new ServerView { Ready = serverReady && nextReady,
                            Attempted = serverAttempted, Payload = CurrentPreviewText(), Badge = documentsButton.NotificationText };
                        RestartRequested = true; Close(); return;
                    }
                    if (printerChanged) { printerReady = false; printerAttempted = false; }
                    if (serverChanged)
                    {
                        serverReady = nextReady = serverAttempted = false;
                        RefreshDashboard(Loc.T("Einstellungen gespeichert."));
                        BeginAutoWorkflow();
                    }
                    else if (printerChanged && serverReady && nextReady) CheckPrinter();
                }
            }
            pollTimer.Interval = config.RefreshMilliseconds;
            pollTimer.Start();
            if (checkUpdates) CheckUpdates(true);
        }

        void RefreshNextNumber(bool quiet)
        {
            if (!testMode) RefreshDocumentBadge();
            if (OperationRunning) return;
            if (config.DemoMode)
            {
                var reservation = DemoReservation();
                SetNextText(reservation.barcodeText);
                UpdatePreview(reservation.barcodeText);
                serverStatus.Text = Loc.T("Demo bereit");
                serverReady = true;
                nextReady = true;
                serverAttempted = true;
                serverDetail.Text = Loc.T("Simuliert ohne API. Nächste ASN: ") + reservation.barcodeText;
                if (!quiet) SetBanner(Loc.T("Demo: Nächste Nummer aktualisiert."), true);
                UpdateStepAvailability();
                return;
            }
            isRefreshingNext = true;
            serverAttempted = true;
            UpdateStepAvailability();
            refreshNextButton.Enabled = false;
            if (!quiet) SetBanner(Loc.T("Aktualisiere nächste ASN..."), true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                Reservation previewReservation = null;
                Exception error = null;
                try { previewReservation = client.PeekNext(); }
                catch (Exception ex) { error = ex; }

                Ui(delegate
                {
                    isRefreshingNext = false;
                    if (previewReservation != null)
                    {
                        SetNextText(previewReservation.barcodeText);
                        UpdatePreview(previewReservation.barcodeText);
                        serverStatus.Text = Loc.T("Verbunden");
                        serverReady = true;
                        nextReady = true;
                        serverDetail.Text = Loc.T("API verbunden. Nächste ASN: ") + previewReservation.barcodeText;
                        if (!quiet) SetBanner(Loc.T("Nächste Nummer aktualisiert: ") + previewReservation.barcodeText, true);
                    }
                    else
                    {
                        SetNextText("Offline");
                        serverReady = false;
                        nextReady = false;
                        serverAttempted = true;
                        serverDetail.Text = Loc.T("Server nicht erreichbar oder API-Key ungültig.");
                        if (!quiet)
                        {
                            SetBanner(Loc.T("Nächste Nummer konnte nicht geladen werden: ") + Friendly(error), false);
                        }
                    }
                    UpdateStepAvailability();
                    if (serverReady && nextReady && !printerAttempted) CheckPrinter();
                });
            });
        }

        void BeginAutoWorkflow()
        {
            if (!OperationRunning) CheckServer();
        }

        void CheckServer()
        {
            if (OperationRunning) return;

            if (config.DemoMode)
            {
                serverChecking = true;
                serverAttempted = false;
                SetBanner(Loc.T("Demo: Simuliere Server und nächste ASN..."), true);
                UpdateStepAvailability();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(250);
                    Ui(delegate
                    {
                        var reservation = DemoReservation();
                        serverChecking = false;
                        serverAttempted = true;
                        serverReady = true;
                        nextReady = true;
                        serverStatus.Text = Loc.T("Demo bereit");
                        SetNextText(reservation.barcodeText);
                        UpdatePreview(reservation.barcodeText);
                        serverDetail.Text = Loc.T("API wird im Demo-Modus nicht benutzt.");
                        SetBanner(Loc.T("Demo-Modus: Bereit zum Drucken."), true);
                        UpdateStepAvailability();
                        if (!printerReady) CheckPrinter();
                    });
                });
                return;
            }

            serverChecking = true;
            serverAttempted = false;
            serverReady = false;
            nextReady = false;
            serverStatus.Text = Loc.T("Prüfe Server...");
            serverDetail.Text = Loc.T("Verbinde mit Reservierungs-API.");
            SetBanner(Loc.T("Prüfe Server und lade nächste ASN..."), true);
            UpdateStepAvailability();

            ThreadPool.QueueUserWorkItem(delegate
            {
                Health health = null;
                Reservation previewReservation = null;
                Exception error = null;
                try
                {
                    health = client.Health();
                    if (health.ok)
                    {
                        previewReservation = client.PeekNext();
                    }
                }
                catch (Exception ex) { error = ex; }

                Ui(delegate
                {
                    serverChecking = false;
                    serverAttempted = true;
                    if (health != null && health.ok && previewReservation != null)
                    {
                        serverReady = true;
                        nextReady = true;
                        serverStatus.Text = Loc.T("Verbunden");
                        SetNextText(previewReservation.barcodeText);
                        UpdatePreview(previewReservation.barcodeText);
                        serverDetail.Text = Loc.T("Nächste ASN geladen: ") + previewReservation.barcodeText;
                        SetBanner(Loc.T("Bereit für Schritt 3."), true);
                    }
                    else
                    {
                        serverReady = false;
                        nextReady = false;
                        serverStatus.Text = Loc.T("Nicht erreichbar");
                        SetNextText("Offline");
                        serverDetail.Text = error == null ? Loc.T("Serverantwort war unklar.") : Friendly(error);
                        SetBanner(Loc.T("Serverfehler: ") + serverDetail.Text, false);
                    }
                    UpdateStepAvailability();
                    if (serverReady && nextReady && !printerReady) CheckPrinter();
                });
            });
        }

        void CheckPrinter()
        {
            if (OperationRunning) return;
            if (!serverReady || !nextReady) return;

            if (config.DemoMode)
            {
                printerChecking = true;
                printerAttempted = false;
                printerReady = false;
                SetBanner(Loc.T("Demo: Simuliere Drucker und DK-Rolle..."), true);
                UpdateStepAvailability();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(250);
                    Ui(delegate
                    {
                        printerChecking = false;
                        printerAttempted = true;
                        detectedMedia = MediaInfo.DefaultDk11221();
                        printerReady = true;
                        printerStatus.Text = Loc.T("Demo bereit");
                        printerDetail.Text = Loc.T("Simulierter Brother QL-800 mit DK-11221.");
                        UpdatePreview(CurrentPreviewText());
                        UpdateStepAvailability();
                    });
                });
                return;
            }

            printerChecking = true;
            printerAttempted = false;
            printerReady = false;
            printerStatus.Text = Loc.T("Suche Brother QL-800...");
            printerDetail.Text = Loc.T("Prüft Erreichbarkeit und liest die eingelegte DK-Rolle.");
            SetBanner("Suche Brother QL-800 und lese Rolle...", true);
            UpdateStepAvailability();

            ThreadPool.QueueUserWorkItem(delegate
            {
                PrinterStatus checkedStatus = null;
                Exception error = null;
                try
                {
                    using (var printer = PrinterBackends.Open(config))
                    {
                        checkedStatus = printer.ReadStatus();
                    }
                }
                catch (Exception ex) { error = ex; }

                Ui(delegate
                {
                    printerChecking = false;
                    printerAttempted = true;
                    if (checkedStatus != null && checkedStatus.CanPrint)
                    {
                        detectedMedia = checkedStatus.Media;
                        printerReady = true;
                        printerStatus.Text = checkedStatus.Description;
                        printerDetail.Text = Loc.T("Bereit: ") + checkedStatus.Media.DisplayName;
                        UpdatePreview(CurrentPreviewText());
                        SetBanner(Loc.T("Bereit zum Drucken."), true);
                        UpdateStepAvailability();
                    }
                    else
                    {
                        detectedMedia = null;
                        printerReady = false;
                        printerStatus.Text = Loc.T("Nicht bereit");
                        printerDetail.Text = checkedStatus == null ? Friendly(error) : checkedStatus.BlockReason;
                        SetBanner(Loc.T("Druckerfehler: ") + printerDetail.Text, false);
                        UpdateStepAvailability();
                    }
                });
            });
        }

        void PrintNext()
        {
            if (!Enabled || OperationRunning) return;
            if (!config.DemoMode && ClientRuntime.UpdateRequired) return;
            if (!printerReady || !serverReady || !nextReady || !previewValid)
            {
                SetBanner(Loc.T("Bitte erst Schritt 1 und 2 erfolgreich abschließen."), false);
                UpdateStepAvailability();
                return;
            }
            var batch = new PrintBatch((int)printCount.Value, cutSwitch.Checked);
            batchTotal = batch.Count;
            batchIndex = 1;
            if (config.DemoMode)
            {
                SetBusy(true);
                SetBanner(Loc.T("Demo: Druckauftrag wird verarbeitet..."), true);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        batch.Run(index =>
                        {
                            Ui(delegate { batchIndex = index; UpdateStepAvailability(); SetBanner(Loc.T("Demo: Label ") + index + Loc.T(" von ") + batch.Count + Loc.T(" wird gedruckt..."), true); });
                            Thread.Sleep(testMode ? 30 : 3000);
                            Ui(delegate
                            {
                                string printed = DemoReservation().barcodeText;
                                config.DemoReusable.Remove(AsnNumber.Parse(printed));
                                config.DemoLastPrinted = AsnNumber.Format(Math.Max(AsnNumber.Parse(config.DemoLastPrinted), AsnNumber.Parse(printed)));
                                if (!testMode) config.Save();
                                var next = DemoReservation();
                                SetNextText(next.barcodeText);
                                UpdatePreview(next.barcodeText);
                            });
                        });
                        Ui(delegate { SetBanner("Demo: " + batch.Completed + Loc.T(" von ") + batch.Count + " Labels gedruckt.", true); });
                    }
                    catch (Exception ex) { Ui(delegate { SetBanner("Demo-Serie gestoppt: " + Friendly(ex), false); }); }
                    finally { Ui(delegate { SetBusy(false); }); }
                });
                return;
            }
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Reservation reservation = null;
                var lifecycle = new PrintLifecycle();
                try
                {
                    batch.Run(index =>
                    {
                    reservation = null;
                    lifecycle = new PrintLifecycle();
                    Ui(delegate { batchIndex = index; UpdateStepAvailability(); });
                    Ui(delegate { SetBanner(Loc.T("Suche Drucker und lese Rolle..."), true); });
                    using (var printer = PrinterBackends.Open(config))
                    {
                        var checkedStatus = printer.ReadStatus();
                        Ui(delegate
                        {
                            detectedMedia = checkedStatus.Media;
                            printerStatus.Text = checkedStatus.Description;
                            printerReady = checkedStatus.CanPrint;
                            if (checkedStatus.CanPrint) UpdatePreview(CurrentPreviewText());
                        });
                        if (!checkedStatus.CanPrint)
                        {
                            throw new InvalidOperationException(checkedStatus.BlockReason);
                        }
                        string plannedPayload = null;
                        Ui(delegate { plannedPayload = CurrentPreviewPayload(); });
                        using (var preflight = LabelRenderer.Render(plannedPayload, checkedStatus.Media, config.UsePfRightAligned)) { }

                        Ui(delegate { SetBanner(Loc.T("Reserviere nächste ASN..."), true); });
                        reservation = client.Next();
                        Ui(delegate
                        {
                            serverReady = true;
                            nextReady = true;
                            SetNextText(reservation.barcodeText);
                            UpdatePreview(reservation.barcodeText);
                            UpdateStepAvailability();
                        });

                        Ui(delegate { SetBanner("Drucke " + reservation.barcodeText + " auf " + checkedStatus.Media.ShortName + "...", true); });
                        lifecycle.Submit(printer, reservation.barcodeText, checkedStatus.Media,
                            () => client.MarkPrinting(reservation.reservationId),
                            () => client.MarkPrinted(reservation.reservationId), batch.CutAfter(index));
                        config.LastPrinted = reservation.barcodeText;
                        config.Save();
                        Ui(delegate
                        {
                            lastPrinted.Text = Loc.T("Letzter Druck: ") + reservation.barcodeText;
                            SetBanner("Gedruckt: " + reservation.barcodeText, true);
                            UpdateStepAvailability();
                        });
                    }
                    });
                    Ui(delegate { SetBanner(batch.Completed + Loc.T(" von ") + batch.Count + " Labels gedruckt.", true); });
                }
                catch (Exception ex)
                {
                    if (reservation != null && !lifecycle.HandshakeStarted)
                    {
                        TryRelease(reservation.reservationId);
                    }
                    Ui(delegate
                    {
                        string message = Loc.T("Serie gestoppt bei Label ") + batchIndex + Loc.T(" von ") + batch.Count + ". " +
                            batch.Completed + " vorherige Labels erfolgreich abgeschlossen. " + (lifecycle.ApiConfirmed ? Loc.T("Aktuelles Label gedruckt und beim Server bestätigt; lokale Einstellungen konnten nicht gespeichert werden. ") :
                            lifecycle.PrintConfirmed ? Loc.T("Label gedruckt, aber API-Bestätigung fehlgeschlagen. Unter Dokumente beheben; nicht erneut drucken. ") :
                            lifecycle.HandshakeStarted ? Loc.T("Druckausgang unklar. ASN bleibt geschützt; unter Dokumente prüfen. ") :
                            Loc.T("Kein Druckauftrag gestartet. ")) + Friendly(ex) +
                            (!cutSwitch.Checked && batch.Count > 1 ? Loc.T(" Der Labelstreifen ist möglicherweise noch ungeschnitten. Kein automatischer Nachdruck oder Schnitt.") : "");
                        if (lifecycle.PrintConfirmed) lastPrinted.Text = Loc.T("Letzter Druck: ") + reservation.barcodeText;
                        SetBanner(message, false);
                        MessageBox.Show(this, message, "ASN Labeler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
                finally
                {
                    Ui(delegate
                    {
                        SetBusy(false);
                        RefreshNextNumber(true);
                    });
                }
            });
        }

        void Ui(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { Invoke(new Action(delegate { if (!IsDisposed && !Disposing) action(); })); }
                catch (ObjectDisposedException) { }
                return;
            }
            action();
        }


        void RefreshDashboard(string message)
        {
            serverStatus.Text = config.DemoMode ? Loc.T("Demo bereit") : (serverReady ? Loc.T("Verbunden") : Loc.T("Nicht geprüft"));
            printerStatus.Text = detectedMedia == null ? Loc.T("Noch nicht erkannt") : detectedMedia.DisplayName;
            lastPrinted.Text = Loc.T("Letzter Druck: ") + (config.DemoMode ? config.DemoLastPrinted : config.LastPrinted);
            modeBadge.Visible = config.DemoMode;
            BackColor = config.DemoMode ? Color.FromArgb(255, 251, 235) : Color.FromArgb(243, 246, 250);
            if (nextAsn.Text == "") SetNextText(Loc.T("Wird geladen..."));
            if (heroNextAsn.Text == "") SetNextText(Loc.T("Wird geladen..."));
            UpdatePreview(config.LastPrinted == "-" ? "ASN00001" : config.LastPrinted);
            SetBanner(config.DemoMode ? Loc.T("Demo-Modus aktiv: Drucker, Server und Druck werden simuliert.") : message, true);
            UpdateStepAvailability();
        }

        Font PreferredMonoFont(float size, FontStyle style)
        {
            string[] names = { "Cascadia Mono", "Consolas", "Lucida Console", "Courier New" };
            foreach (string name in names)
            {
                try
                {
                    var font = new Font(name, size, style);
                    if (string.Equals(font.FontFamily.Name, name, StringComparison.OrdinalIgnoreCase)) return font;
                    font.Dispose();
                }
                catch { }
            }
            return new Font(FontFamily.GenericMonospace, size, style);
        }

        void SetBanner(string text, bool ok, bool global = false)
        {
            showBanner = global || isBusy;
            banner.Visible = showBanner;
            banner.Text = "  " + text;
            if (config.DemoMode && ok)
            {
                banner.BackColor = Color.FromArgb(255, 247, 237);
                banner.ForeColor = Color.FromArgb(154, 52, 18);
            }
            else
            {
                banner.BackColor = ok ? Color.FromArgb(224, 242, 254) : Color.FromArgb(254, 226, 226);
                banner.ForeColor = ok ? Color.FromArgb(7, 89, 133) : Color.FromArgb(153, 27, 27);
            }
            banner.Refresh();
        }

        Font UiFont(float size, FontStyle style)
        {
            string[] names = { "Segoe UI Variable Text", "Segoe UI" };
            foreach (string name in names)
            {
                try
                {
                    var font = new Font(name, size, style);
                    if (string.Equals(font.FontFamily.Name, name, StringComparison.OrdinalIgnoreCase)) return font;
                    font.Dispose();
                }
                catch { }
            }
            return new Font(Font.FontFamily, size, style);
        }

        Reservation DemoReservation()
        {
            int next = DemoNextNumber();
            return new Reservation
            {
                reservationId = "demo-" + next.ToString(),
                asnNumber = next,
                barcodeText = AsnNumber.Format(next)
            };
        }

        int DemoNextNumber()
        {
            return config.NextDemoAsn();
        }

        void SetNextText(string text)
        {
            nextAsn.Text = text;
            heroNextAsn.Text = text;
            bool isStatus = text == "Offline" || text == Loc.T("Wird geladen...");
            float fontSize = isStatus ? 16 : 20;
            if (heroNextAsn.Font.Size != fontSize)
            {
                var oldFont = heroNextAsn.Font;
                heroNextAsn.Font = PreferredMonoFont(fontSize, FontStyle.Bold);
                oldFont.Dispose();
            }
            heroNextAsn.ForeColor = isStatus ? Color.FromArgb(82, 94, 112) : Color.FromArgb(17, 24, 39);
        }

        void SetBusy(bool busy)
        {
            isBusy = busy;
            printButton.Enabled = !busy && printerReady && serverReady && nextReady;
            settingsButton.Enabled = !busy;
            testServerButton.Enabled = !busy && printerReady;
            testPrinterButton.Enabled = !busy;
            refreshNextButton.Enabled = !busy && printerReady;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (busy)
            {
                animationStep = 0;
                animationTimer.Start();
            }
            else
            {
                animationTimer.Stop();
                printButton.BackColor = Color.FromArgb(0, 120, 83);
                if (!testMode) RefreshDocumentBadge();
            }
            UpdateStepAvailability();
        }

        bool OperationRunning { get { return isBusy || printerChecking || serverChecking || isRefreshingNext; } }

        void UpdateStepAvailability()
        {
            bool serverComplete = serverReady && nextReady;
            bool canUsePrinter = !OperationRunning && serverComplete;
            bool canUseServer = !OperationRunning;
            bool canUsePreview = !OperationRunning && printerReady && serverReady && nextReady && previewValid && (config.DemoMode || !ClientRuntime.UpdateRequired);
            settingsButton.Enabled = !OperationRunning;
            documentsButton.Enabled = !OperationRunning;
            activity.Visible = OperationRunning;
            if (OperationRunning) animationTimer.Start(); else animationTimer.Stop();
            printButton.Text = isBusy ? Loc.T("Label ") + batchIndex + Loc.T(" von ") + batchTotal : printCount.Value == 1 ? Loc.T("Label drucken") : printCount.Value + " Labels drucken";
            printCount.Enabled = canUsePreview;
            cutSwitch.Enabled = canUsePreview;
            bool showServerActions = !serverChecking && serverAttempted;
            bool showPreviewContent = printerReady && serverReady && nextReady;

            testPrinterButton.Visible = serverComplete && !printerChecking && printerAttempted;
            testPrinterButton.Enabled = canUsePrinter;

            testServerButton.Visible = showServerActions;
            testServerButton.Enabled = canUseServer;

            refreshNextButton.Visible = false;
            refreshNextButton.Enabled = canUseServer && !isRefreshingNext;
            printerStatus.Visible = false;
            serverStatus.Visible = false;
            printerDetail.Visible = serverComplete && printerAttempted && !printerReady && !printerChecking;
            serverDetail.Visible = !serverChecking && !isRefreshingNext && !config.DemoMode;
            heroNextAsn.Visible = serverComplete;

            printButton.Visible = printerReady && serverReady && nextReady;
            printButton.Enabled = canUsePreview;
            preview.Visible = showPreviewContent;
            previewDetail.Visible = showPreviewContent && !previewValid;
            lastPrinted.Visible = false;
            printCount.Visible = countLabel.Visible = showPreviewContent;
            countFrame.Visible = showPreviewContent;
            cutSwitch.Visible = showPreviewContent && printCount.Value > 1;

            if (!serverComplete)
            {
                printerSub.Text = Loc.T("Wartet auf Server");
            }
            else if (printerChecking)
            {
                printerSub.Text = Loc.T("Suche Drucker und Medium");
            }
            else if (printerReady)
            {
                printerSub.Text = detectedMedia == null ? Loc.T("Verbunden") : detectedMedia.DisplayName;
            }
            else if (printerAttempted)
            {
                printerSub.Text = Loc.T("Verbindung prüfen");
            }
            else
            {
                printerSub.Text = Loc.T("Startet automatisch");
            }

            if (serverChecking || isRefreshingNext)
            {
                serverSub.Text = Loc.T("API wird geprüft");
            }
            else if (serverReady && nextReady)
            {
                serverSub.Text = config.DemoMode ? Loc.T("Simuliert") : Loc.T("Verbunden");
                serverDetail.Text = config.DemoMode ? Loc.T("Demo: kein API-Zugriff") : config.ServerUrl;
            }
            else if (serverAttempted)
            {
                serverSub.Text = Loc.T("Verbindung prüfen");
            }
            else
            {
                serverSub.Text = Loc.T("Startet automatisch");
            }

            previewSub.Text = isBusy ? Loc.T("Druck läuft") : (canUsePreview ? Loc.T("Bereit zum Drucken") : Loc.T("Noch nicht bereit"));

            if (!printerReady)
            {
                if (printerChecking)
                {
                    printerStatus.Text = Loc.T("Prüfung läuft...");
                    printerDetail.Text = Loc.T("Die App sucht den Brother QL-800 und liest das eingelegte Medium.");
                }
                previewDetail.Text = Loc.T("Noch nicht bereit.");
                if (!printerChecking && !printerAttempted)
                {
                    printerStatus.Text = Loc.T("Startet automatisch");
                    printerDetail.Text = Loc.T("Die App sucht den Brother QL-800 und liest die eingelegte DK-Rolle.");
                }
            }
            else if (!serverReady || !nextReady)
            {
                if (serverChecking)
                {
                    serverStatus.Text = Loc.T("Prüfung läuft...");
                    serverDetail.Text = Loc.T("API wird geprüft und nächste ASN wird geladen.");
                }
                else if (isRefreshingNext)
                {
                    serverStatus.Text = Loc.T("Aktualisiere...");
                    serverDetail.Text = Loc.T("Nächste ASN wird neu geladen.");
                }
                else if (!serverAttempted)
                {
                    serverStatus.Text = Loc.T("Startet automatisch");
                    serverDetail.Text = Loc.T("Server-Check startet automatisch.");
                }
                previewDetail.Text = Loc.T("Wartet auf Server und nächste ASN.");
            }
            else
            {
                previewDetail.Text = isBusy ? Loc.T("Warte auf Abschluss des Druckauftrags.") : (previewValid ? Loc.T("Bereit zum Drucken mit ") + (detectedMedia == null ? Loc.T("erkannter Rolle") : detectedMedia.ShortName) + "." : previewError);
            }

            ApplyStepVisual(printerStep, printerBadge, printerHeading, printerSub, serverComplete, serverComplete && printerReady, printerChecking);
            ApplyStepVisual(serverStep, serverBadge, serverHeading, serverSub, true, serverComplete, serverChecking || isRefreshingNext);
            ApplyStepVisual(previewStep, previewBadge, previewHeading, previewSub, canUsePreview, canUsePreview, false);
            LayoutMain();
        }

        void ApplyStepVisual(Panel panel, Label badge, Label heading, Label sub, bool available, bool complete, bool running)
        {
            panel.BackColor = available ? Color.White : Color.FromArgb(232, 238, 247);
            panel.Cursor = panel != previewStep && available && !OperationRunning ? Cursors.Hand : Cursors.Default;
            badge.Cursor = panel.Cursor;
            heading.Cursor = panel.Cursor;
            sub.Cursor = panel.Cursor;
            badge.BackColor = running ? Color.FromArgb(254, 243, 199) : (complete ? Color.FromArgb(209, 250, 229) : (available ? Color.FromArgb(219, 234, 254) : Color.FromArgb(226, 232, 240)));
            badge.ForeColor = running ? Color.FromArgb(146, 64, 14) : (complete ? Color.FromArgb(6, 95, 70) : (available ? Color.FromArgb(29, 78, 216) : Color.FromArgb(100, 116, 139)));
            heading.ForeColor = available ? Color.FromArgb(17, 24, 39) : Color.FromArgb(100, 116, 139);
            sub.ForeColor = available ? Color.FromArgb(100, 108, 122) : Color.FromArgb(148, 163, 184);
            var card = panel as StepCardPanel;
            if (card != null)
            {
                card.BorderColor = running ? Color.FromArgb(245, 158, 11) : (complete ? Color.FromArgb(52, 211, 153) : (available ? Color.FromArgb(191, 219, 254) : Color.FromArgb(226, 232, 240)));
                card.Invalidate();
            }
        }

        void AnimateBusy()
        {
            animationStep++;
            string dots = new string('.', (animationStep / 6) % 4);
            if (isBusy) previewSub.Text = Loc.T("Label ") + batchIndex + Loc.T(" von ") + batchTotal + Loc.T(" wird gedruckt") + dots;
            else if (printerChecking) printerSub.Text = Loc.T("Suche Drucker und Medium") + dots;
            else if (serverChecking || isRefreshingNext) serverSub.Text = Loc.T("API wird geprüft") + dots;
        }

        void UpdatePreview(string text)
        {
            if (preview.Image != null)
            {
                preview.Image.Dispose();
                preview.Image = null;
            }
            try
            {
                preview.Image = LabelRenderer.Render(text, detectedMedia ?? MediaInfo.DefaultDk11221(), config.UsePfRightAligned);
                previewValid = true;
                previewError = null;
            }
            catch (Exception ex)
            {
                previewValid = false;
                previewError = ex.Message;
                SetBanner(previewError, false);
            }
        }

        string CurrentPreviewPayload()
        {
            string result = null;
            Ui(delegate { result = CurrentPreviewText(); });
            return result;
        }

        string CurrentPreviewText()
        {
            if (!string.IsNullOrWhiteSpace(heroNextAsn.Text) && heroNextAsn.Text != "Offline" && heroNextAsn.Text != Loc.T("Wird geladen..."))
            {
                return heroNextAsn.Text;
            }
            if (!string.IsNullOrWhiteSpace(config.LastPrinted) && config.LastPrinted != "-")
            {
                return config.LastPrinted;
            }
            return "ASN00001";
        }

        void TryRelease(string reservationId)
        {
            try { client.Release(reservationId); }
            catch { }
        }

        string Friendly(Exception ex)
        {
            var web = ex as WebException;
            if (web != null && web.Response is HttpWebResponse)
            {
                var response = (HttpWebResponse)web.Response;
                return "HTTP " + (int)response.StatusCode + " vom API-Dienst.";
            }
            return ex == null ? Loc.T("Keine gültige Antwort erhalten.") : ex.Message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pollTimer.Dispose();
                animationTimer.Dispose();
                actionTips.Dispose();
                if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
            }
            base.Dispose(disposing);
        }
    }

    sealed class CutSwitch : CheckBox
    {
        public CutSwitch()
        {
            AutoSize = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            float scale = e.Graphics.DpiX / 96f;
            float height = 22 * scale, width = 40 * scale;
            float top = (Height - height) / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var track = new GraphicsPath())
            {
                track.AddArc(0, top, height, height, 90, 180);
                track.AddArc(width - height, top, height, height, 270, 180);
                track.CloseFigure();
                using (var brush = new SolidBrush(!Enabled ? Color.LightGray : Checked ? Color.FromArgb(0, 120, 83) : Color.FromArgb(130, 140, 154)))
                    e.Graphics.FillPath(brush, track);
            }
            float inset = 3 * scale, diameter = height - inset * 2;
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(brush, Checked ? width - inset - diameter : inset, top + inset, diameter, diameter);
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle((int)(48 * scale), 0, Width - (int)(48 * scale), Height),
                Enabled ? Color.FromArgb(55, 65, 81) : SystemColors.GrayText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
    }

    class ActionButton : Button
    {
        public string NotificationText { get; set; }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); PaintNotification(e); return; }
            e.Graphics.Clear(Color.FromArgb(226, 232, 240));
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.FromArgb(100, 116, 139),
                (String.IsNullOrEmpty(NotificationText) ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            PaintNotification(e);
        }

        void PaintNotification(PaintEventArgs e)
        {
            if (String.IsNullOrEmpty(NotificationText)) return;
            float s = e.Graphics.DpiX / 96f;
            var rect = new Rectangle(Width - (int)(32 * s), (int)(5 * s), (int)(28 * s), (int)(24 * s));
            using (var font = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, NotificationText, font, rect, Enabled ? ForeColor : Color.FromArgb(100, 116, 139), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    sealed class MaterialIconButton : ActionButton
    {
        static readonly PrivateFontCollection Icons = LoadIcons();
        readonly string glyph;
        public MaterialIconButton(string glyph) { this.glyph = glyph; }

        static PrivateFontCollection LoadIcons()
        {
            var fonts = new PrivateFontCollection();
            using (var stream = typeof(MaterialIconButton).Assembly.GetManifestResourceStream("AsnLabeler.MaterialIcons.ttf"))
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                byte[] bytes = buffer.ToArray();
                // GDI+ keeps this font data for the lifetime of the process.
                IntPtr memory = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                fonts.AddMemoryFont(memory, bytes.Length);
            }
            return fonts;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var font = new Font(Icons.Families[0], 17, FontStyle.Regular, GraphicsUnit.Point))
            using (var brush = new SolidBrush(Enabled ? ForeColor : Color.FromArgb(100, 116, 139)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString(glyph, font, brush, ClientRectangle, format);
        }
    }

    sealed class StepCardPanel : Panel
    {
        public Color BorderColor { get; set; }

        public StepCardPanel()
        {
            BorderColor = Color.FromArgb(226, 232, 240);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            using (var fill = new SolidBrush(BackColor))
            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    sealed class SettingsForm : Form
    {
        readonly AppConfig config;
        readonly ComboBox languageChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Label languageHint = new Label { ForeColor = Color.DimGray };
        readonly TabControl settingsTabs = new TabControl();
        readonly TextBox serverUrl = new TextBox();
        readonly TextBox apiKey = new TextBox();
        readonly Button setupBpacButton = new Button();
        readonly NumericUpDown refreshSeconds = new NumericUpDown { Minimum = 10, Maximum = 3600 };
        readonly Button resetRefreshButton = new MaterialIconButton("\ue5d5") { AccessibleName = Loc.T("Refresh auf 30 Sekunden zurücksetzen") };
        readonly ToolTip settingsTips = new ToolTip();
        readonly Button copyLabelButton = new Button();
        readonly Label bpacStatus = new Label();
        bool? bpacAvailable;
        readonly Button diagnoseButton = new Button();
        readonly Button testLabelButton = new Button();
        readonly Button testApiButton = new Button();
        readonly ComboBox rollProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox demoMode = new CheckBox();
        readonly ListBox deviceList = new ListBox();
        readonly Label deviceStatus = new Label();
        readonly Button findDeviceButton = new Button();
        readonly Button resetAsnButton = new Button();
        readonly ProgressBar resetProgress = new ProgressBar();
        readonly Dictionary<Control, Rectangle> logicalBounds = new Dictionary<Control, Rectangle>();
        readonly bool testMode;
        bool resetting;
        bool searching;

        public bool ResetPerformed { get; private set; }
        public bool CheckUpdatesRequested { get; private set; }
        public string ResetNextBarcodeText { get; private set; }

        public SettingsForm(AppConfig config) : this(config, false) { }

        SettingsForm(AppConfig config, bool testMode)
        {
            this.testMode = testMode;
            this.config = config;
            Text = Loc.T("Einstellungen");
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(680, 632);
            AutoScroll = false;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(246, 247, 249);

            var headline = new Label();
            headline.Text = Loc.T("Einstellungen");
            headline.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            headline.SetBounds(22, 18, 300, 34);
            Controls.Add(headline);

            AddSection("Reservierungsdienst", 58);
            AddRow(Loc.T("Serveradresse"), serverUrl, 88, "z. B. http://ugreen-nas:8765");
            AddRow("API-Key", apiKey, 146, Loc.T("Geschützt im Windows-Anmeldeinformationsspeicher"));
            demoMode.Text = Loc.T("Demo-Modus");
            demoMode.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            demoMode.ForeColor = Color.FromArgb(154, 52, 18);
            demoMode.SetBounds(22, 436, 300, 28);
            demoMode.CheckedChanged += delegate { UpdateDemoSettingsState(); };

            AddSection("Brother QL", 214);
            var connectionLabel = new Label { Text = Loc.T("Anbindung"), Font = new Font(Font, FontStyle.Bold) };
            connectionLabel.SetBounds(22, 248, 148, 26);
            setupBpacButton.Text = Loc.T("Brother / b-PAC einrichten…");
            setupBpacButton.SetBounds(180, 242, 476, 36);
            setupBpacButton.Click += delegate
            {
                string instructions = ClientUpdates.Text("1. Offiziellen Brother-Treiber für das gewählte Modell installieren. P-touch muss drucken können.\r\n\r\n", "1. Install the official Brother driver for your model. Printing from P-touch must work.\r\n\r\n") +
                    Loc.T("2. Von Brother den b-PAC Client Component Installer in 64 Bit herunterladen und ausführen. Das vollständige SDK ist nicht erforderlich.\r\n\r\n") +
                    Loc.T("3. Danach hier erneut nach dem Drucker suchen. Windows kann für die Installation Administratorrechte verlangen.\r\n\r\n") +
                    Loc.T("WinUSB wird nicht mehr verwendet. Falls der USB-Treiber zuvor ersetzt wurde, den offiziellen Brother-Treiber wiederherstellen.\r\n\r\n") +
                    Loc.T("Die offizielle Brother-Downloadseite jetzt öffnen?");
                if (MessageBox.Show(this, instructions, Loc.T("b-PAC einrichten"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    try { Process.Start(new ProcessStartInfo("https://support.brother.com/g/s/es/dev/en/bpac/download/index.html?c=eu_ot&comple=on&lang=en&navi=offall&redirect=on") { UseShellExecute = true }); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("Downloadseite nicht geöffnet"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            Controls.Add(connectionLabel);
            Controls.Add(setupBpacButton);
            bpacStatus.SetBounds(180, 242, 476, 36);
            bpacStatus.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(bpacStatus);
            apiKey.PasswordChar = '*';

            serverUrl.Text = config.ServerUrl;
            apiKey.Text = testMode ? "" : CredentialStore.Read("AsnLabeler.ApiKey") ?? "";
            demoMode.Checked = config.DemoMode;

            findDeviceButton.Text = ClientUpdates.Text("Brother QL suchen", "Find Brother QL");
            findDeviceButton.SetBounds(180, 310, 200, 36);
            findDeviceButton.Click += delegate { ScanBrotherDevices(); };

            deviceStatus.Text = Loc.T("Noch nicht gesucht.");
            deviceStatus.ForeColor = Color.FromArgb(90, 98, 112);
            deviceStatus.SetBounds(180, 280, 476, 26);
            deviceStatus.AutoEllipsis = true;

            deviceList.SetBounds(180, 356, 476, 64);
            deviceList.IntegralHeight = false;
            deviceList.HorizontalScrollbar = true;
            deviceList.SelectedIndexChanged += delegate
            {
                if (!searching && deviceList.SelectedItem != null && !testMode) ApplySettings();
            };
            diagnoseButton.Text = Loc.T("Diagnose kopieren");
            diagnoseButton.SetBounds(392, 310, 264, 36);
            diagnoseButton.Click += async delegate
            {
                if (searching || resetting || !ApplySettings()) return;
                searching = true;
                UpdateDemoSettingsState();
                string backend = PrinterBackends.Brother;
                string name = deviceList.SelectedItem == null ? config.PrinterName : deviceList.SelectedItem.ToString();
                try
                {
                    double correction = config.RollCorrectionMm;
                    bool rightAligned = config.UsePfRightAligned;
                    var report = await Task.Run(() => PrinterBackends.Diagnose(backend, name, correction, rightAligned));
                    if (!IsDisposed) { Clipboard.SetText(report); deviceStatus.Text = Loc.T("Diagnose in die Zwischenablage kopiert."); }
                }
                catch (Exception ex) { if (!IsDisposed) deviceStatus.Text = ex.Message; }
                finally { searching = false; if (!IsDisposed) UpdateDemoSettingsState(); }
            };
            Controls.Add(diagnoseButton);
            testLabelButton.Text = Loc.T("Testdruck");
            testLabelButton.SetBounds(392, 478, 264, 36);
            testLabelButton.Click += async delegate
            {
                if (searching || resetting || demoMode.Checked || !ApplySettings()) return;
                if (MessageBox.Show(this, Loc.T("Ein Label TEST0001 wird gedruckt, ohne ASN-Reservierung oder API-Aufruf. Nur starten, wenn keine anderen Druckaufträge laufen. Fortfahren?") + (config.RollCorrectionMm != 0 ? "\r\n\r\nPF-Kalibriertest: Überschreitungen der gemeldeten Druckränder werden zugelassen. Der Ausdruck kann abgeschnitten oder nicht scanbar sein." : ""),
                    Loc.T("Hardwaretest"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                searching = true;
                UpdateDemoSettingsState();
                resetProgress.Visible = true;
                try
                {
                    string selected = config.PrinterName;
                    double correction = config.RollCorrectionMm;
                    bool rightAligned = config.UsePfRightAligned;
                    string result = await Task.Run(() => BpacPrinterSession.TestPrint(PrinterBackends.Select(PrinterBackends.DiscoverBrother(), selected), true, correction, rightAligned));
                    if (!IsDisposed) MessageBox.Show(this, result, Loc.T("Testdruck prüfen"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Loc.T("Testdruck nicht bestätigt"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { searching = false; if (!IsDisposed) { resetProgress.Visible = false; UpdateDemoSettingsState(); } }
            };
            Controls.Add(testLabelButton);

            resetAsnButton.Text = Loc.T("ASN-Stand abgleichen");
            resetAsnButton.SetBounds(22, 478, 260, 36);
            resetAsnButton.BackColor = Color.FromArgb(255, 247, 237);
            resetAsnButton.ForeColor = Color.FromArgb(154, 52, 18);
            resetAsnButton.FlatStyle = FlatStyle.Flat;
            resetAsnButton.FlatAppearance.BorderColor = Color.FromArgb(253, 186, 116);
            resetAsnButton.Click += delegate { ResetAsnState(); };

            Controls.Add(findDeviceButton);
            Controls.Add(demoMode);
            Controls.Add(deviceStatus);
            Controls.Add(deviceList);
            Controls.Add(resetAsnButton);
            resetProgress.SetBounds(22, 524, 634, 4);
            resetProgress.Style = ProgressBarStyle.Marquee;
            resetProgress.Visible = false;
            Controls.Add(resetProgress);
            foreach (Control control in Controls)
                if (control.Top >= 214) control.Top += 44;
            foreach (Control control in Controls)
                if (control.Top >= 480) control.Top += 40;
            var profileLabel = new Label { Text = Loc.T("Rollenprofil"), Font = new Font(Font, FontStyle.Bold) };
            profileLabel.SetBounds(22, 480, 148, 28);
            rollProfile.Items.AddRange(new object[] { Loc.T("Brother Standard"), "PF-B11221 Compatible · 600 dpi" });
            rollProfile.SelectedIndex = config.PfRollProfile ? 1 : 0;
            rollProfile.SetBounds(180, 480, 476, 28);
            rollProfile.SelectedIndexChanged += delegate { UpdateDemoSettingsState(); };
            Controls.AddRange(new Control[] { profileLabel, rollProfile });
            testApiButton.Text = Loc.T("API-Verbindung prüfen");
            testApiButton.SetBounds(180, 206, 260, 36);
            testApiButton.Click += async delegate
            {
                if (searching || resetting || demoMode.Checked || !ApplySettings()) return;
                searching = true;
                UpdateDemoSettingsState();
                resetProgress.Visible = true;
                testApiButton.Text = Loc.T("Verbindung wird geprüft...");
                try
                {
                    var api = new ReservationClient(config);
                    string result = await Task.Run(() =>
                    {
                        var health = api.Health();
                        if (health == null || !health.ok) throw new InvalidOperationException(Loc.T("Der API-Dienst meldet sich nicht als bereit."));
                        var next = api.PeekNext();
                        if (next == null || next.asnNumber < 1 || String.IsNullOrWhiteSpace(next.barcodeText))
                            throw new InvalidOperationException(Loc.T("Die API liefert keine gültige nächste ASN."));
                        return Loc.T("API erreichbar, API-Key akzeptiert und nächste ASN abgefragt: ") + next.barcodeText +
                            Loc.T("\r\nEs wurde keine ASN reserviert und kein Druck ausgelöst.");
                    });
                    if (!IsDisposed) MessageBox.Show(this, result, Loc.T("API-Verbindung erfolgreich"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Loc.T("API-Verbindung fehlgeschlagen"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally
                {
                    searching = false;
                    if (!IsDisposed)
                    {
                        resetProgress.Visible = false;
                        testApiButton.Text = Loc.T("API-Verbindung prüfen");
                        UpdateDemoSettingsState();
                    }
                }
            };
            Controls.Add(testApiButton);
            var refreshLabel = new Label { Text = Loc.T("Refresh (s)"), TextAlign = ContentAlignment.MiddleLeft };
            refreshLabel.SetBounds(452, 206, 90, 36);
            refreshSeconds.SetBounds(544, 210, 72, 28);
            refreshSeconds.Value = config.RefreshMilliseconds / 1000;
            resetRefreshButton.SetBounds(624, 206, 32, 36);
            resetRefreshButton.Click += delegate { refreshSeconds.Value = 30; };
            settingsTips.SetToolTip(resetRefreshButton, resetRefreshButton.AccessibleName);
            refreshSeconds.ValueChanged += delegate { UpdateDemoSettingsState(); if (!testMode) ApplySettings(); };
            copyLabelButton.Text = Loc.T("Sekundärlabel…");
            copyLabelButton.SetBounds(392, 520, 264, 36);
            copyLabelButton.Click += delegate
            {
                if (searching || resetting || !ApplySettings()) return;
                using (var dialog = new CopyLabelForm(config)) dialog.ShowDialog(this);
            };
            Controls.AddRange(new Control[] { refreshLabel, refreshSeconds, resetRefreshButton, copyLabelButton });
            OrganizeSettings();
            foreach (Control control in SettingsControls(this))
                if (!(control is TabPage)) logicalBounds.Add(control, control.Bounds);
            LayoutSettings();
            Shown += delegate { LayoutSettings(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (resetting || searching || !ApplySettings()) { e.Cancel = true; return; }
                DialogResult = DialogResult.OK;
            };
            UpdateDemoSettingsState();
            if (!testMode) ScanBrotherDevices();
        }

        void AddSection(string text, int y)
        {
            var label = new Label { Text = text, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            label.SetBounds(22, y, 634, 26);
            Controls.Add(label);
        }

        static IEnumerable<Control> SettingsControls(Control parent)
        {
            foreach (Control child in parent.Controls) {
                yield return child;
                foreach (var nested in SettingsControls(child)) yield return nested;
            }
        }

        void OrganizeSettings()
        {
            var previousControls = Controls.Cast<Control>().ToArray();
            Controls.Clear();
            var heading = new Label { Text = Loc.T("Einstellungen"), Font = new Font("Segoe UI", 18, FontStyle.Bold) };
            heading.SetBounds(24, 18, 600, 38); Controls.Add(heading);
            settingsTabs.SetBounds(22, 70, 636, 406); Controls.Add(settingsTabs);
            settingsTabs.Multiline = true;
            foreach (string title in new[] { Loc.T("Allgemein"), Loc.T("Server"), Loc.T("Drucker"), Loc.T("Wartung"), Loc.T("Sekundärlabel"), Loc.T("Über") })
                settingsTabs.TabPages.Add(new TabPage(title) { BackColor = BackColor });
            var general = settingsTabs.TabPages[0]; var server = settingsTabs.TabPages[1];
            var printer = settingsTabs.TabPages[2]; var maintenance = settingsTabs.TabPages[3];
            var copies = settingsTabs.TabPages[4]; var about = settingsTabs.TabPages[5];
            Action<Control,Control,int,int,int,int> place = (parent, control, x, y, width, height) => {
                control.SetBounds(x,y,width,height); parent.Controls.Add(control);
            };
            Action<Control,string,int> label = (parent,text,y) => place(parent,new Label { Text = text, Font = new Font(Font, FontStyle.Bold) },20,y,170,28);
            label(general,Loc.T("Sprache"),24);
            languageChoice.Items.AddRange(new object[] { "Deutsch", "English" });
            languageChoice.SelectedIndex = config.Language == "en" ? 1 : 0;
            place(general,languageChoice,200,22,390,30);
            languageHint.Text = Loc.T("Die neue Sprache wird nach dem Schließen dieses Fensters angewendet.");
            place(general,languageHint,200,56,390,52);
            languageHint.Visible = false;
            languageChoice.SelectedIndexChanged += delegate {
                languageHint.Visible = (languageChoice.SelectedIndex == 1 ? "en" : "de") != Loc.Language;
                if (!testMode) ApplySettings();
            };
            label(general,Loc.T("Refresh (Sekunden)"),126);
            place(general,refreshSeconds,200,124,110,30); place(general,resetRefreshButton,320,122,36,36);
            place(general,demoMode,20,188,550,32);
            label(server,Loc.T("Serveradresse"),24); place(server,serverUrl,200,22,390,30);
            place(server,new Label { Text = Loc.T("z. B. https://asn.example.org"), ForeColor = Color.DimGray },200,56,390,26);
            label(server,"API-Key",106); place(server,apiKey,200,104,390,30);
            place(server,new Label { Text = Loc.T("Geschützt im Windows-Anmeldeinformationsspeicher"), ForeColor = Color.DimGray },200,138,390,48);
            place(server,testApiButton,200,202,390,38);
            place(printer,setupBpacButton,20,18,570,36); place(printer,bpacStatus,20,18,570,36);
            place(printer,deviceStatus,20,62,570,30);
            place(printer,deviceList,20,100,570,78);
            place(printer,findDeviceButton,20,190,276,38); place(printer,diagnoseButton,308,190,282,38);
            label(printer,Loc.T("Rollenprofil"),250); place(printer,rollProfile,200,246,390,30);
            place(printer,testLabelButton,308,304,282,38);
            var models = new Button { Text = ClientUpdates.Text("Unterstützte Drucker", "Supported printers") };
            models.Click += delegate { MessageBox.Show(this, PrinterBackends.ModelOverview(), ClientUpdates.Text("Druckermodelle", "Printer models"), MessageBoxButtons.OK, MessageBoxIcon.Information); };
            place(printer,models,20,304,276,38);
            place(maintenance,resetAsnButton,20,24,570,40);
            place(copies,copyLabelButton,20,24,570,40);
            place(copies,new Label { Text = Loc.T("Stellvertreterkarte (A4)"), Font = new Font(Font, FontStyle.Bold) },20,104,570,28);
            place(copies,new Label { Text = Loc.T("Coming Soon"), ForeColor = Color.DimGray },20,140,570,28);
            place(copies,new Button { Text = Loc.T("Stellvertreterkarte drucken"), Enabled = false },20,180,570,40);
            place(about,new Label { Text = "Paperless ASN Labeler", Font = new Font(Font.FontFamily,14,FontStyle.Bold) },20,20,570,32);
            place(about,new Label { Text = "Version " + AppRelease.Version },20,58,570,24);
            place(about,new Label { Text = Loc.T("ASN-Labels und Stellvertreterlabels für paperless-ngx mit Brother QL-800."), ForeColor = Color.DimGray },20,90,570,44);
            var author = new LinkLabel { Text = "nightmind.dev", AccessibleName = Loc.T("Autor: nightmind.dev") };
            author.LinkClicked += delegate {
                try { Process.Start(new ProcessStartInfo("https://nightmind.dev") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this,ex.Message,Loc.T("Link konnte nicht geöffnet werden"),MessageBoxButtons.OK,MessageBoxIcon.Error); }
            };
            place(about,author,20,144,570,28);
            place(about,new Label { Text = Loc.T("AGPLv3 mit zusätzlicher b-PAC-Erlaubnis. Ohne Gewährleistung."), ForeColor = Color.DimGray },20,184,570,40);
            var licenses = new Button { Text = Loc.T("Open-Source-Lizenzen") };
            licenses.Click += delegate { MessageBox.Show(this,ThirdPartyLicenses.Read(),Loc.T("Open-Source-Lizenzen"),MessageBoxButtons.OK,MessageBoxIcon.Information); };
            place(about,licenses,20,236,276,40);
            var updates = new Button { Text = ClientUpdates.Text("Nach Updates suchen", "Check for updates") };
            updates.Click += delegate { CheckUpdatesRequested = true; Close(); };
            place(about,updates,308,236,282,40);
            place(about,new Label { Text = Loc.T("Brother b-PAC und Druckertreiber werden separat installiert. Windows-Schriften werden nicht mitgeliefert."), ForeColor = Color.DimGray },20,292,570,52);
            resetProgress.SetBounds(24,488,632,4); Controls.Add(resetProgress);
            foreach (var control in previousControls) if (control.Parent == null) control.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) settingsTips.Dispose();
            base.Dispose(disposing);
        }

        bool ApplySettings()
        {
            if (resetting) return false;
            try
            {
                Uri uri;
                string url = serverUrl.Text.Trim();
                if (!demoMode.Checked && (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https")))
                    throw new InvalidOperationException(Loc.T("Bitte eine gültige Serveradresse mit http:// oder https:// eingeben."));
                var updated = new AppConfig
                {
                    ServerUrl = url, DevicePath = config.DevicePath, DemoMode = demoMode.Checked,
                    LastPrinted = config.LastPrinted, DemoLastPrinted = config.DemoLastPrinted,
                    DemoBlocked = config.DemoBlocked, DemoReusable = config.DemoReusable, DemoCopyCounts = config.DemoCopyCounts,
                    PrinterBackend = PrinterBackends.Brother,
                    PrinterName = deviceList.SelectedItem == null ? config.PrinterName : deviceList.SelectedItem.ToString(),
                    PfRollProfile = rollProfile.SelectedIndex == 1,
                    ShowCompleted = config.ShowCompleted,
                    BpacSetupPrompted = config.BpacSetupPrompted,
                    RefreshSeconds = (int)refreshSeconds.Value,
                    Language = languageChoice.SelectedIndex == 1 ? "en" : "de"
                };
                if (!testMode)
                {
                    if (apiKey.Text != config.ApiKey) CredentialStore.Write("AsnLabeler.ApiKey", apiKey.Text);
                    updated.Save();
                }
                config.ServerUrl = updated.ServerUrl;
                config.DevicePath = updated.DevicePath;
                config.PrinterBackend = updated.PrinterBackend;
                config.PrinterName = updated.PrinterName;
                config.PfRollProfile = updated.PfRollProfile;
                config.PfRightAligned = updated.PfRightAligned;
                config.PfOffsetMm = updated.PfOffsetMm;
                config.DemoMode = updated.DemoMode;
                config.RefreshSeconds = updated.RefreshSeconds;
                config.Language = updated.Language;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Loc.T("Einstellungen konnten nicht angewendet werden"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void LayoutSettings()
        {
            float scale;
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            SuspendLayout();
            ClientSize = new Size((int)Math.Ceiling(680 * scale), (int)Math.Ceiling(508 * scale));
            foreach (var item in logicalBounds)
            {
                var r = item.Value;
                item.Key.Bounds = Rectangle.FromLTRB((int)Math.Round(r.Left * scale), (int)Math.Round(r.Top * scale),
                    (int)Math.Round(r.Right * scale), (int)Math.Round(r.Bottom * scale));
            }
            ResumeLayout(false);
        }

        internal void SelectPrinterSetup() { settingsTabs.SelectedIndex = 2; }

        internal static List<string> ValidateLayout(string snapshotPath = null)
        {
            var failures = new List<string>();
            using (var form = new SettingsForm(new AppConfig { DemoMode = true, Language = Loc.Language }, true))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                foreach (bool demo in new[] { true, false })
                {
                    form.demoMode.Checked = demo;
                    form.LayoutSettings();
                    foreach (Control control in form.Controls)
                    {
                        if (!control.Visible) continue;
                        if (!form.ClientRectangle.Contains(control.Bounds)) failures.Add("Settings control outside window: " + control.Text);
                        foreach (Control other in form.Controls)
                            if (other != control && other.Visible && control.Bounds.IntersectsWith(other.Bounds))
                                failures.Add("Settings overlap: " + control.Text + " / " + other.Text);
                    }
                    if (snapshotPath != null)
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshotPath), Path.GetFileNameWithoutExtension(snapshotPath) + (demo ? "-settings-demo.png" : "-settings.png")), ImageFormat.Png);
                        }
                }
                if (form.AcceptButton != null || form.Controls.OfType<Button>().Any(b => b.Text == "Anwenden" || b.Text == "Speichern" || b.Text == Loc.T("Abbrechen")))
                    failures.Add("Settings still require a manual save action.");
                if (form.Controls.OfType<ComboBox>().Any(c => c.Items.Cast<object>().Any(i => i.ToString().Contains("WinUSB"))))
                    failures.Add("WinUSB is still offered in settings.");
                if (!SettingsControls(form).Contains(form.setupBpacButton)) failures.Add("b-PAC setup entry is missing.");
                for (int tab = 0; tab < form.settingsTabs.TabPages.Count; tab++)
                {
                    form.settingsTabs.SelectedIndex = tab; Application.DoEvents(); form.LayoutSettings();
                    foreach (Control control in SettingsControls(form.settingsTabs.SelectedTab)) {
                        if (!control.Visible) continue;
                        if (!control.Parent.ClientRectangle.Contains(control.Bounds)) failures.Add("Settings tab overflow: " + control.Text);
                        foreach (Control other in control.Parent.Controls)
                            if (other != control && other.Visible && control.Bounds.IntersectsWith(other.Bounds)) failures.Add("Settings tab overlap: " + control.Text);
                    }
                    if (snapshotPath != null) using (var bitmap = new Bitmap(form.Width, form.Height)) {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshotPath), Path.GetFileNameWithoutExtension(snapshotPath) + "-settings-" + tab + ".png"), ImageFormat.Png);
                    }
                }
                form.settingsTabs.SelectedIndex = 0;
                if (form.settingsTabs.TabPages.Count != 6) failures.Add("Expected six settings tabs.");
                if (form.copyLabelButton.Parent != form.settingsTabs.TabPages[4]) failures.Add("Secondary labels are not on their own tab.");
                if (form.languageHint.Visible) failures.Add("Language hint visible without a change.");
                int originalLanguage = form.languageChoice.SelectedIndex;
                form.languageChoice.SelectedIndex = originalLanguage == 0 ? 1 : 0;
                if (!form.languageHint.Visible) failures.Add("Language change hint missing.");
                form.LayoutSettings();
                if (!form.languageHint.Parent.ClientRectangle.Contains(form.languageHint.Bounds)) failures.Add("Language hint clipped.");
                form.languageChoice.SelectedIndex = originalLanguage;
                if (form.languageHint.Visible) failures.Add("Language hint persists after reverting.");
                if (!ThirdPartyLicenses.Read().Contains("Apache License")) failures.Add("Bundled licenses missing.");
                if (!ThirdPartyLicenses.Read().Contains("GNU AFFERO GENERAL PUBLIC LICENSE") ||
                    !ThirdPartyLicenses.Read().Contains("AGPL-3.0-only") ||
                    !ThirdPartyLicenses.Read().Contains("Additional permission")) failures.Add("Project license or b-PAC permission missing.");
                if (form.resetRefreshButton.Visible) failures.Add("Default refresh should not show reset.");
                form.refreshSeconds.Value = 45;
                if (!form.resetRefreshButton.Visible) failures.Add("Custom refresh should show reset.");
                form.resetRefreshButton.PerformClick();
                if (form.refreshSeconds.Value != 30 || form.resetRefreshButton.Visible) failures.Add("Refresh reset failed.");
                foreach (bool? available in new bool?[] { null, true, false })
                {
                    form.settingsTabs.SelectedIndex = 2;
                    form.bpacAvailable = available;
                    form.UpdateDemoSettingsState();
                    if (form.setupBpacButton.Visible != (available == false)) failures.Add("b-PAC setup visibility is incorrect.");
                    if (form.bpacStatus.Visible == form.setupBpacButton.Visible) failures.Add("b-PAC setup/status overlap.");
                }
                form.serverUrl.Text = "http://nas.test:9876";
                form.demoMode.Checked = true;
                form.Close();
                if (form.config.ServerUrl != "http://nas.test:9876" || !form.config.DemoMode || form.DialogResult != DialogResult.OK)
                    failures.Add("Closing settings did not apply pending edits.");
            }
            return failures;
        }

        void AddRow(string label, TextBox box, int y, string hint)
        {
            var l = new Label { Text = label };
            l.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
            l.SetBounds(22, y, 148, 26);
            box.SetBounds(180, y, 476, 28);
            var h = new Label { Text = hint };
            h.ForeColor = Color.FromArgb(90, 98, 112);
            h.Font = new Font("Segoe UI", 9);
            h.SetBounds(180, y + 30, 476, 22);
            Controls.Add(l);
            Controls.Add(box);
            Controls.Add(h);
        }

        async void ScanBrotherDevices()
        {
            if (searching) return;
            deviceList.Items.Clear();
            searching = true;
            deviceStatus.Text = Loc.T("Suche Drucker…");
            UpdateDemoSettingsState();
            try
            {
                string bpacError = null;
                try { await Task.Run(() => BpacPrinterSession.Probe()); bpacAvailable = true; }
                catch (Exception ex) { bpacAvailable = false; bpacError = ex.Message; }
                if (IsDisposed) return;
                UpdateDemoSettingsState();
                if (demoMode.Checked) return;
                {
                    var printers = await Task.Run(() => PrinterBackends.DiscoverBrother());
                    if (IsDisposed) return;
                    foreach (var printer in printers) deviceList.Items.Add(printer.Name);
                    int chosen = deviceList.Items.IndexOf(config.PrinterName);
                    if (chosen >= 0) deviceList.SelectedIndex = chosen;
                    else if (printers.Count == 1 && String.IsNullOrEmpty(config.PrinterName)) deviceList.SelectedIndex = 0;
                    deviceStatus.Text = printers.Count == 0 ? Loc.T("Kein lokaler QL-800-Treiber gefunden. Brother-Treiber prüfen.") : printers.Count + Loc.T(" Drucker gefunden.");
                    if (bpacError != null) deviceStatus.Text = bpacError;
                }
            }
            catch (Exception ex) { if (!IsDisposed) deviceStatus.Text = Loc.T("Suche fehlgeschlagen: ") + ex.Message; }
            finally
            {
                searching = false;
                if (!IsDisposed) { UpdateDemoSettingsState(); if (!testMode) ApplySettings(); }
            }
        }

        async void ResetAsnState()
        {
            if (!ApplySettings()) return;
            if (demoMode.Checked)
            {
                var demoFirst = MessageBox.Show(
                    this,
                    Loc.T("Dadurch wird nur der lokale Demo-Zähler zurückgesetzt. Die Reservierungs-API und paperless-ngx werden nicht benutzt."),
                    Loc.T("Demo-Zähler zurücksetzen?"),
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning
                );
                if (demoFirst != DialogResult.OK) return;

                var demoSecond = MessageBox.Show(
                    this,
                    Loc.T("Wirklich den Demo-Zähler auf ASN00001 zurücksetzen?"),
                    Loc.T("Bitte nochmal bestätigen"),
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning
                );
                if (demoSecond != DialogResult.OK) return;

                try
                {
                    config.DemoMode = true;
                    config.ResetDemoCounter();
                    config.Save();
                    ResetPerformed = true;
                    ResetNextBarcodeText = "ASN00001";
                    MessageBox.Show(this, Loc.T("Demo-Zähler zurückgesetzt.\n\nNächste ASN: ASN00001"), Loc.T("Demo-Modus"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Reset fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            var first = MessageBox.Show(
                this,
                Loc.T("Nicht gestartete Reservierungen werden mit paperless-ngx abgeglichen. Gedruckte, unklare und gesperrte ASNs bleiben geschützt. Halte andere Druck-Clients vorher an."),
                "ASN-Stand sicher abgleichen?",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning
            );
            if (first != DialogResult.OK) return;

            var second = MessageBox.Show(
                this,
                Loc.T("Wirklich alle noch nicht gestarteten Reservierungen beenden? Andere Clients müssen danach neu reservieren."),
                Loc.T("Bitte nochmal bestätigen"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning
            );
            if (second != DialogResult.OK) return;

            try
            {
                resetting = true;
                foreach (Control control in Controls) control.Enabled = false;
                resetProgress.Enabled = true;
                resetProgress.Visible = true;
                var result = await Task.Run(() => new ReservationClient(config).ResetToPaperless());
                if (!result.ok) throw new InvalidOperationException(Loc.T("API hat das Zurücksetzen nicht bestätigt."));
                ResetPerformed = true;
                config.DemoMode = false;
                config.Save();
                ResetPerformed = true;
                ResetNextBarcodeText = result.nextBarcodeText;
                MessageBox.Show(
                    this,
                    "Sicher abgeglichen.\n\npaperless-Maximum: " + result.paperlessMax +
                    "\nFreigegebene Reservierungen: " + result.releasedReservations +
                    Loc.T("\nGeschützte lokale Einträge oberhalb Paperless: ") + result.retainedReservations +
                    Loc.T("\nNächste ASN: ") + result.nextBarcodeText,
                    "ASN-Stand",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Reset fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                resetting = false;
                foreach (Control control in Controls) control.Enabled = true;
                resetProgress.Visible = false;
                UpdateDemoSettingsState();
            }
        }

        void UpdateDemoSettingsState()
        {
            bool realMode = !demoMode.Checked;
            rollProfile.Enabled = realMode && !searching && !resetting;
            serverUrl.Enabled = realMode && !searching && !resetting;
            apiKey.Enabled = realMode && !searching && !resetting;
            testApiButton.Enabled = realMode && !searching && !resetting;
            refreshSeconds.Enabled = !searching && !resetting;
            languageChoice.Enabled = !searching && !resetting;
            resetRefreshButton.Visible = refreshSeconds.Value != 30;
            resetRefreshButton.Enabled = !searching && !resetting;
            copyLabelButton.Enabled = !searching && !resetting;
            deviceList.Visible = true;
            deviceList.Enabled = realMode && !searching;
            setupBpacButton.Enabled = realMode && !searching && !resetting;
            setupBpacButton.Visible = bpacAvailable == false;
            bpacStatus.Visible = bpacAvailable != false;
            bpacStatus.Text = bpacAvailable == true ? Loc.T("b-PAC 64 Bit bereit") : Loc.T("b-PAC wird geprüft…");
            findDeviceButton.Enabled = realMode && !searching;
            diagnoseButton.Enabled = realMode && !searching;
            testLabelButton.Enabled = realMode && !searching;
            demoMode.Enabled = !searching;
            resetAsnButton.Enabled = !searching;
            resetAsnButton.Text = demoMode.Checked ? Loc.T("Demo-Zähler zurücksetzen") : Loc.T("ASN-Stand abgleichen");
            if (demoMode.Checked)
            {
                deviceStatus.Text = Loc.T("Demo aktiv: kein USB-Zugriff.");
            }
            else if (deviceStatus.Text.StartsWith("Demo"))
            {
                deviceStatus.Text = Loc.T("Noch nicht gesucht.");
            }
        }
    }

    sealed class DocumentStatusResult
    {
        public DocumentStatusItem[] items { get; set; }
        public int total { get; set; }
        public int openCount { get; set; }
        public int pending { get; set; }
        public int inPrint { get; set; }
        public int uncertain { get; set; }
        public int imported { get; set; }
        public int paperlessMax { get; set; }
        public int nextAsnNumber { get; set; }
        public int gap { get; set; }
    }

    sealed class DocumentStatusItem
    {
        public string reservationId { get; set; }
        public string barcodeText { get; set; }
        public string status { get; set; }
        public long updatedAt { get; set; }
        public string documentTitle { get; set; }
        public int secondaryLabelCount { get; set; }
    }

    sealed class ResolveResult
    {
        public string status { get; set; }
    }

    sealed class DocumentStatusForm : Form
    {
        readonly ReservationClient client;
        readonly AppConfig config;
        readonly Label heading = new Label();
        readonly Label summary = new Label();
        readonly Label numbers = new Label();
        readonly Label message = new Label();
        readonly Label pageLabel = new Label();
        readonly CheckBox showCompleted = new CheckBox { Text = Loc.T("Erledigte anzeigen") };
        readonly Button refresh = new MaterialIconButton("\ue5d5") { AccessibleName = Loc.T("Status aktualisieren") };
        readonly Button previous = new MaterialIconButton("\ue5cb") { AccessibleName = Loc.T("Vorherige Seite") };
        readonly Button next = new MaterialIconButton("\ue5cc") { AccessibleName = Loc.T("Nächste Seite") };
        readonly DataGridView grid = new DataGridView();
        readonly ProgressBar progress = new ProgressBar { Style = ProgressBarStyle.Marquee };
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly ToolTip tips = new ToolTip();
        int page = 1;
        int total;
        bool loading;
        string lastRows;

        public DocumentStatusForm(ReservationClient client, AppConfig config)
        {
            this.client = client;
            this.config = config;
            timer.Interval = config.RefreshMilliseconds;
            Text = config.DemoMode ? Loc.T("Dokumentstatus · Demo") : Loc.T("Dokumentstatus");
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 540);
            BackColor = config.DemoMode ? Color.FromArgb(255, 251, 235) : Color.FromArgb(243, 246, 250);
            heading.Text = Loc.T("Dokumente");
            heading.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            summary.Text = Loc.T("Abgleich mit Paperless läuft…");
            foreach (var button in new[] { refresh, previous, next }) button.Font = new Font("Segoe UI", 16);
            tips.SetToolTip(refresh, Loc.T("Status aktualisieren"));
            tips.SetToolTip(previous, Loc.T("Vorherige Seite"));
            tips.SetToolTip(next, Loc.T("Nächste Seite"));
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ScrollBars = ScrollBars.None;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(229, 231, 235);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            foreach (string name in new[] { "ASN", "Status", Loc.T("Aktualisiert"), Loc.T("Dokumenttitel") })
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns[0].FillWeight = 19;
            grid.Columns[1].FillWeight = 25;
            grid.Columns[2].FillWeight = 23;
            grid.Columns[3].FillWeight = 33;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("Aktion"), FillWeight = 15, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("Kopien"), FillWeight = 10, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.CellContentClick += ResolveClicked;
            message.AutoEllipsis = true;
            message.ForeColor = Color.FromArgb(75, 85, 99);
            Controls.AddRange(new Control[] { heading, summary, numbers, message, pageLabel, refresh, previous, next, grid, progress, showCompleted });
            showCompleted.Checked = config.ShowCompleted;
            showCompleted.CheckedChanged += delegate
            {
                config.ShowCompleted = showCompleted.Checked;
                try { config.Save(); }
                catch (Exception ex) { MessageBox.Show(this, Loc.T("Ansicht konnte nicht gespeichert werden: ") + ex.Message, Loc.T("Einstellungen"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                page = 1; RefreshStatus();
            };
            refresh.Click += delegate { RefreshStatus(); };
            previous.Click += delegate { if (page > 1) { page--; RefreshStatus(); } };
            next.Click += delegate { if (page * 10 < total) { page++; RefreshStatus(); } };
            timer.Tick += delegate { if (ContainsFocus && WindowState != FormWindowState.Minimized) RefreshStatus(); };
            Resize += delegate { LayoutStatus(); };
            Shown += delegate { LayoutStatus(); RefreshStatus(); timer.Start(); };
            LayoutStatus();
        }

        void LayoutStatus()
        {
            float s;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            MinimumSize = SizeFromClientSize(new Size((int)(800 * s), (int)(540 * s)));
            int w = (int)(ClientSize.Width / s), h = (int)(ClientSize.Height / s);
            Place(heading, 22, 14, w - 310, 38, s);
            Place(showCompleted, w - 260, 22, 190, 30, s);
            Place(refresh, w - 62, 18, 40, 36, s);
            Place(summary, 22, 65, w - 44, 26, s);
            Place(numbers, 22, 94, w - 44, 26, s);
            Place(grid, 22, 132, w - 44, h - 218, s);
            grid.ColumnHeadersHeight = (int)(32 * s);
            grid.RowTemplate.Height = (int)(28 * s);
            foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
            Place(message, 22, h - 78, w - 235, 60, s);
            Place(previous, w - 184, h - 65, 36, 36, s);
            Place(pageLabel, w - 142, h - 60, 80, 28, s);
            Place(next, w - 58, h - 65, 36, 36, s);
            Place(progress, 22, h - 9, w - 44, 4, s);
        }

        static void Place(Control c, int x, int y, int w, int h, float s)
        { c.SetBounds((int)(x * s), (int)(y * s), (int)(w * s), (int)(h * s)); }

        async void RefreshStatus()
        {
            if (loading) return;
            loading = true;
            refresh.Enabled = previous.Enabled = next.Enabled = showCompleted.Enabled = false;
            progress.Visible = true;
            try
            {
                int requestedPage = page;
                bool includeCompleted = showCompleted.Checked;
                var data = config.DemoMode ? DemoStatus(config, page, includeCompleted) : await Task.Run(() => client.DocumentStatus(requestedPage, includeCompleted));
                if (IsDisposed) return;
                total = data.total;
                if (page > Math.Max(1, (total + 9) / 10))
                {
                    page = Math.Max(1, (total + 9) / 10);
                    requestedPage = page;
                    data = config.DemoMode ? DemoStatus(config, page, includeCompleted) : await Task.Run(() => client.DocumentStatus(requestedPage, includeCompleted));
                    if (IsDisposed) return;
                    total = data.total;
                }
                string rowsSignature = new JavaScriptSerializer().Serialize(data.items);
                var selectedItem = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as DocumentStatusItem;
                string selectedId = selectedItem == null ? null : selectedItem.reservationId;
                if (lastRows != rowsSignature)
                {
                grid.Rows.Clear();
                foreach (var item in data.items ?? new DocumentStatusItem[0])
                {
                    string state = item.status == "pending" ? Loc.T("Scan ausstehend") : item.status == "in_print" ? Loc.T("In Druck") : item.status == "imported" ? Loc.T("In Paperless") : item.status == "uncertain" ? Loc.T("Druck unklar") : item.status == "blocked" ? Loc.T("Gesperrt") : Loc.T("Nicht gedruckt");
                    string updated = item.updatedAt == 0 ? "–" : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(item.updatedAt).ToLocalTime().ToString("dd.MM.yy HH:mm");
                    int row = grid.Rows.Add(item.barcodeText, state, updated, item.documentTitle, "", item.secondaryLabelCount > 0 ? item.secondaryLabelCount.ToString() : "");
                    grid.Rows[row].Cells[5].ToolTipText = CopyReceipts.Tooltip(item.secondaryLabelCount);
                    grid.Rows[row].Tag = item;
                    if (selectedId != null && item.reservationId == selectedId) grid.CurrentCell = grid.Rows[row].Cells[0];
                    if (item.status == "uncertain") grid.Rows[row].Cells[4] = new DataGridViewButtonCell { Value = Loc.T("Beheben") };
                    if (item.status == "pending") grid.Rows[row].Cells[4] = new DataGridViewButtonCell { Value = Loc.T("Abbrechen") };
                    grid.Rows[row].Cells[1].ToolTipText = item.status == "in_print" ? Loc.T("Druckauftrag läuft. Beheben ist bis zum Ablauf der Bestätigungsfrist gesperrt.") : item.status == "pending"
                        ? Loc.T("Druck bestätigt, aber diese ASN wurde in Paperless noch nicht gefunden. Dokument scannen und importieren.")
                        : item.status == "imported"
                        ? Loc.T("Diese ASN wurde in Paperless gefunden. Der Eintrag ist erledigt; die Nummer bleibt gesperrt.")
                        : item.status == "uncertain"
                        ? Loc.T("Druck gestartet, aber nicht bestätigt. Label physisch prüfen. Ein vorhandenes Label scannen; bei fehlendem Label eine neue ASN verwenden.")
                        : item.status == "blocked" ? Loc.T("Gesperrt. Eine sichere Einzelfreigabe kann im Adminpanel geprüft werden.")
                        : Loc.T("Reserviert, aber kein bestätigter Druckstart. Die ASN bleibt vorsichtshalber gesperrt.");
                    grid.Rows[row].Cells[1].Style.ForeColor = item.status == "pending" ? Color.FromArgb(154, 52, 18) : item.status == "imported" ? Color.FromArgb(6, 95, 70) : Color.FromArgb(75, 85, 99);
                }
                lastRows = rowsSignature;
                }
                summary.Text = data.inPrint + Loc.T(" im Druck   ·   ") + data.pending + Loc.T(" ausstehend   ·   ") + data.uncertain + Loc.T(" unklar   ·   ") + data.imported + Loc.T(" in Paperless");
                numbers.Text = Loc.T("Paperless-Maximum: ") + data.paperlessMax + Loc.T("   ·   Nächste ASN: ") + data.nextAsnNumber + Loc.T("   ·   Nummerndifferenz: ") + data.gap;
                message.Text = total == 0 ? (includeCompleted ? Loc.T("Keine Einträge vorhanden.") : Loc.T("Keine offenen Einträge.")) : (config.DemoMode ? Loc.T("Demo: simulierte Druckhistorie") : Loc.T("Stand: ") + DateTime.Now.ToString("HH:mm:ss"));
                pageLabel.Text = page + " / " + Math.Max(1, (total + 9) / 10);
                LayoutStatus();
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    summary.Text = Loc.T("Abgleich nicht möglich · Daten nicht aktuell");
                    message.Text = Loc.T("API/Paperless-Verbindung prüfen; für diese Ansicht ist der aktualisierte Dienst nötig. ") + ex.Message;
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    loading = false;
                    progress.Visible = false;
                    refresh.Enabled = showCompleted.Enabled = true;
                    previous.Enabled = page > 1;
                    next.Enabled = page * 10 < total;
                }
            }
        }

        async void ResolveClicked(object sender, DataGridViewCellEventArgs e)
        {
            timer.Stop();
            try { await ResolveDocument(e); }
            finally { if (!IsDisposed) timer.Start(); }
        }

        async Task ResolveDocument(DataGridViewCellEventArgs e)
        {
            if (loading || e.RowIndex < 0 || e.ColumnIndex != 4) return;
            var item = grid.Rows[e.RowIndex].Tag as DocumentStatusItem;
            if (item == null || (item.status != "uncertain" && item.status != "pending")) return;
            bool cancel = item.status == "pending";
            var answer = cancel ? DialogResult.No : MessageBox.Show(this, Loc.T("Existiert ein verwendbares Label mit ") + item.barcodeText + "?", Loc.T("Druck klären"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.No && MessageBox.Show(this,
                cancel ? item.barcodeText + Loc.T(" abbrechen? Stoppe den zugehörigen Druck-Client und entferne und vernichte alle Labels mit dieser ASN zuerst. Falls keine höhere ASN existiert, kann diese Nummer erneut gedruckt werden.")
                    : Loc.T("Stoppe zuerst den zugehörigen Druck-Client und prüfe Drucker und Warteschlange. Bestätige, dass kein Label mit dieser ASN existiert und kein Auftrag mehr drucken kann. Ein übersehenes Label könnte doppelte ASNs verursachen."),
                Loc.T("Abbruch / Freigabe bestätigen"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            loading = true;
            grid.Enabled = refresh.Enabled = previous.Enabled = next.Enabled = showCompleted.Enabled = false;
            progress.Visible = true;
            try
            {
                var result = config.DemoMode ? ResolveDemo(config, item, false) : await Task.Run(() => client.ResolveDocument(item.reservationId, answer == DialogResult.Yes, cancel, false));
                if (IsDisposed) return;
                if (result.status == "confirmation_required")
                {
                    if (MessageBox.Show(this, Loc.T("Es gibt höhere offene ASNs, aber keine höhere ASN in Paperless. Wenn du fortfährst, wird diese Nummer gesperrt und nicht erneut gedruckt. Wirklich fortfahren?"),
                        Loc.T("ASN sperren?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    result = config.DemoMode ? ResolveDemo(config, item, true) : await Task.Run(() => client.ResolveDocument(item.reservationId, answer == DialogResult.Yes, cancel, true));
                    if (IsDisposed) return;
                }
                if (config.DemoMode) config.Save();
                MessageBox.Show(this, result.status == "blocked" ? Loc.T("Die ASN ist gesperrt, weil eine höhere offene oder bereits erfasste ASN existiert.")
                    : result.status == "next" ? Loc.T("Die Nummer wird bei der nächsten Vergabe wieder berücksichtigt.")
                    : result.status == "imported" ? Loc.T("Das Dokument ist bereits in Paperless erfasst.") : Loc.T("Der Eintrag wartet jetzt auf den Scan."), Loc.T("Druck geklärt"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, Loc.T("Ergebnis nicht bestätigt. Bitte den Status erneut abgleichen. ") + ex.Message, Loc.T("Abgleich fehlgeschlagen"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally
            {
                if (!IsDisposed) { loading = false; grid.Enabled = true; RefreshStatus(); }
            }
        }

        internal static ResolveResult ResolveDemo(AppConfig config, DocumentStatusItem item, bool confirmed)
        {
            int number = AsnNumber.Parse(item.barcodeText), maximum = AsnNumber.Parse(config.DemoLastPrinted);
            if (number < 1 || number > maximum || config.DemoBlocked.Contains(number) || config.DemoReusable.Contains(number)) throw new InvalidOperationException(Loc.T("Eintrag wurde bereits geändert."));
            bool higher = Enumerable.Range(number + 1, maximum - number).Any(n => !config.DemoBlocked.Contains(n) && !config.DemoReusable.Contains(n));
            if (higher && !confirmed) return new ResolveResult { status = "confirmation_required" };
            if (higher) { config.DemoBlocked.Add(number); return new ResolveResult { status = "blocked" }; }
            config.DemoReusable.Add(number);
            return new ResolveResult { status = "next" };
        }

        internal static DocumentStatusResult DemoStatus(AppConfig config, int page, bool includeCompleted = false)
        {
            int count = AsnNumber.Parse(config.DemoLastPrinted);
            var numbers = Enumerable.Range(1, count).Reverse().Where(n => !config.DemoReusable.Contains(n) && (includeCompleted || !config.DemoBlocked.Contains(n))).ToArray();
            int pending = count - config.DemoBlocked.Count(n => n <= count) - config.DemoReusable.Count(n => n <= count);
            return new DocumentStatusResult { total = numbers.Length, pending = pending, openCount = pending, nextAsnNumber = config.NextDemoAsn(), gap = config.NextDemoAsn() - 1,
                items = numbers.Skip((page - 1) * 10).Take(10)
                    .Select(n => new DocumentStatusItem { barcodeText = AsnNumber.Format(n), status = config.DemoBlocked.Contains(n) ? "blocked" : "pending", documentTitle = "" }).ToArray() };
        }

        internal static void RenderSnapshot(string path)
        {
            var demo = new AppConfig { DemoMode = true, DemoLastPrinted = "ASN00013" };
            using (var form = new DocumentStatusForm(new ReservationClient(demo), demo))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                form.grid.Rows[0].Cells[1].Value = Loc.T("Druck unklar");
                form.grid.Rows[0].Cells[4] = new DataGridViewButtonCell { Value = Loc.T("Beheben") };
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-documents.png"), ImageFormat.Png);
                }
            }
        }

        internal static List<string> ValidateForTests()
        {
            var failures = new List<string>();
            var cancellation = new AppConfig { DemoMode = true, DemoLastPrinted = "ASN00003" };
            var first = new DocumentStatusItem { barcodeText = "ASN00001" };
            if (ResolveDemo(cancellation, first, false).status != "confirmation_required" || cancellation.DemoBlocked.Count != 0)
                failures.Add("Demo cancellation changed state without confirmation.");
            if (ResolveDemo(cancellation, first, true).status != "blocked" || DemoStatus(cancellation, 1).openCount != 2)
                failures.Add("Demo cancellation did not block/remove open count.");
            if (ResolveDemo(cancellation, new DocumentStatusItem { barcodeText = "ASN00003" }, false).status != "next" || cancellation.NextDemoAsn() != 3)
                failures.Add("Demo cancellation did not free next ASN.");
            cancellation.ResetDemoCounter();
            if (cancellation.NextDemoAsn() != 1 || cancellation.DemoBlocked.Count != 0 || cancellation.DemoReusable.Count != 0)
                failures.Add("Demo reset retained cancellation state.");
            var config = new AppConfig { DemoMode = true, DemoLastPrinted = "ASN00013", LastPrinted = "ASN99999" };
            using (var form = new DocumentStatusForm(new ReservationClient(config), config))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                if (form.grid.Rows.Count != 10 || !form.next.Enabled || form.total != 13)
                    failures.Add("Document status demo/page count is incorrect.");
                foreach (Control child in form.Controls)
                    if (child.Visible && !form.ClientRectangle.Contains(child.Bounds)) failures.Add("Document status control out of bounds.");
                if (form.grid.ColumnHeadersHeight + form.grid.Rows.Count * form.grid.RowTemplate.Height > form.grid.Height)
                    failures.Add("Document rows do not fit without scrolling.");
                form.next.PerformClick();
                if (form.grid.Rows.Count != 3 || form.next.Enabled || !form.previous.Enabled)
                    failures.Add("Document pagination failed.");
                config.ResetDemoCounter();
                form.refresh.PerformClick();
                if (form.total != 0 || form.page != 1 || form.grid.Rows.Count != 0)
                    failures.Add("Document status did not recover from a reset on page 2.");
            }
            return failures;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
    }

    sealed class SettingsSnapshot
    {
        readonly string server, key, printer, backend;
        readonly bool demo, profile;
        public SettingsSnapshot(AppConfig config, string apiKey = null)
        {
            server = config.ServerUrl.Trim().TrimEnd('/'); key = apiKey ?? config.ApiKey;
            printer = config.PrinterName; backend = config.PrinterBackend;
            demo = config.DemoMode; profile = config.PfRollProfile;
        }
        public bool ServerChanged(AppConfig config, string apiKey = null)
        { return server != config.ServerUrl.Trim().TrimEnd('/') || key != (apiKey ?? config.ApiKey) || demo != config.DemoMode; }
        public bool PrinterChanged(AppConfig config)
        { return printer != config.PrinterName || backend != config.PrinterBackend || profile != config.PfRollProfile || demo != config.DemoMode; }
    }

    sealed class AppConfig
    {
        public bool BpacSetupPrompted;
        public string ServerUrl = "http://nas.local:8765";
        public string DevicePath = "";
        public string PrinterBackend = PrinterBackends.Brother;
        public string PrinterName = "";
        public bool PfRollProfile = false;
        public bool PfRightAligned = false;
        public decimal PfOffsetMm = 2.0m;
        // Legacy field names remain readable, but old offsets are never applied.
        public bool UsePfRightAligned { get { return PrinterBackend == PrinterBackends.Brother && PfRollProfile; } }
        public double RollCorrectionMm { get { return 0; } }
        public string LastPrinted = "-";
        public string DemoLastPrinted = "-";
        public List<int> DemoBlocked = new List<int>();
        public List<int> DemoReusable = new List<int>();
        public Dictionary<string, int> DemoCopyCounts = new Dictionary<string, int>();
        public bool DemoMode = false;
        public bool ShowCompleted = false;
        public int RefreshSeconds = 30;
        public string Language = "de";
        [ScriptIgnore] public int RefreshMilliseconds { get { return (RefreshSeconds >= 10 && RefreshSeconds <= 3600 ? RefreshSeconds : 30) * 1000; } }
        [ScriptIgnore] public bool LegacyPrinterMigrated;

        internal void NormalizePrinterBackend()
        {
            if (PrinterBackend != PrinterBackends.Brother)
            {
                LegacyPrinterMigrated = true;
                PrinterBackend = PrinterBackends.Brother;
            }
        }

        public int NextDemoAsn() { return DemoReusable.Count > 0 ? DemoReusable.Min() : AsnNumber.Parse(DemoLastPrinted) + 1; }
        public void ResetDemoCounter() { DemoLastPrinted = "-"; DemoBlocked.Clear(); DemoReusable.Clear(); }

        static string ConfigPath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsnLabeler");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        static string LegacyConfigPath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsnLabeler");
                return Path.Combine(dir, "config.json");
            }
        }

        public static AppConfig Load()
        {
            try
            {
                string path = File.Exists(ConfigPath) ? ConfigPath : LegacyConfigPath;
                if (!File.Exists(path)) return new AppConfig();
                var loaded = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
                loaded.NormalizePrinterBackend();
                return loaded;
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save()
        {
            var data = new Dictionary<string, object>();
            data["ServerUrl"] = ServerUrl;
            data["DevicePath"] = DevicePath;
            data["PrinterBackend"] = PrinterBackend;
            data["PrinterName"] = PrinterName;
            data["PfRollProfile"] = PfRollProfile;
            data["LastPrinted"] = LastPrinted;
            data["DemoLastPrinted"] = DemoLastPrinted;
            data["DemoBlocked"] = DemoBlocked;
            data["DemoReusable"] = DemoReusable;
            data["DemoCopyCounts"] = DemoCopyCounts;
            data["DemoMode"] = DemoMode;
            data["ShowCompleted"] = ShowCompleted;
            data["RefreshSeconds"] = RefreshMilliseconds / 1000;
            data["Language"] = Language == "en" ? "en" : "de";
            data["BpacSetupPrompted"] = BpacSetupPrompted;
            File.WriteAllText(ConfigPath, new JavaScriptSerializer().Serialize(data));
        }

        [ScriptIgnore]
        public string ApiKey
        {
            get { return CredentialStore.Read("AsnLabeler.ApiKey") ?? ""; }
        }
    }

    sealed class ReservationClient
    {
        readonly AppConfig config;
        readonly string apiKeyOverride;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public ReservationClient(AppConfig config, string apiKey = null)
        {
            this.config = config;
            apiKeyOverride = apiKey;
        }

        public Reservation Next()
        {
            return Request<Reservation>("POST", "/api/reservations/next");
        }

        public Reservation PeekNext()
        {
            return Request<Reservation>("GET", "/api/reservations/next");
        }

        public Health Health()
        {
            var health = Request<Health>("GET", "/health", false);
            ValidateCompatibility(health);
            return health;
        }

        internal static void ValidateCompatibility(Health health, bool secondaryLabels = false)
        {
            if (health == null || !health.ok) throw new InvalidOperationException(Loc.T("Serverantwort war unklar."));
            if (health.apiVersion > 1 || health.apiVersion < 0)
                throw new InvalidOperationException(Loc.T("Diese Server-API wird vom Client nicht unterstützt. Bitte den Client aktualisieren."));
            Version minimum;
            if (!string.IsNullOrEmpty(health.minimumClientVersion) &&
                (!Version.TryParse(health.minimumClientVersion, out minimum) || new Version(AppRelease.NumericVersion) < minimum))
            {
                Volatile.Write(ref ClientRuntime.RequiredVersion, health.minimumClientVersion);
                throw new InvalidOperationException(Loc.T("Veralteter Client. Bitte den Client aktualisieren."));
            }
            Volatile.Write(ref ClientRuntime.RequiredVersion, null);
            if (secondaryLabels && (health.capabilities == null || !health.capabilities.Contains("secondary-label-tracking-v1")))
                throw new InvalidOperationException(Loc.T("Bitte zuerst den Reservierungsdienst auf Version 1.4.3 oder neuer aktualisieren. Kein Sekundärlabel gedruckt."));
        }

        public DocumentStatusResult DocumentStatus(int page, bool includeCompleted)
        {
            return Request<DocumentStatusResult>("GET", "/api/documents/status?page=" + page + "&include_completed=" + (includeCompleted ? "true" : "false"));
        }

        public CopyDocumentList CopyDocuments(int page = 1, int asn = 0)
        {
            return Request<CopyDocumentList>("GET", "/api/documents/copy-labels?" + (asn > 0 ? "asn=" + asn : "page=" + page));
        }

        public void PrepareCopy(string id, CopyDocument document)
        {
            ValidateCompatibility(Health(), true);
            Request<object>("POST", "/api/documents/copy-labels/start", true,
                new { printId = id, documentId = document.documentId, asnNumber = document.asnNumber });
        }

        public void ConfirmCopy(string id)
        { Request<object>("POST", "/api/documents/copy-labels/printed", true, new { printId = id }); }

        public ResolveResult ResolveDocument(string id, bool exists, bool cancel, bool confirmBlock)
        {
            string action = cancel ? "cancel" : exists ? "resolve-exists" : "resolve-missing";
            return Request<ResolveResult>("POST", "/api/reservations/" + Uri.EscapeDataString(id) + "/" + action + (confirmBlock ? "-confirmed" : ""),
                true, new { physicalLabelAbsent = !exists || cancel });
        }

        public void MarkPrinting(string id)
        {
            Request<object>("POST", "/api/reservations/" + Uri.EscapeDataString(id) + "/printing");
        }

        public void MarkPrinted(string id)
        {
            Request<object>("POST", "/api/reservations/" + Uri.EscapeDataString(id) + "/printed");
        }

        public void Release(string id)
        {
            Request<object>("POST", "/api/reservations/" + Uri.EscapeDataString(id) + "/release");
        }

        public ResetResult ResetToPaperless()
        {
            var health = Health();
            if (health.capabilities == null || !health.capabilities.Contains("safe-sync-v1"))
                throw new InvalidOperationException(Loc.T("Bitte zuerst den Reservierungsdienst auf Version 0.5.0 aktualisieren. Der alte Reset ist nicht sicher."));
            return Request<ResetResult>("POST", "/api/admin/reset-to-paperless");
        }

        T Request<T>(string method, string path)
        {
            return Request<T>(method, path, true);
        }

        T Request<T>(string method, string path, bool requireAuth, object payload = null)
        {
            if (requireAuth) ClientRuntime.RequireInstallation();
            if (string.IsNullOrWhiteSpace(config.ServerUrl)) throw new InvalidOperationException(Loc.T("Server URL fehlt."));
            string apiKey = apiKeyOverride ?? config.ApiKey;
            if (requireAuth && string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException(Loc.T("API-Key fehlt."));

            var url = config.ServerUrl.TrimEnd('/') + path;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 10000;
            request.AllowAutoRedirect = false;
            request.Headers["X-ASN-Client-Version"] = AppRelease.Version;
            request.Headers["X-ASN-API-Version"] = "1";
            if (requireAuth) request.Headers["Authorization"] = "Bearer " + apiKey;
            if (method == "POST")
            {
                byte[] bytes = payload == null ? new byte[0] : Encoding.UTF8.GetBytes(json.Serialize(payload));
                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;
                if (bytes.Length > 0)
                    using (var output = request.GetRequestStream()) output.Write(bytes, 0, bytes.Length);
            }
            try
            {
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                var body = reader.ReadToEnd();
                if (typeof(T) == typeof(object)) return default(T);
                return json.Deserialize<T>(body);
            }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null) throw;
                using (response)
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    ApiError error = null;
                    try { error = json.Deserialize<ApiError>(reader.ReadToEnd()); } catch (ArgumentException) { } catch (InvalidOperationException) { }
                    if (error != null && error.errorCode == "client_update_required")
                    {
                        Volatile.Write(ref ClientRuntime.RequiredVersion, error.minimumClientVersion ?? "?");
                        throw new InvalidOperationException(ClientUpdates.Text("Update erforderlich. Mindestversion: ", "Update required. Minimum version: ") + ClientRuntime.RequiredVersion, ex);
                    }
                    throw new InvalidOperationException("API HTTP " + (int)response.StatusCode + ": " +
                        (error == null ? Loc.T("Anfrage nicht bestätigt.") : Loc.ApiError(error.errorCode, error.error)) +
                        (error == null || String.IsNullOrEmpty(error.requestId) ? "" : "\nRequest-ID: " + error.requestId), ex);
                }
            }
        }
    }

    sealed class ApiError
    {
        public string minimumClientVersion { get; set; }
        public string errorCode { get; set; }
        public string error { get; set; }
        public string requestId { get; set; }
    }

    sealed class Reservation
    {
        public string reservationId { get; set; }
        public int asnNumber { get; set; }
        public string barcodeText { get; set; }

    }

    sealed class Health
    {
        public int apiVersion { get; set; }
        public string minimumClientVersion { get; set; }
        public string[] capabilities { get; set; }
        public bool ok { get; set; }
        public int time { get; set; }

    }

    sealed class ResetResult
    {
        public int releasedReservations { get; set; }
        public int retainedReservations { get; set; }
        public bool ok { get; set; }
        public int paperlessMax { get; set; }
        public int deletedReservations { get; set; }
        public int nextAsnNumber { get; set; }
        public string nextBarcodeText { get; set; }

    }

    sealed class Ql800Printer
    {
        readonly AppConfig config;

        public Ql800Printer(AppConfig config)
        {
            this.config = config;
        }

        public PrinterStatus ReadStatus(UsbDevice device)
        {
            device.Write(new byte[] { 0x1B, 0x69, 0x53 });
            for (int i = 0; i < 10; i++)
            {
                var status = device.Read(32);
                if (!PrinterStatus.ValidFrame(status) || status[18] == 0 || status[18] == 2 || status[18] == 4)
                    return PrinterStatus.Parse(status);
            }
            throw new InvalidOperationException(Loc.T("Keine aktuelle Statusantwort vom Drucker."));
        }

        public void Print(UsbDevice device, string text, MediaInfo media)
        {
            using (var image = LabelRenderer.Render(text, media))
            {
                var commands = BrotherRaster.Build(image, media);
                device.Write(commands);
            }
            VerifyAfterPrint(device);
        }

        public void PrintPrepared(UsbDevice device, byte[] commands)
        {
            device.Write(commands);
            VerifyAfterPrint(device);
        }

        void VerifyAfterPrint(UsbDevice device)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(60))
            {
                try
                {
                    // Brother emits completion itself. Never send ESC i S during printing.
                    if (ConfirmsCompletion(device.Read(32))) return;
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode != 121) throw;
                }
            }
            throw new InvalidOperationException(Loc.T("Keine Druckbestätigung innerhalb von 60 Sekunden. ASN bleibt gesperrt; Label am Drucker kontrollieren."));
        }

        internal static bool ConfirmsCompletion(byte[] frame)
        {
            if (!PrinterStatus.ValidFrame(frame)) throw new InvalidOperationException(Loc.T("Ungültige Brother-Statusantwort."));
            if (frame[8] != 0 || frame[9] != 0 || frame[18] == 2 || frame[18] == 4)
                throw new InvalidOperationException("Druck fehlgeschlagen: Brother meldet Fehler " + frame[8].ToString("X2") + "/" + frame[9].ToString("X2") + ". ASN bleibt gesperrt.");
            return frame[18] == 1;
        }
    }

    enum MediaKind
    {
        None,
        Continuous,
        DieCut
    }

    sealed class MediaInfo
    {
        const float DotsPerMm = 300f / 25.4f;

        public MediaKind Kind { get; private set; }
        public int WidthMm { get; private set; }
        public int LengthMm { get; private set; }
        public int RenderLengthMm { get; private set; }
        Rectangle? driverRasterArea;

        internal MediaInfo WithDriverPrintableArea(int pageWidth, int pageHeight, int left, int top, int right, int bottom)
        {
            if (!IsContinuous) return this;
            if (Math.Abs(pageWidth * 25.4 / 1440 - WidthMm) > 0.3 ||
                Math.Abs(pageHeight * 25.4 / 1440 - PageLengthMm) > 0.3 ||
                left < 0 || top < 0 || right < 0 || bottom < 0)
                throw new InvalidOperationException(Loc.T("Endlosformat oder Treiberränder weichen von der erkannten Rolle ab."));
            var copy = FromStatus(0x4A, WidthMm, 0);
            int x1 = Math.Max(copy.RasterArea.Left, (int)Math.Ceiling(left * 300.0 / 1440));
            int y1 = (int)Math.Ceiling(top * 300.0 / 1440);
            int x2 = Math.Min(copy.RasterArea.Right, (int)Math.Floor((pageWidth - right) * 300.0 / 1440));
            int y2 = Math.Min(copy.PixelHeight, (int)Math.Floor((pageHeight - bottom) * 300.0 / 1440));
            if (x2 <= x1 || y2 <= y1) throw new InvalidOperationException(Loc.T("Treiber meldet keine nutzbare Endlos-Druckfläche."));
            copy.driverRasterArea = Rectangle.FromLTRB(x1, y1, x2, y2);
            return copy;
        }

        public bool IsContinuous { get { return Kind == MediaKind.Continuous; } }
        public bool IsDieCut { get { return Kind == MediaKind.DieCut; } }
        public bool HasMedia { get { return Kind != MediaKind.None && WidthMm > 0; } }

        public int MediaTypeForPrint
        {
            get { return IsContinuous ? 0x0A : 0x0B; }
        }

        public int MediaLengthForPrint
        {
            get { return IsContinuous ? 0 : LengthMm; }
        }

        public int PageLengthMm
        {
            get { return IsContinuous ? RenderLengthMm : LengthMm; }
        }

        public int PixelWidth
        {
            get
            {
                if (IsDieCut && WidthMm == LengthMm && WidthMm != 23)
                    return HeadWidth + (WidthMm == 58 ? 70 : 48);
                return HeadWidth + (IsContinuous && WidthMm == 54 ? 48 : 36);
            }
        }

        public int PixelHeight
        {
            get
            {
                // Never round an endless bitmap beyond the physical page edge.
                if (IsContinuous) return Math.Max(150, (int)Math.Floor(PageLengthMm * 300.0 / 25.4));
                if (WidthMm == LengthMm) return PixelWidth;
                switch (LengthMm)
                {
                    case 29: return 341; case 42: return 495; case 48: return 565;
                    case 54: return 636; case 86: return 1016; case 87: return 1026;
                    case 90: return 1061; case 100: return 1179;
                }
                throw new InvalidOperationException(Loc.T("Unbekannte DK-Label-Länge: ") + LengthMm);
            }
        }

        public int HeadOffset
        {
            get
            {
                if (IsContinuous)
                {
                    switch (WidthMm) { case 12: return 585; case 29: return 408; case 38: return 295; case 50: return 154; case 54: return 130; case 62: return 12; }
                }
                else
                {
                    switch (WidthMm) { case 12: return 513; case 17: return 555; case 23: case 24: return 442; case 29: return 408; case 38: return 295; case 39: return 289; case 52: return 142; case 54: return 59; case 58: return 51; case 60: return 24; case 62: return 12; }
                }
                throw new InvalidOperationException("Unbekannte DK-Rollenbreite: " + WidthMm);
            }
        }

        public int HeadWidth
        {
            get
            {
                if (IsContinuous)
                {
                    switch (WidthMm) { case 12: return 106; case 29: return 306; case 38: return 413; case 50: return 554; case 54: return 590; case 62: return 696; }
                }
                else
                {
                    switch (WidthMm) { case 12: return 94; case 17: return 165; case 23: case 24: return 236; case 29: return 306; case 38: return 413; case 39: return 425; case 52: return 578; case 54: return 602; case 58: return 618; case 60: return 672; case 62: return 696; }
                }
                throw new InvalidOperationException("Unbekannte DK-Rollenbreite: " + WidthMm);
            }
        }

        public Rectangle RasterArea
        {
            get
            {
                if (driverRasterArea.HasValue) return driverRasterArea.Value;
                int top = IsContinuous ? 0 : (WidthMm == LengthMm && WidthMm != 23 ? (PixelHeight - HeadWidth) / 2 : 35);
                return new Rectangle((PixelWidth - HeadWidth) / 2, top, HeadWidth, PixelHeight - 2 * top);
            }
        }

        public int PrintableLeft
        {
            get { return Math.Min(24, Math.Max(10, PixelWidth / 12)); }
        }

        public int PrintableTop
        {
            get { return Math.Min(35, Math.Max(14, PixelHeight / 12)); }
        }

        public Rectangle PrintableArea
        {
            get
            {
                int left = PrintableLeft;
                int top = PrintableTop;
                return new Rectangle(
                    left,
                    top,
                    Math.Max(80, PixelWidth - left * 2),
                    Math.Max(80, PixelHeight - top * 2)
                );
            }
        }

        public string DisplayName
        {
            get
            {
                if (!HasMedia) return Loc.T("Keine Rolle");
                if (IsContinuous) return Loc.T("Endlosrolle ") + WidthMm + Loc.T(" mm, Drucklänge ") + RenderLengthMm + " mm";
                return Loc.T("DK-Stanzlabel ") + WidthMm + " x " + LengthMm + " mm";
            }
        }

        public string ShortName
        {
            get
            {
                if (!HasMedia) return Loc.T("Keine Rolle");
                if (IsContinuous) return WidthMm + " mm endlos";
                return WidthMm + "x" + LengthMm + " mm";
            }
        }

        public static MediaInfo FromStatus(int mediaType, int widthMm, int lengthMm)
        {
            if (mediaType == 0x4A || mediaType == 0x0A)
            {
                return new MediaInfo
                {
                    Kind = MediaKind.Continuous,
                    WidthMm = widthMm,
                    LengthMm = 0,
                    RenderLengthMm = ContinuousLengthFor(widthMm)
                };
            }
            if (mediaType == 0x4B || mediaType == 0x0B || mediaType == 0x02)
            {
                return new MediaInfo
                {
                    Kind = MediaKind.DieCut,
                    WidthMm = widthMm,
                    LengthMm = lengthMm,
                    RenderLengthMm = Math.Max(lengthMm, 1)
                };
            }
            return new MediaInfo { Kind = MediaKind.None };
        }

        public static MediaInfo DefaultDk11221()
        {
            return new MediaInfo { Kind = MediaKind.DieCut, WidthMm = 23, LengthMm = 23, RenderLengthMm = 23 };
        }

        static int ContinuousLengthFor(int widthMm)
        {
            if (widthMm <= 12) return 50;
            if (widthMm <= 29) return 38;
            if (widthMm <= 38) return 42;
            return 50;
        }

        static int ToDots(int mm)
        {
            return (int)Math.Round(mm * DotsPerMm);
        }
    }

    sealed class PrinterStatus
    {
        internal static PrinterStatus Ready(MediaInfo media)
        {
            if (media == null || !media.HasMedia) throw new InvalidOperationException(Loc.T("Keine unterstützte Rolle eingelegt."));
            return new PrinterStatus { CanPrint = true, Media = media, Description = media.ShortName, BlockReason = "" };
        }
        internal static PrinterStatus Unverified(MediaInfo media, string reason)
        {
            return new PrinterStatus { CanPrint = false, Media = media, Description = reason, BlockReason = reason };
        }
        public bool CanPrint { get; private set; }
        public string Description { get; private set; }
        public string BlockReason { get; private set; }
        public MediaInfo Media { get; private set; }

        internal static bool ValidFrame(byte[] s)
        {
            return s != null && s.Length == 32 && s[0] == 0x80 && s[1] == 0x20 && s[2] == 0x42 && s[3] == 0x34;
        }

        public static PrinterStatus Parse(byte[] s)
        {
            if (!ValidFrame(s))
            {
                return Block(Loc.T("Keine vollständige Statusantwort vom Drucker."));
            }

            int mediaType = s[11];
            int mediaWidth = s[10];
            int mediaLength = s[17];
            int error1 = s[8];
            int error2 = s[9];
            var media = MediaInfo.FromStatus(mediaType, mediaWidth, mediaLength);

            var description = string.Format("{0} (Status {1:X2}, Fehler {2:X2}/{3:X2})", media.DisplayName, mediaType, error1, error2);

            if (error1 != 0 || error2 != 0)
            {
                return Block("Brother meldet einen Fehlerstatus: " + description, description, media);
            }
            if (s[19] == 1 || s[18] == 2 || s[18] == 4)
                return Block(Loc.T("Drucker beschäftigt oder nicht bereit."), description, media);

            if (!media.HasMedia)
            {
                return Block(Loc.T("Es ist keine DK-Rolle eingelegt oder der Medientyp wurde nicht erkannt."), description, media);
            }

            return new PrinterStatus
            {
                CanPrint = true,
                Description = media.ShortName,
                BlockReason = "",
                Media = media
            };
        }

        static PrinterStatus Block(string reason, string description = null, MediaInfo media = null)
        {
            return new PrinterStatus
            {
                CanPrint = false,
                Description = description ?? reason,
                BlockReason = reason,
                Media = media
            };
        }
    }


    static class BrotherRaster
    {
        public static byte[] Build(Bitmap label, MediaInfo media, bool cutAfter = true)
        {
            const int headBytes = 90;
            var area = media.RasterArea;
            int rows = area.Height;
            int left = media.HeadOffset;

            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x1B, 0x40 });
            bytes.AddRange(new byte[] { 0x1B, 0x69, 0x61, 0x01 });
            bytes.AddRange(new byte[] { 0x1B, 0x69, 0x21, 0x00 });
            bytes.AddRange(new byte[] { 0x1B, 0x69, 0x7A, (byte)(media.IsContinuous ? 0xC6 : 0xCE), (byte)media.MediaTypeForPrint, (byte)media.WidthMm, (byte)media.MediaLengthForPrint });
            bytes.AddRange(BitConverter.GetBytes(rows));
            bytes.Add(0x00);
            bytes.Add(0x00);
            bytes.AddRange(new byte[] { 0x1B, 0x69, 0x4D, (byte)(cutAfter ? 0x40 : 0x00) });
            bytes.AddRange(new byte[] { 0x1B, 0x69, 0x4B, (byte)(cutAfter ? 0x08 : 0x00) });
            bytes.AddRange(new byte[] { 0x1B, 0x69, 0x64, (byte)(media.IsContinuous ? 35 : 0), 0x00 });

            for (int y = 0; y < rows; y++)
            {
                var row = new byte[headBytes];
                for (int x = 0; x < area.Width; x++)
                {
                    var pixel = label.GetPixel(area.Left + x, area.Top + y);
                    if ((pixel.R + pixel.G + pixel.B) / 3 < 128)
                    {
                        int bit = left + x;
                        row[bit / 8] |= (byte)(0x80 >> (bit % 8));
                    }
                }
                bytes.Add(0x67);
                bytes.Add(0x00);
                bytes.Add((byte)(headBytes & 0xFF));
                bytes.AddRange(row);
            }
            bytes.Add(0x1A);
            return bytes.ToArray();
        }
    }

    sealed class UsbDevice : IDisposable
    {
        IntPtr handle;
        IntPtr winUsb;
        byte outPipe;
        byte inPipe;

        public UsbDevice(IntPtr handle, IntPtr winUsb, byte outPipe, byte inPipe)
        {
            this.handle = handle;
            this.winUsb = winUsb;
            this.outPipe = outPipe;
            this.inPipe = inPipe;
        }

        public static UsbDevice OpenBrotherQl800(string configuredPath)
        {
            var paths = new List<string>();
            if (!string.IsNullOrWhiteSpace(configuredPath)) paths.Add(configuredPath);
            paths.AddRange(FindBrotherQl800Paths());
            if (paths.Count == 0) throw new InvalidOperationException(Loc.T("Kein QL-800 erreichbar. USB-Verbindung prüfen oder in den Einstellungen suchen."));

            Exception last = null;
            foreach (var path in paths.Distinct())
            {
                try { return WinUsbInterop.Open(path); }
                catch (Exception ex) { last = ex; }
            }
            throw new InvalidOperationException("Brother QL-800 konnte nicht geöffnet werden. WinUSB/libusb-Treiber prüfen.", last);
        }

        public static IEnumerable<string> FindBrotherQl800Paths()
        {
            return WinUsbInterop.FindDevicePaths("vid_04f9", "pid_209b");
        }

        public void Write(byte[] data)
        {
            int written;
            if (!WinUsbInterop.WinUsb_WritePipe(winUsb, outPipe, data, data.Length, out written, IntPtr.Zero) || written != data.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "USB write failed.");
            }
        }

        public byte[] Read(int maxBytes)
        {
            var buffer = new byte[maxBytes];
            int read;
            if (!WinUsbInterop.WinUsb_ReadPipe(winUsb, inPipe, buffer, maxBytes, out read, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "USB read failed.");
            }
            return buffer.Take(read).ToArray();
        }

        public void Dispose()
        {
            if (winUsb != IntPtr.Zero) WinUsbInterop.WinUsb_Free(winUsb);
            if (handle != IntPtr.Zero) WinUsbInterop.CloseHandle(handle);
            winUsb = IntPtr.Zero;
            handle = IntPtr.Zero;
        }
    }

    static class WinUsbInterop
    {
        static readonly Guid UsbDeviceGuid = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
        const int DIGCF_PRESENT = 0x02;
        const int DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1;
        const uint FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3;
        static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public static IEnumerable<string> FindDevicePaths(string vid, string pid)
        {
            var list = new List<string>();
            Guid usbDeviceGuid = UsbDeviceGuid;
            IntPtr info = SetupDiGetClassDevs(ref usbDeviceGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (info == INVALID_HANDLE_VALUE) return list;
            try
            {
                uint index = 0;
                while (true)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref usbDeviceGuid, index, ref data)) break;
                    int needed = 0;
                    SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, ref needed, IntPtr.Zero);
                    IntPtr detail = Marshal.AllocHGlobal(needed);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(info, ref data, detail, needed, ref needed, IntPtr.Zero))
                        {
                            string path = Marshal.PtrToStringAuto(detail + 4);
                            string lower = path.ToLowerInvariant();
                            if (lower.Contains(vid) && lower.Contains(pid)) list.Add(path);
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                    index++;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(info); }
            return list;
        }

        public static UsbDevice Open(string path)
        {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0x40000000, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile failed.");
            IntPtr winUsb;
            if (!WinUsb_Initialize(handle, out winUsb))
            {
                CloseHandle(handle);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize failed.");
            }

            USB_INTERFACE_DESCRIPTOR desc;
            if (!WinUsb_QueryInterfaceSettings(winUsb, 0, out desc))
            {
                WinUsb_Free(winUsb);
                CloseHandle(handle);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings failed.");
            }

            byte inPipe = 0, outPipe = 0;
            for (byte i = 0; i < desc.bNumEndpoints; i++)
            {
                WINUSB_PIPE_INFORMATION pipe;
                if (WinUsb_QueryPipe(winUsb, 0, i, out pipe))
                {
                    if (pipe.PipeType != 2) continue;
                    if ((pipe.PipeId & 0x80) == 0x80) inPipe = pipe.PipeId;
                    else outPipe = pipe.PipeId;
                }
            }
            if (inPipe == 0 || outPipe == 0)
            {
                WinUsb_Free(winUsb);
                CloseHandle(handle);
                throw new InvalidOperationException("USB Bulk IN/OUT endpoints nicht gefunden.");
            }
            uint timeout = 3000;
            if (!WinUsb_SetPipePolicy(winUsb, inPipe, 3, 4, ref timeout) ||
                !WinUsb_SetPipePolicy(winUsb, outPipe, 3, 4, ref timeout))
            {
                int error = Marshal.GetLastWin32Error();
                WinUsb_Free(winUsb);
                CloseHandle(handle);
                throw new Win32Exception(error, "USB-Timeout konnte nicht gesetzt werden.");
            }
            return new UsbDevice(handle, winUsb, outPipe, inPipe);
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, ref int requiredSize, IntPtr deviceInfoData);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("winusb.dll", SetLastError = true)]
        static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint value);
        [DllImport("winusb.dll", SetLastError = true)]
        static extern bool WinUsb_Initialize(IntPtr deviceHandle, out IntPtr interfaceHandle);
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_Free(IntPtr interfaceHandle);
        [DllImport("winusb.dll", SetLastError = true)]
        static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, out USB_INTERFACE_DESCRIPTOR usbAltInterfaceDescriptor);
        [DllImport("winusb.dll", SetLastError = true)]
        static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WINUSB_PIPE_INFORMATION pipeInformation);
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, int bufferLength, out int lengthTransferred, IntPtr overlapped);
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, int bufferLength, out int lengthTransferred, IntPtr overlapped);

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        struct USB_INTERFACE_DESCRIPTOR { public byte bLength, bDescriptorType, bInterfaceNumber, bAlternateSetting, bNumEndpoints, bInterfaceClass, bInterfaceSubClass, bInterfaceProtocol; public byte iInterface; }
        [StructLayout(LayoutKind.Sequential)]
        struct WINUSB_PIPE_INFORMATION { public int PipeType; public byte PipeId; public ushort MaximumPacketSize; public byte Interval; }
    }

    static class CredentialStore
    {
        const int CRED_TYPE_GENERIC = 1;
        const int CRED_PERSIST_LOCAL_MACHINE = 2;

        public static void Write(string target, string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                Delete(target);
                return;
            }
            byte[] bytes = Encoding.Unicode.GetBytes(secret ?? "");
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = Marshal.AllocHGlobal(bytes.Length),
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Environment.UserName
            };
            Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
            try
            {
                if (!CredWrite(ref cred, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CredWrite failed.");
            }
            finally { Marshal.FreeHGlobal(cred.CredentialBlob); }
        }

        public static string Read(string target)
        {
            IntPtr ptr;
            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out ptr)) return null;
            try
            {
                var cred = (CREDENTIAL)Marshal.PtrToStructure(ptr, typeof(CREDENTIAL));
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize <= 0) return null;
                byte[] bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally { CredFree(ptr); }
        }

        public static void Delete(string target)
        {
            CredDelete(target, CRED_TYPE_GENERIC, 0);
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CredDelete(string target, int type, int flags);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern void CredFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }
    }

    static class SelfTest
    {
        public static int Run()
        {
            var failures = new List<string>();
            try
            {
                var apiJson = new JavaScriptSerializer();
                var healthJson = apiJson.Deserialize<Health>("{\"ok\":true,\"time\":1791446400,\"capabilities\":[\"safe-sync-v1\"]}");
                if (!healthJson.ok || healthJson.capabilities.Length != 1) failures.Add("Health JSON contract failed.");
                var reservationJson = apiJson.Deserialize<Reservation>("{\"reservationId\":\"test-id\",\"asnNumber\":123,\"barcodeText\":\"ASN00123\",\"expiresAt\":1791446700}");
                if (reservationJson.reservationId != "test-id" || reservationJson.asnNumber != 123 || reservationJson.barcodeText != "ASN00123") failures.Add("Reservation JSON contract failed.");
                var resetJson = apiJson.Deserialize<ResetResult>("{\"ok\":true,\"paperlessMax\":122,\"deletedReservations\":0,\"nextAsnNumber\":123,\"nextBarcodeText\":\"ASN00123\",\"releasedReservations\":1,\"retainedReservations\":2}");
                if (!resetJson.ok || resetJson.nextAsnNumber != 123 || resetJson.retainedReservations != 2) failures.Add("Sync JSON contract failed.");
                var payload = "ASN00123";
                var dk11221 = MediaInfo.DefaultDk11221();
                using (var image = LabelRenderer.Render(payload, dk11221))
                {
                    if (image.Width != 272 || image.Height != 272) failures.Add("DK-11221 label size is not 272x272.");
                    var commands = BrotherRaster.Build(image, dk11221);
                    if (commands.Length < 1000) failures.Add("Raster command stream too small.");
                    if (!commands.Take(2).SequenceEqual(new byte[] { 0x1B, 0x40 })) failures.Add("Missing Brother init command.");
                }
                var continuous = MediaInfo.FromStatus(0x4A, 62, 0);
                using (var image = LabelRenderer.Render(payload, continuous))
                {
                    if (image.Width <= image.Height / 2) failures.Add("Continuous media layout has unexpected aspect.");
                    var commands = BrotherRaster.Build(image, continuous);
                    if (!commands.Contains((byte)0x0A)) failures.Add("Continuous media command type missing.");
                }
                var status = new byte[32];
                status[0] = 0x80; status[1] = 0x20; status[2] = 0x42; status[3] = 0x34;
                status[10] = 23;
                status[11] = 0x4B;
                status[17] = 23;
                if (!PrinterStatus.Parse(status).CanPrint) failures.Add("DK-11221 status was rejected.");
                status[10] = 62;
                status[11] = 0x4A;
                status[17] = 0;
                if (!PrinterStatus.Parse(status).CanPrint) failures.Add("Continuous roll status was rejected.");
                if (Ql800Printer.ConfirmsCompletion(status)) failures.Add("Idle status treated as print success.");
                status[19] = 1;
                if (PrinterStatus.Parse(status).CanPrint) failures.Add("Busy printer accepted.");
                status[19] = 0;
                status[18] = 1;
                if (!Ql800Printer.ConfirmsCompletion(status)) failures.Add("Completion event rejected.");
                status[9] = 0x10;
                if (PrinterStatus.Parse(status).CanPrint) failures.Add("Open cover accepted.");
                try { Ql800Printer.ConfirmsCompletion(status); failures.Add("Error event accepted as completion."); }
                catch (InvalidOperationException) { }
                status[9] = 0;
                status[0] = 0;
                if (PrinterStatus.Parse(status).CanPrint) failures.Add("Malformed status accepted.");
                using (var label = LabelRenderer.Render(payload, dk11221))
                {
                    var raster = BrotherRaster.Build(label, dk11221);
                    const int rasterStart = 36;
                    if (raster.Length != rasterStart + 202 * 93 + 1) failures.Add("DK-11221 raster length incorrect.");
                    for (int row = 0; row < 202; row++)
                    {
                        int offset = rasterStart + row * 93;
                        if (raster[offset] != 0x67 || raster[offset + 1] != 0 || raster[offset + 2] != 90)
                        { failures.Add("Invalid raster row framing."); break; }
                    }
                }
                failures.AddRange(DataMatrixTests.Run());
                failures.AddRange(ClientUpdates.Validate());
                failures.AddRange(AppDialogs.Validate());
                failures.AddRange(UpdateForm.ValidateLayout());
                if (AsnNumber.Parse("ASN00123") != 123) failures.Add("ASN parser rejected prefixed value.");
                if (AsnNumber.Parse("00124") != 124) failures.Add("ASN parser rejected numeric value.");
                if (AsnNumber.Format(125) != "ASN00125") failures.Add("ASN formatter padding changed.");
                failures.AddRange(MainForm.ValidateLayoutForTests());
                failures.AddRange(SettingsForm.ValidateLayout());
                Loc.Language = "en";
                failures.AddRange(AppDialogs.Validate());
                failures.AddRange(UpdateForm.ValidateLayout());
                failures.AddRange(SettingsForm.ValidateLayout());
                if (Loc.T("Einstellungen") != "Settings" || Loc.T("ASN00001") != "ASN00001") failures.Add("Localization fallback failed.");
                Loc.Language = "de";
                failures.AddRange(CopyLabelForm.Validate());
                if (new AppConfig().RefreshMilliseconds != 30000 || new AppConfig { RefreshSeconds = 15 }.RefreshMilliseconds != 15000 ||
                    new AppConfig { RefreshSeconds = -1 }.RefreshMilliseconds != 30000)
                    failures.Add("Client refresh interval defaults or validation failed.");
                failures.AddRange(DocumentStatusForm.ValidateForTests());
                failures.AddRange(PrinterBackendTests.Run());
            }
            catch (Exception ex)
            {
                failures.Add(ex.ToString());
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("Self-test passed.");
                return 0;
            }
            foreach (var failure in failures) Console.Error.WriteLine(failure);
            return 1;
        }
    }
}
