using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AsnLabeler
{
    static class ClientRuntime
    {
        internal static bool DevelopmentMode = false;
        internal static string RequiredVersion;
        internal static string SecurityMinimum;
        internal static string EffectiveMinimum { get { return new[] { RequiredVersion, SecurityMinimum }.Where(v => !String.IsNullOrEmpty(v)).OrderByDescending(v => v == "?" ? new Version(65534,65534,65534) : ClientUpdates.ParseVersion(v)).FirstOrDefault(); } }
        internal static bool UpdateRequired { get { return !String.IsNullOrEmpty(EffectiveMinimum); } }
        internal static void RequireInstallation()
        {
            if (!DevelopmentMode && ClientUpdates.InstallScope(Application.ExecutablePath) == null)
                throw new InvalidOperationException(ClientUpdates.Text(
                    "Bitte ASN Labeler mit Setup installieren. Die einzelne EXE kann keine produktiven Serveraktionen ausführen. Demo und Diagnose bleiben verfügbar.",
                    "Install ASN Labeler using Setup. The standalone EXE cannot perform production server actions. Demo and diagnostics remain available."));
        }
    }

    sealed class ReleaseAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public long size { get; set; }
        public string digest { get; set; }
    }
    sealed class ClientRelease
    {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public string body { get; set; }
        public string target_commitish { get; set; }
        public ReleaseAsset[] assets { get; set; }
        public string VersionText { get { return tag_name.Substring("client-v".Length); } }
        public string InstallerName { get { return "asn-labeler-" + VersionText + "-setup.exe"; } }
    }
    sealed class UpdateFile { public string sha256 { get; set; } public long size { get; set; } }
    sealed class SafetyPolicy
    {
        public int schema { get; set; }
        public int revision { get; set; }
        public string minimumClientVersion { get; set; }
        public string minimumServerVersion { get; set; }
        public string reason { get; set; }
    }
    sealed class UpdateManifest
    {
        public string component { get; set; }
        public string version { get; set; }
        public string commit { get; set; }
        public Dictionary<string, UpdateFile> files { get; set; }
    }

    static class ClientUpdates
    {
        const string Repository = "NightmindOfficial/asn-labeler";
        internal static string CacheRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsnLabeler", "Updates"); } }
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        static readonly object CheckLock = new object();
        const string PolicyUrl = "https://raw.githubusercontent.com/NightmindOfficial/asn-labeler/main/security/policy.json";
        static SafetyPolicy retainedPolicy;
        static bool safetyChecked;
        internal static void EnsureSafetyChecked()
        {
            lock (CheckLock) { if (!safetyChecked) CheckSafety(); }
        }

        internal static SafetyPolicy VerifyPolicy(string envelope)
        {
            var data = Json.Deserialize<Dictionary<string, string>>(envelope);
            byte[] payload = Convert.FromBase64String(data["payload"]);
            if (payload.Length > 16384) throw new InvalidDataException("Security policy too large.");
            using (var resource = typeof(ClientUpdates).Assembly.GetManifestResourceStream("AsnLabeler.PolicyKey"))
            using (var reader = new StreamReader(resource))
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                var key = Json.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(key["modulus"]), Exponent = Convert.FromBase64String(key["exponent"]) });
                if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(data["signature"])))
                    throw new InvalidDataException("Invalid security policy signature.");
            }
            var policy = Json.Deserialize<SafetyPolicy>(Encoding.UTF8.GetString(payload));
            if (policy.schema != 1 || policy.revision <= 0 || policy.reason == null || policy.reason.Length > 1000) throw new InvalidDataException("Invalid security policy.");
            ParseVersion(policy.minimumClientVersion); ParseVersion(policy.minimumServerVersion);
            return policy;
        }
        static void RetainPolicy(SafetyPolicy policy)
        {
            if (retainedPolicy != null && (policy.revision <= retainedPolicy.revision ||
                ParseVersion(policy.minimumClientVersion) < ParseVersion(retainedPolicy.minimumClientVersion))) return;
            retainedPolicy = policy;
            ClientRuntime.SecurityMinimum = ParseVersion(AppRelease.Version) < ParseVersion(policy.minimumClientVersion) ? policy.minimumClientVersion : null;
        }
        internal static void CheckSafety()
        {
            lock (CheckLock)
            {
                safetyChecked = true;
                string cache = Path.Combine(CacheRoot, "security-policy.json");
                try { if (File.Exists(cache)) RetainPolicy(VerifyPolicy(File.ReadAllText(cache))); } catch (Exception) { /* Unknown is not revoked. */ }
                try
                {
                    string envelope = Encoding.UTF8.GetString(GetBytes(PolicyUrl, 32768, null));
                    var policy = VerifyPolicy(envelope);
                    foreach (string component in new[] { "client", "server" })
                    {
                        string minimum = component == "client" ? policy.minimumClientVersion : policy.minimumServerVersion;
                        var release = Json.Deserialize<ClientRelease>(Encoding.UTF8.GetString(GetBytes("https://api.github.com/repos/" + Repository + "/releases/tags/" + component + "-v" + minimum, 1024 * 1024, null)));
                        if (release.draft || release.prerelease || release.tag_name != component + "-v" + minimum ||
                            (component == "client" && (release.assets == null || !release.assets.Any(a => a.name == "asn-labeler-" + minimum + "-setup.exe"))))
                            throw new InvalidDataException("Required release not available.");
                    }
                    RetainPolicy(policy);
                    if (retainedPolicy != policy) return;
                    Directory.CreateDirectory(CacheRoot);
                    string temporary = cache + ".tmp";
                    File.WriteAllText(temporary, envelope);
                    if (File.Exists(cache)) File.Replace(temporary, cache, null); else File.Move(temporary, cache);
                }
                catch (Exception) { /* Keep a verified cached restriction when offline. */ }
            }
        }

        public static string Text(string de, string en) { return Loc.Language == "en" ? en : de; }
        internal static Version ParseVersion(string value)
        {
            if (value == null || !System.Text.RegularExpressions.Regex.IsMatch(value, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"))
                throw new InvalidDataException("Invalid update version.");
            var result = new Version(value);
            if (result.Major > 65534 || result.Minor > 65534 || result.Build > 65534) throw new InvalidDataException("Invalid update version.");
            return result;
        }
        internal static ClientRelease Select(IEnumerable<ClientRelease> releases, string current)
        {
            var version = ParseVersion(current);
            return releases.Where(r => r != null && !r.draft && !r.prerelease && r.tag_name != null &&
                System.Text.RegularExpressions.Regex.IsMatch(r.tag_name, @"\Aclient-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"))
                .Where(r => ParseVersion(r.VersionText) > version)
                .OrderByDescending(r => ParseVersion(r.VersionText)).FirstOrDefault();
        }
        public static ClientRelease Check() { CheckSafety(); return Check(LookupRelease); }
        internal static ClientRelease Check(Func<ClientRelease> lookup)
        {
            lock (CheckLock) { return lookup(); }
        }
        static ClientRelease LookupRelease()
        {
            var releases = new List<ClientRelease>();
            for (int page = 1; page <= 20; page++)
            {
                var batch = Json.Deserialize<ClientRelease[]>(Encoding.UTF8.GetString(GetBytes(
                    "https://api.github.com/repos/" + Repository + "/releases?per_page=100&page=" + page, 4 * 1024 * 1024, null)));
                if (batch == null) throw new InvalidDataException("Invalid GitHub response.");
                releases.AddRange(batch);
                if (batch.Length < 100) return Select(releases, AppRelease.Version);
            }
            throw new InvalidDataException("Release lookup limit reached.");
        }
        internal static bool AllowedUrl(Uri uri)
        {
            if (uri.AbsoluteUri == PolicyUrl) return true;
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0) return false;
            if (uri.Host == "api.github.com") return uri.AbsolutePath.StartsWith("/repos/" + Repository + "/", StringComparison.Ordinal);
            if (uri.Host == "github.com") return uri.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal);
            return uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com";
        }
        static byte[] GetBytes(string address, int limit, CancellationToken? cancel)
        {
            using (var output = new MemoryStream()) { Download(address, output, limit, cancel, null); return output.ToArray(); }
        }
        static void Download(string address, Stream output, long limit, CancellationToken? cancel, Action<long> progress)
        {
            var uri = new Uri(address);
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                if (!AllowedUrl(uri)) throw new InvalidDataException("Untrusted update download URL.");
                if (cancel.HasValue) cancel.Value.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
                request.UserAgent = "ASN-Labeler/" + AppRelease.Version;
                request.Accept = "application/json, application/octet-stream";
                using (var registration = cancel.HasValue ? cancel.Value.Register(request.Abort) : default(CancellationTokenRegistration))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400) { uri = new Uri(uri, response.Headers["Location"]); continue; }
                    if (status != 200 || response.ContentLength > limit) throw new InvalidDataException("Unexpected update response.");
                    using (var input = response.GetResponseStream())
                    {
                        byte[] buffer = new byte[65536]; long total = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (cancel.HasValue) cancel.Value.ThrowIfCancellationRequested();
                            total += read;
                            if (total > limit) throw new InvalidDataException("Update download is too large.");
                            output.Write(buffer, 0, read); if (progress != null) progress(total);
                        }
                    }
                    return;
                }
            }
            throw new InvalidDataException("Too many download redirects.");
        }
        internal static UpdateFile ValidateManifest(ClientRelease release, UpdateManifest manifest)
        {
            UpdateFile file;
            if (manifest == null || manifest.component != "client" || manifest.version != release.VersionText ||
                manifest.commit != release.target_commitish || !System.Text.RegularExpressions.Regex.IsMatch(manifest.commit ?? "", @"\A[0-9a-f]{40}\z") ||
                manifest.files == null || !manifest.files.TryGetValue(release.InstallerName, out file) ||
                file.size < 1 || file.size > 256 * 1024 * 1024 ||
                !System.Text.RegularExpressions.Regex.IsMatch(file.sha256 ?? "", @"\A[0-9a-f]{64}\z")) throw new InvalidDataException("Update manifest mismatch.");
            var asset = release.assets.Single(a => a.name == release.InstallerName);
            if (asset.size != file.size || (asset.digest != null && asset.digest != "sha256:" + file.sha256)) throw new InvalidDataException("Update asset mismatch.");
            return file;
        }
        internal static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static string Fetch(ClientRelease release, CancellationToken cancel, Action<long> progress, out string hash)
        {
            if (release.assets == null) throw new InvalidDataException("Release assets missing.");
            var manifestAsset = release.assets.Single(a => a.name == "release-manifest.json");
            var manifest = Json.Deserialize<UpdateManifest>(Encoding.UTF8.GetString(GetBytes(manifestAsset.browser_download_url, 65536, cancel)));
            var expected = ValidateManifest(release, manifest);
            string folder = Path.Combine(CacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "setup.exe");
            try
            {
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    Download(release.assets.Single(a => a.name == release.InstallerName).browser_download_url, output, expected.size, cancel, progress);
                cancel.ThrowIfCancellationRequested();
                if (new FileInfo(path).Length != expected.size || Hash(path) != expected.sha256) throw new InvalidDataException("Installer checksum mismatch.");
                // Preserve Windows' Internet-zone warning for downloaded unsigned setup files.
                MarkInternetDownload(path);
                hash = expected.sha256; return path;
            }
            catch { File.Delete(path); throw; }
        }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        // .NET Framework's path validation rejects alternate data stream names.
        static FileStream OpenZoneStream(string path, bool write)
        {
            var handle = CreateFile(Path.GetFullPath(path) + ":Zone.Identifier",
                write ? 0x40000000u : 0x80000000u, 1, IntPtr.Zero,
                write ? 2u : 3u, 0x80, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, Text("Windows-Herkunftsmarkierung konnte nicht geöffnet werden.",
                    "Could not open the Windows download origin marker."));
            }
            try { return new FileStream(handle, write ? FileAccess.Write : FileAccess.Read); }
            catch { handle.Dispose(); throw; }
        }
        internal static void MarkInternetDownload(string path)
        {
            using (var stream = OpenZoneStream(path, true))
            {
                byte[] data = Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n");
                stream.Write(data, 0, data.Length);
            }
        }
        static string Quote(string path)
        {
            if (path.Contains("\"") || path.EndsWith("\\")) throw new InvalidDataException("Invalid installer path.");
            return "\"" + path + "\"";
        }
        internal static string InstallScope(string executable)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(executable));
            foreach (var scope in new[] { "user", "machine" })
                using (var hive = RegistryKey.OpenBaseKey(scope == "user" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hive.OpenSubKey(@"Software\nightmind.dev\AsnLabeler"))
                    if (key != null)
                    {
                        string installed = Convert.ToString(key.GetValue("InstallPath"));
                        if (String.Equals(folder, installed, StringComparison.OrdinalIgnoreCase) ||
                            (!String.IsNullOrEmpty(installed) && String.Equals(Path.GetFileName(executable), "asnlbl.exe", StringComparison.OrdinalIgnoreCase) &&
                             String.Equals(folder, Path.Combine(installed, "cli"), StringComparison.OrdinalIgnoreCase))) return scope;
                    }
            return null;
        }
        public static void PrepareInstall(string path, string hash)
        {
            if (BpacPrinterSession.IsPrinting) throw new InvalidOperationException(Text("Druckauftrag noch aktiv. Update nicht gestartet.", "A print job is still active. Update was not started."));
            string scope = InstallScope(Application.ExecutablePath);
            if (scope == null) throw new InvalidOperationException(Text("Bitte die App zuerst mit dem Installer installieren.", "Install the app using Setup first."));
            string helper = Path.Combine(Path.GetDirectoryName(path), "update-helper.exe");
            File.Copy(Application.ExecutablePath, helper, false);
            var process = Process.GetCurrentProcess();
            Process.Start(new ProcessStartInfo(helper, "--apply-update " + process.Id + " " + process.StartTime.ToUniversalTime().Ticks + " " + Quote(path) + " " + hash + " " + scope)
                { UseShellExecute = false, CreateNoWindow = true });
        }
        public static int Apply(string[] args)
        {
            try
            {
                if (args.Length != 6) throw new InvalidDataException("Invalid updater arguments.");
                string path = Path.GetFullPath(args[3]);
                if (!path.StartsWith(Path.GetFullPath(CacheRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path) != "setup.exe" || Path.GetDirectoryName(path) != Path.GetDirectoryName(Application.ExecutablePath) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(args[4], @"\A[0-9a-f]{64}\z") || (args[5] != "user" && args[5] != "machine"))
                    throw new InvalidDataException("Invalid updater handoff.");
                try
                {
                    using (var parent = Process.GetProcessById(Int32.Parse(args[1], CultureInfo.InvariantCulture)))
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != Int64.Parse(args[2], CultureInfo.InvariantCulture)) throw new InvalidDataException("Updater parent mismatch.");
                        if (!parent.WaitForExit(60000)) throw new InvalidOperationException("ASN Labeler is still running. Update cancelled.");
                    }
                }
                catch (ArgumentException) { /* Parent has already exited. */ }
                // Keep the verified installer immutable until Setup has finished using it.
                using (var installer = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(installer)).Replace("-", "").ToLowerInvariant();
                    if (actual != args[4]) throw new InvalidDataException("Installer checksum mismatch.");
                    using (var setup = Process.Start(new ProcessStartInfo(path, (args[5] == "machine" ? "/ALLUSERS" : "/CURRENTUSER") + " /NORESTART") { UseShellExecute = true }))
                        if (setup != null) setup.WaitForExit();
                }
                return 0;
            }
            catch (Exception ex) { AppDialogs.Show(ex.Message, "ASN Labeler Update", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
        internal static IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            string envelope;
            using (var stream = typeof(ClientUpdates).Assembly.GetManifestResourceStream("AsnLabeler.PolicyFixture"))
            using (var reader = new StreamReader(stream)) envelope = reader.ReadToEnd();
            var verified = VerifyPolicy(envelope);
            var tampered = Json.Deserialize<Dictionary<string,string>>(envelope);
            tampered["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
            try { VerifyPolicy(Json.Serialize(tampered)); errors.Add("Tampered policy accepted."); } catch (InvalidDataException) { }
            var savedPolicy = retainedPolicy;
            string savedMinimum = ClientRuntime.SecurityMinimum;
            try
            {
                RetainPolicy(new SafetyPolicy { revision = Int32.MaxValue, minimumClientVersion = "99.0.0" });
                RetainPolicy(verified);
                if (ClientRuntime.SecurityMinimum != "99.0.0") errors.Add("Safety floor rollback accepted.");
                ReservationClient.ValidateCompatibility(new Health { ok = true, minimumClientVersion = "2.0.0" });
                if (!ClientRuntime.UpdateRequired) errors.Add("Server cleared independent safety restriction.");
            }
            finally { retainedPolicy = savedPolicy; ClientRuntime.SecurityMinimum = savedMinimum; }
            int checks = 0;
            Func<ClientRelease> lookup = () => { checks++; return null; };
            Check(lookup); Check(lookup);
            if (checks != 2) errors.Add("Update checks were throttled across starts.");
            string download = Path.Combine(Path.GetTempPath(), "ASN update \u00e4 " + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.WriteAllText(download, "test installer");
                string before = Hash(download);
                MarkInternetDownload(download);
                MarkInternetDownload(download);
                using (var reader = new StreamReader(OpenZoneStream(download, false)))
                    if (reader.ReadToEnd() != "[ZoneTransfer]\r\nZoneId=3\r\n") errors.Add("Internet zone marker missing or invalid.");
                if (Hash(download) != before) errors.Add("Origin marking changed installer bytes.");
                try { MarkInternetDownload(Path.Combine(download, "missing.exe")); errors.Add("Origin marking error was ignored."); }
                catch (Win32Exception) { }
            }
            finally { File.Delete(download); }
            var releases = new[] {
                new ClientRelease { tag_name = "server-v99.0.0" }, new ClientRelease { tag_name = "client-v9.0.0", draft = true },
                new ClientRelease { tag_name = "client-v8.0.0", prerelease = true }, new ClientRelease { tag_name = "client-v2.0.1" },
                new ClientRelease { tag_name = "client-v2.0.0" }, new ClientRelease { tag_name = "client-v2.0.2-rc1" } };
            if (Select(releases, "2.0.0").VersionText != "2.0.1" || Select(releases, "2.0.1") != null) errors.Add("Update version filtering failed.");
            foreach (string url in new[] { "http://github.com/", "https://example.com/setup.exe", "https://github.com/evil/repo/releases/download/v1/a.exe", "https://api.github.com:444/repos/" + Repository + "/releases" })
                if (AllowedUrl(new Uri(url))) errors.Add("Unsafe update URL accepted.");
            var release = new ClientRelease { tag_name = "client-v2.0.1", target_commitish = new string('a', 40),
                assets = new[] { new ReleaseAsset { name = "asn-labeler-2.0.1-setup.exe", size = 10, digest = "sha256:" + new string('b', 64) } } };
            var manifest = new UpdateManifest { component = "client", version = "2.0.1", commit = new string('a', 40),
                files = new Dictionary<string, UpdateFile> { { release.InstallerName, new UpdateFile { size = 10, sha256 = new string('b', 64) } } } };
            ValidateManifest(release, manifest);
            foreach (string component in new[] { "server", "", null })
            {
                manifest.component = component;
                try { ValidateManifest(release, manifest); errors.Add("Wrong update component accepted."); } catch (InvalidDataException) { }
            }
            manifest.component = "client"; manifest.commit = new string('c', 40);
            try { ValidateManifest(release, manifest); errors.Add("Wrong update commit accepted."); } catch (InvalidDataException) { }
            manifest.commit = release.target_commitish; manifest.files[release.InstallerName].sha256 = new string('c', 64);
            try { ValidateManifest(release, manifest); errors.Add("Wrong update digest accepted."); } catch (InvalidDataException) { }
            foreach (string version in new[] { "2.0.1-rc1", "02.0.1", "2.0", "2.0.0\n", "999999999999.0.0" })
                try { ParseVersion(version); errors.Add("Invalid version accepted."); } catch (Exception ex) { if (!(ex is InvalidDataException || ex is ArgumentException || ex is OverflowException)) throw; }
            bool development = ClientRuntime.DevelopmentMode;
            string required = ClientRuntime.RequiredVersion;
            try
            {
                ClientRuntime.DevelopmentMode = false;
                if (InstallScope(Application.ExecutablePath) == null)
                    try { ClientRuntime.RequireInstallation(); errors.Add("Standalone production access accepted."); } catch (InvalidOperationException) { }
                ClientRuntime.DevelopmentMode = true;
                ClientRuntime.RequireInstallation();
                try { ReservationClient.ValidateCompatibility(new Health { ok = true, minimumClientVersion = "99.0.0" }); errors.Add("Required upgrade ignored."); } catch (InvalidOperationException) { }
                if (!ClientRuntime.UpdateRequired) errors.Add("Update requirement not recorded.");
                ReservationClient.ValidateCompatibility(new Health { ok = true, minimumClientVersion = "2.0.0" });
                if (ClientRuntime.UpdateRequired) errors.Add("Stale version requirement retained.");
            }
            finally { ClientRuntime.DevelopmentMode = development; ClientRuntime.RequiredVersion = required; }
            return errors;
        }
    }

    sealed class UpdateForm : Form
    {
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        bool downloading;
        public bool InstallRequested { get; private set; }
        public UpdateForm(ClientRelease release)
        {
            Text = "ASN Labeler " + release.VersionText;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Segoe UI", 10); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 360); MinimumSize = new Size(520, 360);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = ClientUpdates.Text("Neue Version verfügbar: ", "New version available: ") + release.VersionText, AutoSize = true, Padding = new Padding(0, 0, 0, 12) });
            layout.Controls.Add(new TextBox { Text = release.body ?? "", ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill });
            var status = new Label { Text = ClientUpdates.Text("Installation nach Bestätigung. Setup ist unsigniert.", "Installation requires confirmation. Setup is unsigned."), Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
            layout.Controls.Add(status);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var later = new Button { Text = ClientUpdates.Text("Später", "Later"), AutoSize = true, MinimumSize = new Size(90, 36) };
            var install = new Button { Text = ClientUpdates.Text("Herunterladen und installieren", "Download and install"), AutoSize = true, MinimumSize = new Size(240, 36) };
            actions.Controls.Add(later); actions.Controls.Add(install); layout.Controls.Add(actions); Controls.Add(layout);
            Shown += delegate { ActiveControl = later; };
            later.Click += delegate { if (downloading) cancellation.Cancel(); else Close(); };
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (downloading) { cancellation.Cancel(); e.Cancel = true; } };
            install.Click += async delegate
            {
                if (downloading) return;
                downloading = true; install.Enabled = false; later.Text = ClientUpdates.Text("Abbrechen", "Cancel");
                try
                {
                    string hash = null;
                    var progress = new Progress<long>(bytes => status.Text = ClientUpdates.Text("Heruntergeladen: ", "Downloaded: ") + (bytes / 1024) + " KB");
                    string path = await Task.Run(() => ClientUpdates.Fetch(release, cancellation.Token, n => ((IProgress<long>)progress).Report(n), out hash));
                    cancellation.Token.ThrowIfCancellationRequested();
                    ClientUpdates.PrepareInstall(path, hash);
                    InstallRequested = true;
                }
                catch (Exception ex)
                {
                    if (!cancellation.IsCancellationRequested) AppDialogs.Show(this, ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { downloading = false; Close(); }
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) cancellation.Dispose(); base.Dispose(disposing); }
        protected override void OnLoad(EventArgs e)
        {
            using (var graphics = CreateGraphics())
                MinimumSize = new Size((int)Math.Ceiling(520 * graphics.DpiX / 96f), (int)Math.Ceiling(360 * graphics.DpiY / 96f));
            base.OnLoad(e);
        }

        internal static IEnumerable<string> ValidateLayout(string snapshotPath = null)
        {
            var errors = new List<string>();
            using (var form = new UpdateForm(new ClientRelease { tag_name = "client-v2.0.1", body = "Release notes\r\n" + new string('x', 200) }))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false;
                form.Show();
                foreach (var size in new[] { new Size(520, 360), new Size(720, 480) })
                {
                    form.Size = size; form.PerformLayout(); Application.DoEvents();
                    CheckBounds(form, errors);
                    if (snapshotPath != null)
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshotPath), Path.GetFileNameWithoutExtension(snapshotPath) + "-update-" + size.Width + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                }
                form.Close();
            }
            return errors;
        }
        static void CheckBounds(Control parent, List<string> errors)
        {
            foreach (Control control in parent.Controls)
            {
                if (!control.Visible) continue;
                if (!parent.ClientRectangle.Contains(control.Bounds)) errors.Add("Update dialog overflow: " + control.Text);
                foreach (Control other in parent.Controls)
                    if (other != control && other.Visible && control.Bounds.IntersectsWith(other.Bounds)) errors.Add("Update dialog overlap: " + control.Text);
                CheckBounds(control, errors);
            }
        }
    }
}
