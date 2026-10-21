using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexMeter.Core
{
    public static class WindowsReleaseSource
    {
        public const string ProjectHome = "https://github.com/bsxucome/CodexMeter-for-Windows";
        public const string Releases = ProjectHome + "/releases";
        public const string LatestRelease = Releases + "/latest";
        public const string GitHubFeed = Releases + "/download/windows-update-feed/{channel}-{runtime}.json";
        public const string UpstreamHome = "https://github.com/raycalrui/CodexMeter";

        public static string DefaultFeed(string address, string publicKey)
        { return string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(publicKey) ? GitHubFeed : address ?? ""; }
        public static bool Configured(Settings value)
        {
            return !string.IsNullOrWhiteSpace(value.UpdateFeed) && !string.IsNullOrWhiteSpace(value.UpdatePublicKey);
        }
        public static void Validate(Settings value)
        {
            string address = value.UpdateFeed ?? "", key = value.UpdatePublicKey ?? "";
            if (address.Length > 0)
            {
                SignedRelease.Https(address.Replace("{channel}", value.UpdateChannel).Replace("{runtime}", SignedRelease.CurrentRuntime));
            }
            if (key.Length > 0) { if (address.Length == 0) throw new ArgumentException(); SignedRelease.PublicKey(key); }
            if ((value.UpdateAutomatic || value.UpdateAutoInstall) && !Configured(value)) throw new ArgumentException();
            if (value.UpdateAutoInstall && !value.UpdateAutomatic) throw new ArgumentException();
        }
    }
    public sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        private int major, minor, patch; private string[] prerelease;
        public string Value { get; private set; }
        public static ReleaseVersion Parse(string value)
        {
            var match = Regex.Match(value ?? "", @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+([0-9A-Za-z.-]+))?$");
            var result = new ReleaseVersion { Value = value };
            if (!match.Success || value.Length > 100 || !int.TryParse(match.Groups[1].Value, out result.major) || !int.TryParse(match.Groups[2].Value, out result.minor) || !int.TryParse(match.Groups[3].Value, out result.patch))
                throw new InvalidDataException("版本号无效。");
            result.prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : new string[0];
            if (result.prerelease.Any(p => p.Length == 0 || (Regex.IsMatch(p, @"^\d+$") && p.Length > 1 && p[0] == '0')) ||
                (match.Groups[5].Success && match.Groups[5].Value.Split('.').Any(p => p.Length == 0))) throw new InvalidDataException("版本号无效。");
            return result;
        }
        public int CompareTo(ReleaseVersion other)
        {
            int compared = major.CompareTo(other.major); if (compared != 0) return compared;
            compared = minor.CompareTo(other.minor); if (compared != 0) return compared;
            compared = patch.CompareTo(other.patch); if (compared != 0) return compared;
            if (prerelease.Length == 0 || other.prerelease.Length == 0) return prerelease.Length == other.prerelease.Length ? 0 : prerelease.Length == 0 ? 1 : -1;
            for (int i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
            {
                string a = prerelease[i], b = other.prerelease[i]; bool an = Regex.IsMatch(a, @"^\d+$"), bn = Regex.IsMatch(b, @"^\d+$");
                compared = an && bn ? a.Length == b.Length ? string.CompareOrdinal(a, b) : a.Length.CompareTo(b.Length) : an != bn ? an ? -1 : 1 : string.CompareOrdinal(a, b);
                if (compared != 0) return compared;
            }
            return prerelease.Length.CompareTo(other.prerelease.Length);
        }
        public bool Prerelease { get { return prerelease.Length != 0; } }
    }
    public sealed class ReleaseFile { public string Path, Sha256; public long Size; }
    public sealed class SignedRelease
    {
        public string Version, Channel, Runtime, Url, Sha256, Notes;
        public long Size;
        public IList<ReleaseFile> Files;
        public static string CurrentRuntime
        {
            get {
#if NETFRAMEWORK
                return "framework-x64";
#else
                return "dotnet10-x64";
#endif
            }
        }
        public static RSAParameters PublicKey(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 4096) throw new InvalidDataException("更新签名公钥无效。");
            try
            {
                var key = Json.Parse(json); byte[] modulus = Convert.FromBase64String(Json.Text(key, "modulus") ?? ""), exponent = Convert.FromBase64String(Json.Text(key, "exponent") ?? "");
                if (modulus.Length < 256 || modulus.Length > 1024 || exponent.Length < 1 || exponent.Length > 8 || key.Count != 2) throw new InvalidDataException("更新签名公钥无效。");
                return new RSAParameters { Modulus = modulus, Exponent = exponent };
            }
            catch (FormatException) { throw new InvalidDataException("更新签名公钥无效。"); }
            catch (ArgumentException) { throw new InvalidDataException("更新签名公钥无效。"); }
        }
        public static Uri Https(string value)
        {
            Uri uri;
            if (value == null || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidDataException("Windows 更新地址必须使用 HTTPS。");
            return uri;
        }
        public static SignedRelease Verify(string envelope, string publicKey, string channel, string runtime)
        {
            if (envelope == null || envelope.Length > 524288) throw new InvalidDataException("更新清单过大。");
            var signed = Json.Parse(envelope); byte[] payload, signature;
            try { payload = Convert.FromBase64String(Json.Text(signed, "payload") ?? ""); signature = Convert.FromBase64String(Json.Text(signed, "signature") ?? ""); }
            catch (FormatException) { throw new InvalidDataException("更新签名无效。"); }
            if (payload.Length == 0 || payload.Length > 262144 || signature.Length < 256 || signature.Length > 1024) throw new InvalidDataException("更新签名无效。");
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false; rsa.ImportParameters(PublicKey(publicKey));
                if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), signature)) throw new InvalidDataException("更新签名校验失败。");
            }
            var data = Json.Parse(Encoding.UTF8.GetString(payload));
            if (Json.Text(data, "product") != "CodexMeter.Windows" || Json.Text(data, "channel") != channel || Json.Text(data, "runtime") != runtime ||
                (channel != "stable" && channel != "beta")) throw new InvalidDataException("更新包不适用于当前 Windows 版本。");
            var version = ReleaseVersion.Parse(Json.Text(data, "version"));
            if (channel == "stable" && version.Prerelease) throw new InvalidDataException("稳定通道不接受预览发行。");
            var result = new SignedRelease { Version = version.Value, Channel = channel, Runtime = runtime, Url = Https(Json.Text(data, "url")).AbsoluteUri,
                Sha256 = Hash(data, "sha256"), Size = SizeValue(data, "size", 536870912), Notes = (Json.Text(data, "notes") ?? "") };
            if (result.Notes.Length > 8000) throw new InvalidDataException("更新说明过长。");
            var rows = Json.Get(data, "files") as IEnumerable;
            if (rows == null || rows is string || rows is IDictionary) throw new InvalidDataException("更新文件清单无效。");
            result.Files = new List<ReleaseFile>(); long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var file = row as Dictionary<string, object>; string name = PortableFiles.SafeRelativeName(Json.Text(file, "path"));
                if (result.Files.Count >= 4096 || !names.Add(name)) throw new InvalidDataException("更新文件清单无效。");
                long size = SizeValue(file, "size", 536870912, true); total += size;
                if (total > 1073741824) throw new InvalidDataException("更新包解压后过大。");
                result.Files.Add(new ReleaseFile { Path = name, Size = size, Sha256 = Hash(file, "sha256") });
            }
            if (!names.Contains("CodexMeter.exe") || !names.Contains("CodexMeter.Core.dll")) throw new InvalidDataException("更新包缺少 Windows 程序文件。");
            return result;
        }
        private static string Hash(Dictionary<string, object> data, string key)
        { var value = Json.Text(data, key); if (!Regex.IsMatch(value ?? "", "^[a-f0-9]{64}$")) throw new InvalidDataException("文件哈希无效。"); return value; }
        private static long SizeValue(Dictionary<string, object> data, string key, long maximum, bool allowZero = false)
        { var value = Json.Number(data, key); if (!value.HasValue || value < (allowZero ? 0 : 1) || value > maximum || value != Math.Floor(value.Value)) throw new InvalidDataException("更新文件大小无效。"); return (long)value.Value; }
        public void VerifyArchive(string path)
        {
            if (new FileInfo(path).Length != Size || PortableFiles.Sha256(path) != Sha256) throw new InvalidDataException("更新包哈希校验失败。");
            using (var stream = File.OpenRead(path)) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0) throw new InvalidDataException("更新包包含链接。");
                    string name = entry.FullName;
                    if (name.EndsWith("/"))
                    { PortableFiles.SafeRelativeName(name.TrimEnd('/')); if (!Files.Any(f => f.Path.StartsWith(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("更新目录无效。"); continue; }
                    PortableFiles.SafeRelativeName(name);
                    var expected = Files.FirstOrDefault(f => string.Equals(f.Path, name, StringComparison.OrdinalIgnoreCase));
                    if (expected == null || !seen.Add(name) || entry.Length != expected.Size) throw new InvalidDataException("更新文件清单不一致。");
                    using (var input = entry.Open())
                        if (StreamHash(input, expected.Size) != expected.Sha256) throw new InvalidDataException("更新文件校验失败。");
                }
                if (seen.Count != Files.Count) throw new InvalidDataException("更新文件缺失。");
            }
        }
        private static string StreamHash(Stream input, long expected)
        {
            using (var hash = SHA256.Create())
            {
                var buffer = new byte[65536]; int count; long total = 0;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                { total += count; if (total > expected) throw new InvalidDataException("更新文件大小不一致。"); hash.TransformBlock(buffer, 0, count, buffer, 0); }
                if (total != expected) throw new InvalidDataException("更新文件大小不一致。");
                hash.TransformFinalBlock(new byte[0], 0, 0); return PortableFiles.Hex(hash.Hash);
            }
        }
        public void Extract(string archivePath, string destination)
        {
            VerifyArchive(archivePath);
            if (Directory.Exists(destination)) throw new IOException("更新目标目录已存在。");
            Directory.CreateDirectory(destination);
            using (var stream = File.OpenRead(archivePath)) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                foreach (var file in Files)
                {
                    string path = PortableFiles.ChildPath(destination, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var entry = archive.Entries.First(e => string.Equals(e.FullName, file.Path, StringComparison.OrdinalIgnoreCase));
                    using (var input = entry.Open()) using (var output = new FileStream(path, FileMode.CreateNew)) PortableFiles.CopyBounded(input, output, file.Size);
                    if (new FileInfo(path).Length != file.Size || PortableFiles.Sha256(path) != file.Sha256) throw new InvalidDataException("更新解压校验失败。");
                }
        }
    }
    public sealed class UpdateDownload : IDisposable
    {
        private readonly HttpClient client;
        public UpdateDownload(HttpMessageHandler handler = null)
        { client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }; }
        public async Task<string> ManifestAsync(string address, CancellationToken token)
        {
            using (var response = await Get(SignedRelease.Https(address), token).ConfigureAwait(false))
            using (token.Register(() => response.Dispose()))
            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new MemoryStream())
            { await Copy(input, output, 524288, token, null).ConfigureAwait(false); return Encoding.UTF8.GetString(output.ToArray()); }
        }
        public async Task DownloadAsync(SignedRelease release, string path, CancellationToken token, IProgress<double> progress = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var response = await Get(SignedRelease.Https(release.Url), token).ConfigureAwait(false))
            using (token.Register(() => response.Dispose()))
            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await Copy(input, output, release.Size, token, progress).ConfigureAwait(false);
            release.VerifyArchive(path);
        }
        private async Task<HttpResponseMessage> Get(Uri uri, CancellationToken token)
        {
            for (int i = 0; i < 6; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("CodexMeter-Windows/" + ProductVersion.Current);
                HttpResponseMessage response;
                using (request) response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400 && response.Headers.Location != null)
                { var next = new Uri(uri, response.Headers.Location); response.Dispose(); uri = SignedRelease.Https(next.AbsoluteUri); continue; }
                if (!response.IsSuccessStatusCode) { response.Dispose(); throw new IOException("无法读取 Windows 更新源。"); }
                return response;
            }
            throw new IOException("更新地址重定向次数过多。");
        }
        private static async Task Copy(Stream input, Stream output, long maximum, CancellationToken token, IProgress<double> progress)
        {
            var buffer = new byte[65536]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
            { total += count; if (total > maximum) throw new InvalidDataException("更新文件超过声明大小。"); await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false); if (progress != null) progress.Report(total * 100.0 / maximum); }
        }
        public void Dispose() { client.Dispose(); }
    }
}
