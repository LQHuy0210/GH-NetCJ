namespace GH_Net.Service.Abstractions;

public sealed record CsvPreviewRow(int SourceLineNumber, IReadOnlyList<string> Values);

public sealed record CsvInspection(
    string FilePath,
    char Delimiter,
    IReadOnlyList<string> Headers,
    int DataRowCount,
    IReadOnlyList<CsvPreviewRow> PreviewRows);

public sealed record ImportedCode(int SourceRowIndex, int SourceLineNumber, string Code);

public interface ICsvImportService
{
    Task<CsvInspection> InspectAsync(string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<ImportedCode>> ReadCodeColumnAsync(
        string filePath,
        string columnName,
        CancellationToken ct = default);
}
