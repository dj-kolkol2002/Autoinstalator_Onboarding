namespace OnboardingManager.Models;

public sealed record OperationResult(bool Success, string Message, int ExitCode = 0, bool RebootRequired = false)
{
    public static OperationResult Ok(string message, bool rebootRequired = false) =>
        new(true, message, 0, rebootRequired);

    public static OperationResult Fail(string message, int exitCode = 1) =>
        new(false, message, exitCode);
}

public sealed record DomainJoinRequest(
    string ComputerName,
    string DomainName,
    string UserName,
    string OuPath,
    System.Security.SecureString Password);

