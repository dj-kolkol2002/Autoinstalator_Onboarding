using OnboardingManager.Models;

namespace OnboardingManager.Services;

public sealed class PackageInstallerService(ProcessRunner processRunner, AppLog log)
{
    public async Task<OperationResult> InstallAsync(
        ApplicationItem application,
        CancellationToken cancellationToken = default)
    {
        await log.WriteAsync($"Installing application: {application.Name} ({application.Provider}:{application.PackageId})");

        var provider = application.Provider.Trim().ToLowerInvariant();
        var args = provider switch
        {
            "winget" => BuildWingetArguments(application),
            "chocolatey" or "choco" => BuildChocolateyArguments(application),
            _ => null
        };

        if (args is null)
            return OperationResult.Fail($"Unsupported package provider: {application.Provider}");

        var executable = provider == "winget" ? "winget.exe" : "choco.exe";
        var result = await processRunner.RunAsync(
            executable,
            args,
            onOutput: line => _ = log.WriteAsync($"[{application.Name}] {line}"),
            cancellationToken: cancellationToken);

        var reboot = result.ExitCode is 1641 or 3010;
        if (result.ExitCode == 0 || reboot)
            return OperationResult.Ok($"Installed: {application.Name}", reboot);

        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"exit code {result.ExitCode}"
            : result.StandardError.Trim();
        return OperationResult.Fail($"Failed to install {application.Name}: {detail}", result.ExitCode);
    }

    private static string[] BuildWingetArguments(ApplicationItem item)
    {
        var args = new List<string>
        {
            "install", "--id", item.PackageId, "--exact", "--silent",
            "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity"
        };
        if (item.Scope is "machine" or "user")
        {
            args.Add("--scope");
            args.Add(item.Scope);
        }
        return [.. args];
    }

    private static string[] BuildChocolateyArguments(ApplicationItem item) =>
        ["install", item.PackageId, "--yes", "--no-progress"];
}

