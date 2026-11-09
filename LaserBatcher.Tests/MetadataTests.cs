using System.Text.Json.Nodes;
using System.Xml.Linq;
using LaserBatcher.Services;
using Xunit;

namespace LaserBatcher.Tests;

public class MetadataTests
{
    private static readonly XNamespace S = SvgBatchService.Svg;
    private static XDocument Batch(int version = 1) => SvgMetadataService.Parse($$"""
        <svg xmlns="{{S}}" xmlns:foreign="urn:keep" width="200mm" height="100mm" viewBox="10 20 200 100">
          <metadata><lb:batch xmlns:lb="urn:laserbatcher:metadata:{{version}}" schemaVersion="{{version}}" custom="keep">{"schemaVersion":{{version}},"program":{"partName":"Widget","orderNumber":null,"batch":null,"yield":8,"custom":"keep"},"sourceYield":4,"copyCount":2,"totalYield":8,"unknown":{"array":[1,"two",null]} }</lb:batch><foreign:license>keep</foreign:license></metadata>
          <style>path:hover { stroke: red }</style><g id="art" transform="translate(3,4)"><path id="path" d="M0 0h10v20z"/></g><text style="display:none">License</text>
        </svg>
        """);
    private static XElement Record(XDocument doc) => doc.Root!.Element(S + "metadata")!.Elements().First();
    private static JsonObject Json(XDocument doc) => JsonNode.Parse(Record(doc).Value)!.AsObject();
    private static void Alter(XDocument doc, Action<JsonObject> action) { var json = Json(doc); action(json); Record(doc).Value = json.ToJsonString(); }

    [Fact]
    public void MetadataOnlyMigratesPatchesAndPreservesAllOtherXmlAndUnknownJson()
    {
        var source = Batch();
        Record(source).AddFirst(new XComment("retain record annotation"));
        var before = new XDocument(source);
        var analysis = SvgBatchService.Analyze(source.ToString());
        Assert.NotEmpty(analysis.Errors); // Unsupported CSS must not prevent metadata-only updates.
        var result = SvgMetadataService.Update(analysis.SourceDocument, new("Renamed", "Order42", "Lot", 7), 90061);
        Assert.Equal("Renamed_Order Order42_Batch Lot_x7.svg", result.Filename);
        Assert.True(XNode.DeepEquals(source, before));
        var record = Record(result.Document);
        Assert.Equal(SvgMetadataService.Namespace + "batch", record.Name);
        Assert.Equal("keep", record.Attribute("custom")!.Value);
        Assert.Equal("retain record annotation", record.Nodes().OfType<XComment>().Single().Value);
        var json = Json(result.Document);
        Assert.Equal(90061, json["estimatedRuntimeSeconds"]!.GetValue<long>());
        Assert.Equal(7, json["yieldOverride"]!.GetValue<int>());
        Assert.Equal(4, json["sourceYield"]!.GetValue<int>());
        Assert.Equal(2, json["copyCount"]!.GetValue<int>());
        Assert.Equal(Json(source)["unknown"]!.ToJsonString(), json["unknown"]!.ToJsonString());
        Assert.Equal("keep", json["program"]!["custom"]!.GetValue<string>());
        var comparison = new XDocument(result.Document);
        Record(comparison).ReplaceWith(new XElement(Record(source)));
        Assert.True(XNode.DeepEquals(source, comparison));
        var serialized = SvgMetadataService.Parse(SvgSerializer.Serialize(result.Document));
        var read = SvgMetadataService.Read(serialized);
        Assert.Empty(read.Warnings);
        Assert.Equal(7, read.Metadata!.Program.Yield);
        Assert.Equal(90061, read.Metadata.EstimatedRuntimeSeconds);
        var cleared = SvgMetadataService.Update(serialized, read.Metadata.Program with { Yield = 8 }, null);
        Assert.False(Json(cleared.Document).ContainsKey("estimatedRuntimeSeconds"));
        Assert.False(Json(cleared.Document).ContainsKey("yieldOverride"));
    }

    [Fact]
    public void RebatchUsesEffectiveYieldAndDoesNotCarryRuntimeOrOverride()
    {
        var source = SvgMetadataService.Update(Batch(), new("Widget", null, null, 7), 90).Document;
        source.Descendants(S + "style").Remove();
        var a = SvgBatchService.Analyze(source.ToString());
        var map = a.Items.Where(i => i.Production).ToDictionary(i => i.Key, _ => LaserOperation.ExteriorCut);
        var result = SvgBatchService.Build(a, "Widget_x7.svg", a.Metadata!.Program, new(30, 20, 0, 0, 0, 3, OrderingMode.WithinEachCopy), map);
        var read = SvgMetadataService.Read(result.Document);
        Assert.Empty(read.Warnings);
        Assert.Equal(21, read.Metadata!.Program.Yield);
        Assert.Equal(7, read.Metadata.SourceYield);
        Assert.Null(read.Metadata.YieldOverride);
        Assert.Null(read.Metadata.EstimatedRuntimeSeconds);
    }

    [Fact]
    public void FilenameConflictsRequireAChoiceButRenamedFilesRestoreMetadata()
    {
        var metadata = SvgMetadataService.Read(Batch()).Metadata!;
        Assert.False(SvgMetadataService.SelectProgram("renamed.svg", metadata).Conflict);
        Assert.Equal(metadata.Program, SvgMetadataService.SelectProgram("renamed.svg", metadata).Program);
        Assert.False(SvgMetadataService.SelectProgram("Widget_x8.svg", metadata).Conflict);
        Assert.True(SvgMetadataService.SelectProgram("Widget_x3.svg", metadata).Conflict);
        Assert.NotNull(SvgMetadataService.SelectProgram("bad_x0.svg", null).Error);
    }

    [Fact]
    public void HistoricalRecordsAreIgnoredAndDuplicateRootRecordsAreRejected()
    {
        var doc = Batch();
        doc.Root!.Element(S + "g")!.Add(new XElement(S + "metadata", new XElement(Record(doc))));
        Assert.NotNull(SvgMetadataService.Read(doc).Metadata);
        doc.Root.Add(new XElement(S + "metadata", new XElement(Record(doc))));
        Assert.Null(SvgMetadataService.Read(doc).Metadata);
        Assert.Throws<FormatException>(() => SvgMetadataService.Update(doc, new("Widget", null, null, 8), null));
    }

    [Theory]
    [InlineData("version")][InlineData("counts")][InlineData("overflow")][InlineData("runtime")][InlineData("duplicate")][InlineData("malformed")][InlineData("v1override")][InlineData("xmlchild")][InlineData("nullruntime")]
    public void InvalidMetadataIsPreservedWithWarnings(string kind)
    {
        var doc = Batch();
        if (kind == "version") Record(doc).Name = XName.Get("batch", "urn:laserbatcher:metadata:9");
        if (kind == "counts") Alter(doc, j => j["totalYield"] = 9);
        if (kind == "overflow") Alter(doc, j => j["copyCount"] = int.MaxValue);
        if (kind == "runtime") Alter(doc, j => j["estimatedRuntimeSeconds"] = 0.5);
        if (kind == "duplicate") Record(doc).Value = Record(doc).Value.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1");
        if (kind == "malformed") Record(doc).Value = "{";
        if (kind == "v1override") Alter(doc, j => j["yieldOverride"] = 8);
        if (kind == "xmlchild") Record(doc).ReplaceNodes(new XElement("foreign", Record(doc).Value));
        if (kind == "nullruntime") Alter(doc, j => j["estimatedRuntimeSeconds"] = null);
        var before = doc.ToString();
        var read = SvgMetadataService.Read(doc);
        Assert.Null(read.Metadata);
        Assert.NotEmpty(read.Warnings);
        Assert.Equal(before, doc.ToString());
    }

    [Theory]
    [InlineData("25", "1", "1", 90061L)] [InlineData("", "", "1", 1L)] [InlineData(null, null, null, null)]
    public void RuntimeIsOptionalWholeJobDuration(string? h, string? m, string? s, long? expected) => Assert.Equal(expected, EstimatedRuntime.Parse(h, m, s));

    [Theory]
    [InlineData("0", "0", "0")][InlineData("-1", "0", "0")][InlineData("0", "60", "0")][InlineData("0", "0", "1.5")]
    [InlineData("9223372036854775807", "0", "0")][InlineData("9999999999999999999999", "0", "0")]
    public void RuntimeRejectsInvalidValues(string h, string m, string s) => Assert.Throws<FormatException>(() => EstimatedRuntime.Parse(h, m, s));

    [Theory]
    [InlineData("1in")][InlineData("25.4mm")][InlineData("2.54cm")][InlineData("72pt")][InlineData("6pc")]
    public void PhysicalUnitsShareTheSameConversion(string length) => Assert.Equal(1, SvgLengths.PhysicalInches(length), 10);

    [Theory]
    [InlineData("96px")][InlineData("96")][InlineData("100%")][InlineData("0mm")][InlineData("-1in")][InlineData("1e309in")][InlineData(null)]
    public void PhysicalUnitsRejectAmbiguousOrInvalidDimensions(string? length) => Assert.Throws<FormatException>(() => SvgLengths.PhysicalInches(length));
}
