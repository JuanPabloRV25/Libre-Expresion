using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Portal.Application.Commercial.Reports;
using Portal.Infrastructure.Commercial.Reports;

namespace Portal.IntegrationTests;

public sealed class ReportExcelCompatibilityTests
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Chart = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private sealed record Diagnostic(string Id, string Description, string Part, string Path)
    {
        public string Signature => string.Join("\u001f", Id, Description, Part, Path);
    }

    // SDK 3.5.1 reports these six Office chart extension diagnostics in the
    // supplied native workbook as well. Keep their exact identities and paths;
    // no new schema or markup-compatibility failure is accepted.
    private static readonly Diagnostic[] NativeChartDiagnostics =
    [
        new("Sch_UndeclaredAttribute", "The 'uri' attribute is not declared.",
            "/xl/charts/chart1.xml", "/c:chartSpace[1]/c:chart[1]/c:extLst[1]/c:ext[1]"),
        new("Sch_InvalidElementContentExpectingComplex",
            "The element has invalid child element 'http://schemas.microsoft.com/office/drawing/2017/03/chart:dataDisplayOptions16'. List of possible elements expected: <http://schemas.microsoft.com/office/drawing/2017/03/chart:dispNaAsBlank>.",
            "/xl/charts/chart1.xml", "/c:chartSpace[1]/c:chart[1]/c:extLst[1]/c:ext[1]"),
        new("Sch_UnexpectedElementContentExpectingComplex",
            "The element has unexpected child element 'http://schemas.microsoft.com/office/drawing/2012/chart:xForSave'.",
            "/xl/charts/chart1.xml", "/c:chartSpace[1]/c:chart[1]/c:plotArea[1]/c:pie3DChart[1]/c:ser[1]/c:dLbls[1]/c:dLbl[4]/c:extLst[1]/c:ext[1]"),
        new("Sch_UnexpectedElementContentExpectingComplex",
            "The element has unexpected child element 'http://schemas.microsoft.com/office/drawing/2012/chart:showDataLabelsRange'.",
            "/xl/charts/chart1.xml", "/c:chartSpace[1]/c:chart[1]/c:plotArea[1]/c:pie3DChart[1]/c:ser[1]/c:dLbls[1]/c:dLbl[3]/c:extLst[1]/c:ext[1]"),
        new("Sch_UnexpectedElementContentExpectingComplex",
            "The element has unexpected child element 'http://schemas.microsoft.com/office/drawing/2012/chart:showDataLabelsRange'.",
            "/xl/charts/chart1.xml", "/c:chartSpace[1]/c:chart[1]/c:plotArea[1]/c:pie3DChart[1]/c:ser[1]/c:dLbls[1]/c:dLbl[2]/c:extLst[1]/c:ext[1]"),
        new("Sch_UnexpectedElementContentExpectingComplex",
            "The element has unexpected child element 'http://schemas.microsoft.com/office/drawing/2012/chart:showDataLabelsRange'.",
            "/xl/charts/chart1.xml", "/c:chartSpace[1]/c:chart[1]/c:plotArea[1]/c:pie3DChart[1]/c:ser[1]/c:dLbls[1]/c:dLbl[1]/c:extLst[1]/c:ext[1]")
    ];

    [Fact]
    public void Embedded_template_preserves_native_chart_compatibility_without_example_sales_or_caches()
    {
        var bytes = EmbeddedTemplate();
        AssertNativeCompatibility(bytes);
        var parts = ReadParts(bytes);
        AssertPackageRelationships(parts);
        AssertRequiresPrefixes(parts);

        var strings = Xml(parts, "xl/sharedStrings.xml").Root!.Elements(Spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(t => t.Value))).ToArray();
        var usedStrings = new HashSet<int>();
        foreach (var (name, content) in parts.Where(part => part.Key.StartsWith("xl/worksheets/", StringComparison.Ordinal) && part.Key.EndsWith(".xml", StringComparison.Ordinal)))
        {
            var cells = Parse(content).Descendants(Spreadsheet + "c").ToArray();
            foreach (var cell in cells)
            {
                if ((string?)cell.Attribute("t") == "s")
                    usedStrings.Add(int.Parse(cell.Element(Spreadsheet + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture));
                if (cell.Element(Spreadsheet + "f") is not null)
                    Assert.Null(cell.Element(Spreadsheet + "v"));
            }
            if (name != "xl/worksheets/sheet1.xml") continue;
            foreach (var cell in cells.Where(c => int.Parse(new string(((string)c.Attribute("r")!).Where(char.IsDigit).ToArray()), System.Globalization.CultureInfo.InvariantCulture) >= 4))
            {
                Assert.Null(cell.Element(Spreadsheet + "f"));
                var value = cell.Element(Spreadsheet + "v")?.Value ?? "";
                if ((string?)cell.Attribute("t") == "s") value = strings[int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)];
                Assert.True(string.IsNullOrEmpty(value), $"Example value remains in {name}:{cell.Attribute("r")?.Value}.");
                Assert.All(cell.Descendants(Spreadsheet + "t"), text => Assert.Empty(text.Value));
            }
        }
        Assert.All(strings.Select((value, index) => (value, index)), item =>
            Assert.True(usedStrings.Contains(item.index) || string.IsNullOrWhiteSpace(item.value), $"Unreferenced sample string {item.index} remains."));
        var chartParts = parts.Where(part => part.Key.StartsWith("xl/charts/chart", StringComparison.Ordinal) && part.Key.EndsWith(".xml", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(chartParts);
        foreach (var (_, content) in chartParts)
        {
            var chart = Parse(content);
            Assert.Empty(chart.Descendants(Chart + "pt"));
            Assert.All(chart.Descendants().Where(element => element.Name.LocalName == "ptCount"),
                count => Assert.Equal("0", (string?)count.Attribute("val")));
        }
        Assert.DoesNotContain(parts.Keys, name => name.StartsWith("customXml/", StringComparison.Ordinal) ||
            name.StartsWith("xl/comments", StringComparison.Ordinal) || name.StartsWith("xl/printerSettings/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Each_export_variant_has_no_added_ooxml_diagnostics_and_preserves_chart_parts(
        bool draft, bool previousVersion, bool incompleteTotals)
    {
        SalesGroup[] rows =
        [
            new("compatibility-a", "sale-a", "500", "2026-10-07", "000450 / 000451", "", "Cliente de prueba", "CONTADO",
                100m, "", "Línea A", "Comercial A", ["Detalle uno", "Detalle dos"], ["detail-a", "detail-b"], [], false),
            new("compatibility-b", "sale-b", "501", "2026-10-08", "000452", "", "Otro cliente de prueba", "30",
                250m, "Dato manual", "Línea B", "Comercial B", ["Detalle independiente"], ["detail-c"], [], false)
        ];
        var bytes = ReportExcel.ExportSales(rows, incompleteTotals, draft, previousVersion);
        AssertNativeCompatibility(bytes);
        var exported = ReadParts(bytes);
        AssertPackageRelationships(exported);
        AssertRequiresPrefixes(exported);
        var templateCharts = ReadParts(EmbeddedTemplate()).Where(part => part.Key.StartsWith("xl/charts/", StringComparison.Ordinal))
            .ToDictionary(part => part.Key, part => part.Value);
        Assert.NotEmpty(templateCharts);
        Assert.Equal(templateCharts.Keys.Order(StringComparer.Ordinal), exported.Keys.Where(name => name.StartsWith("xl/charts/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        foreach (var (name, content) in templateCharts) Assert.Equal(content, exported[name]);
    }

    private static void AssertNativeCompatibility(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019) { MaxNumberOfErrors = 0 };
        var actual = validator.Validate(document).Select(error => new Diagnostic(error.Id, error.Description,
            error.Part?.Uri.ToString() ?? "", error.Path?.XPath ?? "").Signature).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(NativeChartDiagnostics.Select(error => error.Signature).Order(StringComparer.Ordinal).ToArray(), actual);
    }

    private static void AssertRequiresPrefixes(Dictionary<string, byte[]> parts)
    {
        foreach (var (name, content) in parts.Where(part => part.Key.EndsWith(".xml", StringComparison.Ordinal)))
            foreach (var choice in Parse(content).Descendants(Compatibility + "Choice"))
            {
                var required = ((string?)choice.Attribute("Requires") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                Assert.NotEmpty(required);
                foreach (var prefix in required)
                    Assert.True(choice.GetNamespaceOfPrefix(prefix) is { NamespaceName.Length: > 0 }, $"Undefined Requires prefix {prefix} in {name}.");
            }
    }

    private static void AssertPackageRelationships(Dictionary<string, byte[]> parts)
    {
        foreach (var (name, content) in parts.Where(part => part.Key.EndsWith(".rels", StringComparison.Ordinal)))
        {
            var owner = name == "_rels/.rels" ? "" : name.Replace("/_rels/", "/", StringComparison.Ordinal)[..^5];
            if (owner.Length > 0) Assert.Contains(owner, parts.Keys);
            var relationships = Parse(content).Root!.Elements().ToArray();
            Assert.Equal(relationships.Length, relationships.Select(relationship => (string?)relationship.Attribute("Id")).Distinct().Count());
            if (owner.EndsWith(".xml", StringComparison.Ordinal))
                foreach (var reference in Xml(parts, owner).Descendants().Attributes().Where(attribute => attribute.Name.Namespace == OfficeRelationships))
                    Assert.Contains(relationships, relationship => (string?)relationship.Attribute("Id") == reference.Value);
            foreach (var relationship in relationships)
            {
                Assert.NotEqual("External", (string?)relationship.Attribute("TargetMode"));
                var target = (string?)relationship.Attribute("Target");
                Assert.False(string.IsNullOrWhiteSpace(target));
                var resolved = new Uri(new Uri("https://ooxml.invalid/" + owner), target!).AbsolutePath.TrimStart('/');
                Assert.Contains(Uri.UnescapeDataString(resolved), parts.Keys);
            }
        }
        foreach (var declaration in Xml(parts, "[Content_Types].xml").Root!.Elements().Where(element => element.Name.LocalName == "Override"))
            Assert.Contains(((string)declaration.Attribute("PartName")!).TrimStart('/'), parts.Keys);
    }

    private static byte[] EmbeddedTemplate()
    {
        using var template = typeof(ReportExcel).Assembly.GetManifestResourceStream("Portal.Reports.VentasTemplate.xlsx")!;
        Assert.NotNull(template);
        using var bytes = new MemoryStream();
        template.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static Dictionary<string, byte[]> ReadParts(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var data = new MemoryStream();
            using var content = entry.Open();
            content.CopyTo(data);
            return data.ToArray();
        });
    }

    private static XDocument Xml(Dictionary<string, byte[]> parts, string name) => Parse(parts[name]);
    private static XDocument Parse(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        return XDocument.Load(stream);
    }
}
