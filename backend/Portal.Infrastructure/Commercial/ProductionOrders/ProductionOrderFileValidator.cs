using System.Text.RegularExpressions;

namespace Portal.Infrastructure.Commercial.ProductionOrders;

internal static partial class ProductionOrderFileValidator
{
    public const long MaximumFileSize = 25L * 1024 * 1024;
    public const long MaximumOrderSize = 100L * 1024 * 1024;

    private static readonly HashSet<string> QuotationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".xlsx", ".xls",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".xlsx", ".xls", ".docx", ".doc", ".ods", ".odt", ".csv", ".txt",
        ".jpg", ".jpeg", ".png", ".webp",
    };

    private static readonly HashSet<string> DangerousInnerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".cmd", ".bat", ".com", ".ps1", ".js", ".vbs", ".msi", ".scr", ".sh",
        ".jar", ".dll", ".html", ".htm", ".svg", ".xlsm", ".docm", ".pptm", ".zip", ".rar", ".7z",
    };

    public static string? ValidateQuotation(string fileName, long length, ReadOnlySpan<byte> header) =>
        Validate(fileName, length, header, QuotationExtensions);

    public static string? ValidateDocument(string fileName, long length, ReadOnlySpan<byte> header) =>
        Validate(fileName, length, header, DocumentExtensions);

    public static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
        ".odt" => "application/vnd.oasis.opendocument.text",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName).Trim();
        name = UnsafeFileNameCharacters().Replace(name, "_");
        return name.Length <= 255 ? name : name[^255..];
    }

    private static string? Validate(string fileName, long length, ReadOnlySpan<byte> header, HashSet<string> allowed)
    {
        if (length <= 0) return "El archivo está vacío.";
        if (length > MaximumFileSize) return "El archivo supera el límite de 25 MB.";

        var safeName = Path.GetFileName(fileName);
        var extension = Path.GetExtension(safeName);
        if (!allowed.Contains(extension)) return "El formato del archivo no está permitido.";

        var withoutFinalExtension = Path.GetFileNameWithoutExtension(safeName);
        if (DangerousInnerExtensions.Contains(Path.GetExtension(withoutFinalExtension)))
            return "El nombre contiene una doble extensión no permitida.";

        if (!SignatureMatches(extension, header))
            return "El contenido del archivo no coincide con su extensión.";

        return null;
    }

    private static bool SignatureMatches(string extension, ReadOnlySpan<byte> header)
    {
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return header.StartsWith("%PDF-"u8);
        if (extension is ".xlsx" or ".docx" or ".ods" or ".odt")
            return header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B;
        if (extension is ".xls" or ".doc")
            return header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
        if (extension is ".jpg" or ".jpeg")
            return header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        if (extension == ".png")
            return header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        if (extension == ".webp")
            return header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8);
        return extension is ".csv" or ".txt";
    }

    [GeneratedRegex(@"[^a-zA-Z0-9áéíóúÁÉÍÓÚñÑüÜ ._()\-]", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeFileNameCharacters();
}
