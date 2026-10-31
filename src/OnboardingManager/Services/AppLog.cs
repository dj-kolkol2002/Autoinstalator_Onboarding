namespace OnboardingManager.Services;

public sealed class AppLog
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _logPath;

    public AppLog()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OnboardingManager",
            "Logs");
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, $"onboarding-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public event Action<string>? EntryWritten;
    public string LogPath => _logPath;

    public async Task WriteAsync(string message)
    {
        var normalized = message.ReplaceLineEndings(" ").Trim();
        if (normalized.Length == 0) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {normalized}";

        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_logPath, line + Environment.NewLine);
        }
        finally
        {
            _writeLock.Release();
        }

        EntryWritten?.Invoke(line);
    }
}

