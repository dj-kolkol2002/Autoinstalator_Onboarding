using System.Text.Json;
using OnboardingManager.Models;

namespace OnboardingManager.Services;

public sealed class ApplicationCatalogService
{
    public async Task<IReadOnlyList<ApplicationItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalog", "applications.json");
        await using var stream = File.OpenRead(path);
        var items = await JsonSerializer.DeserializeAsync<List<ApplicationItem>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);
        return items ?? [];
    }
}

