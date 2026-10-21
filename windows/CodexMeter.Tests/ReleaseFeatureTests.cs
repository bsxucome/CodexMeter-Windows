using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static partial class Program
    {
        private sealed class FixtureHandler : HttpMessageHandler
        {
            public int Calls;
            public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { Calls++; return Handler(request, token); }
        }
        private static HttpResponseMessage Reply(int code, string body)
        { return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body) }; }
        private static void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch (Exception ex) { if (ex is OutOfMemoryException) throw; rejected = true; } Check(rejected, name); }
        private static string TestDirectory(string name)
        { string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        private static UsageState RemoteState(DateTimeOffset at, double remaining, string session)
        { return new UsageState { SessionId = session, UpdatedAt = at, Windows = new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 100 - remaining, DurationMinutes = 300, ResetsAt = at.AddHours(3) } } }; }
        private sealed class MemorySecretVault : ISecretVault
        {
            private byte[] data;
            public byte[] Read() { return data == null ? null : (byte[])data.Clone(); }
            public void Write(byte[] value) { data = (byte[])value.Clone(); }
        }
        private static async Task RemoteFeatures()
        {
            var now = DateTimeOffset.UtcNow; var rules = new ReplenishmentRules();
            Check(rules.Evaluate(RemoteState(now, 50, "a"), now).Count == 0, "remote first observation never sends replenishment");
            var state = RemoteState(now.AddSeconds(60), 90, "a");
            Check(rules.Evaluate(state, now.AddSeconds(60)).Single().Before == 50, "remote actual increase creates a replenishment event");
            Check(rules.Evaluate(state, now.AddSeconds(61)).Count == 0, "remote rendering identical snapshot deduplicates");
            Check(rules.Evaluate(RemoteState(now.AddSeconds(90), 95, "a"), now.AddSeconds(90)).Count == 0, "remote replenishment cooldown bounds repeated increases");
            Check(rules.Evaluate(RemoteState(now.AddSeconds(120), 100, "b"), now.AddSeconds(120)).Count == 0, "remote account boundary starts a new baseline");
            rules.Reset(); rules.Evaluate(RemoteState(now, 20, "a"), now);
            Check(rules.Evaluate(RemoteState(now.AddMinutes(3), 100, "a"), now.AddMinutes(3)).Count == 0, "remote missing observation gap cannot infer replenishment");
            rules.Reset(); rules.Evaluate(RemoteState(now, 20, "a"), now);
            state = RemoteState(now.AddSeconds(20), 20, "a"); state.Windows[0].ResetsAt = now.AddMinutes(-1);
            Check(rules.Evaluate(state, now.AddSeconds(20)).Count == 0, "remote elapsed reset without an increase never alerts");
            state.IsStale = true; rules.Evaluate(state, now.AddSeconds(21)); state = RemoteState(now.AddSeconds(40), 100, "a");
            Check(rules.Evaluate(state, now.AddSeconds(40)).Count == 0, "remote stale interruption resets observation baseline");
            var channels = new[] {
                new RemoteChannel { Kind = "Bark", Endpoint = "https://example.test/bark", Secret = "PRIVATE_SENTINEL" },
                new RemoteChannel { Kind = "Gotify", Endpoint = "https://example.test/gotify", Secret = "PRIVATE_SENTINEL" },
                new RemoteChannel { Kind = "Telegram", Secret = "123:PRIVATE_SENTINEL", Recipient = "-1234" },
                new RemoteChannel { Kind = "Discord", Secret = "https://discord.com/api/webhooks/123/PRIVATE_SENTINEL" } };
            foreach (var channel in channels)
            {
                using (var request = RemoteDelivery.Request(channel, "Title", "@everyone text"))
                {
                    var data = Json.Parse(await request.Content.ReadAsStringAsync());
                    Check(request.Method == HttpMethod.Post && request.RequestUri.Scheme == "https", channel.Kind + " uses HTTPS POST");
                    if (channel.Kind == "Bark") Check(Json.Text(data, "device_key") == channel.Secret && request.RequestUri.AbsolutePath == "/bark/push", "Bark device key is carried in the JSON body");
                    if (channel.Kind == "Gotify") Check(request.Headers.GetValues("X-Gotify-Key").Single() == channel.Secret && !request.RequestUri.Query.Contains(channel.Secret), "Gotify authenticates in a header");
                    if (channel.Kind == "Telegram") Check(Json.Text(data, "chat_id") == "-1234" && request.RequestUri.Host == "api.telegram.org", "Telegram targets the chosen chat on its fixed API host");
                    if (channel.Kind == "Discord") Check(Json.Object(data, "allowed_mentions") != null && request.RequestUri.Query == "?wait=true", "Discord disables mention parsing and waits for confirmation");
                }
                string confirmation = channel.Kind == "Bark" ? "{\"code\":200}" : channel.Kind == "Telegram" ? "{\"ok\":true}" : "{\"id\":123}";
                if (channel.Kind == "Discord") confirmation = "{\"id\":\"123\"}";
                var handler = new FixtureHandler { Handler = (r, t) => Task.FromResult(Reply(200, confirmation)) };
                using (var sender = new RemoteDelivery(handler)) Check((await sender.SendAsync(channel, "x", "y", CancellationToken.None)).Success, channel.Kind + " confirms accepted delivery");
            }
            foreach (int code in new[] { 401, 403, 302, 400 })
            {
                var handler = new FixtureHandler { Handler = (r, t) => Task.FromResult(Reply(code, "PRIVATE_SENTINEL")) };
                using (var sender = new RemoteDelivery(handler))
                { var result = await sender.SendAsync(channels[0], "x", "y", CancellationToken.None); Check(!result.Success && result.Attempts == 1 && !result.Status.Contains("PRIVATE"), "remote permanent or redirect response " + code + " is sanitized without retry"); }
            }
            var retry = new FixtureHandler { Handler = (r, t) => { var response = Reply(429, "PRIVATE_SENTINEL"); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1)); return Task.FromResult(response); } };
            using (var sender = new RemoteDelivery(retry))
            {
                int waits = 0; sender.Delay = (delay, token) => { Check(delay.TotalSeconds == 30, "remote server retry delay is bounded to thirty seconds"); waits++; return Task.FromResult(0); };
                var result = await sender.SendAsync(channels[0], "x", "y", CancellationToken.None);
                Check(!result.Success && result.Attempts == 3 && retry.Calls == 3 && waits == 2, "remote throttling stops after three attempts");
            }
            bool validScope = true; var boundary = new FixtureHandler { Handler = (r, t) => Task.FromResult(Reply(503, "")) };
            using (var sender = new RemoteDelivery(boundary))
            {
                sender.Delay = (delay, token) => { validScope = false; return Task.FromResult(0); };
                var result = await sender.SendAsync(channels[0], "x", "y", CancellationToken.None, () => validScope);
                Check(boundary.Calls == 1 && result.Status == "已取消", "remote retry rechecks account scope before any subsequent transmission");
            }
            var unknown = new FixtureHandler { Handler = (r, t) => { throw new HttpRequestException("PRIVATE_SENTINEL"); } };
            using (var sender = new RemoteDelivery(unknown))
            { var result = await sender.SendAsync(channels[0], "x", "y", CancellationToken.None); Check(result.Attempts == 1 && result.Status.Contains("未知") && !result.Status.Contains("PRIVATE"), "remote ambiguous network outcome never blindly replays"); }
            var cancelled = new FixtureHandler { Handler = (r, t) => Task.FromResult(Reply(200, "{}")) };
            using (var sender = new RemoteDelivery(cancelled)) using (var token = new CancellationTokenSource())
            { token.Cancel(); Check((await sender.SendAsync(channels[0], "x", "y", token.Token)).Attempts == 0 && cancelled.Calls == 0, "remote cancellation prevents transmission"); }
            var malformed = new FixtureHandler { Handler = (r, t) => Task.FromResult(Reply(200, "{garbage PRIVATE_SENTINEL")) };
            using (var sender = new RemoteDelivery(malformed)) Check(!(await sender.SendAsync(channels[0], "x", "y", CancellationToken.None)).Success, "remote malformed JSON fails closed");
            Reject(() => RemoteDelivery.Server("http://example.test"), "remote rejects nonlocal cleartext services");
            Check(RemoteDelivery.Server("http://127.0.0.1:1234").IsLoopback, "remote permits an explicitly configured local service");
            Reject(() => RemoteDelivery.Validate(new RemoteChannel { Kind = "Discord", Secret = "https://example.test/api/webhooks/123/key" }), "Discord credentials cannot be redirected to an arbitrary host");
            string directory = TestDirectory("vault");
            try
            {
                string path = Path.Combine(directory, "channels.bin"); var vault = new WindowsSecretVault(path); var profile = new RemoteProfile(); profile.Channels[0].Secret = "PRIVATE_SENTINEL";
                var memory = new MemorySecretVault(); profile.Save(memory); Check(RemoteProfile.Load(memory).Channels[0].Secret == "PRIVATE_SENTINEL", "remote profile roundtrip through an isolated secret vault");
                bool protectedData = true;
                try { profile.Save(vault); }
                catch (IOException ex)
                {
                    if (!(ex.InnerException is System.ComponentModel.Win32Exception) || ((System.ComponentModel.Win32Exception)ex.InnerException).NativeErrorCode != 2) throw;
                    protectedData = false;
                    Console.WriteLine("UNVERIFIED: DPAPI profile unavailable in this execution context; native code=" + ((System.ComponentModel.Win32Exception)ex.InnerException).NativeErrorCode);
                    Check(!File.Exists(path), "unavailable Windows encryption refuses to write plaintext credentials");
                }
                if (protectedData)
                {
                    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("PRIVATE_SENTINEL"), "remote credentials are DPAPI ciphertext on disk");
                    Check(RemoteProfile.Load(vault).Channels[0].Secret == "PRIVATE_SENTINEL", "remote DPAPI roundtrip preserves credentials");
                }
                var copy = profile.Copy(); copy.Channels[0].Secret = "changed"; Check(profile.Channels[0].Secret == "PRIVATE_SENTINEL", "remote editing is isolated until saved");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 }); Reject(() => RemoteProfile.Load(vault), "corrupted DPAPI data does not fall back to plaintext");
            }
            finally { Directory.Delete(directory, true); }
        }
        private static void BackupFeatures()
        {
            string directory = TestDirectory("backup"); var now = DateTimeOffset.UtcNow;
            try
            {
                string path = Path.Combine(directory, "source.sqlite3"), archive = Path.Combine(directory, "all.codexbackup");
                string key; var windows = new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 50, DurationMinutes = 300, ResetsAt = now.AddHours(3) } };
                using (var source = new HistoryStore(path))
                {
                    key = source.AccountKey("chatgpt", "fixture@example.test", "a");
                    source.Record(key, now, windows, new TokenUsage { DailyAvailable = true, Daily = new List<DailyTokens> { new DailyTokens { Date = now.UtcDateTime.ToString("yyyy-MM-dd"), Tokens = 123 } } });
                    source.Record("second-account", now, windows, null);
                    HistoryBackup.Create(source, archive);
                    Check(source.Accounts().Count == 2, "backup covers multiple account partitions from a live WAL database");
                    Reject(() => HistoryBackup.Create(source, path), "backup cannot overwrite the live database");
                    using (var prepared = HistoryBackup.Prepare(archive, directory))
                    {
                        Check(prepared.Info.Accounts == 2 && prepared.Info.QuotaRows == 2 && prepared.Info.TokenRows == 1, "backup validates all-account quota and token totals");
                        using (var target = new HistoryStore(Path.Combine(directory, "target.sqlite3")))
                        {
                            string oldKey = target.AccountKey("chatgpt", "fixture@example.test", "a"); target.Record("existing", now, windows, null);
                            string rollback = Path.Combine(directory, "rollback.codexbackup");
                            Reject(() => target.RestoreSnapshot(prepared.DatabasePath, rollback, () => { throw new IOException("injected replace failure"); }), "restore failure after replacement is reported");
                            Check(target.Accounts().Single().Key == "existing" && oldKey == target.AccountKey("chatgpt", "fixture@example.test", "a"), "failed restore rolls back both observations and identity salt");
                            using (var saved = HistoryBackup.Prepare(rollback, directory)) Check(saved.Info.QuotaRows == 1, "restore creates a valid independent pre-restore archive");
                            target.RestoreSnapshot(prepared.DatabasePath, Path.Combine(directory, "before-success.codexbackup"));
                            Check(target.AccountKey("chatgpt", "fixture@example.test", "other") == key && target.ReadTokens(key, "0001-01-01", "9999-12-31").Single().Tokens == 123, "successful restore preserves salted identities and daily tokens");
                            target.Clear("second-account"); Check(target.Accounts().Count == 1, "account management clears only the selected partition");
                            target.ClearAll(); Check(target.Accounts().Count == 0 && target.AccountKey("chatgpt", "fixture@example.test", "a") == key, "clear all removes history while keeping the local salt");
                        }
                    }
                }
                using (var zip = ZipFile.OpenRead(archive)) Check(zip.Entries.Select(e => e.FullName).OrderBy(x => x).SequenceEqual(new[] { "history.sqlite3", "manifest.json" }), "backup excludes preferences, auth and remote credentials");
                foreach (string kind in new[] { "hash", "version", "count", "extra", "traversal" })
                {
                    string altered = Path.Combine(directory, kind + ".codexbackup"); File.Copy(archive, altered);
                    using (var zip = ZipFile.Open(altered, ZipArchiveMode.Update))
                    {
                        if (kind == "extra" || kind == "traversal") { zip.CreateEntry(kind == "extra" ? "secret.bin" : "../history.sqlite3"); }
                        else
                        {
                            var entry = zip.GetEntry("manifest.json"); Dictionary<string, object> content; using (var reader = new StreamReader(entry.Open())) content = Json.Parse(reader.ReadToEnd()); entry.Delete();
                            content[kind == "hash" ? "sha256" : kind == "version" ? "version" : "quotaRows"] = kind == "hash" ? (object)new string('0', 64) : 999;
                            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(Json.Encode(content));
                        }
                    }
                    Reject(() => { using (var unused = HistoryBackup.Prepare(altered, directory)) { } }, "restore rejects altered " + kind + " without changing the live store");
                }
                string bad = Path.Combine(directory, "bad.sqlite3"); File.WriteAllText(bad, new string('x', 200)); Reject(() => HistoryBackup.ValidateDatabase(bad), "restore rejects a corrupt SQLite file");
                using (var prepared = HistoryBackup.Prepare(archive, directory))
                {
                    var bytes = File.ReadAllBytes(prepared.DatabasePath); bytes[63] = 2; File.WriteAllBytes(prepared.DatabasePath, bytes);
                    Reject(() => HistoryBackup.ValidateDatabase(prepared.DatabasePath), "restore refuses a newer SQLite schema without downgrading it");
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        private sealed class SignedFixture : IDisposable
        {
            public readonly RSACryptoServiceProvider Key = new RSACryptoServiceProvider(2048);
            public string Public, Archive, Directory;
            public Dictionary<string, object> Payload;
            public SignedFixture()
            {
                Key.PersistKeyInCsp = false; var key = Key.ExportParameters(false); Public = Json.Encode(new { modulus = Convert.ToBase64String(key.Modulus), exponent = Convert.ToBase64String(key.Exponent) });
                Directory = TestDirectory("updates"); Archive = Path.Combine(Directory, "package.zip");
                using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Create))
                    foreach (var name in new[] { "CodexMeter.exe", "CodexMeter.Core.dll", "LICENSE" }) using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("fixture " + name);
                var files = new List<object>();
                using (var zip = ZipFile.OpenRead(Archive)) foreach (var entry in zip.Entries)
                {
                    byte[] bytes; using (var input = entry.Open()) bytes = PortableFiles.ReadBounded(input, 4096);
                    using (var sha = SHA256.Create()) files.Add(new { path = entry.FullName, size = entry.Length, sha256 = PortableFiles.Hex(sha.ComputeHash(bytes)) });
                }
                Payload = new Dictionary<string, object> { { "product", "CodexMeter.Windows" }, { "version", "9.9.9" }, { "channel", "stable" }, { "runtime", SignedRelease.CurrentRuntime },
                    { "url", "https://example.test/windows.zip" }, { "size", new FileInfo(Archive).Length }, { "sha256", PortableFiles.Sha256(Archive) }, { "notes", "fixture" }, { "files", files } };
            }
            public string Envelope()
            { var bytes = Encoding.UTF8.GetBytes(Json.Encode(Payload)); return Json.Encode(new { payload = Convert.ToBase64String(bytes), signature = Convert.ToBase64String(Key.SignData(bytes, CryptoConfig.MapNameToOID("SHA256"))) }); }
            public SignedRelease Release() { return SignedRelease.Verify(Envelope(), Public, "stable", SignedRelease.CurrentRuntime); }
            public void Dispose() { Key.Dispose(); System.IO.Directory.Delete(Directory, true); }
        }
        private static void UpdateFeatures()
        {
            Check(ReleaseVersion.Parse("1.0.0").CompareTo(ReleaseVersion.Parse("1.0.0-beta.9")) > 0, "stable version sorts after its prereleases");
            Check(ReleaseVersion.Parse("1.0.0-beta.10").CompareTo(ReleaseVersion.Parse("1.0.0-beta.9")) > 0, "numeric prerelease identifiers compare numerically");
            Check(ReleaseVersion.Parse("1.0.0+build1").CompareTo(ReleaseVersion.Parse("1.0.0+build2")) == 0, "build metadata does not change upgrade precedence");
            foreach (string bad in new[] { "v1.0", "01.2.3", "1.0.0-01", "1.0.0-..", "1.0.0+..", "1.0.0/evil" }) Reject(() => ReleaseVersion.Parse(bad), "invalid release version rejected: " + bad);
            using (var fixture = new SignedFixture())
            {
                var release = fixture.Release(); release.VerifyArchive(fixture.Archive); Check(release.Version == "9.9.9", "signed Windows envelope and exact file archive verify");
                var envelope = Json.Parse(fixture.Envelope()); byte[] data = Convert.FromBase64String(Json.Text(envelope, "payload")); data[data.Length / 2] ^= 1; envelope["payload"] = Convert.ToBase64String(data);
                Reject(() => SignedRelease.Verify(Json.Encode(envelope), fixture.Public, "stable", SignedRelease.CurrentRuntime), "tampered signed payload is rejected before parsing");
                using (var other = new SignedFixture()) Reject(() => SignedRelease.Verify(fixture.Envelope(), other.Public, "stable", SignedRelease.CurrentRuntime), "a different signing key cannot authorize a release");
                Reject(() => SignedRelease.Verify(fixture.Envelope(), fixture.Public, "beta", SignedRelease.CurrentRuntime), "stable and beta releases cannot cross channels");
                Reject(() => SignedRelease.Verify(fixture.Envelope(), fixture.Public, "stable", "macos-arm64"), "updater rejects packages for another platform or runtime");
                fixture.Payload["version"] = "9.9.9-beta.1"; Reject(() => fixture.Release(), "stable rejects a prerelease even with a valid signature");
                fixture.Payload["channel"] = "beta"; Check(SignedRelease.Verify(fixture.Envelope(), fixture.Public, "beta", SignedRelease.CurrentRuntime).Version == "9.9.9-beta.1", "beta accepts a signed prerelease"); fixture.Payload["channel"] = "stable"; fixture.Payload["version"] = "9.9.9";
                fixture.Payload["product"] = "CodexMeter.macOS"; Reject(() => fixture.Release(), "updater rejects a macOS product identity"); fixture.Payload["product"] = "CodexMeter.Windows";
                string corrupted = Path.Combine(fixture.Directory, "corrupt.zip"); File.Copy(fixture.Archive, corrupted); using (var append = new FileStream(corrupted, FileMode.Append)) append.WriteByte(1);
                Reject(() => release.VerifyArchive(corrupted), "download size and hash reject altered package bytes");
                foreach (string name in new[] { "../evil.exe", "/absolute.exe", "C:/evil.exe", "CON", "folder/file. ", "folder\\evil", "file:stream" }) Reject(() => PortableFiles.SafeRelativeName(name), "unsafe update file path rejected: " + name);
                string unpacked = Path.Combine(fixture.Directory, "unpacked"); release.Extract(fixture.Archive, unpacked);
                Check(File.ReadAllText(Path.Combine(unpacked, "CodexMeter.exe")).Contains("fixture"), "verified archive extracts the declared Windows executable");
                Reject(() => release.Extract(fixture.Archive, unpacked), "update extraction never overwrites an existing installation");
                string oldFolder = Path.Combine(fixture.Directory, "old"); Directory.CreateDirectory(oldFolder); string old = Path.Combine(oldFolder, "CodexMeter.exe"); File.WriteAllText(old, "old version");
                int rollbacks = 0; bool started = false;
                bool success = UpdateInstall.Run(release, fixture.Archive, Path.Combine(fixture.Directory, "new-good"), old, next => { started = File.Exists(next); return true; }, previous => rollbacks++);
                Check(success && started && rollbacks == 0 && File.ReadAllText(old) == "old version", "successful updater launches the verified sibling installation and preserves the old version");
                success = UpdateInstall.Run(release, fixture.Archive, Path.Combine(fixture.Directory, "new-failed"), old, next => false, previous => { if (previous == old) rollbacks++; });
                Check(!success && rollbacks == 1 && File.ReadAllText(old) == "old version", "failed readiness returns to the untouched previous executable");
                Reject(() => UpdateInstall.Run(release, fixture.Archive, oldFolder, old, next => true, previous => { }), "installer rejects reusing its running directory");
                string malicious = Path.Combine(fixture.Directory, "traversal.zip"); File.Copy(fixture.Archive, malicious);
                using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Update)) using (var writer = new StreamWriter(zip.CreateEntry("../outside").Open())) writer.Write("unsafe");
                fixture.Payload["size"] = new FileInfo(malicious).Length; fixture.Payload["sha256"] = PortableFiles.Sha256(malicious);
                Reject(() => fixture.Release().VerifyArchive(malicious), "even a signed package cannot contain an unlisted traversal entry");
                foreach (string change in new[] { "changed-file", "duplicate", "missing", "unlisted", "symlink" })
                {
                    string modified = Path.Combine(fixture.Directory, change + ".zip"); File.Copy(fixture.Archive, modified);
                    using (var zip = ZipFile.Open(modified, ZipArchiveMode.Update))
                    {
                        if (change == "missing" || change == "changed-file") zip.GetEntry("CodexMeter.Core.dll").Delete();
                        if (change == "changed-file" || change == "duplicate" || change == "unlisted") using (var writer = new StreamWriter(zip.CreateEntry(change == "unlisted" ? "surprise.exe" : "CodexMeter.Core.dll").Open())) writer.Write("different bytes");
                        if (change == "symlink") zip.GetEntry("CodexMeter.Core.dll").ExternalAttributes = unchecked((int)0xa0000000);
                    }
                    fixture.Payload["size"] = new FileInfo(modified).Length; fixture.Payload["sha256"] = PortableFiles.Sha256(modified);
                    Reject(() => fixture.Release().VerifyArchive(modified), "signed update rejects " + change + " even when the archive hash matches");
                }
                var settings = new SettingsStore(Path.Combine(fixture.Directory, "settings.json"));
                settings.Save(new Settings { UpdateFeed = "https://example.test/{channel}/{runtime}.json", UpdatePublicKey = fixture.Public, UpdateChannel = "beta", UpdateAutomatic = true, UpdateAutoInstall = true }); string warning;
                var loaded = settings.Load(out warning); Check(loaded.Copy().UpdateFeed.Contains("{runtime}") && loaded.UpdateChannel == "beta" && loaded.UpdateAutomatic && loaded.UpdateAutoInstall && loaded.UpdatePublicKey == fixture.Public, "update settings preserve source, trust key, channel and opt-in choices");
                File.WriteAllText(settings.FilePath, "{}"); loaded = settings.Load(out warning);
                Check(loaded.UpdateFeed == WindowsReleaseSource.GitHubFeed && loaded.UpdatePublicKey == "" && !loaded.UpdateAutomatic && !loaded.UpdateAutoInstall && !WindowsReleaseSource.Configured(loaded), "existing installations use the project GitHub source without inventing trust or enabling automatic updates");
                WindowsReleaseSource.Validate(loaded); settings.Save(loaded); loaded = settings.Load(out warning);
                Check(loaded.UpdateFeed == WindowsReleaseSource.GitHubFeed && !WindowsReleaseSource.Configured(loaded), "a prepared GitHub source can be saved without a key but cannot check or install signed updates");
                Check(new Settings().UpdateFeed == WindowsReleaseSource.GitHubFeed && !WindowsReleaseSource.Configured(new Settings()), "new installations default to the project source and remain untrusted");
                foreach (string feedChannel in new[] { "stable", "beta" }) foreach (string runtime in new[] { "framework-x64", "dotnet10-x64" })
                    Check(WindowsReleaseSource.GitHubFeed.Replace("{runtime}", runtime).Replace("{channel}", feedChannel).EndsWith("/releases/download/windows-update-feed/" + feedChannel + "-" + runtime + ".json"), "GitHub feed path matches the Actions mirrored asset: " + feedChannel + "-" + runtime);
                File.WriteAllText(settings.FilePath, "{\"updateFeed\":\"\",\"updateChannel\":\"beta\"}"); loaded = settings.Load(out warning);
                Check(loaded.UpdateFeed == WindowsReleaseSource.GitHubFeed && loaded.UpdateChannel == "beta" && !WindowsReleaseSource.Configured(loaded), "beta source migration preserves the channel without inventing trust");
                File.WriteAllText(settings.FilePath, Json.Encode(new { updateFeed = "", updatePublicKey = fixture.Public, updateAutomatic = true, updateAutoInstall = true })); loaded = settings.Load(out warning);
                Check(loaded.UpdateFeed == "" && loaded.UpdatePublicKey == fixture.Public && !WindowsReleaseSource.Configured(loaded) && !loaded.UpdateAutomatic && !loaded.UpdateAutoInstall, "migration never binds an orphaned trust key or enables saved automatic flags against a new source");
                File.WriteAllText(settings.FilePath, "{\"updateFeed\":\"\",\"updateAutomatic\":true,\"updateAutoInstall\":true}"); loaded = settings.Load(out warning);
                Check(loaded.UpdateFeed == WindowsReleaseSource.GitHubFeed && !loaded.UpdateAutomatic && !loaded.UpdateAutoInstall, "preparing a default source never activates old automatic flags");
                var policy = new Settings { UpdatePublicKey = fixture.Public }; WindowsReleaseSource.Validate(policy);
                Check(WindowsReleaseSource.Configured(policy), "signed GitHub stable source is enabled only with explicit trust");
                policy.UpdateChannel = "beta";
                WindowsReleaseSource.Validate(policy);
                Check(WindowsReleaseSource.Configured(policy), "GitHub mirror supports explicit signed beta updates without using latest stable");
                policy.UpdateFeed = "https://example.test/{channel}/{runtime}.json"; WindowsReleaseSource.Validate(policy);
                Check(WindowsReleaseSource.Configured(policy), "custom signed beta feeds remain supported");
                policy.UpdatePublicKey = ""; policy.UpdateAutomatic = true;
                Reject(() => WindowsReleaseSource.Validate(policy), "automatic checks cannot be enabled without a trusted public key");
                policy.UpdateAutomatic = false; policy.UpdatePublicKey = fixture.Public; policy.UpdateAutoInstall = true;
                Reject(() => WindowsReleaseSource.Validate(policy), "automatic installation still requires automatic checks");
                policy.UpdateAutoInstall = false; policy.UpdateFeed = "http://example.test/feed.json";
                Reject(() => WindowsReleaseSource.Validate(policy), "prepared source addresses must retain HTTPS");
            }
        }
        private static async Task UpdateTransportFeatures()
        {
            var downgraded = new FixtureHandler { Handler = (r, t) => { var reply = Reply(302, ""); reply.Headers.Location = new Uri("http://example.test/insecure"); return Task.FromResult(reply); } };
            using (var updater = new UpdateDownload(downgraded))
            {
                bool rejected = false; try { await updater.ManifestAsync("https://example.test/feed", CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
                Check(rejected && downgraded.Calls == 1, "updater refuses HTTPS to HTTP redirect before following it");
            }
            int calls = 0; var redirected = new FixtureHandler { Handler = (r, t) => { calls++; if (calls > 1) return Task.FromResult(Reply(200, "{}")); var reply = Reply(302, ""); reply.Headers.Location = new Uri("https://assets.example.test/package"); return Task.FromResult(reply); } };
            using (var updater = new UpdateDownload(redirected)) Check(await updater.ManifestAsync("https://example.test/feed", CancellationToken.None) == "{}" && calls == 2, "updater supports HTTPS asset redirects without credentials");
            var bounded = new FixtureHandler { Handler = (r, t) => Task.FromResult(Reply(200, new string('x', 524289))) };
            using (var updater = new UpdateDownload(bounded))
            { bool rejected = false; try { await updater.ManifestAsync("https://example.test/feed", CancellationToken.None); } catch (InvalidDataException) { rejected = true; } Check(rejected, "updater bounds downloaded manifest memory"); }
        }
        private static async Task RestoreServiceFeatures()
        {
            string directory = TestDirectory("maintenance");
            try
            {
                using (var history = new HistoryStore(Path.Combine(directory, "history.sqlite3")))
                {
                    var fake = new FakeConnection { DelayLimits = true };
                    using (var service = new UsageService(new FixedLocator(Mock("normal")), new FakeFactory(fake)))
                    {
                        service.ConfigureHistory(history, 0, true);
                        var refresh = service.RefreshAsync(true); await fake.LimitsEntered.Task;
                        bool entered = false; string session = service.State.SessionId;
                        var maintenance = service.WithHistoryPausedAsync(() => { entered = true; history.ClearAll(); });
                        await Task.Delay(50); Check(!entered, "history maintenance waits for an in-flight service refresh");
                        fake.DelayedLimits.TrySetResult(Json.Parse(MockServer.Limits)); await refresh; await maintenance;
                        Check(entered && service.State.AccountKey == null && service.State.SessionId != session, "maintenance clears salted identity selection and notification scope before replacement");
                        fake.DelayLimits = false; service.ConfigureHistory(history, 0, true); await service.RefreshAsync(true);
                        Check(service.State.AccountKey != null && history.Accounts().Count == 1 && !service.State.IsLoading, "history collection resumes with a newly identified account after maintenance");
                    }
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        private static void ReleaseScriptFixture(string destination)
        {
            Directory.CreateDirectory(destination);
            using (var fixture = new SignedFixture())
            {
                File.Copy(fixture.Archive, Path.Combine(destination, "fixture.zip"), false);
                File.WriteAllText(Path.Combine(destination, "TEST-ONLY-private.xml"), fixture.Key.ToXmlString(true));
            }
        }
    }
}
