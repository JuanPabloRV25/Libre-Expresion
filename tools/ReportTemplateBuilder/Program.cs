using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.IO.Compression;
using System.Xml.Linq;
using Portal.Infrastructure.Commercial.Reports;
if (args.Length == 2 && args[0] == "--test-template")
{
    using var sample = new XSSFWorkbook(); var sales = sample.CreateSheet("VENTAS ");
    sales.CreateRow(1).CreateCell(7).SetCellFormula("SUBTOTAL(9,H4:H1048576)");
    var headers = sales.CreateRow(2);
    for (var i = 0; i < ReportExcel.OutputHeaders.Length; i++) headers.CreateCell(i + 1).SetCellValue(ReportExcel.OutputHeaders[i]);
    var fulfillment = sample.CreateSheet("CUMPLIMIENTO");
    fulfillment.CreateRow(1).CreateCell(1).SetCellValue("Datos ficticios para pruebas");
    var row = fulfillment.CreateRow(2); row.CreateCell(1).SetCellValue("Vendedor de prueba");
    row.CreateCell(2).SetCellFormula("SUMIFS('VENTAS '!H4:H861,'VENTAS '!K4:K861,B3,'VENTAS '!J4:J861,\"EMPAQUE\")");
    row.CreateCell(3).SetCellValue(1000000); row.CreateCell(4).SetCellFormula("C3/D3");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    using var output = File.Create(args[1]); sample.Write(output, true); return;
}
if (args.Length == 3 && args[0] == "--check-sources")
{
    var manager = File.ReadAllBytes(args[1]); var ops = File.ReadAllBytes(args[2]);
    var sales = ReportExcel.ReadSales(manager, Guid.NewGuid(), "Manager.xlsx", "local-check");
    var records = ReportExcel.ReadOps(ops);
    if (sales.Details.Count != 312 || records.Length != 4322 || sales.Details.Count(d => d.ManagerOp.Length == 0) != 152)
        throw new InvalidOperationException($"Unexpected source counts: sales={sales.Details.Count}, ops={records.Length}.");
    Console.WriteLine("PASS: 312 ventas, 152 OP vacías y 4322 registros de OP leídos localmente; ninguna fórmula de origen se ejecutó."); return;
}
if (args.Length != 2) throw new ArgumentException("Provide the source template and sanitized output paths.");
// Keep Excel's chart XML and its namespace bindings. NPOI's chart writer can
// reorder schema children even though its own reader accepts the result.
Dictionary<string, byte[]> nativeCharts;
using (var sourceArchive = ZipFile.OpenRead(args[0]))
    nativeCharts = sourceArchive.Entries.Where(e => e.FullName.StartsWith("xl/charts/", StringComparison.Ordinal))
        .ToDictionary(e => e.FullName, e => { using var data = new MemoryStream(); using var content = e.Open(); content.CopyTo(data); return data.ToArray(); });
using var input = File.OpenRead(args[0]); using var book = new XSSFWorkbook(input);
var sheet = book.GetSheet("VENTAS ") ?? throw new InvalidOperationException("VENTAS sheet missing.");
var styles = Enumerable.Range(1, 10).Select(i => sheet.GetRow(3)?.GetCell(i)?.CellStyle).ToArray();
for (var i = sheet.LastRowNum; i >= 3; i--) if (sheet.GetRow(i) is { } row) sheet.RemoveRow(row);
var empty = sheet.CreateRow(3); for (var i = 1; i <= 10; i++) { var cell = empty.CreateCell(i); cell.SetCellValue(""); if (styles[i - 1] is not null) cell.CellStyle = styles[i - 1]; }
sheet.GetRow(1).GetCell(7).SetCellFormula("SUBTOTAL(9,H4:H1048576)");
var outputFulfillment = book.GetSheet("CUMPLIMIENTO") ?? throw new InvalidOperationException("CUMPLIMIENTO sheet missing.");
var labelRows = new HashSet<int> { 21, 23, 26, 27, 28, 29, 31, 32, 33, 34 };
foreach (IRow row in outputFulfillment)
    foreach (var cell in row.Cells)
    {
        var excelRow = row.RowNum + 1;
        if (cell.CellType == CellType.Formula)
        {
            // Empty targets must not cause division errors in the reusable template.
            if (cell.CellFormula.Contains('/') && !cell.CellFormula.StartsWith("IFERROR(", StringComparison.Ordinal))
                cell.SetCellFormula("IFERROR(" + cell.CellFormula + ",0)");
        }
        else if (excelRow is not (2 or 11 or 17) && !(cell.ColumnIndex == 1 && labelRows.Contains(excelRow)))
            cell.SetCellValue("");
    }
book.SetForceFormulaRecalculation(true);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
using (var output = File.Create(args[1])) book.Write(output, true);
// Removing rows does not necessarily purge orphaned shared strings. Remove the
// original sales text from the asset as well, while keeping referenced labels.
using var archive = ZipFile.Open(args[1], ZipArchiveMode.Update);
foreach (var (name, content) in nativeCharts)
{
    archive.GetEntry(name)?.Delete();
    using var restored = archive.CreateEntry(name).Open(); restored.Write(content);
}
var used = new HashSet<int>();
XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml")).ToArray())
{
    XDocument document; using (var xml = entry.Open()) document = XDocument.Load(xml);
    foreach (var cell in document.Descendants(ns + "c").Where(c => (string?)c.Attribute("t") == "s"))
        if (int.TryParse(cell.Element(ns + "v")?.Value, out var index)) used.Add(index);
    foreach (var cell in document.Descendants(ns + "c").Where(c => c.Element(ns + "f") is not null))
    { cell.Element(ns + "v")?.Remove(); cell.Attribute("t")?.Remove(); }
    var entryName = entry.FullName; entry.Delete(); using var output = archive.CreateEntry(entryName).Open(); document.Save(output);
}
if (archive.GetEntry("xl/sharedStrings.xml") is { } strings)
{
    XDocument document; using (var xml = strings.Open()) document = XDocument.Load(xml);
    var items = document.Root!.Elements(ns + "si").ToArray();
    for (var index = 0; index < items.Length; index++) if (!used.Contains(index)) { items[index].RemoveAll(); items[index].Add(new XElement(ns + "t", "")); }
    strings.Delete(); using var output = archive.CreateEntry("xl/sharedStrings.xml").Open(); document.Save(output);
}
if (archive.GetEntry("docProps/core.xml") is { } properties)
{
    XDocument document; using (var xml = properties.Open()) document = XDocument.Load(xml);
    document.Root!.RemoveNodes();
    properties.Delete(); using var output = archive.CreateEntry("docProps/core.xml").Open(); document.Save(output);
}
var removedParts = archive.Entries.Select(e => e.FullName).Where(name => name.StartsWith("customXml/") ||
    name.StartsWith("xl/comments") || name.StartsWith("xl/printerSettings/") ||
    name is "docProps/custom.xml" or "docProps/thumbnail.emf" or "xl/calcChain.xml" or "xl/drawings/vmlDrawing1.vml").ToHashSet();
foreach (var name in removedParts) archive.GetEntry(name)!.Delete();
foreach (var name in archive.Entries.Select(e => e.FullName).ToArray())
{
    if (!name.EndsWith(".xml") && !name.EndsWith(".rels")) continue;
    var entry = archive.GetEntry(name)!;
    XDocument document; using (var xml = entry.Open()) document = XDocument.Load(xml);
    if (name.EndsWith(".rels"))
    {
        var owner = name == "_rels/.rels" ? "" : name.Replace("/_rels/", "/")[..^5];
        var dropped = new HashSet<string>();
        foreach (var relationship in document.Root!.Elements().ToArray())
        {
            var target = new Uri(new Uri("https://template.invalid/" + owner), (string?)relationship.Attribute("Target") ?? "").AbsolutePath.TrimStart('/');
            if (!removedParts.Contains(target) && (string?)relationship.Attribute("TargetMode") != "External") continue;
            dropped.Add((string)relationship.Attribute("Id")!); relationship.Remove();
        }
        if (dropped.Count > 0 && archive.GetEntry(owner) is { } ownerEntry)
        {
            XDocument ownerDocument; using (var xml = ownerEntry.Open()) ownerDocument = XDocument.Load(xml);
            XNamespace relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            foreach (var node in ownerDocument.Descendants().Where(e => dropped.Contains((string?)e.Attribute(relations + "id") ?? "")).ToArray())
                if (node.Name.LocalName == "pageSetup") node.Attribute(relations + "id")!.Remove(); else node.Remove();
            Rewrite(archive, owner, ownerDocument);
        }
    }
    else if (name.StartsWith("xl/charts/chart") && name.EndsWith(".xml"))
    {
        foreach (var cache in document.Descendants().Where(e => e.Name.LocalName is "numCache" or "strCache" or "multiLvlStrCache" or "numLit" or "strLit"))
            foreach (var node in cache.Elements().ToArray())
                if (node.Name.LocalName is "pt" or "lvl") node.Remove();
                else if (node.Name.LocalName == "ptCount") node.SetAttributeValue("val", "0");
        XNamespace chartNamespace = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        foreach (var point in document.Descendants(chartNamespace + "pt").ToArray()) point.Remove();
        foreach (var count in document.Descendants().Where(e => e.Name.LocalName == "ptCount")) count.SetAttributeValue("val", "0");
    }
    else if (name == "[Content_Types].xml")
        foreach (var node in document.Root!.Elements().Where(e => removedParts.Contains(((string?)e.Attribute("PartName") ?? "").TrimStart('/'))).ToArray()) node.Remove();
    else if (name == "docProps/app.xml")
        foreach (var node in document.Descendants().Where(e => e.Name.LocalName is "Company" or "Manager" or "HyperlinkBase")) node.Value = "";
    else continue;
    Rewrite(archive, name, document);
}
static void Rewrite(ZipArchive archive, string name, XDocument document)
{
    archive.GetEntry(name)!.Delete(); using var output = archive.CreateEntry(name).Open(); document.Save(output);
}
