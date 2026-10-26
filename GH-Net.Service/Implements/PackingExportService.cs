using GH_Net.Core.Enums;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace GH_Net.Service.Implements;

public sealed class PackingExportService(IUnitOfWorkFactory factory) : IPackingExportService
{
    public async Task<PackingExportResult> ExportAsync(int jobId, PackingExportRequest request, Stream destination, CancellationToken ct = default)
    {
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Format)) throw new ArgumentOutOfRangeException(nameof(request));
        if (!Enum.IsDefined(request.Scope)) throw new ArgumentOutOfRangeException(nameof(request));
        if ((request.CodeSelection & ~PackingCodeSelection.All) != 0) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Scope == PackingExportScope.UnusedCodes && request.CodeSelection == PackingCodeSelection.None)
            throw new InvalidOperationException("Select at least one unused code type to export.");
        ArgumentNullException.ThrowIfNull(destination);
        Snapshot s = await LoadAsync(jobId, ct);
        IReadOnlyList<SheetData> sheets = Sheets(s, request);
        if (request.Format == PackingExportFormat.Excel) WriteXlsx(destination, sheets, ct); else WriteCsvZip(destination, sheets, ct);
        await destination.FlushAsync(ct);
        return request.Scope == PackingExportScope.FullJob
            ? new(s.Products.Count, s.Cartons.Count, s.Pallets.Count, s.Scans.Count, s.PrintAttempts.Count)
            : new(
                request.CodeSelection.HasFlag(PackingCodeSelection.Products) ? s.Products.Count(x => x.Status == ProductStatus.Available) : 0,
                request.CodeSelection.HasFlag(PackingCodeSelection.Cartons) ? s.Cartons.Count(x => x.Status == CartonStatus.Available) : 0,
                request.CodeSelection.HasFlag(PackingCodeSelection.Pallets) ? s.Pallets.Count(x => x.Status == PalletStatus.Available) : 0,
                0,
                0);
    }

    private async Task<Snapshot> LoadAsync(int jobId, CancellationToken ct)
    {
        using var uow = factory.Create(); await using var tx = await uow.BeginReadTransactionAsync(ct);
        var job = await uow.Repository<Job>().Query().AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, ct) ?? throw new InvalidOperationException($"Job {jobId} was not found.");
        var products = await uow.Repository<Product>().Query().AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.SourceRowIndex).ToListAsync(ct);
        var cartons = await uow.Repository<Carton>().Query().AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.SourceRowIndex).ToListAsync(ct);
        var pallets = await uow.Repository<Pallet>().Query().AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.SourceRowIndex).ToListAsync(ct);
        var scans = await uow.Repository<ScanEvent>().Query().AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.Id).ToListAsync(ct);
        var prints = await uow.Repository<PrintAttempt>().Query().AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.Id).ToListAsync(ct);
        var lineEvents = await uow.Repository<LineEvent>().Query().AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.Id).ToListAsync(ct);
        await tx.CommitAsync(ct); return new(job, products, cartons, pallets, scans, prints, lineEvents);
    }

    private static IReadOnlyList<SheetData> Sheets(Snapshot s, PackingExportRequest request)
    {
        if (request.Scope == PackingExportScope.UnusedCodes) return UnusedCodeSheets(s, request.CodeSelection);
        var cartonCodes = s.Cartons.ToDictionary(x => x.Id, x => x.Code); var palletCodes = s.Pallets.ToDictionary(x => x.Id, x => x.Code);
        List<SheetData> sheets =
        [
            new("Job", ["Field","Value"],
            [
                ["Mode",s.Job.Mode.ToString()], ["UsePlc",s.Job.UsePlc], ["LastCameraSequence",s.Job.LastCameraSequence], ["JobId",s.Job.Id], ["Name",s.Job.Name], ["Status",s.Job.Status.ToString()], ["Workflow",s.Job.PackingWorkflowState.ToString()], ["CreatedAt",s.Job.CreatedAt], ["StartedAt",s.Job.StartedAt], ["StoppedAt",s.Job.StoppedAt], ["CompletedAt",s.Job.CompletedAt],
                ["ProductsPerCarton",s.Job.ProductsPerCarton], ["CartonsPerPallet",s.Job.CartonsPerPallet], ["ProductCsv",s.Job.ProductCsvFileName], ["ProductColumn",s.Job.ProductCodeColumn], ["CartonCsv",s.Job.CartonCsvFileName], ["CartonColumn",s.Job.CartonCodeColumn], ["PalletCsv",s.Job.PalletCsvFileName], ["PalletColumn",s.Job.PalletCodeColumn]
            ]),
            new("Products", ["Id","SourceRow","Code","Status","CartonCode","ScannedAt","PackedAt"], s.Products.Select(x => new object?[]{x.Id,x.SourceRowIndex,x.Code,x.Status.ToString(),x.CartonId is int id && cartonCodes.TryGetValue(id,out var c)?c:null,x.ScannedAt,x.PackedAt}).ToList()),
            new("Cartons", ["Id","SourceRow","Sequence","Code","Status","ProductCount","PalletCode","FillingStartedAt","PrintedAt","CompletedAt","PrintCount"], s.Cartons.Select(x => new object?[]{x.Id,x.SourceRowIndex,x.Sequence,x.Code,x.Status.ToString(),x.ProductCount,x.PalletId is int id && palletCodes.TryGetValue(id,out var p)?p:null,x.FillingStartedAt,x.PrintedAt,x.CompletedAt,x.PrintCount}).ToList()),
            new("Pallets", ["Id","SourceRow","Sequence","Code","Status","CartonCount","FillingStartedAt","PrintedAt","CompletedAt","PrintCount"], s.Pallets.Select(x => new object?[]{x.Id,x.SourceRowIndex,x.Sequence,x.Code,x.Status.ToString(),x.CartonCount,x.FillingStartedAt,x.PrintedAt,x.CompletedAt,x.PrintCount}).ToList()),
            new("ScanEvents", ["Id","ScannedCode","ExpectedType","Result","Detail","CreatedAt","Source","CameraSequence"], s.Scans.Select(x => new object?[]{x.Id,x.ScannedCode,x.ExpectedCodeType.ToString(),x.ResultCode.ToString(),x.Detail,x.CreatedAt,x.Source,x.CameraSequence}).ToList()),
            new("PrintAttempts", ["Id","TargetType","TargetCode","AttemptNumber","Succeeded","Error","CreatedAt","ZplSnapshot"], s.PrintAttempts.Select(x => new object?[]{x.Id,x.CartonId.HasValue?"Carton":"Pallet",x.CartonId is int cid && cartonCodes.TryGetValue(cid,out var c)?c:x.PalletId is int pid && palletCodes.TryGetValue(pid,out var p)?p:null,x.AttemptNumber,x.Succeeded,x.Error,x.CreatedAt,x.ZplSnapshot}).ToList())
        ];
        if (s.Job.UsePlc || s.LineEvents.Count > 0)
            sheets.Add(new("LineEvents", ["Id","Kind","Payload","Outcome","Error","CameraSequence","CreatedAt","SentAt"],
                s.LineEvents.Select(x => new object?[]{x.Id,x.Kind,x.Payload,x.Outcome,x.Error,x.CameraSequence,x.CreatedAt,x.SentAt}).ToList()));
        return sheets;
    }

    private static IReadOnlyList<SheetData> UnusedCodeSheets(Snapshot s, PackingCodeSelection selection)
    {
        List<SheetData> sheets = [];
        if (selection.HasFlag(PackingCodeSelection.Products))
            sheets.Add(new("UnusedProducts", ["SourceRow", "Code", "Status"], s.Products
                .Where(x => x.Status == ProductStatus.Available)
                .Select(x => new object?[] { x.SourceRowIndex, x.Code, x.Status.ToString() }).ToList()));
        if (selection.HasFlag(PackingCodeSelection.Cartons))
            sheets.Add(new("UnusedCartons", ["SourceRow", "Code", "Status"], s.Cartons
                .Where(x => x.Status == CartonStatus.Available)
                .Select(x => new object?[] { x.SourceRowIndex, x.Code, x.Status.ToString() }).ToList()));
        if (selection.HasFlag(PackingCodeSelection.Pallets))
            sheets.Add(new("UnusedPallets", ["SourceRow", "Code", "Status"], s.Pallets
                .Where(x => x.Status == PalletStatus.Available)
                .Select(x => new object?[] { x.SourceRowIndex, x.Code, x.Status.ToString() }).ToList()));
        return sheets;
    }

    private static void WriteCsvZip(Stream destination, IReadOnlyList<SheetData> sheets, CancellationToken ct)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var sheet in sheets)
        {
            ct.ThrowIfCancellationRequested(); var entry = zip.CreateEntry(sheet.Name + ".csv", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(true)); writer.WriteLine(string.Join(',', sheet.Headers.Select(Csv)));
            foreach (var row in sheet.Rows) { ct.ThrowIfCancellationRequested(); writer.WriteLine(string.Join(',', row.Select(x => Csv(Text(x))))); }
        }
    }

    private static void WriteXlsx(Stream destination, IReadOnlyList<SheetData> sheets, CancellationToken ct)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(zip,"[Content_Types].xml",w=>{w.WriteStartElement("Types","http://schemas.openxmlformats.org/package/2006/content-types");w.WriteStartElement("Default");w.WriteAttributeString("Extension","rels");w.WriteAttributeString("ContentType","application/vnd.openxmlformats-package.relationships+xml");w.WriteEndElement();w.WriteStartElement("Default");w.WriteAttributeString("Extension","xml");w.WriteAttributeString("ContentType","application/xml");w.WriteEndElement();w.WriteStartElement("Override");w.WriteAttributeString("PartName","/xl/workbook.xml");w.WriteAttributeString("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");w.WriteEndElement();w.WriteStartElement("Override");w.WriteAttributeString("PartName","/xl/styles.xml");w.WriteAttributeString("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");w.WriteEndElement();for(int i=1;i<=sheets.Count;i++){w.WriteStartElement("Override");w.WriteAttributeString("PartName",$"/xl/worksheets/sheet{i}.xml");w.WriteAttributeString("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");w.WriteEndElement();}w.WriteEndElement();});
        WriteEntry(zip,"_rels/.rels",w=>{w.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");Rel(w,"rId1","http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument","xl/workbook.xml");w.WriteEndElement();});
        WriteEntry(zip,"xl/workbook.xml",w=>{w.WriteStartElement("workbook","http://schemas.openxmlformats.org/spreadsheetml/2006/main");w.WriteAttributeString("xmlns","r",null,"http://schemas.openxmlformats.org/officeDocument/2006/relationships");w.WriteStartElement("sheets");for(int i=0;i<sheets.Count;i++){w.WriteStartElement("sheet");w.WriteAttributeString("name",sheets[i].Name);w.WriteAttributeString("sheetId",(i+1).ToString());w.WriteAttributeString("r","id","http://schemas.openxmlformats.org/officeDocument/2006/relationships",$"rId{i+1}");w.WriteEndElement();}w.WriteEndElement();w.WriteEndElement();});
        WriteEntry(zip,"xl/_rels/workbook.xml.rels",w=>{w.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");for(int i=1;i<=sheets.Count;i++)Rel(w,$"rId{i}","http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",$"worksheets/sheet{i}.xml");Rel(w,$"rId{sheets.Count+1}","http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles","styles.xml");w.WriteEndElement();});
        WriteEntry(zip,"xl/styles.xml",WriteStyles);
        for(int i=0;i<sheets.Count;i++){ct.ThrowIfCancellationRequested();var data=sheets[i];WriteEntry(zip,$"xl/worksheets/sheet{i+1}.xml",w=>WriteSheet(w,data,ct));}
    }
    private static void WriteSheet(XmlWriter w, SheetData data, CancellationToken ct){w.WriteStartElement("worksheet","http://schemas.openxmlformats.org/spreadsheetml/2006/main");w.WriteStartElement("sheetViews");w.WriteStartElement("sheetView");w.WriteAttributeString("workbookViewId","0");w.WriteStartElement("pane");w.WriteAttributeString("ySplit","1");w.WriteAttributeString("topLeftCell","A2");w.WriteAttributeString("state","frozen");w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();w.WriteStartElement("cols");for(int i=0;i<data.Headers.Count;i++){int longest=Math.Max(data.Headers[i].Length,data.Rows.Take(200).Select(r=>r.Length>i?(r[i] is DateTime?19:Text(r[i]).Length):0).DefaultIfEmpty().Max());w.WriteStartElement("col");w.WriteAttributeString("min",(i+1).ToString());w.WriteAttributeString("max",(i+1).ToString());w.WriteAttributeString("width",Math.Min(45,Math.Max(12,longest+2)).ToString(CultureInfo.InvariantCulture));w.WriteAttributeString("customWidth","1");w.WriteEndElement();}w.WriteEndElement();w.WriteStartElement("sheetData");Row(w,1,data.Headers.Cast<object?>().ToArray(),true);int r=2;foreach(var row in data.Rows){ct.ThrowIfCancellationRequested();Row(w,r++,row,false);}w.WriteEndElement();w.WriteStartElement("autoFilter");w.WriteAttributeString("ref",$"A1:{ColumnName(data.Headers.Count)}{Math.Max(1,r-1)}");w.WriteEndElement();w.WriteEndElement();}
    private static void Row(XmlWriter w,int row,object?[] values,bool header){w.WriteStartElement("row");w.WriteAttributeString("r",row.ToString());for(int i=0;i<values.Length;i++)Cell(w,$"{ColumnName(i+1)}{row}",values[i],header);w.WriteEndElement();}
    private static void Cell(XmlWriter w,string reference,object? value,bool header){w.WriteStartElement("c");w.WriteAttributeString("r",reference);if(header)w.WriteAttributeString("s","1");if(value is DateTime dt){w.WriteAttributeString("s","2");w.WriteStartElement("v");w.WriteString(dt.ToOADate().ToString(CultureInfo.InvariantCulture));w.WriteEndElement();}else if(value is int or long){w.WriteStartElement("v");w.WriteString(Convert.ToString(value,CultureInfo.InvariantCulture));w.WriteEndElement();}else if(value is bool b){w.WriteAttributeString("t","b");w.WriteStartElement("v");w.WriteString(b?"1":"0");w.WriteEndElement();}else{w.WriteAttributeString("t","inlineStr");w.WriteStartElement("is");w.WriteElementString("t",Text(value));w.WriteEndElement();}w.WriteEndElement();}
    private static void WriteStyles(XmlWriter w){w.WriteStartElement("styleSheet","http://schemas.openxmlformats.org/spreadsheetml/2006/main");w.WriteStartElement("numFmts");w.WriteAttributeString("count","1");w.WriteStartElement("numFmt");w.WriteAttributeString("numFmtId","164");w.WriteAttributeString("formatCode","yyyy-mm-dd hh:mm:ss");w.WriteEndElement();w.WriteEndElement();w.WriteStartElement("fonts");w.WriteAttributeString("count","2");for(int i=0;i<2;i++){w.WriteStartElement("font");if(i==1)w.WriteStartElement("b");if(i==1)w.WriteEndElement();w.WriteStartElement("sz");w.WriteAttributeString("val","11");w.WriteEndElement();w.WriteStartElement("name");w.WriteAttributeString("val","Calibri");w.WriteEndElement();if(i==1){w.WriteStartElement("color");w.WriteAttributeString("rgb","FFFFFFFF");w.WriteEndElement();}w.WriteEndElement();}w.WriteEndElement();w.WriteStartElement("fills");w.WriteAttributeString("count","2");for(int i=0;i<2;i++){w.WriteStartElement("fill");w.WriteStartElement("patternFill");w.WriteAttributeString("patternType",i==0?"none":"solid");if(i==1){w.WriteStartElement("fgColor");w.WriteAttributeString("rgb","FF0F766E");w.WriteEndElement();w.WriteStartElement("bgColor");w.WriteAttributeString("indexed","64");w.WriteEndElement();}w.WriteEndElement();w.WriteEndElement();}w.WriteEndElement();w.WriteStartElement("borders");w.WriteAttributeString("count","1");w.WriteStartElement("border");w.WriteEndElement();w.WriteEndElement();w.WriteStartElement("cellStyleXfs");w.WriteAttributeString("count","1");Xf(w,0,0,0,0);w.WriteEndElement();w.WriteStartElement("cellXfs");w.WriteAttributeString("count","3");Xf(w,0,0,0,0);Xf(w,1,1,0,0);Xf(w,0,0,164,1);w.WriteEndElement();w.WriteEndElement();}
    private static void Xf(XmlWriter w,int font,int fill,int numFmt,int apply){w.WriteStartElement("xf");w.WriteAttributeString("numFmtId",numFmt.ToString());w.WriteAttributeString("fontId",font.ToString());w.WriteAttributeString("fillId",fill.ToString());w.WriteAttributeString("borderId","0");w.WriteAttributeString("xfId","0");if(apply==1)w.WriteAttributeString("applyNumberFormat","1");w.WriteEndElement();}
    private static void Rel(XmlWriter w,string id,string type,string target){w.WriteStartElement("Relationship");w.WriteAttributeString("Id",id);w.WriteAttributeString("Type",type);w.WriteAttributeString("Target",target);w.WriteEndElement();}
    private static void WriteEntry(ZipArchive zip,string name,Action<XmlWriter> write){var e=zip.CreateEntry(name,CompressionLevel.Optimal);using var s=e.Open();using var w=XmlWriter.Create(s,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false});w.WriteStartDocument();write(w);w.WriteEndDocument();}
    private static string ColumnName(int n){var s="";while(n>0){n--;s=(char)('A'+n%26)+s;n/=26;}return s;}
    private static string Text(object? x)=>x switch{null=>"",DateTime d=>d.ToString("O"),_=>Convert.ToString(x,CultureInfo.InvariantCulture)??""};
    private static string Csv(string value)=>value.IndexOfAny([',','"','\r','\n'])<0?value:$"\"{value.Replace("\"","\"\"")}\"";
    private sealed record Snapshot(Job Job,IReadOnlyList<Product> Products,IReadOnlyList<Carton> Cartons,IReadOnlyList<Pallet> Pallets,IReadOnlyList<ScanEvent> Scans,IReadOnlyList<PrintAttempt> PrintAttempts,IReadOnlyList<LineEvent> LineEvents);
    private sealed record SheetData(string Name,IReadOnlyList<string> Headers,IReadOnlyList<object?[]> Rows);
}
