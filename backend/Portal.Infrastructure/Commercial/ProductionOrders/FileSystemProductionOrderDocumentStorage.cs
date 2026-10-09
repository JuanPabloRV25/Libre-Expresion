using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Portal.Application.Commercial.ProductionOrders;

namespace Portal.Infrastructure.Commercial.ProductionOrders;

public sealed record ProductionOrderDocumentStorageOptions
{
    public const string SectionName = "CommercialDocuments";
    public string RootPath { get; init; } = "/var/lib/portal/documents";
}

public sealed class FileSystemProductionOrderDocumentStorage(
    IOptions<ProductionOrderDocumentStorageOptions> options) : IProductionOrderDocumentStorage
{
    private readonly string rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredDocument> SaveAsync(Guid orderId, UploadedFileCommand file, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storageKey = $"{orderId:N}/{Guid.NewGuid():N}{extension}";
        var fullPath = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var sha256 = SHA256.Create();
        await using var crypto = new CryptoStream(destination, sha256, CryptoStreamMode.Write, leaveOpen: true);
        await file.Content.CopyToAsync(crypto, cancellationToken);
        await crypto.FlushFinalBlockAsync(cancellationToken);

        return new StoredDocument(
            storageKey,
            ProductionOrderFileValidator.SanitizeFileName(file.FileName),
            ProductionOrderFileValidator.ContentType(extension),
            destination.Length,
            Convert.ToHexString(sha256.Hash!).ToLowerInvariant());
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(Resolve(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        var path = Path.GetFullPath(Path.Combine(rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid document storage key.");
        return path;
    }
}
