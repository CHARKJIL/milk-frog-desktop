using System;
using System.IO;
using Microsoft.Win32;
using MilkFrogDesktop;

// 实际检查当前用户的启动项，结束时恢复原值，不留下测试自启动。
class StartupRegistrationTest
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static int Main()
    {
        object original = null;
        RegistryValueKind kind = RegistryValueKind.String;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath))
        {
            if (key != null)
            {
                original = key.GetValue(StartupRegistration.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (original != null) kind = key.GetValueKind(StartupRegistration.ValueName);
            }
        }
        try
        {
            StartupRegistration.SetEnabled(false);
            Require(!StartupRegistration.IsEnabled(), "Disabled state was not reflected.");
            string executable = typeof(StartupRegistration).Assembly.Location;
            Require(executable.Contains(" "), "Test must use a folder whose name contains spaces.");
            Require(StartupRegistration.Command == "\"" + executable + "\"", "The executable path must be quoted.");
            StartupRegistration.SetEnabled(true);
            Require(StartupRegistration.IsEnabled(), "Enabling was not reflected.");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath))
            {
                Require((string)key.GetValue(StartupRegistration.ValueName) == StartupRegistration.Command, "Unexpected launch command.");
                Require(key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.String, "Expected REG_SZ.");
            }
            StartupRegistration.SetEnabled(true);
            Require(StartupRegistration.IsEnabled(), "Repeated enable failed.");
            // 模拟搬动文件夹后还留着旧路径；新版本必须识别并更新。
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath, true))
                key.SetValue(StartupRegistration.ValueName, "\"C:\\OldMilkFrog\\MilkFrog.exe\"", RegistryValueKind.String);
            Require(!StartupRegistration.IsEnabled(), "Stale executable path was considered enabled.");
            StartupRegistration.SetEnabled(true);
            Require(StartupRegistration.IsEnabled(), "Stale executable path was not updated.");
            StartupRegistration.SetEnabled(false);
            StartupRegistration.SetEnabled(false);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath))
                Require(key == null || key.GetValue(StartupRegistration.ValueName) == null, "Disabling did not remove the value.");
            Console.WriteLine("PASS: enable, disable, quoted space-containing path, repeat toggles, stale path replacement.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        finally
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistration.RunKeyPath))
            {
                if (original == null) key.DeleteValue(StartupRegistration.ValueName, false);
                else key.SetValue(StartupRegistration.ValueName, original, kind);
            }
            Console.WriteLine("Original startup value restored.");
        }
    }
}
