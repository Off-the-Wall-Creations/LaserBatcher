using LaserBatcher.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Components;

namespace LaserBatcher.Pages;

public partial class Index
{
    private SvgAnalysis? analysis;
    private BatchResult? rendering;
    private string sourceFilename = "";
    private string? error, sourceNameError, partName, orderNumber, batch;
    private string? runtimeHours, runtimeMinutes, runtimeSeconds, notice;
    private string workMode = "CreateBatch";
    private bool EditingMetadata => workMode == "EditMetadata";
    private bool metadataConflict;
    private ProgramMetadata? filenameCandidate;
    private int sourceYield = 1, copyQuantity = 1;
    private double boxWidthInches = 19, boxHeightInches = 12, horizontalMarginInches, verticalMarginInches, spaceBetweenInches;
    private string woodColor = "#8B4513";
    private OrderingMode ordering = OrderingMode.WithinEachCopy;
    private readonly Dictionary<string, LaserOperation> mappings = new();
    private ElementReference mappingList;
    private IJSObjectReference? thumbnailModule;
    private SvgAnalysis? thumbnailAnalysis;
    private static int MappingDepth(SvgItem item) => item.Key.Count(c => c == '/');
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (analysis == thumbnailAnalysis) return;
        if (thumbnailModule != null) await thumbnailModule.InvokeVoidAsync("dispose", mappingList);
        thumbnailAnalysis = analysis;
        if (analysis == null || analysis.Errors.Any() || EditingMetadata) return;
        thumbnailModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./mappingPreviews.js");
        await thumbnailModule.InvokeVoidAsync("initialize", mappingList, analysis.Document.ToString());
    }
    public async ValueTask DisposeAsync()
    {
        if (thumbnailModule == null) return;
        try { await thumbnailModule.InvokeVoidAsync("dispose", mappingList); await thumbnailModule.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
    private ProgramMetadata SourceProgram => new(partName, orderNumber, batch, sourceYield);
    private ProgramMetadata OutputProgram => EditingMetadata ? SourceProgram : ProgramNameParser.Total(SourceProgram, copyQuantity);
    private long? Runtime => EstimatedRuntime.Parse(runtimeHours, runtimeMinutes, runtimeSeconds);
    private string? RuntimeError { get { try { _ = Runtime; return null; } catch (FormatException ex) { return ex.Message; } } }
    private bool MetadataBlocked => analysis == null || sourceNameError != null || metadataConflict || RuntimeError != null;
    private BatchSettings Settings => new(boxWidthInches, boxHeightInches, horizontalMarginInches, verticalMarginInches, spaceBetweenInches, copyQuantity, ordering);
    private IReadOnlyDictionary<string, LaserOperation?> Resolved => analysis == null ? new Dictionary<string, LaserOperation?>() : SvgBatchService.Resolve(analysis, mappings);
    private int UnresolvedCount => Resolved.Count(p => p.Value == null);
    private IEnumerable<SvgItem> MappingItems => analysis?.Items.Where(i => i.Production || !i.Hidden && (i.Element is "g" or "svg") && analysis.Items.Any(p => p.Production && p.Key.StartsWith(i.Key + "/", StringComparison.Ordinal))) ?? Enumerable.Empty<SvgItem>();
    private string PreviewUrl => "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(SvgSerializer.Serialize(rendering!.Document)));
    private string FilenamePreview { get { try { return ProgramNameParser.Format(OutputProgram); } catch (Exception ex) when (ex is FormatException or OverflowException) { return ex is OverflowException ? "Total yield exceeds 2147483647." : ex.Message; } } }
    private void Invalidate() { rendering = null; error = null; }
    private void MetadataChanged() { sourceNameError = null; metadataConflict = false; Invalidate(); }
    private void BatchChanged()
    {
        if (new[] { runtimeHours, runtimeMinutes, runtimeSeconds }.Any(s => !string.IsNullOrWhiteSpace(s)))
        {
            SetRuntime(null);
            notice = "Estimated runtime was cleared because the batch changed. Enter a new estimate for the complete output job.";
        }
        Invalidate();
    }
    private void ClearRuntime() { SetRuntime(null); notice = null; Invalidate(); }
    private void SetRuntime(long? value)
    {
        runtimeHours = value == null ? null : (value / 3600).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        runtimeMinutes = value == null ? null : (value / 60 % 60).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        runtimeSeconds = value == null ? null : (value % 60).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private void UseProgram(ProgramMetadata value)
    {
        partName = value.PartName; orderNumber = value.OrderNumber; batch = value.Batch; sourceYield = value.Yield;
        MetadataChanged();
    }
    private void ModeChanged(ChangeEventArgs args)
    {
        workMode = args.Value?.ToString() == "EditMetadata" && analysis?.Metadata != null ? "EditMetadata" : "CreateBatch";
        thumbnailAnalysis = null;
        SetRuntime(EditingMetadata ? analysis?.Metadata?.EstimatedRuntimeSeconds : null);
        notice = EditingMetadata ? null : "A new batch needs its own runtime estimate. The source estimate is not multiplied or carried forward.";
        Invalidate();
    }
    private string MappingValue(string key) => mappings.TryGetValue(key, out var value) ? value.ToString() : "";
    private void SetMapping(string key, string? value)
    {
        if (Enum.TryParse<LaserOperation>(value, out var operation)) mappings[key] = operation; else mappings.Remove(key);
        BatchChanged();
    }
    private async Task HandleFileUpload(InputFileChangeEventArgs args)
    {
        Invalidate(); analysis = null; mappings.Clear(); sourceNameError = null; metadataConflict = false; filenameCandidate = null; notice = null;
        workMode = "CreateBatch"; SetRuntime(null);
        partName = orderNumber = batch = null; sourceYield = 1;
        try
        {
            sourceFilename = args.File.Name;
            using var stream = args.File.OpenReadStream(10 * 1024 * 1024);
            analysis = SvgBatchService.Analyze(await new StreamReader(stream).ReadToEndAsync());
            var source = SvgMetadataService.SelectProgram(sourceFilename, analysis.Metadata);
            UseProgram(source.Program);
            filenameCandidate = source.FilenameCandidate; metadataConflict = source.Conflict;
            sourceNameError = source.Error;
            workMode = analysis.Metadata != null ? "EditMetadata" : "CreateBatch";
            if (EditingMetadata) SetRuntime(analysis.Metadata!.EstimatedRuntimeSeconds);
        }
        catch (XmlException ex) { error = $"Could not load SVG: invalid XML near line {ex.LineNumber}, column {ex.LinePosition}. Check that all elements and attributes are properly closed."; }
        catch (Exception ex) { error = "Could not load SVG: " + ex.Message; }
    }
    private void CalculateFillSheet()
    {
        BatchChanged();
        try { if (analysis != null) { var count = SvgBatchService.Capacity(analysis, Settings); if (count < 1) throw new FormatException("No copies fit within the sheet margins."); copyQuantity = count; } }
        catch (Exception ex) { error = ex.Message; }
    }
    private void RenderGrid()
    {
        Invalidate();
        try { if (analysis != null && !MetadataBlocked) rendering = SvgBatchService.Build(analysis, sourceFilename, SourceProgram, Settings, mappings, Runtime); }
        catch (OverflowException) { error = "The yield or copy capacity exceeds 2147483647. Reduce yield or copies."; }
        catch (Exception ex) { error = ex.Message; }
    }
    private async Task SaveBatch()
    {
        try
        {
            if (MetadataBlocked) return;
            if (EditingMetadata) rendering = SvgMetadataService.Update(analysis!.SourceDocument, SourceProgram, Runtime);
            else RenderGrid();
            if (rendering == null) return;
            await JSRuntime.InvokeVoidAsync("window.saveAsFile", rendering.Filename, Convert.ToBase64String(Encoding.UTF8.GetBytes(SvgSerializer.Serialize(rendering.Document))));
        }
        catch (Exception ex) { error = "Could not download SVG: " + ex.Message; }
    }
}
