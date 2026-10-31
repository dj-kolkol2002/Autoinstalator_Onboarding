using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OnboardingManager.Models;

public sealed class ApplicationItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; init; }
    public required string PackageId { get; init; }
    public string Provider { get; init; } = "winget";
    public string Category { get; init; } = "Other";
    public string Scope { get; init; } = "machine";
    public string Notes { get; init; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

