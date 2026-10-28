using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace AsnLabeler
{
    static class HeadlessCli
    {
        static void Write(TextWriter output, object value, string message, bool json)
        { output.WriteLine(json ? new JavaScriptSerializer().Serialize(value) : message); output.Flush(); }

        static void Progress(TextWriter output, string message, bool json)
        { if (!json) { output.WriteLine(message); output.Flush(); } }

        public static int Run(string[] args)
        {
            try { Loc.Language = AppConfig.Load().Language; } catch { }
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            bool json = args.Contains("-j") || args.Contains("--json");
            if (args.Length == 1 && (args[0] == "-v" || args[0] == "--version")) { Console.WriteLine(AppRelease.Version); return 0; }
            if (args.Length == 0 || (args.Length == 1 && (args[0] == "help" || args[0] == "-h" || args[0] == "--help")) || (args.Length==2 && args[0]=="print" && (args[1]=="-h" || args[1]=="--help")))
            {
                Console.WriteLine("ASN Labeler " + AppRelease.Version + "\n\n" + Loc.T("Syntax:\n  asnlbl print [-c ANZAHL] [-s] [-j]\n  asnlbl help\n  asnlbl -v\n\nOptionen:\n  -c ANZAHL  1 bis 10 Labels (Standard: 1)\n  -s         Jedes Label einzeln schneiden (sonst am Serienende)\n  -j         JSON-Ausgabe für Skripte statt lesbarer Texte\n  -h         Hilfe anzeigen\n  -v         Version anzeigen\n\nAliase: --count, --cut-each, --json, --help, --version\n\nBeispiele:\n  asnlbl print\n  asnlbl print -c 3 -s\n  asnlbl print -c 3 -j\n\nZuerst die App einrichten, Demo-Modus ausschalten und die App schließen.\nFehlgeschlagene Serien niemals ungeprüft wiederholen.\n\nExitcodes: 0 Erfolg, 2 Argumente/Konfiguration/Verbindung, 3 bereits aktiv,\n4 aktueller Druck nicht gestartet, 5 Druckausgang prüfen."));
                Console.WriteLine("\n"+Loc.T("Drucker und Medium abfragen (ohne Druck):\n  asnlbl -m [-j]\n  -m / --media  Modell und aktuell eingelegte Rolle anzeigen"));
                return 0;
            }
            int count;
            bool cutEach;
            bool media = IsMediaCommand(args);
            count=1; cutEach=false;
            if (!media && !Parse(args, out count, out cutEach)) { Write(Console.Error, new { error = "invalid_arguments" }, Loc.T("Ungültiger Befehl. Beispiel: asnlbl print -c 3. Hilfe: asnlbl help. Anzahl: 1 bis 10."), json); return 2; }
            try
            {
                ClientRuntime.RequireInstallation();
                if(!media) {
                    Progress(Console.Out,Loc.T("Sicherheitsfreigabe wird geprüft…"),json);
                    ClientUpdates.CheckSafety();
                }
                if (!media && ClientRuntime.UpdateRequired)
                { Write(Console.Error, new { error = "client_update_required", minimumVersion = ClientRuntime.EffectiveMinimum }, Loc.T("Update erforderlich. Bitte die App aktualisieren. Mindestversion: ")+ClientRuntime.EffectiveMinimum,json); return 2; }
                Mutex setup;
                if (Mutex.TryOpenExisting("Global\\AsnLabelerSetup", out setup))
                { setup.Dispose(); Write(Console.Error, new { error = "setup_running" },Loc.T("Das Setup läuft gerade. Bitte die Installation abwarten."),json); return 3; }
                bool created;
                using (var single = new Mutex(true, "Local\\AsnLabelerBrotherQl800", out created))
                {
                    if (!created) { Write(Console.Error, new { error = "close_gui_or_other_cli_first" },Loc.T("ASN Labeler ist bereits aktiv. Bitte die App schließen oder den laufenden CLI-Auftrag abwarten."),json); return 3; }
                    var security = new System.Security.AccessControl.MutexSecurity();
                    security.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(
                        new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                        System.Security.AccessControl.MutexRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
                    using (var presence = new Mutex(false, "Global\\AsnLabelerRunning", out created, security))
                    {
                        var config = AppConfig.Load();
                        if(media) {
                            var selected=PrinterBackends.Select(PrinterBackends.DiscoverBrother(),config.PrinterName);
                            return ReadMedia(selected,()=>new BpacPrinterSession(selected,config.UsePfRightAligned),Console.Out,Console.Error,json);
                        }
                        if (config.DemoMode || String.IsNullOrWhiteSpace(config.ApiKey))
                        { Write(Console.Error, new { error = "configure_gui_and_disable_demo_first" },Loc.T("Bitte zuerst die App einrichten, den API-Schlüssel hinterlegen und den Demo-Modus ausschalten."),json); return 2; }
                        var client = new ReservationClient(config);
                        Progress(Console.Out,Loc.T("Serververbindung wird geprüft…"),json);
                        client.Health();
                        return Print(count, cutEach, config, () => PrinterBackends.Open(config), client.PeekNext,
                            client.Next, client.MarkPrinting, client.MarkPrinted, client.Release, Console.Out, Console.Error,json);
                    }
                }
            }
            catch (Exception ex)
            {
                // API responses can contain sensitive URLs. Emit a stable category,
                // not raw exception text, to terminal logs.
                Write(Console.Error, new { error = media ? "printer_query_failed" : "configuration_or_connection_failed", kind = ex.GetType().Name },Loc.T(media ? "Druckerabfrage fehlgeschlagen. Bitte Druckerauswahl, USB-Verbindung und b-PAC prüfen. Fehlerart: " : "Start fehlgeschlagen. Bitte Installation und Serververbindung in den App-Einstellungen prüfen. Fehlerart: ")+ex.GetType().Name,json);
                return 2;
            }
        }

        internal static bool IsMediaCommand(string[] args)
        {
            return args.Length>=1 && args.Length<=2 && args.Count(a=>a=="-m" || a=="--media")==1 &&
                args.All(a=>a=="-m" || a=="--media" || a=="-j" || a=="--json");
        }

        internal static int ReadMedia(PrinterChoice selected, Func<IPrinterSession> open, TextWriter output, TextWriter error, bool json)
        {
            try {
                using(var printer=open()) {
                    var status=printer.ReadStatus();
                    var medium=status.Media;
                    bool hasMedia=medium!=null && medium.HasMedia;
                    string model=PrinterBackends.KnownModel(selected.Driver);
                    string description=hasMedia ? medium.ShortName : Loc.T("Kein Medium erkannt");
                    string message=Loc.T("Modell: ")+model+Environment.NewLine+Loc.T("Drucker: ")+selected.Name+Environment.NewLine+
                        Loc.T("Medium: ")+description+Environment.NewLine+Loc.T("Druckbereit: ")+Loc.T(status.CanPrint ? "Ja" : "Nein");
                    Write(output,new {status="printer_info",model=model,printer=selected.Name,medium=description,
                        widthMm=hasMedia ? (int?)medium.WidthMm : null,lengthMm=hasMedia && !medium.IsContinuous ? (int?)medium.LengthMm : null,
                        continuous=hasMedia && medium.IsContinuous,ready=status.CanPrint},message,json);
                    return 0;
                }
            } catch(Exception ex) {
                Write(error,new {error="printer_query_failed",kind=ex.GetType().Name},
                    Loc.T("Druckerabfrage fehlgeschlagen. Bitte Druckerauswahl, USB-Verbindung und b-PAC prüfen. Fehlerart: ")+ex.GetType().Name,json);
                return 2;
            }
        }

        internal static bool Parse(string[] args, out int count, out bool cutEach)
        {
            count = 1; cutEach = false; bool countSeen = false, jsonSeen = false;
            if (args.Length == 0 || args[0] != "print") return false;
            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "-s" || args[i] == "--cut-each") && !cutEach) cutEach = true;
                else if ((args[i] == "-j" || args[i] == "--json") && !jsonSeen) jsonSeen = true;
                else if ((args[i] == "-c" || args[i] == "--count") && !countSeen && ++i < args.Length && Int32.TryParse(args[i], out count)) countSeen = true;
                else return false;
            }
            return count >= 1 && count <= 10;
        }

        internal static int Print(int count, bool cutEach, AppConfig config, Func<IPrinterSession> open,
            Func<Reservation> peek, Func<Reservation> next, Action<string> printing, Action<string> printed,
            Action<string> release, TextWriter output, TextWriter error, bool json = false)
        {
            var batch = new PrintBatch(count, cutEach);
            Reservation current = null;
            var lifecycle = new PrintLifecycle();
            try
            {
                Progress(output,String.Format(Loc.T("Druckserie: {0} Label(s). Schnitt: {1}."),count,Loc.T(cutEach ? "nach jedem Label" : "am Serienende")),json);
                batch.Run(index =>
                {
                    current = null; lifecycle = new PrintLifecycle();
                    Progress(output,String.Format(Loc.T("[{0}/{1}] Drucker und eingelegte Rolle werden geprüft…"),index,count),json);
                    using (var printer = open())
                    {
                        var status = printer.ReadStatus();
                        if (!status.CanPrint) throw new InvalidOperationException("printer_not_ready");
                        using (var bitmap = LabelRenderer.Render(peek().barcodeText, status.Media, config.UsePfRightAligned)) { }
                        current = next();
                        Write(output, new { status = "reserved", asn = current.barcodeText, reservationId = current.reservationId },String.Format(Loc.T("[{0}/{1}] {2} wird gedruckt. Warte auf Druckbestätigung…"),index,count,current.barcodeText),json);
                        lifecycle.Submit(printer, current.barcodeText, status.Media,
                            () => printing(current.reservationId), () => printed(current.reservationId), batch.CutAfter(index));
                        Write(output, new { status = "printed", asn = current.barcodeText, completed = index, total = count },String.Format(Loc.T("[{0}/{1}] {2}: Druck und Serverbestätigung erfolgreich."),index,count,current.barcodeText),json);
                    }
                });
                Progress(output,String.Format(Loc.T("Fertig. {0} Label(s) erfolgreich gedruckt."),batch.Completed),json);
                return 0;
            }
            catch (Exception ex)
            {
                bool released = false;
                if (current != null && !lifecycle.HandshakeStarted)
                    try { release(current.reservationId); released = true; } catch { }
                string message=String.Format(Loc.T("Druckserie angehalten: {0} von {1} Label(s) bestätigt."),batch.Completed,count)+Environment.NewLine;
                if(current!=null) message+=Loc.T("Betroffene ASN: ")+current.barcodeText+Environment.NewLine;
                message+=Loc.T(lifecycle.HandshakeStarted ? "Druckausgang oder Serverbestätigung unklar. Ausdruck und Dokumentenliste prüfen; nicht einfach erneut drucken." : "Das aktuelle Label wurde nicht gedruckt. Drucker, Rolle und Serververbindung prüfen.");
                if(released) message+=Environment.NewLine+Loc.T("Die unbenutzte Reservierung wurde zurückgegeben.");
                if(current!=null && !lifecycle.HandshakeStarted && !released) message+=Environment.NewLine+Loc.T("Die Reservierung konnte nicht zurückgegeben werden. Bitte die Dokumentenliste prüfen.");
                message+=Environment.NewLine+Loc.T("Fehlerart: ")+ex.GetType().Name;
                Write(error, new { error = lifecycle.HandshakeStarted ? "review_print_outcome_do_not_retry" : "no_print_started",
                    kind = ex.GetType().Name, completed = batch.Completed, reservationId = current == null ? null : current.reservationId,
                    released = released, printConfirmed = lifecycle.PrintConfirmed, apiConfirmed = lifecycle.ApiConfirmed },message,json);
                // A timed-out COM worker may still finish. Keep the process/Setup
                // exclusion alive until it stops; never launch another job here.
                if(BpacPrinterSession.IsPrinting) Progress(error,Loc.T("Warte auf das Ende des noch laufenden Druckauftrags. Keinen weiteren Druck starten."),json);
                while (BpacPrinterSession.IsPrinting) Thread.Sleep(250);
                return lifecycle.HandshakeStarted ? 5 : 4;
            }
        }
    }
}
