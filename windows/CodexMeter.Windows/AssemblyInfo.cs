using System.Reflection;
[assembly: AssemblyTitle("CodexMeter for Windows")]
[assembly: AssemblyDescription("Local Codex quota tray companion")]
[assembly: AssemblyProduct("CodexMeter for Windows")]
[assembly: AssemblyVersion(CodexMeter.Core.ProductVersion.Assembly)]
[assembly: AssemblyFileVersion(CodexMeter.Core.ProductVersion.Assembly)]
[assembly: AssemblyInformationalVersion(CodexMeter.Core.ProductVersion.Current)]
#if !NETFRAMEWORK
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]
#endif
