using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using OnboardingManager.Models;

namespace OnboardingManager.Services;

public sealed class ActiveDirectoryService(ProcessRunner processRunner, AppLog log)
{
    public async Task<OperationResult> TestDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        var validation = InputValidator.ValidateDomainName(domain);
        if (validation is not null) return OperationResult.Fail(validation);

        await log.WriteAsync($"Locating a domain controller for: {domain}");
        var result = await processRunner.RunAsync(
            Path.Combine(Environment.SystemDirectory, "nltest.exe"),
            [$"/dsgetdc:{domain.Trim()}", "/force"],
            cancellationToken: cancellationToken);

        return result.ExitCode == 0
            ? OperationResult.Ok($"A domain controller was found for {domain}.")
            : OperationResult.Fail($"No domain controller was found. Check DNS and the corporate network connection. {result.StandardError}", result.ExitCode);
    }

    public async Task<OperationResult> JoinAsync(
        DomainJoinRequest request,
        CancellationToken cancellationToken = default)
    {
        var password = SecureStringToString(request.Password);
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                request.ComputerName,
                request.DomainName,
                request.UserName,
                request.OuPath,
                Password = password
            });
            var input = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            await log.WriteAsync($"Joining host {request.ComputerName} to domain {request.DomainName}.");

            var result = await RunPowerShellAsync(DomainJoinScript, input, cancellationToken);
            return result.ExitCode == 0
                ? OperationResult.Ok($"The computer was joined to domain {request.DomainName}. A restart is required.", true)
                : OperationResult.Fail($"Failed to join the domain: {result.StandardError.Trim()}", result.ExitCode);
        }
        finally
        {
            password = string.Empty;
        }
    }

    public async Task<OperationResult> RenameAsync(string computerName, CancellationToken cancellationToken = default)
    {
        var input = Convert.ToBase64String(Encoding.UTF8.GetBytes(computerName));
        await log.WriteAsync($"Renaming the computer to {computerName}.");
        var result = await RunPowerShellAsync(RenameScript, input, cancellationToken);
        return result.ExitCode == 0
            ? OperationResult.Ok($"The computer was renamed to {computerName}. A restart is required.", true)
            : OperationResult.Fail($"Failed to rename the computer: {result.StandardError.Trim()}", result.ExitCode);
    }

    private async Task<ProcessResult> RunPowerShellAsync(
        string script,
        string standardInput,
        CancellationToken cancellationToken)
    {
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        return await processRunner.RunAsync(
            powershell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encodedScript],
            standardInput,
            line => _ = log.WriteAsync($"[AD] {line}"),
            cancellationToken);
    }

    private static string SecureStringToString(SecureString value)
    {
        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(value);
            return Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }

    private const string DomainJoinScript = """
        $ErrorActionPreference = 'Stop'
        try {
            $encoded = [Console]::In.ReadLine()
            $jsonText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded))
            $data = $jsonText | ConvertFrom-Json
            $securePassword = ConvertTo-SecureString $data.Password -AsPlainText -Force
            $credential = [PSCredential]::new($data.UserName, $securePassword)
            $params = @{
                DomainName = $data.DomainName
                Credential = $credential
                NewName = $data.ComputerName
                Force = $true
                PassThru = $true
            }
            if (-not [string]::IsNullOrWhiteSpace($data.OuPath)) { $params['OUPath'] = $data.OuPath }
            Add-Computer @params | Out-String | Write-Output
            Write-Output 'DOMAIN_JOIN_OK'
            exit 0
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;

    private const string RenameScript = """
        $ErrorActionPreference = 'Stop'
        try {
            $encoded = [Console]::In.ReadLine()
            $name = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded))
            Rename-Computer -NewName $name -Force -PassThru | Out-String | Write-Output
            exit 0
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;
}

