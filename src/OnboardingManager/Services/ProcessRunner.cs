using System.Diagnostics;
using System.IO;
using System.Text;

namespace OnboardingManager.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Unable to start: {fileName}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(9009, string.Empty, ex.Message);
        }

        if (standardInput is not null)
        {
            await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        var output = new StringBuilder();
        var error = new StringBuilder();
        var outputTask = PumpAsync(process.StandardOutput, output, onOutput, cancellationToken);
        var errorTask = PumpAsync(process.StandardError, error, onOutput, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder destination,
        Action<string>? callback,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            destination.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(line)) callback?.Invoke(line);
        }
    }
}

