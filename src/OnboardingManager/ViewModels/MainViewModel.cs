using System.Collections.ObjectModel;
using System.Security;
using System.Text;
using System.Windows;
using OnboardingManager.Models;
using OnboardingManager.Services;

namespace OnboardingManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private readonly ProcessRunner _processRunner = new();
    private readonly AppLog _log;
    private readonly PreflightService _preflightService;
    private readonly ApplicationCatalogService _catalogService = new();
    private readonly PackageInstallerService _packageInstaller;
    private readonly ActiveDirectoryService _activeDirectory;
    private CancellationTokenSource? _cancellation;

    private string _computerName = Environment.MachineName;
    private string _domainName = string.Empty;
    private string _domainUser = string.Empty;
    private string _ouPath = string.Empty;
    private bool _joinDomain = true;
    private bool _isBusy;
    private double _progress;
    private string _statusText = "Ready";
    private string _preflightText = "The system check has not been run yet.";
    private string _domainCheckText = "The domain connection has not been tested.";
    private string _planText = "Configure onboarding and select “Show plan”.";
    private string _customPackageId = string.Empty;
    private string _customPackageName = string.Empty;
    private string _customProvider = "winget";

    public MainViewModel()
    {
        _log = new AppLog();
        _preflightService = new PreflightService(_processRunner);
        _packageInstaller = new PackageInstallerService(_processRunner, _log);
        _activeDirectory = new ActiveDirectoryService(_processRunner, _log);
        _log.EntryWritten += line => _uiContext?.Post(_ => Logs.Add(line), null);
    }

    public ObservableCollection<ApplicationItem> Applications { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public string[] Providers { get; } = ["winget", "chocolatey"];

    public string ComputerName { get => _computerName; set => SetProperty(ref _computerName, value); }
    public string DomainName { get => _domainName; set => SetProperty(ref _domainName, value); }
    public string DomainUser { get => _domainUser; set => SetProperty(ref _domainUser, value); }
    public string OuPath { get => _ouPath; set => SetProperty(ref _ouPath, value); }
    public bool JoinDomain { get => _joinDomain; set => SetProperty(ref _joinDomain, value); }
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string PreflightText { get => _preflightText; set => SetProperty(ref _preflightText, value); }
    public string DomainCheckText { get => _domainCheckText; set => SetProperty(ref _domainCheckText, value); }
    public string PlanText { get => _planText; set => SetProperty(ref _planText, value); }
    public string CustomPackageId { get => _customPackageId; set => SetProperty(ref _customPackageId, value); }
    public string CustomPackageName { get => _customPackageName; set => SetProperty(ref _customPackageName, value); }
    public string CustomProvider { get => _customProvider; set => SetProperty(ref _customProvider, value); }
    public string LogPath => _log.LogPath;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    public bool IsNotBusy => !IsBusy;

    public async Task InitializeAsync()
    {
        try
        {
            foreach (var app in await _catalogService.LoadAsync()) Applications.Add(app);
            await RefreshPreflightAsync();
            await _log.WriteAsync($"Onboarding Manager started. Log: {LogPath}");
        }
        catch (Exception ex)
        {
            StatusText = $"Initialization error: {ex.Message}";
            await _log.WriteAsync(StatusText);
        }
    }

    public async Task RefreshPreflightAsync()
    {
        try
        {
            var result = await _preflightService.RunAsync();
            PreflightText = result.ToString();
            StatusText = result.CanContinue ? "System check completed successfully." : "The system check found issues that require attention.";
        }
        catch (Exception ex)
        {
            PreflightText = $"System check failed: {ex.Message}";
        }
    }

    public async Task TestDomainAsync()
    {
        DomainCheckText = "Checking the domain controller...";
        var result = await _activeDirectory.TestDomainAsync(DomainName);
        DomainCheckText = result.Message;
        await _log.WriteAsync(result.Message);
    }

    public string? AddCustomApplication()
    {
        if (!InputValidator.IsValidPackageId(CustomPackageId))
            return "Invalid package identifier.";
        if (Applications.Any(x => x.Provider.Equals(CustomProvider, StringComparison.OrdinalIgnoreCase) &&
                                  x.PackageId.Equals(CustomPackageId, StringComparison.OrdinalIgnoreCase)))
            return "This package is already on the list.";

        Applications.Add(new ApplicationItem
        {
            Name = string.IsNullOrWhiteSpace(CustomPackageName) ? CustomPackageId.Trim() : CustomPackageName.Trim(),
            PackageId = CustomPackageId.Trim(),
            Provider = CustomProvider,
            Category = "Custom",
            Scope = "machine",
            IsSelected = true
        });
        CustomPackageId = string.Empty;
        CustomPackageName = string.Empty;
        return null;
    }

    public bool BuildPlan(out string? validationError)
    {
        validationError = ValidateConfiguration(passwordRequired: false, passwordLength: 0);
        if (validationError is not null) return false;

        var selected = Applications.Where(x => x.IsSelected).ToList();
        var plan = new StringBuilder();
        plan.AppendLine("ONBOARDING PLAN");
        plan.AppendLine("1. Windows 11 system check");
        plan.AppendLine($"2. Install applications: {selected.Count}");
        foreach (var app in selected) plan.AppendLine($"   • {app.Name} ({app.Provider}:{app.PackageId})");
        plan.AppendLine($"3. Computer name: {ComputerName.Trim().ToUpperInvariant()}");
        plan.AppendLine(JoinDomain
            ? $"4. Join domain: {DomainName.Trim()}" + (string.IsNullOrWhiteSpace(OuPath) ? string.Empty : $"\n   OU: {OuPath.Trim()}")
            : "4. Do not join a domain");
        plan.AppendLine("5. Summary and restart after confirmation");
        PlanText = plan.ToString();
        return true;
    }

    public async Task ExecuteAsync(SecureString password)
    {
        var error = ValidateConfiguration(passwordRequired: JoinDomain, password.Length);
        if (error is not null)
        {
            MessageBox.Show(error, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        var selected = Applications.Where(x => x.IsSelected).ToList();
        var totalSteps = selected.Count + 2;
        var completed = 0;
        var failures = new List<string>();
        var rebootRequired = false;

        try
        {
            await _log.WriteAsync("Started executing the onboarding plan.");
            var preflight = await _preflightService.RunAsync(_cancellation.Token);
            if (!preflight.CanContinue)
                throw new InvalidOperationException("The preflight check does not allow onboarding to continue. Review the System tab.");

            foreach (var provider in selected.Select(x => x.Provider.Trim().ToLowerInvariant()).Distinct())
            {
                var executable = provider switch
                {
                    "winget" => "winget.exe",
                    "chocolatey" or "choco" => "choco.exe",
                    _ => throw new InvalidOperationException($"Unsupported package provider: {provider}")
                };
                var providerCheck = await _processRunner.RunAsync(
                    "where.exe", [executable], cancellationToken: _cancellation.Token);
                if (providerCheck.ExitCode != 0)
                    throw new InvalidOperationException($"Package provider {provider} is not available on this computer.");
            }

            Progress = ++completed * 100d / totalSteps;
            foreach (var app in selected)
            {
                StatusText = $"Installing: {app.Name}";
                var result = await _packageInstaller.InstallAsync(app, _cancellation.Token);
                rebootRequired |= result.RebootRequired;
                if (!result.Success) failures.Add(result.Message);
                await _log.WriteAsync(result.Message);
                Progress = ++completed * 100d / totalSteps;
            }

            OperationResult identityResult;
            if (JoinDomain)
            {
                StatusText = "Checking the domain...";
                var domainTest = await _activeDirectory.TestDomainAsync(DomainName, _cancellation.Token);
                if (!domainTest.Success) throw new InvalidOperationException(domainTest.Message);

                StatusText = "Joining the computer to the domain...";
                identityResult = await _activeDirectory.JoinAsync(
                    new DomainJoinRequest(
                        ComputerName.Trim().ToUpperInvariant(),
                        DomainName.Trim(),
                        DomainUser.Trim(),
                        OuPath.Trim(),
                        password),
                    _cancellation.Token);
            }
            else if (!ComputerName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                identityResult = await _activeDirectory.RenameAsync(
                    ComputerName.Trim().ToUpperInvariant(),
                    _cancellation.Token);
            }
            else
            {
                identityResult = OperationResult.Ok("The computer name is unchanged.");
            }

            rebootRequired |= identityResult.RebootRequired;
            if (!identityResult.Success) failures.Add(identityResult.Message);
            await _log.WriteAsync(identityResult.Message);
            Progress = 100;

            StatusText = failures.Count == 0
                ? "Onboarding completed successfully."
                : $"Onboarding completed with {failures.Count} error(s).";

            var summary = StatusText + $"\n\nLog: {LogPath}";
            if (failures.Count > 0) summary += "\n\n" + string.Join("\n", failures.Select(x => "• " + x));
            if (rebootRequired) summary += "\n\nA restart is required to complete the configuration.";
            MessageBox.Show(summary, "Onboarding result", MessageBoxButton.OK,
                failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Onboarding was cancelled.";
            await _log.WriteAsync(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Onboarding stopped: {ex.Message}";
            await _log.WriteAsync(StatusText);
            MessageBox.Show(StatusText, "Onboarding error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    private string? ValidateConfiguration(bool passwordRequired, int passwordLength)
    {
        var hostnameError = InputValidator.ValidateComputerName(ComputerName);
        if (hostnameError is not null) return hostnameError;
        if (!JoinDomain) return null;

        var domainError = InputValidator.ValidateDomainName(DomainName);
        if (domainError is not null) return domainError;
        if (string.IsNullOrWhiteSpace(DomainUser)) return "Enter an account authorized to join computers to the domain.";
        if (passwordRequired && passwordLength == 0) return "Enter the domain account password.";
        return InputValidator.ValidateOuPath(OuPath);
    }
}

