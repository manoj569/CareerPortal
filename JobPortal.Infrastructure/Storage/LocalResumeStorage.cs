using JobPortal.Application.Abstractions.Candidates;
using Microsoft.Extensions.Configuration;

namespace JobPortal.Infrastructure.Storage;

public sealed class LocalResumeStorage : IResumeStorage
{
    private static readonly HashSet<string> AllowedExtensions =
        new([".pdf", ".doc", ".docx"], StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    public LocalResumeStorage(IConfiguration configuration)
    {
        var configured = configuration["ResumeStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("ResumeStorage:RootPath is not configured.");
        _root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured));
        if (_root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(x => x.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Resume storage must be outside the public web root.");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> StoreAsync(
        Stream content, string extension, CancellationToken cancellationToken = default)
    {
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Invalid resume extension.");
        var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Resolve(key);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await content.CopyToAsync(destination, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        var extension = Path.GetExtension(storageKey);
        if (string.IsNullOrWhiteSpace(storageKey) || Path.GetFileName(storageKey) != storageKey ||
            !AllowedExtensions.Contains(extension) ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(storageKey), "N", out _))
            throw new InvalidOperationException("Invalid resume storage key.");
        var path = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid resume storage key.");
        return path;
    }
}
