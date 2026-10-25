using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;

namespace AsnLabeler
{
    static class HeadlessCli
    {
        static void Write(TextWriter output, object value)
        { output.WriteLine(new JavaScriptSerializer().Serialize(value)); output.Flush(); }

        public static int Run(string[] args)
        {
            if (args.Length == 1 && (args[0] == "-v" || args[0] == "--version")) { Console.WriteLine(AppRelease.Version); return 0; }
            if (args.Length == 0 || (args.Length == 1 && (args[0] == "help" || args[0] == "-h" || args[0] == "--help")))
            {
                Console.WriteLine("ASN Labeler " + AppRelease.Version + "\n\nSyntax:\n  asnlbl print [-c NUMBER] [-s]\n  asnlbl help\n  asnlbl -v\n\nFlags:\n  -c NUMBER   Label count, 1..10 (default: 1)\n  -s          Cut each label separately (default: cut at end of batch)\n  -h          Show this help\n  -v          Show version\n\nAliases: --count, --cut-each, --help, --version\n\nExamples:\n  asnlbl print\n  asnlbl print -c 3\n  asnlbl print -c 3 -s\n\nConfigure the installed GUI first, disable demo mode and close it before printing.\nOutput: JSON lines. Never blindly retry a failed batch.\nExit codes: 0 success, 2 configuration/arguments, 3 busy,\n4 current print not started, 5 print outcome requires review.");
                return 0;
            }
            int count;
            bool cutEach;
            if (!Parse(args, out count, out cutEach)) { Write(Console.Error, new { error = "invalid_arguments" }); return 2; }
            try
            {
                ClientRuntime.RequireInstallation();
                Mutex setup;
                if (Mutex.TryOpenExisting("Global\\AsnLabelerSetup", out setup))
                { setup.Dispose(); Write(Console.Error, new { error = "setup_running" }); return 3; }
                bool created;
                using (var single = new Mutex(true, "Local\\AsnLabelerBrotherQl800", out created))
                {
                    if (!created) { Write(Console.Error, new { error = "close_gui_or_other_cli_first" }); return 3; }
                    var security = new System.Security.AccessControl.MutexSecurity();
                    security.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(
                        new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                        System.Security.AccessControl.MutexRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
                    using (var presence = new Mutex(false, "Global\\AsnLabelerRunning", out created, security))
                    {
                        var config = AppConfig.Load();
                        if (config.DemoMode || String.IsNullOrWhiteSpace(config.ApiKey))
                        { Write(Console.Error, new { error = "configure_gui_and_disable_demo_first" }); return 2; }
                        var client = new ReservationClient(config);
                        client.Health();
                        return Print(count, cutEach, config, () => PrinterBackends.Open(config), client.PeekNext,
                            client.Next, client.MarkPrinting, client.MarkPrinted, client.Release, Console.Out, Console.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                // API responses can contain sensitive URLs. Emit a stable category,
                // not raw exception text, to terminal logs.
                Write(Console.Error, new { error = "configuration_or_connection_failed", kind = ex.GetType().Name });
                return 2;
            }
        }

        internal static bool Parse(string[] args, out int count, out bool cutEach)
        {
            count = 1; cutEach = false; bool countSeen = false;
            if (args.Length == 0 || args[0] != "print") return false;
            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "-s" || args[i] == "--cut-each") && !cutEach) cutEach = true;
                else if ((args[i] == "-c" || args[i] == "--count") && !countSeen && ++i < args.Length && Int32.TryParse(args[i], out count)) countSeen = true;
                else return false;
            }
            return count >= 1 && count <= 10;
        }

        internal static int Print(int count, bool cutEach, AppConfig config, Func<IPrinterSession> open,
            Func<Reservation> peek, Func<Reservation> next, Action<string> printing, Action<string> printed,
            Action<string> release, TextWriter output, TextWriter error)
        {
            var batch = new PrintBatch(count, cutEach);
            Reservation current = null;
            var lifecycle = new PrintLifecycle();
            try
            {
                batch.Run(index =>
                {
                    current = null; lifecycle = new PrintLifecycle();
                    using (var printer = open())
                    {
                        var status = printer.ReadStatus();
                        if (!status.CanPrint) throw new InvalidOperationException("printer_not_ready");
                        using (var bitmap = LabelRenderer.Render(peek().barcodeText, status.Media, config.UsePfRightAligned)) { }
                        current = next();
                        Write(output, new { status = "reserved", asn = current.barcodeText, reservationId = current.reservationId });
                        lifecycle.Submit(printer, current.barcodeText, status.Media,
                            () => printing(current.reservationId), () => printed(current.reservationId), batch.CutAfter(index));
                        Write(output, new { status = "printed", asn = current.barcodeText, completed = index, total = count });
                    }
                });
                return 0;
            }
            catch (Exception ex)
            {
                bool released = false;
                if (current != null && !lifecycle.HandshakeStarted)
                    try { release(current.reservationId); released = true; } catch { }
                Write(error, new { error = lifecycle.HandshakeStarted ? "review_print_outcome_do_not_retry" : "no_print_started",
                    kind = ex.GetType().Name, completed = batch.Completed, reservationId = current == null ? null : current.reservationId,
                    released = released, printConfirmed = lifecycle.PrintConfirmed, apiConfirmed = lifecycle.ApiConfirmed });
                // A timed-out COM worker may still finish. Keep the process/Setup
                // exclusion alive until it stops; never launch another job here.
                while (BpacPrinterSession.IsPrinting) Thread.Sleep(250);
                return lifecycle.HandshakeStarted ? 5 : 4;
            }
        }
    }
}
