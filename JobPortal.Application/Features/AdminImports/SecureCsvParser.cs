using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.AdminImports;

public static class AdminImportLimits
{
    public const long MaximumFileSizeBytes = 5 * 1024 * 1024;
    public const int MaximumDataRows = 500;
}

internal sealed record ParsedCsvRow(
    int RowNumber,
    IReadOnlyDictionary<string, string> Fields);

internal static class SecureCsvParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<IReadOnlyCollection<ParsedCsvRow>> ParseAsync(
        CsvImportFile file,
        IReadOnlyCollection<string> requiredHeaders,
        IReadOnlyCollection<string> optionalHeaders,
        CancellationToken cancellationToken)
    {
        ValidateFile(file);
        var content = await ReadUtf8Async(file.Content, cancellationToken);
        if (content.Length > 0 && content[0] == '\uFEFF')
            content = content[1..];
        if (string.IsNullOrWhiteSpace(content))
            throw InvalidCsv("The CSV file is empty.");
        if (content.Contains('\0', StringComparison.Ordinal))
            throw InvalidCsv("The file must use UTF-8 encoding.");

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = _ => throw InvalidCsv("The CSV file is malformed."),
            DetectDelimiter = false,
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            Mode = CsvMode.RFC4180,
            TrimOptions = TrimOptions.Trim
        };

        try
        {
            using var textReader = new StringReader(content);
            using var csv = new CsvReader(textReader, configuration);
            if (!await csv.ReadAsync())
                throw InvalidCsv("The CSV file is empty.");
            csv.ReadHeader();
            var headers = csv.HeaderRecord;
            var allowedHeaders = requiredHeaders.Concat(optionalHeaders).ToArray();
            ValidateHeaders(headers, requiredHeaders, allowedHeaders);

            var rows = new List<ParsedCsvRow>();
            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rows.Count == AdminImportLimits.MaximumDataRows)
                    throw InvalidCsv(
                        $"The CSV file cannot contain more than {AdminImportLimits.MaximumDataRows} data rows.");

                var fields = new Dictionary<string, string>(
                    allowedHeaders.Length,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var header in allowedHeaders)
                    fields[header] = string.Empty;
                for (var index = 0; index < headers!.Length; index++)
                    fields[headers[index].Trim()] = csv.GetField(index)?.Trim() ?? string.Empty;
                rows.Add(new(csv.Parser.Row, fields));
            }

            if (rows.Count == 0)
                throw InvalidCsv("The CSV file must contain at least one data row.");
            return rows;
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (CsvHelperException exception)
        {
            var rowNumber = exception.Context?.Parser?.Row ?? 0;
            throw new BadRequestException(
                $"The CSV file is malformed near row {rowNumber}.",
                "invalid_csv");
        }
    }

    private static void ValidateFile(CsvImportFile file)
    {
        if (file.Content is null)
            throw InvalidCsv("A CSV file is required.");
        if (!string.Equals(
                Path.GetExtension(file.FileName),
                ".csv",
                StringComparison.OrdinalIgnoreCase))
            throw InvalidCsv("Only .csv files are accepted.");
        if (file.Length <= 0)
            throw InvalidCsv("The CSV file is empty.");
        if (file.Length > AdminImportLimits.MaximumFileSizeBytes)
            throw InvalidCsv("The CSV file exceeds the 5 MB limit.");
    }

    private static async Task<string> ReadUtf8Async(
        Stream source,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;
            if (buffer.Length + read > AdminImportLimits.MaximumFileSizeBytes)
                throw InvalidCsv("The CSV file exceeds the 5 MB limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        try
        {
            return StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        }
        catch (DecoderFallbackException)
        {
            throw InvalidCsv("The file must use UTF-8 encoding.");
        }
    }

    private static void ValidateHeaders(
        string[]? headers,
        IReadOnlyCollection<string> requiredHeaders,
        IReadOnlyCollection<string> allowedHeaders)
    {
        if (headers is null || headers.Length == 0)
            throw InvalidCsv("The CSV header row is required.");
        var normalized = headers.Select(header => header.Trim()).ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace))
            throw InvalidCsv("CSV headers cannot be empty.");
        var duplicates = normalized
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw InvalidCsv("The CSV contains duplicate headers.");

        var required = requiredHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = allowedHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = normalized.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (required.Except(actual).Any())
            throw InvalidCsv("The CSV is missing one or more required headers.");
        if (actual.Except(allowed).Any())
            throw InvalidCsv("The CSV contains one or more unknown headers.");
    }

    private static BadRequestException InvalidCsv(string message) =>
        new(message, "invalid_csv");
}
