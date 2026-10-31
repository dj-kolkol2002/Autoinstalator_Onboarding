using System.Net.NetworkInformation;
using System.Security.Principal;
using Microsoft.Win32;

namespace OnboardingManager.Services;

public sealed record PreflightResult(
    bool IsAdministrator,
    bool IsWindows11,
    bool NetworkAvailable,
    bool WingetAvailable,
    bool PendingReboot,
    long FreeDiskGb,
    string OsDescription)
{
    public bool CanContinue => IsAdministrator && IsWindows11 && NetworkAvailable && FreeDiskGb >= 10;

    public override string ToString() =>
        $"Administrator: {YesNo(IsAdministrator)}\n" +
        $"Windows 11: {YesNo(IsWindows11)}\n" +
        $"Network: {YesNo(NetworkAvailable)}\n" +
        $"WinGet: {YesNo(WingetAvailable)}\n" +
        $"Free disk space: {FreeDiskGb} GB\n" +
        $"Pending restart: {YesNo(PendingReboot)}\n\n{OsDescription}";

    private static string YesNo(bool value) => value ? "YES" : "NO";
}

public sealed class PreflightService(ProcessRunner processRunner)
{
    public async Task<PreflightResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var os = Environment.OSVersion;
        var isWindows11 = OperatingSystem.IsWindows() && os.Version.Build >= 22000;
        var winget = await processRunner.RunAsync("where.exe", ["winget.exe"], cancellationToken: cancellationToken);
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var freeGb = new DriveInfo(root).AvailableFreeSpace / 1024 / 1024 / 1024;

        return new PreflightResult(
            isAdmin,
            isWindows11,
            NetworkInterface.GetIsNetworkAvailable(),
            winget.ExitCode == 0,
            HasPendingReboot(),
            freeGb,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    }

    private static bool HasPendingReboot()
    {
        using var cbs = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        using var wu = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        using var session = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Session Manager");
        var pendingRename = session?.GetValue("PendingFileRenameOperations") is not null;
        return cbs is not null || wu is not null || pendingRename;
    }
}

