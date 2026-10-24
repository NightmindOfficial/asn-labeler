using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Drawing.Imaging;
using System.Globalization;
using System.Xml.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AsnLabeler
{
    interface IPrinterSession : IDisposable
    {
        PrinterStatus ReadStatus();
        void Print(string payload, MediaInfo media, Action beforeSend, bool cutAfter = true);
    }

    sealed class PrinterChoice
    {
        public string Name;
        public string Driver;
        public string Port;
        public bool Offline;
        public override string ToString() { return Name; }
    }

    sealed class PrintBatch
    {
        public int Count { get; private set; }
        public int Completed { get; private set; }
        int started;
        readonly bool cutIndividually;
        public PrintBatch(int count, bool cutIndividually = false)
        {
            if (count < 1 || count > 10) throw new ArgumentOutOfRangeException("count");
            Count = count;
            this.cutIndividually = cutIndividually;
        }
        public bool CutAfter(int index)
        {
            if (index < 1 || index > Count) throw new ArgumentOutOfRangeException("index");
            return cutIndividually || index == Count;
        }
        public void Run(Action<int> printOne)
        {
            if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException(Loc.T("Druckserie wurde bereits gestartet."));
            for (int index = 1; index <= Count; index++)
            {
                printOne(index);
                Completed++;
            }
        }
    }

    sealed class PrintLifecycle
    {
        public bool HandshakeStarted { get; private set; }
        public bool PrintConfirmed { get; private set; }
        public bool ApiConfirmed { get; private set; }

        public void Submit(IPrinterSession printer, string payload, MediaInfo media, Action markPrinting, Action markPrinted, bool cutAfter = true)
        {
            printer.Print(payload, media, delegate
            {
                if (HandshakeStarted) throw new InvalidOperationException("Doppelter Druckstart verweigert.");
                // An API timeout can mean the server committed printing already.
                HandshakeStarted = true;
                markPrinting();
            }, cutAfter);
            if (!HandshakeStarted) throw new InvalidOperationException(Loc.T("Druckbackend hat die Druckfreigabe übersprungen."));
            PrintConfirmed = true;
            markPrinted();
            ApiConfirmed = true;
        }
    }

    static class PrinterBackends
    {
        public const string Brother = "Brother";
        public const string WinUsb = "WinUSB";

        public static IPrinterSession Open(AppConfig config)
        {
            config.NormalizePrinterBackend();
            var selected = Select(DiscoverBrother(), config.PrinterName);
            RequireProductionModel(selected);
            if (String.IsNullOrWhiteSpace(config.PrinterName)) { config.PrinterName = selected.Name; config.Save(); }
            return new BpacPrinterSession(selected, config.UsePfRightAligned);
        }

        public static List<PrinterChoice> DiscoverBrother()
        {
            var result = new List<PrinterChoice>();
            var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = false };
            using (var searcher = new ManagementObjectSearcher("root\\CIMV2",
                "SELECT Name, DriverName, PortName, WorkOffline FROM Win32_Printer WHERE Local = TRUE", options))
            using (var printers = searcher.Get())
                foreach (ManagementObject printer in printers)
                    using (printer)
                    {
                        string driver = Convert.ToString(printer["DriverName"]);
                        if (KnownModel(driver) != null)
                            result.Add(new PrinterChoice { Name = Convert.ToString(printer["Name"]), Driver = driver,
                                Port = Convert.ToString(printer["PortName"]), Offline = Convert.ToBoolean(printer["WorkOffline"] ?? false) });
                    }
            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static bool IsQl800Driver(string driver)
        {
            return driver != null && System.Text.RegularExpressions.Regex.IsMatch(driver,
                @"\bBrother\b.*\bQL-800\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        internal static string KnownModel(string driver)
        {
            if (driver == null) return null;
            var match = System.Text.RegularExpressions.Regex.Match(driver,
                @"\bBrother\b.*\b(QL-800|QL-810W|QL-820NWB)(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }
        internal static void RequireProductionModel(PrinterChoice printer)
        {
            if (!IsQl800Driver(printer.Driver)) throw new InvalidOperationException(ClientUpdates.Text(
                "Dieses Modell ist experimentell. Nur Testdruck und Diagnose sind freigegeben; ASN-Druck benötigt eine Hardware-Abnahme.",
                "This model is experimental. Only test printing and diagnostics are enabled; ASN printing requires hardware validation."));
        }
        public static string ModelOverview()
        {
            return ClientUpdates.Text(
                "QL-800: bestätigt unterstützt.\r\nQL-810W: experimentell, nur Testdruck/Diagnose.\r\nQL-820NWB: experimentell, nur Testdruck/Diagnose.\r\n\r\nNeue Modelle werden zunächst lokal über USB geprüft. Eine b-PAC-Kompatibilität allein bestätigt weder Medienerkennung noch sicheren Druckabschluss. Andere QL-Modelle und Netzwerkdruck sind noch nicht freigegeben.",
                "QL-800: validated support.\r\nQL-810W: experimental, test printing/diagnostics only.\r\nQL-820NWB: experimental, test printing/diagnostics only.\r\n\r\nNew models are initially evaluated using local USB. b-PAC compatibility alone does not validate media detection or reliable print completion. Other QL models and network printing are not yet approved.");
        }

        internal static PrinterChoice Select(List<PrinterChoice> printers, string selected)
        {
            if (!String.IsNullOrWhiteSpace(selected))
            {
                var match = printers.SingleOrDefault(p => String.Equals(p.Name, selected, StringComparison.OrdinalIgnoreCase));
                if (match == null) throw new InvalidOperationException(Loc.T("Der ausgewählte Brother-Drucker fehlt. Bitte in den Einstellungen erneut suchen."));
                return match;
            }
            if (printers.Count == 0) throw new InvalidOperationException(ClientUpdates.Text("Kein unterstützter Brother-QL-Treiber gefunden. Windows-Drucker und Brother-Treiber prüfen.", "No supported Brother QL driver found. Check Windows printers and the Brother driver."));
            if (printers.Count > 1) throw new InvalidOperationException(ClientUpdates.Text("Mehrere Brother-QL-Drucker gefunden. Bitte in den Einstellungen auswählen.", "Multiple Brother QL printers found. Select one in Settings."));
            return printers[0];
        }

        public static string Diagnose(string backend, string selected, double correctionMm = 0, bool rightAligned = false)
        {
            var report = new StringBuilder("ASN Labeler - Druckerdiagnose\r\n");
            report.AppendLine("Version: " + AppRelease.Version);
            report.AppendLine("Zeit (UTC): " + DateTime.UtcNow.ToString("u"));
            report.AppendLine("Prozess: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            report.AppendLine("Anbindung: " + backend);
            report.AppendLine("Auswahl: " + (String.IsNullOrEmpty(selected) ? "Automatisch" : selected));
            report.AppendLine("PF-B11221-Korrektur (nur 23x23 mm): " + correctionMm.ToString("0.0", CultureInfo.InvariantCulture) + " mm rechts");
            report.AppendLine("Barcode: DataMatrix ECC 200 (ZXing.Net 0.16.11)");
            report.AppendLine("PF-B11221 Compatible (gedreht, 300×600 dpi angefordert): " + rightAligned);
            try
            {
                var printers = DiscoverBrother();
                report.AppendLine("QL-Warteschlangen: " + printers.Count);
                foreach (var p in printers) report.AppendLine(p.Name + " | " + p.Driver + " | " + p.Port + " | Windows-Offline=" + p.Offline);
            }
            catch (Exception ex) { report.AppendLine("Windows-Suche: " + ex.GetType().Name + " (0x" + ex.HResult.ToString("X8") + ")"); }
            try
            {
                report.AppendLine(BpacPrinterSession.Probe());
                var printer = Select(DiscoverBrother(), selected);
                report.AppendLine(BpacPrinterSession.Inspect(printer).Description);
                if (backend == Brother) report.AppendLine(BpacPrinterSession.TestPrint(printer, false, correctionMm, rightAligned));
            }
            catch (Exception ex) { report.AppendLine(ex.Message); }
            report.AppendLine("ASN-Druck nur auf freigegebenen Modellen. Abschluss: Printed(0) und Printer.ErrorCode=0. Scanbarkeit am physischen Label pruefen.");
            report.AppendLine("Letzter b-PAC-Aufruffehler (diese Sitzung): " + BpacPrinterSession.LastFailure);
            report.AppendLine("Letzte Testlabel-Geometrie (diese Sitzung): " + BpacPrinterSession.LastGeometry);
            report.AppendLine("Keine API-Schlüssel, Serveradressen oder Dokumentdaten enthalten.");
            return report.ToString();
        }
    }

    sealed class WinUsbPrinterSession : IPrinterSession
    {
        readonly UsbDevice device;
        readonly Ql800Printer printer;
        public WinUsbPrinterSession(AppConfig config)
        {
            printer = new Ql800Printer(config);
            device = UsbDevice.OpenBrotherQl800(config.DevicePath);
        }
        public PrinterStatus ReadStatus() { return printer.ReadStatus(device); }
        public void Print(string payload, MediaInfo media, Action beforeSend, bool cutAfter = true)
        {
            var current = ReadStatus();
            if (!current.CanPrint || !BpacPrinterSession.SameMedia(current.Media, media))
                throw new InvalidOperationException(Loc.T("Drucker oder Rolle geändert. Kein Druckauftrag gestartet."));
            using (var image = LabelRenderer.Render(payload, media))
            {
                var commands = BrotherRaster.Build(image, media, cutAfter);
                beforeSend();
                printer.PrintPrepared(device, commands);
            }
        }
        public void Dispose() { device.Dispose(); }
    }

    // Test and ASN jobs share the exact same rendering and SDK submission path.
    sealed class BpacPrinterSession : IPrinterSession
    {
        static int testInFlight;
        internal static bool IsPrinting { get { return Volatile.Read(ref testInFlight) != 0; } }
        static string lastFailure;
        static string lastGeometry;
        internal static string LastFailure { get { return Volatile.Read(ref lastFailure) ?? "Keiner aufgezeichnet."; } }
        internal static string LastGeometry { get { return Volatile.Read(ref lastGeometry) ?? Loc.T("Noch kein Testdruck vorbereitet."); } }
        readonly PrinterChoice printer;
        readonly bool compatible;
        public BpacPrinterSession(PrinterChoice printer, bool compatible = false) { this.printer = printer; this.compatible = compatible; }

        public static string Probe()
        {
            return StaOperation.Run(delegate
            {
                Type type = Type.GetTypeFromProgID("bpac.Document", false);
                if (type == null)
                {
                    bool x86;
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32))
                    using (var key = root.OpenSubKey("bpac.Document\\CLSID")) x86 = key != null;
                    throw new InvalidOperationException(x86
                        ? Loc.T("b-PAC ist nur als 32-Bit-Komponente registriert. Diese App benötigt den b-PAC-Client in 64 Bit.")
                        : Loc.T("Brother-b-PAC-Client (64 Bit) fehlt. Bitte die offizielle Client-Komponente installieren; den Brother-Treiber beibehalten."));
                }
                object document = null;
                try
                {
                    document = Activator.CreateInstance(type);
                    return "b-PAC x64: COM-Aktivierung erfolgreich (kein Drucktest).";
                }
                catch (COMException ex)
                {
                    throw new InvalidOperationException(Loc.T("b-PAC konnte nicht aktiviert werden (0x") + ex.HResult.ToString("X8") + Loc.T("). Client-Installation prüfen."));
                }
                finally { if (document != null && Marshal.IsComObject(document)) Marshal.FinalReleaseComObject(document); }
            }, TimeSpan.FromSeconds(15));
        }

        public PrinterStatus ReadStatus()
        {
            if (Volatile.Read(ref testInFlight) != 0) throw new InvalidOperationException(Loc.T("Ein b-PAC-Auftrag ist noch aktiv. Bitte warten."));
            RequireUsb(printer.Port);
            if (printer.Offline) throw new InvalidOperationException(Loc.T("Windows meldet den ausgewählten Brother-Drucker als offline."));
            CheckQueue(printer.Name);
            var media = Inspect(printer);
            return PrinterStatus.Ready(media.Media);
        }
        public void Print(string payload, MediaInfo media, Action beforeSend, bool cutAfter = true)
        {
            if (beforeSend == null) throw new ArgumentNullException("beforeSend");
            RequireUsb(printer.Port);
            PrintLabel(printer, payload, true, 0, compatible, media, beforeSend, cutAfter);
        }
        public void Dispose() { }

        internal static void RequireUsb(string port)
        {
            if (String.IsNullOrWhiteSpace(port) || !port.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Loc.T("ASN-Druck benötigt einen lokal per USB angeschlossenen Brother (Windows-Port USB...). Netzwerk-/virtuelle Ports sind nicht freigegeben."));
        }

        internal static bool SameMedia(MediaInfo a, MediaInfo b)
        {
            return a != null && b != null && a.Kind == b.Kind && a.WidthMm == b.WidthMm && a.LengthMm == b.LengthMm;
        }

        static void CheckQueue(string name)
        {
            var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = false };
            using (var search = new ManagementObjectSearcher("root\\CIMV2",
                "SELECT Name, WorkOffline, PrinterStatus, DetectedErrorState FROM Win32_Printer", options))
            using (var rows = search.Get())
            {
                bool found = false;
                foreach (ManagementObject row in rows)
                    using (row)
                    {
                        if (!String.Equals(Convert.ToString(row["Name"]), name, StringComparison.OrdinalIgnoreCase)) continue;
                        found = true;
                        int state = Convert.ToInt32(row["PrinterStatus"] ?? 0), error = Convert.ToInt32(row["DetectedErrorState"] ?? 0);
                        ValidateWindowsStatus(Convert.ToBoolean(row["WorkOffline"] ?? false), state, error);
                    }
                if (!found) throw new InvalidOperationException(Loc.T("Der ausgewählte Windows-Drucker fehlt."));
            }
            using (var search = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_PrintJob", options))
            using (var rows = search.Get())
                foreach (ManagementObject row in rows)
                    using (row)
                        if (Convert.ToString(row["Name"]).StartsWith(name + ",", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(Loc.T("Die Brother-Warteschlange enthält noch einen Auftrag. Bitte zuerst am Drucker prüfen."));
        }

        internal static void ValidateWindowsStatus(bool offline, int state, int error)
        {
            if (offline || state >= 4 || (error != 0 && error != 2))
                throw new InvalidOperationException(Loc.T("Windows meldet den Drucker als beschäftigt oder fehlerhaft (Status ") + state + ", Fehler " + error + ").");
        }

        internal sealed class LoadedMedia
        {
            public int Id;
            public string Description;
            public MediaInfo Media;
        }

        static object CreateDocument()
        {
            var type = Type.GetTypeFromProgID("bpac.Document", false);
            if (type == null) { Probe(); throw new InvalidOperationException("b-PAC fehlt."); }
            return Activator.CreateInstance(type);
        }

        static void Check(bool success, string operation, dynamic source)
        {
            if (success) return;
            // Read immediately: another SDK call can overwrite ErrorCode.
            int? code = null;
            try { code = Convert.ToInt32(source.ErrorCode); } catch { }
            string message = DescribeFailure(operation, code);
            Volatile.Write(ref lastFailure, DateTime.UtcNow.ToString("u") + " | " + message);
            throw new InvalidOperationException(message);
        }

        internal static string DescribeFailure(string operation, int? code)
        {
            string detail;
            switch (code)
            {
                case 17498114: detail = Loc.T("Bildobjekt konnte nicht gerendert werden."); break;
                case 17498115: detail = Loc.T("Bild konnte nicht gedreht werden."); break;
                case 17498117: detail = Loc.T("Labelauflösung konnte nicht konvertiert werden."); break;
                case 13: detail = Loc.T("Druckerkontext konnte nicht erstellt werden."); break;
                case 10: detail = Loc.T("Vorlage enthält keine druckbaren Objekte."); break;
                case 301: detail = Loc.T("Medienbreite stimmt nicht überein."); break;
                case 303: detail = Loc.T("Bilddatei konnte nicht geöffnet werden."); break;
                default: detail = Loc.T("Bitte diesen Fehler mit der Druckerdiagnose weitergeben."); break;
            }
            return "b-PAC: " + operation + " fehlgeschlagen (SDK " +
                (code.HasValue ? code.Value.ToString(CultureInfo.InvariantCulture) + " / 0x" + code.Value.ToString("X8") : Loc.T("Code nicht verfügbar")) + "). " + detail;
        }

        static LoadedMedia QueryMedia(dynamic document, string name)
        {
            dynamic device = document.Printer;
            try
            {
                Check((bool)device.IsPrinterOnline(name), Loc.T("Drucker erreichbar"), (object)device);
                int id = device.GetMediaId();
                int idError = device.ErrorCode;
                string mediaName = device.GetMediaName();
                int error = device.ErrorCode;
                if (idError != 0 || error != 0 || id <= 0 || String.IsNullOrWhiteSpace(mediaName))
                    throw new InvalidOperationException(Loc.T("Brother-Rolle nicht lesbar oder Druckerfehler (") + idError + "/" + error + Loc.T("). Rolle, Deckel und USB prüfen."));
                return new LoadedMedia { Id = id, Media = ParseMedia(mediaName), Description = "Eingelegte Rolle: " + mediaName + " (ID " + id + ")" };
            }
            finally { Marshal.FinalReleaseComObject((object)device); }
        }

        internal static MediaInfo ParseMedia(string name)
        {
            // Match complete dimensions, never use a default or template size as a
            // substitute for an unrecognized device response.
            var pair = System.Text.RegularExpressions.Regex.Match(name,
                @"^\s*(\d+)\s*(?:mm)?\s*[x×]\s*(\d+)\s*(?:mm)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (pair.Success) return MediaInfo.FromStatus(0x4B, Int32.Parse(pair.Groups[1].Value), Int32.Parse(pair.Groups[2].Value));
            var tape = System.Text.RegularExpressions.Regex.Match(name,
                @"^\s*(\d+)\s*mm\s*(?:\((?:continuous|endlos)\))?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tape.Success) return MediaInfo.FromStatus(0x4A, Int32.Parse(tape.Groups[1].Value), 0);
            throw new InvalidOperationException(Loc.T("Rollenbezeichnung noch nicht zugeordnet: ") + name + Loc.T(". Bitte Diagnose senden; keine Standardgröße wird angenommen."));
        }

        public static LoadedMedia Inspect(PrinterChoice selected)
        {
            return StaOperation.Run(() => WithDocument(selected, (document, folder) =>
            {
                LoadedMedia media = QueryMedia(document, selected.Name);
                ConfigureContinuous(document, media);
                return media;
            }), TimeSpan.FromSeconds(20));
        }

        static void ConfigureContinuous(dynamic document, LoadedMedia media)
        {
            if (!media.Media.IsContinuous) return;
            Check((bool)document.SetMediaById(media.Id, false), "Endlosrolle einstellen", (object)document);
            // SDK Length <= 0 enables automatic sizing. Explicit positive length
            // and fitPage=false keep bitmap scale and cut length independent.
            document.Length = (int)Math.Round(media.Media.PageLengthMm * 1440.0 / 25.4);
            int width = document.Width, height = document.Length;
            int left = document.MarginLeft, right = document.MarginRight;
            int top = document.MarginTop, bottom = document.MarginBottom;
            media.Media = media.Media.WithDriverPrintableArea(width, height, left, top, right, bottom);
            Volatile.Write(ref lastGeometry, "Endlos " + media.Media.ShortName + "; Seite " + width + "x" + height +
                "; Ränder L/R/O/U " + left + "/" + right + "/" + top + "/" + bottom +
                " (1/1440 Zoll); Raster " + media.Media.PixelWidth + "x" + media.Media.PixelHeight + "; Druckfläche " + media.Media.RasterArea);
        }

        static T WithDocument<T>(PrinterChoice selected, Func<dynamic, string, T> action)
        {
            string folder = Path.Combine(Path.GetTempPath(), "AsnLabeler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            dynamic document = null;
            try
            {
                document = CreateDocument();
                string path = BpacTemplate.Create(folder, MediaInfo.DefaultDk11221(), "TEST0001", selected.Name);
                Check((bool)document.Open(path), Loc.T("Vorlage öffnen"), (object)document);
                Check((bool)document.SetPrinter(selected.Name, false), Loc.T("Drucker auswählen"), (object)document);
                return action(document, folder);
            }
            finally
            {
                if (document != null)
                {
                    try { document.Close(); } catch { }
                    Marshal.FinalReleaseComObject((object)document);
                }
                // Only the files created in this private per-operation directory.
                foreach (string name in new[] { "label.lbx", "test.lbx", "print.bmp" })
                    try { File.Delete(Path.Combine(folder, name)); } catch (IOException) { }
                try { Directory.Delete(folder, false); } catch (IOException) { }
            }
        }

        public static string TestPrint(PrinterChoice selected, bool sendToPrinter = true, double correctionMm = 0, bool rightAligned = false)
        {
            return PrintLabel(selected, "TEST0001", sendToPrinter, correctionMm, rightAligned, null, null);
        }

        internal static int PrintOptions(bool cutAfter, bool highResolution)
        {
            // SDK param_printoption / appendix002 (QL-800): explicit NoCut
            // overrides driver defaults; only the final job enables CutAtEnd.
            return (cutAfter ? 0x04000001 : 0x10000000) | (highResolution ? 0x02000000 : 0);
        }

        public static string PrintSecondary(PrinterChoice selected, string payload, bool compatible, MediaInfo media, Action beforeSend)
        {
            if (beforeSend == null) throw new ArgumentNullException("beforeSend");
            return PrintLabel(selected, payload, true, 0, compatible, media, beforeSend, true, true);
        }

        static string PrintLabel(PrinterChoice selected, string payload, bool sendToPrinter,
            double correctionMm, bool rightAligned, MediaInfo expectedMedia, Action beforeSend, bool cutAfter = true, bool copy = false)
        {
            if (beforeSend != null || copy) PrinterBackends.RequireProductionModel(selected);
            if (!PrinterBackends.IsQl800Driver(selected.Driver) &&
                !System.Text.RegularExpressions.Regex.IsMatch(selected.Port ?? "", @"\AUSB[0-9]+:?\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidOperationException(ClientUpdates.Text("Experimentelle Modelle bitte per USB anschließen.", "Please connect experimental models via USB."));
            if (Interlocked.CompareExchange(ref testInFlight, 1, 0) != 0)
                throw new InvalidOperationException(Loc.T("Ein b-PAC-Auftrag ist noch aktiv oder blockiert. Keinen weiteren Auftrag senden."));
            var deadline = new SubmissionDeadline(TimeSpan.FromSeconds(85));
            return StaOperation.Run(() =>
            {
                try { return WithDocument(selected, (document, folder) =>
            {
                LoadedMedia media = QueryMedia(document, selected.Name);
                ConfigureContinuous(document, media);
                if (expectedMedia != null && !SameMedia(expectedMedia, media.Media))
                    throw new InvalidOperationException(Loc.T("Die Rolle wurde seit der Prüfung gewechselt. Kein Druckauftrag gestartet."));
                if (expectedMedia != null && media.Media.IsContinuous && expectedMedia.RasterArea != media.Media.RasterArea)
                    throw new InvalidOperationException(Loc.T("Die Endlos-Druckfläche hat sich geändert. Bitte Drucker aktualisieren; kein Druckauftrag gestartet."));
                rightAligned = rightAligned && media.Media.IsDieCut && media.Media.WidthMm == 23 && media.Media.LengthMm == 23;
                string calibrationWarning = "";
                document.Close();
                // Do not overwrite the bootstrap template: the SDK may retain a
                // file handle even after Close. Prepare all files before Open.
                string path = BpacTemplate.Create(folder, media.Media, payload, selected.Name, "test.lbx", rightAligned, copy);
                Check((bool)document.Open(path), Loc.T("Testvorlage öffnen"), (object)document);
                RequireImageObject(document, "direkt nach Open");
                Check((bool)document.SetPrinter(selected.Name, false), Loc.T("Testdrucker auswählen"), (object)document);
                Check((bool)document.SetMediaById(media.Id, false), "Rolle einstellen", (object)document);
                RequireImageObject(document, "nach Drucker-/Medienauswahl");
                var templateMedia = new LoadedMedia { Id = media.Id, Media = media.Media };
                ConfigureContinuous(document, templateMedia);
                if (templateMedia.Media.RasterArea != media.Media.RasterArea)
                    throw new InvalidOperationException(Loc.T("Treiber verändert die Endlos-Druckfläche beim Vorlagenwechsel. Bitte Diagnose kopieren."));
                int pageWidth = (int)document.Width, pageHeight = (int)document.Length;
                if (Math.Abs(pageWidth * 25.4 / 1440.0 - media.Media.WidthMm) > 0.3 ||
                    Math.Abs(pageHeight * 25.4 / 1440.0 - media.Media.PageLengthMm) > 0.3)
                    throw new InvalidOperationException(Loc.T("Treiberformat weicht vom Label ab. Testdruck gesperrt."));
                // Feed the same raster as the preview through the documented image
                // replacement API; do not rely solely on the LBX image cache.
                string bitmapPath = Path.Combine(folder, "print.bmp");
                dynamic imageObject = document.GetObject("LabelBitmap");
                if (imageObject == null)
                {
                    dynamic objects = document.Objects;
                    int count;
                    try { count = objects.Count; }
                    finally { Marshal.FinalReleaseComObject((object)objects); }
                    string failure = "b-PAC: Bildobjekt LabelBitmap fehlt in der Testvorlage. Erkannte Objekte: " + count + Loc.T(". Kein Testauftrag gestartet.");
                    Volatile.Write(ref lastFailure, DateTime.UtcNow.ToString("u") + " | " + failure);
                    throw new InvalidOperationException(failure);
                }
                try
                {
                    Check((bool)imageObject.SetData(0, bitmapPath, 4), "Testbild laden", (object)document);
                    if (media.Media.IsContinuous) document.Length = pageHeight;
                    // Media selection and image replacement may move the object.
                    // Restore the entire frame last, at native 300-dpi raster size.
                    var target = BpacTemplate.CenteredImageFrame(media.Media, pageWidth, pageHeight);
                    double appliedCorrection = BpacTemplate.EffectiveCorrection(media.Media, rightAligned ? 0 : correctionMm);
                    target.Offset((int)Math.Round(appliedCorrection * 1440 / 25.4), 0);
                    if (appliedCorrection != 0 || rightAligned || media.Media.IsContinuous)
                    {
                        var printable = new System.Drawing.Rectangle((int)document.MarginLeft, (int)document.MarginTop,
                            pageWidth - (int)document.MarginLeft - (int)document.MarginRight,
                            pageHeight - (int)document.MarginTop - (int)document.MarginBottom);
                        Volatile.Write(ref lastGeometry, "PF-B11221; Versatz " + appliedCorrection.ToString("0.0", CultureInfo.InvariantCulture) +
                            " mm; Soll " + target + "; Druckfläche " + printable + "; Einheit 1/1440 Zoll. Noch nicht an Drucker gesendet.");
                        try
                        {
                            using (var raster = new System.Drawing.Bitmap(bitmapPath))
                                calibrationWarning = BpacTemplate.ValidateInkBounds(raster, target, printable, !rightAligned);
                        }
                        catch (InvalidOperationException ex)
                        {
                            Volatile.Write(ref lastFailure, DateTime.UtcNow.ToString("u") + " | " + ex.Message);
                            throw;
                        }
                    }
                    string before = "vor Zentrierung: " + imageObject.X + "," + imageObject.Y;
                    Check((bool)imageObject.SetPosition(target.X, target.Y, target.Width, target.Height), "Testbild zentrieren", (object)document);
                    var actual = new System.Drawing.Rectangle((int)imageObject.X, (int)imageObject.Y, (int)imageObject.Width, (int)imageObject.Height);
                    Volatile.Write(ref lastGeometry, "Einheit 1/1440 Zoll; Seite " + pageWidth + "x" + pageHeight + "; " + before +
                        "; Soll " + target + "; Ist " + actual + "; " + calibrationWarning);
                    if (!BpacTemplate.MatchesFrame(target, actual))
                        throw new InvalidOperationException(Loc.T("b-PAC hat die zentrierte Bildposition oder den Druckmaßstab verändert. Kein Testauftrag gestartet. Bitte Diagnose kopieren."));
                }
                finally { Marshal.FinalReleaseComObject((object)imageObject); }
                if (!sendToPrinter)
                {
                    // SDK GetImageData(bexBmp) renders without starting a print job.
                    string margins = "b-PAC-Ränder (1/1440 Zoll) L/R/O/U: " + document.MarginLeft + "/" + document.MarginRight + "/" + document.MarginTop + "/" + document.MarginBottom;
                    byte[] data = (byte[])document.GetImageData(4, media.Media.PixelWidth, media.Media.PixelHeight);
                    if (data == null || data.Length == 0) throw new InvalidOperationException(Loc.T("b-PAC liefert kein Vorschaubild."));
                    string diagnostics = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsnLabeler", "Diagnostics");
                    Directory.CreateDirectory(diagnostics);
                    string previewPath = Path.Combine(diagnostics, "bpac-preview.png");
                    using (var input = new MemoryStream(data))
                    using (var rendered = System.Drawing.Image.FromStream(input)) rendered.Save(previewPath, ImageFormat.Png);
                    using (var source = LabelRenderer.Render(payload, media.Media, rightAligned, copy)) source.Save(Path.Combine(diagnostics, "source-preview.png"), ImageFormat.Png);
                    return margins + "\r\n" + calibrationWarning + "\r\nVorschau ohne Druckauftrag: " + previewPath + "\r\nVergleichsbild: " + Path.Combine(diagnostics, "source-preview.png");
                }
                var current = QueryMedia(document, selected.Name);
                if (current.Id != media.Id || !SameMedia(current.Media, media.Media)) throw new InvalidOperationException(Loc.T("Rolle wurde gewechselt. Bitte erneut prüfen."));
                CheckQueue(selected.Name);
                var completion = new BpacCompletion();
                Action<int, object> callback = delegate(int value, object details)
                {
                    int? error = null;
                    object device = null;
                    try { device = document.Printer; error = Convert.ToInt32(((dynamic)device).ErrorCode); }
                    catch { }
                    finally { if (device != null && Marshal.IsComObject(device)) Marshal.FinalReleaseComObject(device); }
                    completion.Record(value, error);
                };
                // IPrintEvents GUID and DISPID verified in SDK 3.4.017 x64 interop.
                var events = new Guid("d02892da-2fb4-42c7-aa62-1ea5a96daf56");
                ComEventsHelper.Combine((object)document, events, 1, callback);
                try
                {
                    int printOptions = PrintOptions(cutAfter, rightAligned);
                    deadline.Check();
                    if (beforeSend != null) beforeSend();
                    deadline.Check();
                    Check((bool)document.StartPrint(copy ? "ASN Labeler COPY" : beforeSend == null ? "ASN Labeler TEST - keine ASN" : "ASN Labeler", printOptions), "Druckauftrag beginnen", (object)document);
                    deadline.Check();
                    Check((bool)document.PrintOut(1, printOptions), Loc.T("Label hinzufügen"), (object)document);
                    deadline.Check();
                    Check((bool)document.EndPrint(), "Druckauftrag senden", (object)document);
                    var watch = Stopwatch.StartNew();
                    while (!completion.Finished && watch.Elapsed < TimeSpan.FromSeconds(60)) { Application.DoEvents(); Thread.Sleep(20); }
                    completion.EnsureSuccess();
                    return media.Description + "\r\nDataMatrix ECC 200, Inhalt " + payload + "." + (rightAligned ? "\r\nPF-B11221 Compatible: 300×600 dpi, um 90° gedreht." : "") +
                        Loc.T("\r\nb-PAC meldet PrintEnd (0), Printer.ErrorCode=0. Bitte Ausdruck und Scanbarkeit prüfen.\r\n") + calibrationWarning;
                }
                finally { ComEventsHelper.Remove((object)document, events, 1, callback); }
                }); }
                finally { Interlocked.Exchange(ref testInFlight, 0); }
            }, TimeSpan.FromSeconds(90));
        }

        static void RequireImageObject(dynamic document, string stage)
        {
            dynamic objects = document.Objects;
            int count;
            try { count = objects.Count; }
            finally { Marshal.FinalReleaseComObject((object)objects); }
            dynamic label = document.GetObject("LabelBitmap");
            if (label != null) { Marshal.FinalReleaseComObject((object)label); return; }
            string failure = "b-PAC: LabelBitmap fehlt " + stage + ". Erkannte Objekte: " + count + Loc.T(". Kein Testauftrag gestartet.");
            Volatile.Write(ref lastFailure, DateTime.UtcNow.ToString("u") + " | " + failure);
            throw new InvalidOperationException(failure);
        }
    }

    sealed class BpacCompletion
    {
        public bool Finished { get; private set; }
        int status;
        int? printerError;
        public void OnPrinted(int value, object details)
        {
            Record(value, 0);
        }
        public void Record(int value, int? error)
        {
            if (Finished) return;
            status = value;
            printerError = error;
            Finished = true;
        }
        public void EnsureSuccess()
        {
            if (!Finished) throw new TimeoutException(Loc.T("Keine b-PAC-Abschlussmeldung. Druckausgang unklar; Drucker und Warteschlange prüfen."));
            if (status != 0 || printerError != 0) throw new InvalidOperationException("b-PAC meldet Ereignis " + status + ", Druckerfehler " + (printerError.HasValue ? printerError.Value.ToString() : "nicht lesbar") + Loc.T(". Keine erfolgreiche Druckbestätigung."));
        }
    }

    sealed class SubmissionDeadline
    {
        readonly Stopwatch elapsed = Stopwatch.StartNew();
        readonly TimeSpan limit;
        public SubmissionDeadline(TimeSpan limit) { this.limit = limit; }
        public void Check()
        {
            if (elapsed.Elapsed >= limit) throw new TimeoutException(Loc.T("Druckvorbereitung hat das Zeitlimit überschritten. Keine weiteren Druckdaten werden gesendet."));
        }
    }

    static class BpacTemplate
    {
        internal static double EffectiveCorrection(MediaInfo media, double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value) || Math.Abs(value) > 3)
                throw new InvalidOperationException("Ungültige Rollenkorrektur. Erlaubt sind -3 bis +3 mm.");
            return media.IsDieCut && media.WidthMm == 23 && media.LengthMm == 23 ? value : 0;
        }

        internal static string ValidateInkBounds(System.Drawing.Bitmap raster, System.Drawing.Rectangle target, System.Drawing.Rectangle printable, bool calibrationTest = false)
        {
            int left = raster.Width, right = -1, top = raster.Height, bottom = -1;
            for (int y = 0; y < raster.Height; y++)
                for (int x = 0; x < raster.Width; x++)
                    if (raster.GetPixel(x, y).GetBrightness() < 0.5f)
                    { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            if (right < 0) throw new InvalidOperationException(Loc.T("Testbild enthält keine Druckdaten."));
            double x1 = target.Left + left * (double)target.Width / raster.Width;
            double x2 = target.Left + (right + 1) * (double)target.Width / raster.Width;
            double y1 = target.Top + top * (double)target.Height / raster.Height;
            double y2 = target.Top + (bottom + 1) * (double)target.Height / raster.Height;
            if (x1 < printable.Left || x2 > printable.Right || y1 < printable.Top || y2 > printable.Bottom)
            {
                if (calibrationTest)
                    return "Kalibrierwarnung: Druckbild überschreitet die gemeldete Druckfläche. Für TEST0001 zugelassen; Abschneiden und Scanbarkeit am Ausdruck prüfen.";
                throw new InvalidOperationException("Rollenkorrektur überschreitet die vom Treiber gemeldete Druckfläche. Kein Druck gestartet. " +
                    "Der Barcode wird weder abgeschnitten noch automatisch verkleinert. Bitte Diagnose kopieren; ein kleinerer Versatz ist einstellbar.");
            }
            return "";
        }

        internal static System.Drawing.Rectangle CenteredImageFrame(MediaInfo media, int pageWidth, int pageHeight)
        {
            var area = media.RasterArea;
            if (media.IsContinuous)
            {
                var frame = new System.Drawing.Rectangle((int)Math.Round(area.X * 1440.0 / 300),
                    (int)Math.Round(area.Y * 1440.0 / 300), (int)Math.Round(area.Width * 1440.0 / 300),
                    (int)Math.Round(area.Height * 1440.0 / 300));
                if (frame.Left < 0 || frame.Top < 0 || frame.Right > pageWidth || frame.Bottom > pageHeight)
                    throw new InvalidOperationException(Loc.T("Endlosbild außerhalb der Seite: Bild ") + frame + "; Seite " + pageWidth + "x" + pageHeight + " (1/1440 Zoll).");
                return frame;
            }
            int width = (int)Math.Round(area.Width * 1440.0 / 300);
            int height = (int)Math.Round(area.Height * 1440.0 / 300);
            // Continuous tape height is rounded to whole raster dots. Clamp only
            // sub-dot conversion overflow at the physical page boundary.
            if (width > pageWidth && width - pageWidth <= 2) width = pageWidth;
            if (height > pageHeight && height - pageHeight <= 2) height = pageHeight;
            if (width > pageWidth || height > pageHeight || pageWidth <= 0 || pageHeight <= 0)
                throw new InvalidOperationException(Loc.T("Das Testbild passt nicht maßstabsgetreu auf das erkannte Label."));
            return new System.Drawing.Rectangle((pageWidth - width) / 2, (pageHeight - height) / 2, width, height);
        }

        internal static bool MatchesFrame(System.Drawing.Rectangle target, System.Drawing.Rectangle actual)
        {
            // One 300-dpi dot accommodates SDK coordinate quantization.
            const int tolerance = 5;
            return Math.Abs(target.X - actual.X) <= tolerance && Math.Abs(target.Y - actual.Y) <= tolerance &&
                Math.Abs(target.Width - actual.Width) <= tolerance && Math.Abs(target.Height - actual.Height) <= tolerance;
        }

        static readonly XNamespace Pt = "http://schemas.brother.info/ptouch/2007/lbx/main";
        static readonly XNamespace Style = "http://schemas.brother.info/ptouch/2007/lbx/style";
        static readonly XNamespace Image = "http://schemas.brother.info/ptouch/2007/lbx/image";
        static string Points(double mm) { return (mm * 72 / 25.4).ToString("0.#####", CultureInfo.InvariantCulture) + "pt"; }

        public static string Create(string folder, MediaInfo media, string payload, string printer, string fileName = "label.lbx", bool rightAligned = false, bool copy = false)
        {
            string path = Path.Combine(folder, fileName);
            bool compatible = rightAligned && media.IsDieCut && media.WidthMm == 23 && media.LengthMm == 23;
            using (var bitmap = compatible ? LabelRenderer.RenderCompatible(payload, media, copy) : LabelRenderer.Render(payload, media, false, copy))
            {
                var area = media.RasterArea;
                double left = area.Left * 25.4 / 300, top = area.Top * 25.4 / 300;
                double width = area.Width * 25.4 / 300, height = area.Height * 25.4 / 300;
                var image = new XElement(Image + "image",
                    new XElement(Pt + "objectStyle", new XAttribute("x", Points(left)), new XAttribute("y", Points(top)),
                        new XAttribute("width", Points(width)), new XAttribute("height", Points(height)), new XAttribute("angle", "0"),
                        new XAttribute("anchor", "TOPLEFT"), new XAttribute("flip", "NONE"), new XAttribute("ropMode", "COPYPEN"),
                        new XAttribute("backColor", "#FFFFFF"),
                        new XElement(Pt + "pen", new XAttribute("style", "NULL"), new XAttribute("widthX", "0.2pt"), new XAttribute("widthY", "0.2pt"), new XAttribute("color", "#000000")),
                        new XElement(Pt + "brush", new XAttribute("style", "NULL"), new XAttribute("color", "#000000"), new XAttribute("id", "0")),
                        new XElement(Pt + "expanded", new XAttribute("objectName", "LabelBitmap"), new XAttribute("ID", "0"),
                            new XAttribute("lock", "0"), new XAttribute("templateMergeTarget", "LABELLIST"), new XAttribute("templateMergeType", "NONE"),
                            new XAttribute("templateMergeID", "0"), new XAttribute("linkStatus", "NONE"), new XAttribute("linkID", "0"))),
                    new XElement(Image + "imageStyle", new XAttribute("fileName", "Object0.bmp"), new XAttribute("originalName", "Object0.bmp"),
                        new XAttribute("alignInText", "LEFT"), new XAttribute("firstMerge", "true"),
                        new XElement(Image + "transparent", new XAttribute("flag", "false"), new XAttribute("color", "#FFFFFF")),
                        new XElement(Image + "trimming", new XAttribute("flag", "false"), new XAttribute("shape", "RECTANGLE"),
                            new XAttribute("trimOrgX", "0pt"), new XAttribute("trimOrgY", "0pt"), new XAttribute("trimOrgWidth", Points(width)), new XAttribute("trimOrgHeight", Points(height))),
                        new XElement(Image + "orgPos", new XAttribute("x", Points(left)), new XAttribute("y", Points(top)),
                            new XAttribute("width", Points(width)), new XAttribute("height", Points(height))),
                        new XElement(Image + "effect", new XAttribute("effect", "NONE"), new XAttribute("brightness", "50"), new XAttribute("contrast", "50"), new XAttribute("photoIndex", "4"))));
                var xml = new XDocument(new XElement(Pt + "document", new XAttribute(XNamespace.Xmlns + "pt", Pt),
                    new XAttribute(XNamespace.Xmlns + "style", Style), new XAttribute(XNamespace.Xmlns + "image", Image),
                    new XAttribute("version", "1.1"), new XAttribute("generator", "ASN Labeler"),
                    new XElement(Pt + "body", new XAttribute("currentSheet", "Label"),
                        new XElement(Style + "sheet", new XAttribute("name", "Label"),
                            new XElement(Style + "paper", new XAttribute("media", "0"), new XAttribute("width", Points(media.WidthMm)),
                                new XAttribute("height", Points(media.PageLengthMm)), new XAttribute("orientation", "portrait"),
                                new XAttribute("autoLength", "false"), new XAttribute("printerName", printer), new XAttribute("monochromeDisplay", "true"),
                                new XAttribute("marginLeft", Points(left)), new XAttribute("marginTop", Points(top)),
                                new XAttribute("marginRight", Points(Math.Max(0, media.WidthMm - left - width))),
                                new XAttribute("marginBottom", Points(Math.Max(0, media.PageLengthMm - top - height)))),
                            new XElement(Pt + "objects", image)))));
                using (var file = File.Create(path))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    // Match the SDK samples' compact UTF-8 XML. Avoid incidental
                    // whitespace nodes and BOMs in the legacy template reader.
                    using (var stream = zip.CreateEntry("label.xml").Open()) SaveXml(xml, stream);
                    XNamespace meta = "http://schemas.brother.info/ptouch/2007/lbx/meta";
                    XNamespace dc = "http://purl.org/dc/elements/1.1/";
                    var properties = new XDocument(new XElement(meta + "properties",
                        new XAttribute(XNamespace.Xmlns + "meta", meta), new XAttribute(XNamespace.Xmlns + "dc", dc),
                        new XElement(meta + "appName", "ASN Labeler"), new XElement(dc + "title", "Testlabel"),
                        new XElement(meta + "numPages", "1"), new XElement(meta + "security", "0")));
                    using (var stream = zip.CreateEntry("prop.xml").Open()) SaveXml(properties, stream);
                    var bitmapArea = compatible ? new System.Drawing.Rectangle(area.Left, area.Top * 2, area.Width, area.Height * 2) : area;
                    using (var cropped = bitmap.Clone(bitmapArea, PixelFormat.Format24bppRgb))
                    {
                        cropped.SetResolution(300, compatible ? 600 : 300);
                        cropped.Save(Path.Combine(folder, "print.bmp"), ImageFormat.Bmp);
                        using (var stream = zip.CreateEntry("Object0.bmp").Open()) cropped.Save(stream, ImageFormat.Bmp);
                    }
                }
            }
            return path;
        }

        static void SaveXml(XDocument document, Stream stream)
        {
            var settings = new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false };
            using (var writer = System.Xml.XmlWriter.Create(stream, settings)) document.Save(writer);
        }
    }

    static class StaOperation
    {
        public static T Run<T>(Func<T> operation, TimeSpan timeout)
        {
            var completion = new TaskCompletionSource<T>();
            var thread = new Thread(delegate()
            {
                try
                {
                    using (var context = new ApplicationContext())
                    using (var dispatcher = new Control())
                    {
                        dispatcher.CreateControl();
                        dispatcher.BeginInvoke(new Action(delegate
                        {
                            try { completion.TrySetResult(operation()); }
                            catch (Exception ex) { completion.TrySetException(ex); }
                            finally { context.ExitThread(); }
                        }));
                        Application.Run(context);
                    }
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            thread.IsBackground = true;
            thread.Name = "Brother b-PAC STA";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            // A stalled COM activation cannot safely be aborted. It remains on its
            // background STA; no UI control or print operation is handed to it.
            if (Task.WhenAny(completion.Task, Task.Delay(timeout)).GetAwaiter().GetResult() != completion.Task)
            {
                completion.Task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException(Loc.T("Brother-b-PAC antwortet nicht innerhalb des Zeitlimits. Bei einem gestarteten Testdruck ist der Ausgang unklar; nicht automatisch erneut drucken."));
            }
            return completion.Task.GetAwaiter().GetResult();
        }
    }

    static class PrintLifecycleTests
    {
        sealed class FakePrinter : IPrinterSession
        {
            public Action<Action> Send;
            public PrinterStatus ReadStatus() { return PrinterStatus.Ready(MediaInfo.DefaultDk11221()); }
            public bool CutAfter;
            public void Print(string text, MediaInfo media, Action beforeSend, bool cutAfter = true) { CutAfter = cutAfter; Send(beforeSend); }
            public void Dispose() { }
        }
        public static IEnumerable<string> Run()
        {
            var failures = new List<string>();
            int cliCount; bool cliCut;
            foreach (var invalidArgs in new[] { new[] { "print", "--count", "0" }, new[] { "print", "--count", "11" },
                new[] { "print", "--count" }, new[] { "print", "--unknown" }, new[] { "print", "--cut-each", "--cut-each" } })
                if (HeadlessCli.Parse(invalidArgs, out cliCount, out cliCut)) failures.Add("CLI accepted invalid arguments.");
            if (!HeadlessCli.Parse(new[] { "print", "--count", "10", "--cut-each" }, out cliCount, out cliCut) || cliCount != 10 || !cliCut)
                failures.Add("CLI argument parsing failed.");
            if (!HeadlessCli.Parse(new[] { "print", "-c", "3", "-s" }, out cliCount, out cliCut) || cliCount != 3 || !cliCut)
                failures.Add("CLI short flags failed.");
            foreach (var invalidArgs in new[] { new[] { "print", "-c", "0" }, new[] { "print", "-c" },
                new[] { "print", "-s", "--cut-each" }, new[] { "print", "-c", "2", "--count", "3" } })
                if (HeadlessCli.Parse(invalidArgs, out cliCount, out cliCut)) failures.Add("CLI accepted duplicate/invalid short flags.");
            foreach (string scenario in new[] { "success", "preflight", "reserve", "prepare", "handshake", "send", "ack" })
            {
                int allocated = 0, acknowledged = 0, released = 0;
                var cuts = new List<bool>();
                var printer = new FakePrinter { Send = before => {
                    if (scenario == "prepare") throw new InvalidOperationException();
                    before();
                    if (scenario == "send") throw new TimeoutException();
                } };
                var output = new StringWriter(); var errors = new StringWriter();
                int result = HeadlessCli.Print(2, false, new AppConfig(), () => printer,
                    () => { if (scenario == "preflight") throw new TimeoutException(); return new Reservation { barcodeText = "ASN00001" }; },
                    () => { if (scenario == "reserve") throw new TimeoutException(); allocated++; return new Reservation { reservationId = "test", barcodeText = "ASN00001" }; },
                    id => { if (scenario == "handshake") throw new TimeoutException(); },
                    id => { if (scenario == "ack") throw new TimeoutException(); acknowledged++; cuts.Add(printer.CutAfter); },
                    id => released++, output, errors);
                int expected = scenario == "success" ? 0 : new[] { "handshake", "send", "ack" }.Contains(scenario) ? 5 : 4;
                if (result != expected) failures.Add("CLI exit code: " + scenario);
                if (released != (scenario == "prepare" ? 1 : 0)) failures.Add("CLI unsafe release: " + scenario);
                if (allocated > (scenario == "success" ? 2 : 1)) failures.Add("CLI continued after failure.");
                if (scenario == "success" && (acknowledged != 2 || !cuts.SequenceEqual(new[] { false, true }))) failures.Add("CLI batch/cut policy failed.");
            }
            foreach (int invalid in new[] { 0, 11 })
            {
                try { new PrintBatch(invalid); failures.Add("Invalid batch quantity accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            foreach (int count in new[] { 1, 2, 10 })
            {
                foreach (bool individual in new[] { false, true })
                {
                    var cuts = new PrintBatch(count, individual);
                    cuts.Run(index =>
                    {
                        bool expected = individual || index == count;
                        using (var fake = new FakePrinter { Send = before => before() })
                        {
                            new PrintLifecycle().Submit(fake, "ASN00001", MediaInfo.DefaultDk11221(), () => { }, () => { }, cuts.CutAfter(index));
                            if (fake.CutAfter != expected) failures.Add("Cut policy lost in backend submission.");
                        }
                        foreach (bool highResolution in new[] { false, true })
                        {
                            int options = BpacPrinterSession.PrintOptions(cuts.CutAfter(index), highResolution);
                            int cutBits = options & ~0x02000000;
                            if (cutBits != (expected ? 0x04000001 : 0x10000000)) failures.Add("Incorrect SDK cut options.");
                            if (((options & 0x02000000) != 0) != highResolution) failures.Add("Cut option changed PF resolution.");
                        }
                    });
                }
                var batch = new PrintBatch(count);
                var sequence = new List<int>();
                batch.Run(index => sequence.Add(index));
                if (batch.Completed != count || !sequence.SequenceEqual(Enumerable.Range(1, count))) failures.Add("Batch ordering failed.");
                try { batch.Run(index => sequence.Add(index)); failures.Add("Batch sent twice."); }
                catch (InvalidOperationException) { }
            }
            foreach (int stop in Enumerable.Range(1, 10))
            {
                var batch = new PrintBatch(10);
                var sequence = new List<int>();
                try { batch.Run(index => { sequence.Add(index); if (index == stop) throw new TimeoutException(); }); }
                catch (TimeoutException) { }
                if (batch.Completed != stop - 1 || sequence.Count != stop) failures.Add("Batch continued after uncertain print.");
            }
            foreach (string scenario in new[] { "success", "prepare", "handshake", "send", "timeout", "ack", "duplicate", "missing-handshake" })
            {
                var calls = new List<string>();
                var lifecycle = new PrintLifecycle();
                using (var printer = new FakePrinter { Send = before =>
                {
                    if (scenario == "prepare") throw new InvalidOperationException("media changed");
                    if (scenario == "missing-handshake") return;
                    before();
                    if (scenario == "duplicate") before();
                    calls.Add("send");
                    if (scenario == "send") throw new InvalidOperationException("USB lost");
                    if (scenario == "timeout") throw new TimeoutException();
                } })
                {
                    bool threw = false;
                    try
                    {
                        lifecycle.Submit(printer, "ASN00001", MediaInfo.DefaultDk11221(), () =>
                        {
                            calls.Add("printing");
                            if (scenario == "handshake") throw new TimeoutException("lost API response");
                        }, () =>
                        {
                            calls.Add("printed");
                            if (scenario == "ack") throw new TimeoutException("API unavailable");
                        });
                    }
                    catch (Exception) { threw = true; }
                    if (threw != (scenario != "success")) failures.Add("Lifecycle exception: " + scenario);
                    if (lifecycle.HandshakeStarted != (scenario != "prepare" && scenario != "missing-handshake")) failures.Add("Unsafe release eligibility: " + scenario);
                    if (lifecycle.PrintConfirmed != (scenario == "success" || scenario == "ack")) failures.Add("Incorrect completion: " + scenario);
                    if (lifecycle.ApiConfirmed != (scenario == "success")) failures.Add("Incorrect API acknowledgement: " + scenario);
                    if (scenario == "success" && String.Join(",", calls) != "printing,send,printed") failures.Add("Print/API order changed.");
                    if ((scenario == "handshake" || scenario == "duplicate") && calls.Contains("send")) failures.Add("Sent without unique API permission.");
                    if ((scenario == "send" || scenario == "timeout") && calls.Contains("printed")) failures.Add("Uncertain print acknowledged.");
                }
            }
            return failures;
        }
    }

    static class PrinterBackendTests
    {
        public static IEnumerable<string> Run()
        {
            var failures = new List<string>();
            var profileConfig = new AppConfig();
            var legacy = new AppConfig { PrinterBackend = PrinterBackends.WinUsb, DevicePath = "preserve-legacy-path" };
            legacy.NormalizePrinterBackend();
            if (legacy.PrinterBackend != PrinterBackends.Brother || !legacy.LegacyPrinterMigrated || legacy.DevicePath != "preserve-legacy-path")
                failures.Add("Legacy printer migration loses settings or keeps WinUSB active.");
            profileConfig.NormalizePrinterBackend();
            if (profileConfig.LegacyPrinterMigrated) failures.Add("New b-PAC installation shows legacy migration warning.");
            if (profileConfig.RollCorrectionMm != 0) failures.Add("Default roll profile changes original media.");
            profileConfig.PfRollProfile = true;
            if (profileConfig.RollCorrectionMm != 0) failures.Add("Legacy PF correction is still applied.");
            profileConfig.PfRightAligned = true;
            if (profileConfig.RollCorrectionMm != 0 || !profileConfig.UsePfRightAligned) failures.Add("Right alignment stacks with manual correction.");
            profileConfig.PrinterBackend = PrinterBackends.WinUsb;
            if (profileConfig.RollCorrectionMm != 0) failures.Add("PF profile leaks into WinUSB.");
            if (profileConfig.UsePfRightAligned) failures.Add("Right alignment leaks into WinUSB.");
            foreach (string payload in new[] { "TEST0001", "ASN00001", "ASN99999", "ASN100000" })
            {
                var media = MediaInfo.DefaultDk11221();
                using (var aligned = LabelRenderer.RenderCompatible(payload, media))
                using (var cropped = aligned.Clone(new System.Drawing.Rectangle(media.RasterArea.Left, media.RasterArea.Top * 2,
                    media.RasterArea.Width, media.RasterArea.Height * 2), PixelFormat.Format24bppRgb))
                {
                    DataMatrixTests.CheckDecode(cropped, payload, failures);
                    BpacTemplate.ValidateInkBounds(cropped, BpacTemplate.CenteredImageFrame(media, 1306, 1306),
                        new System.Drawing.Rectangle(86, 168, 1132, 968));
                }
            }
            if (BpacTemplate.EffectiveCorrection(MediaInfo.DefaultDk11221(), 2) != 2 ||
                BpacTemplate.EffectiveCorrection(MediaInfo.FromStatus(0x4A, 62, 0), 2) != 0)
                failures.Add("Roll correction is applied to wrong media.");
            try { BpacTemplate.EffectiveCorrection(MediaInfo.DefaultDk11221(), Double.NaN); failures.Add("Invalid offset accepted."); }
            catch (InvalidOperationException) { }
            using (var full = LabelRenderer.Render("TEST0001", MediaInfo.DefaultDk11221()))
            using (var raster = full.Clone(MediaInfo.DefaultDk11221().RasterArea, PixelFormat.Format24bppRgb))
            {
                var centered = BpacTemplate.CenteredImageFrame(MediaInfo.DefaultDk11221(), 1306, 1306);
                var printable = new System.Drawing.Rectangle(86, 168, 1132, 968);
                BpacTemplate.ValidateInkBounds(raster, centered, printable);
                centered.Offset(450, 0);
                if (!BpacTemplate.ValidateInkBounds(raster, centered, printable, true).Contains("Kalibrierwarnung"))
                    failures.Add("Calibration test should warn about a clipping offset.");
                try { BpacTemplate.ValidateInkBounds(raster, centered, printable); failures.Add("Clipping shift was accepted."); }
                catch (InvalidOperationException) { }
            }
            string renderFailure = BpacPrinterSession.DescribeFailure(Loc.T("Testlabel hinzufügen"), 17498114);
            if (!renderFailure.Contains("17498114 / 0x010B0002") || !renderFailure.Contains("Bildobjekt"))
                failures.Add("b-PAC rendering error lost its SDK code or description.");
            if (!BpacPrinterSession.DescribeFailure("Test", null).Contains(Loc.T("Code nicht verfügbar")) ||
                !BpacPrinterSession.DescribeFailure("Test", 123456).Contains("123456"))
                failures.Add("Unknown b-PAC errors are not preserved.");
            if (!PrinterBackends.IsQl800Driver("Brother QL-800") || PrinterBackends.IsQl800Driver("Brother QL-8000") || PrinterBackends.IsQl800Driver("Brother QL-810W"))
                failures.Add("Printer driver model matching failed.");
            if (PrinterBackends.KnownModel("Brother QL-810W") != "QL-810W" || PrinterBackends.KnownModel("Brother QL-820NWB") != "QL-820NWB" || PrinterBackends.KnownModel("Brother QL-8000") != null)
                failures.Add("Experimental model matching failed.");
            foreach (string model in new[] { "Brother QL-810W", "Brother QL-820NWB" })
                try { PrinterBackends.RequireProductionModel(new PrinterChoice { Driver = model }); failures.Add("Experimental production printing allowed."); }
                catch (InvalidOperationException) { }
            var a = new PrinterChoice { Name = "Mein Labeldrucker", Driver = "Brother QL-800" };
            var b = new PrinterChoice { Name = "Zweiter Drucker" };
            if (PrinterBackends.Select(new List<PrinterChoice> { a }, "") != a) failures.Add("Unique printer was not selected.");
            try { PrinterBackends.Select(new List<PrinterChoice> { a, b }, ""); failures.Add("Ambiguous printer selected silently."); } catch (InvalidOperationException) { }
            try { PrinterBackends.Select(new List<PrinterChoice> { a }, "Removed"); failures.Add("Missing selection fell back silently."); } catch (InvalidOperationException) { }
            if (StaOperation.Run(() => Thread.CurrentThread.GetApartmentState(), TimeSpan.FromSeconds(5)) != ApartmentState.STA)
                failures.Add("Brother worker is not STA.");
            try { StaOperation.Run<int>(() => { throw new InvalidOperationException("test"); }, TimeSpan.FromSeconds(5)); failures.Add("STA error lost."); } catch (InvalidOperationException) { }
            failures.AddRange(PrintLifecycleTests.Run());
            BpacPrinterSession.RequireUsb("USB001");
            foreach (string port in new[] { "", "IP_192.0.2.1", "FILE:" })
                try { BpacPrinterSession.RequireUsb(port); failures.Add("Non-USB production print allowed."); } catch (InvalidOperationException) { }
            if (!PrinterStatus.Ready(MediaInfo.DefaultDk11221()).CanPrint) failures.Add("Verified media cannot become ready.");
            if (BpacPrinterSession.SameMedia(MediaInfo.DefaultDk11221(), MediaInfo.FromStatus(0x4A, 23, 0))) failures.Add("Media kind change ignored.");
            BpacPrinterSession.ValidateWindowsStatus(false, 3, 2);
            foreach (int state in new[] { 4, 5, 6, 7 })
                try { BpacPrinterSession.ValidateWindowsStatus(false, state, 2); failures.Add("Busy/offline Windows status accepted."); } catch (InvalidOperationException) { }
            foreach (int error in new[] { 1, 4, 5, 7, 8, 9, 11 })
                try { BpacPrinterSession.ValidateWindowsStatus(false, 3, error); failures.Add("Windows printer error accepted."); } catch (InvalidOperationException) { }
            try { new SubmissionDeadline(TimeSpan.Zero).Check(); failures.Add("Expired COM operation can submit late."); } catch (TimeoutException) { }
            if (BpacPrinterSession.ParseMedia("23 mm x 23 mm").WidthMm != 23 || !BpacPrinterSession.ParseMedia("62mm").IsContinuous)
                failures.Add("Brother media mapping failed.");
            try { BpacPrinterSession.ParseMedia("Unknown roll"); failures.Add("Unknown media used a default."); } catch (InvalidOperationException) { }
            var missing = new BpacCompletion();
            try { missing.EnsureSuccess(); failures.Add("Missing completion accepted."); } catch (TimeoutException) { }
            for (int status = 1; status <= 5; status++)
            {
                var failed = new BpacCompletion();
                failed.OnPrinted(status, null);
                failed.OnPrinted(0, null);
                try { failed.EnsureSuccess(); failures.Add("Error completion overwritten by success."); } catch (InvalidOperationException) { }
            }
            var completed = new BpacCompletion();
            completed.OnPrinted(0, null);
            completed.OnPrinted(0, null);
            completed.EnsureSuccess();
            foreach (int? error in new int?[] { null, 1, 22 })
            {
                var deviceFailure = new BpacCompletion();
                deviceFailure.Record(0, error);
                try { deviceFailure.EnsureSuccess(); failures.Add("PrintEnd accepted with unknown/device error."); } catch (InvalidOperationException) { }
            }
            string folder = Path.Combine(Path.GetTempPath(), "AsnLabeler-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                foreach (int widthMm in new[] { 12, 29, 38, 50, 54, 62 })
                {
                    var tape = MediaInfo.FromStatus(0x4A, widthMm, 0);
                    int pageWidth = (int)Math.Round(widthMm * 1440.0 / 25.4);
                    int pageHeight = (int)Math.Round(tape.PageLengthMm * 1440.0 / 25.4);
                    foreach (int feedMargin in new[] { 0, 170 })
                    {
                        var actual = tape.WithDriverPrintableArea(pageWidth, pageHeight, 86, feedMargin, 88, feedMargin);
                        var frame = BpacTemplate.CenteredImageFrame(actual, pageWidth, pageHeight);
                        var printable = new System.Drawing.Rectangle(86, feedMargin, pageWidth - 174, pageHeight - 2 * feedMargin);
                        using (var rendered = LabelRenderer.Render("ASN00001", actual))
                        using (var cropped = rendered.Clone(actual.RasterArea, PixelFormat.Format24bppRgb))
                        {
                            DataMatrixTests.CheckDecode(rendered, "ASN00001", failures);
                            BpacTemplate.ValidateInkBounds(cropped, frame, printable);
                            if (Math.Abs(frame.Height * 300.0 / 1440 - cropped.Height) > 0.11)
                                failures.Add("Continuous bitmap is scaled rather than cropped to printer margins.");
                        }
                        BpacTemplate.Create(folder, actual, "ASN00001", "Test Brother QL-800", "test.lbx");
                        using (var bitmap = System.Drawing.Image.FromFile(Path.Combine(folder, "print.bmp")))
                            if (bitmap.Width != actual.RasterArea.Width || bitmap.Height != actual.RasterArea.Height)
                                failures.Add("Continuous template and preview disagree.");
                    }
                    try { tape.WithDriverPrintableArea(pageWidth, 10, 86, 170, 88, 170); failures.Add("Invalid continuous dimensions accepted."); }
                    catch (InvalidOperationException) { }
                }
                string highRes = BpacTemplate.Create(folder, MediaInfo.DefaultDk11221(), "TEST0001", "Test Brother QL-800", "test.lbx", true);
                using (var input = File.OpenRead(highRes))
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
                using (var bmp = zip.GetEntry("Object0.bmp").Open())
                using (var raster = System.Drawing.Image.FromStream(bmp))
                    if (raster.Width != 236 || raster.Height != 404 || Math.Abs(raster.HorizontalResolution - 300) > 1 || Math.Abs(raster.VerticalResolution - 600) > 1)
                        failures.Add("Compatible LBX raster does not preserve 300x600 DPI.");
                foreach (var media in new[] { MediaInfo.DefaultDk11221(), MediaInfo.FromStatus(0x4A, 62, 0) })
                {
                    int pageWidth = (int)Math.Round(media.WidthMm * 1440.0 / 25.4);
                    int pageHeight = (int)Math.Round(media.PageLengthMm * 1440.0 / 25.4);
                    var frame = BpacTemplate.CenteredImageFrame(media, pageWidth, pageHeight);
                    if ((!media.IsContinuous && (Math.Abs(frame.Left - (pageWidth - frame.Right)) > 1 ||
                        Math.Abs(frame.Top - (pageHeight - frame.Bottom)) > 1)) ||
                        Math.Abs(frame.Width * 300.0 / 1440 - media.RasterArea.Width) > 0.11)
                        failures.Add("b-PAC image is not centered at native raster scale.");
                    var shifted = frame;
                    shifted.Offset(72, 0);
                    if (BpacTemplate.MatchesFrame(frame, shifted)) failures.Add("b-PAC accepted shifted image placement.");
                    using (var rendered = LabelRenderer.Render("TEST0001", media))
                        DataMatrixTests.CheckDecode(rendered, "TEST0001", failures);
                    string file = BpacTemplate.Create(folder, media, "TEST0001", "Test Brother QL-800");
                    // Simulate the SDK keeping the bootstrap LBX exclusively open.
                    using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        BpacTemplate.Create(folder, media, "TEST0001", "Test Brother QL-800", "test.lbx");
                        using (var testLocked = new FileStream(Path.Combine(folder, "test.lbx"), FileMode.Open, FileAccess.Read, FileShare.None))
                        using (var preparedImage = System.Drawing.Image.FromFile(Path.Combine(folder, "print.bmp")))
                            if (preparedImage.Width != media.RasterArea.Width || preparedImage.Height != media.RasterArea.Height)
                                failures.Add("Prepared b-PAC image is invalid while templates are locked.");
                    }
                    using (var stream = File.OpenRead(file))
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        if (zip.GetEntry("prop.xml") == null) failures.Add("LBX package metadata is missing.");
                        using (var raw = zip.GetEntry("label.xml").Open())
                        {
                            if (raw.ReadByte() != '<') failures.Add("LBX XML has an unexpected byte-order mark.");
                        }
                        using (var xml = zip.GetEntry("label.xml").Open())
                        {
                            var document = XDocument.Load(xml, LoadOptions.PreserveWhitespace);
                            if (document.DescendantNodes().OfType<XText>().Any(t => String.IsNullOrWhiteSpace(t.Value)))
                                failures.Add("LBX XML has formatting whitespace absent from SDK samples.");
                            XNamespace pt = "http://schemas.brother.info/ptouch/2007/lbx/main";
                            XNamespace style = "http://schemas.brother.info/ptouch/2007/lbx/style";
                            XNamespace img = "http://schemas.brother.info/ptouch/2007/lbx/image";
                            var labelImage = document.Root.Element(pt + "body").Element(style + "sheet").Element(pt + "objects").Element(img + "image");
                            if ((string)labelImage.Element(pt + "objectStyle").Element(pt + "expanded").Attribute("objectName") != "LabelBitmap" ||
                                labelImage.Element(img + "imageStyle").Element(img + "orgPos") == null)
                                failures.Add("LBX image identity or original dimensions are missing.");
                        }
                        using (var data = zip.GetEntry("Object0.bmp").Open())
                        using (var image = new System.Drawing.Bitmap(data))
                        {
                            if (image.Width != media.RasterArea.Width || image.Height != media.RasterArea.Height)
                                failures.Add("LBX image dimensions differ from preview raster.");
                            DataMatrixTests.CheckDecode(image, "TEST0001", failures);
                        }
                    }
                }
            }
            finally
            {
                File.Delete(Path.Combine(folder, "label.lbx"));
                File.Delete(Path.Combine(folder, "test.lbx"));
                File.Delete(Path.Combine(folder, "print.bmp"));
                Directory.Delete(folder);
            }
            return failures;
        }
    }
}
