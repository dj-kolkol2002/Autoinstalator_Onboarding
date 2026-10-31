using System.Windows;
using OnboardingManager.ViewModels;

namespace OnboardingManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private async void RefreshPreflight_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshPreflightAsync();

    private async void TestDomain_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.TestDomainAsync();

    private void AddPackage_Click(object sender, RoutedEventArgs e)
    {
        var error = _viewModel.AddCustomApplication();
        if (error is not null)
            MessageBox.Show(error, "Add package", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BuildPlan_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.BuildPlan(out var error))
            MessageBox.Show(error, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        using var password = DomainPasswordBox.SecurePassword.Copy();
        await _viewModel.ExecuteAsync(password);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.Cancel();
}

