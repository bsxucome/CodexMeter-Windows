using System;
using System.IO;
using Microsoft.Win32;

namespace CodexMeter.Core
{
    public interface IStartupValueStore
    {
        string ReadCommand();
        byte[] ReadApproval();
        void WriteCommand(string command);
    }
    public sealed class RegistryStartupValueStore : IStartupValueStore
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string Name = "CodexMeter";
        public string ReadCommand()
        { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key == null ? null : key.GetValue(Name) as string; }
        public byte[] ReadApproval()
        { using (var key = Registry.CurrentUser.OpenSubKey(ApprovedKey)) return key == null ? null : key.GetValue(Name) as byte[]; }
        public void WriteCommand(string command)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                if (command == null) key.DeleteValue(Name, false); else key.SetValue(Name, command, RegistryValueKind.String);
        }
    }
    public sealed class StartupStatus
    {
        public bool Registered, OtherLocation, DisabledByWindows, ApprovalUnknown;
        public string Description
        {
            get { return !Registered ? OtherLocation ? "启动项指向其他版本；启用后将更新到当前路径。" : "尚未启用登录启动。" :
                DisabledByWindows ? "已注册，但被 Windows 禁用；请在系统启动应用设置中启用。" :
                ApprovalUnknown ? "已注册；Windows 启动批准状态无法确认。" : "已注册当前版本，登录后自动运行。"; }
        }
    }
    public sealed class StartupRegistration
    {
        private readonly IStartupValueStore store;
        private readonly string command;
        public StartupRegistration(IStartupValueStore store, string executable)
        {
            this.store = store;
            if (!Path.IsPathRooted(executable) || executable.IndexOf('"') >= 0 || executable.IndexOf('\n') >= 0 || executable.IndexOf('\r') >= 0)
                throw new ArgumentException("A complete executable path is required.");
            command = "\"" + executable + "\"";
        }
        public StartupStatus Read()
        {
            var value = store.ReadCommand();
            var approval = store.ReadApproval();
            // StartupApproved is observed only. Never override a user's system-level disable choice.
            int flag = approval != null && approval.Length >= 12 ? approval[0] : 0;
            return new StartupStatus { Registered = string.Equals(value, command, StringComparison.OrdinalIgnoreCase),
                OtherLocation = !string.IsNullOrEmpty(value) && !string.Equals(value, command, StringComparison.OrdinalIgnoreCase),
                DisabledByWindows = flag == 3 || flag == 7, ApprovalUnknown = approval != null && flag != 2 && flag != 3 && flag != 6 && flag != 7 };
        }
        public void Set(bool enabled)
        {
            if (enabled && command.Length > 260) throw new IOException("登录启动路径过长，请将应用放到较短的目录。");
            store.WriteCommand(enabled ? command : null);
            var value = store.ReadCommand();
            if (enabled ? !string.Equals(value, command, StringComparison.OrdinalIgnoreCase) : value != null)
                throw new IOException("登录启动设置未能生效。");
        }
        public string CaptureCommand() { return store.ReadCommand(); }
        public void RestoreCommand(string value) { store.WriteCommand(value); }
    }
}
