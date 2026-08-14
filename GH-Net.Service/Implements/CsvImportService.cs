using GH_Net.Service.Abstractions;
using System.Text;

namespace GH_Net.Service.Implements;

public sealed class CsvImportService : ICsvImportService
{
    private const int PreviewLimit = 5;
    private static readonly char[] DelimiterCandidates = [',', ';', '\t', '|'];

    public async Task<CsvInspection> InspectAsync(string filePath, CancellationToken ct = default)
    {
        ParsedCsv parsed = await ParseFileAsync(filePath, ct).ConfigureAwait(false);
        return new CsvInspection(
            Path.GetFullPath(filePath),
            parsed.Delimiter,
            parsed.Headers,
            parsed.Rows.Count,
            parsed.Rows.Take(PreviewLimit)
                .Select(row => new CsvPreviewRow(row.SourceLineNumber, row.Values))
                .ToArray());
    }

    public async Task<IReadOnlyList<ImportedCode>> ReadCodeColumnAsync(
        string filePath,
        string columnName,
        CancellationToken ct = default)
    {
        ParsedCsv parsed = await ParseFileAsync(filePath, ct).ConfigureAwait(false);
        int columnIndex = -1;
        for (int index = 0; index < parsed.Headers.Count; index++)
        {
            if (string.Equals(parsed.Headers[index], columnName, StringComparison.Ordinal))
            {
                columnIndex = index;
                break;
            }
        }
        if (columnIndex < 0)
            throw new InvalidOperationException($"CSV column '{columnName}' was not found in {Path.GetFileName(filePath)}.");

        var result = new List<ImportedCode>(parsed.Rows.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < parsed.Rows.Count; index++)
        {
            ParsedRow row = parsed.Rows[index];
            string code = row.Values[columnIndex];
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException($"Selected code is empty at CSV line {row.SourceLineNumber} in {Path.GetFileName(filePath)}.");
            if (seen.TryGetValue(code, out int firstLine))
                throw new InvalidOperationException($"Duplicate code '{code}' at CSV line {row.SourceLineNumber}; first seen at line {firstLine} in {Path.GetFileName(filePath)}.");
            seen.Add(code, row.SourceLineNumber);
            result.Add(new ImportedCode(index, row.SourceLineNumber, code));
        }

        if (result.Count == 0)
            throw new InvalidOperationException($"CSV file {Path.GetFileName(filePath)} contains no data rows.");
        return result;
    }

    private static async Task<ParsedCsv> ParseFileAsync(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("CSV file path is required.", nameof(filePath));
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("CSV file was not found.", fullPath);

        string content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct).ConfigureAwait(false);
        if (content.Length == 0)
            throw new InvalidOperationException($"CSV file {Path.GetFileName(fullPath)} is empty.");
        if (content[0] == '\uFEFF') content = content[1..];

        char delimiter = DetectDelimiter(content);
        List<ParsedRow> records = ParseRecords(content, delimiter, ct);
        if (records.Count == 0)
            throw new InvalidOperationException($"CSV file {Path.GetFileName(fullPath)} has no header.");

        string[] headers = records[0].Values.Select(value => value.Trim()).ToArray();
        if (headers.Length == 0 || headers.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"CSV file {Path.GetFileName(fullPath)} contains an empty header.");
        string? duplicateHeader = headers.GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateHeader is not null)
            throw new InvalidOperationException($"CSV file {Path.GetFileName(fullPath)} contains duplicate header '{duplicateHeader}'.");

        var rows = new List<ParsedRow>();
        foreach (ParsedRow row in records.Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            if (row.Values.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Values.Count != headers.Length)
                throw new InvalidOperationException($"CSV line {row.SourceLineNumber} has {row.Values.Count} fields; expected {headers.Length} in {Path.GetFileName(fullPath)}.");
            rows.Add(row);
        }
        return new ParsedCsv(delimiter, headers, rows);
    }

    private static char DetectDelimiter(string content)
    {
        int end = FindFirstRecordEnd(content);
        string header = content[..end];
        char best = ',';
        int bestCount = -1;
        foreach (char candidate in DelimiterCandidates)
        {
            int count = CountOutsideQuotes(header, candidate);
            if (count > bestCount) { best = candidate; bestCount = count; }
        }
        return best;
    }

    private static int FindFirstRecordEnd(string content)
    {
        bool quoted = false;
        for (int index = 0; index < content.Length; index++)
        {
            char value = content[index];
            if (value == '"')
            {
                if (quoted && index + 1 < content.Length && content[index + 1] == '"') { index++; continue; }
                quoted = !quoted;
            }
            else if (!quoted && value is '\r' or '\n') return index;
        }
        return content.Length;
    }

    private static int CountOutsideQuotes(string value, char delimiter)
    {
        bool quoted = false;
        int count = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                if (quoted && index + 1 < value.Length && value[index + 1] == '"') { index++; continue; }
                quoted = !quoted;
            }
            else if (!quoted && value[index] == delimiter) count++;
        }
        return count;
    }

    private static List<ParsedRow> ParseRecords(string content, char delimiter, CancellationToken ct)
    {
        var records = new List<ParsedRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        int line = 1;
        int recordStartLine = 1;

        for (int index = 0; index < content.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            char value = content[index];
            if (quoted)
            {
                if (value == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"') { field.Append('"'); index++; }
                    else quoted = false;
                }
                else
                {
                    field.Append(value);
                    if (value == '\n') line++;
                }
                continue;
            }

            if (value == '"' && field.Length == 0) { quoted = true; continue; }
            if (value == delimiter) { fields.Add(field.ToString()); field.Clear(); continue; }
            if (value is '\r' or '\n')
            {
                fields.Add(field.ToString()); field.Clear();
                records.Add(new ParsedRow(recordStartLine, fields.ToArray())); fields.Clear();
                if (value == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                line++; recordStartLine = line;
                continue;
            }
            field.Append(value);
        }

        if (quoted) throw new InvalidOperationException($"CSV contains an unterminated quoted field starting at line {recordStartLine}.");
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new ParsedRow(recordStartLine, fields.ToArray()));
        }
        return records;
    }

    private sealed record ParsedRow(int SourceLineNumber, IReadOnlyList<string> Values);
    private sealed record ParsedCsv(char Delimiter, IReadOnlyList<string> Headers, IReadOnlyList<ParsedRow> Rows);
}
