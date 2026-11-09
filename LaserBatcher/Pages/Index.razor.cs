using LaserBatcher.Services;
using LaserBatcher.Models;
using LaserBatcher.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components;

namespace LaserBatcher.Pages;
public partial class Index
{
    private SvgAnalysis? analysis;
    private BatchResult? rendering;
    private string sourceFilename = "", workMode = "CreateBatch", canvasView = "Source", woodColor = "#8B4513";
    private string? error, sourceNameError, notice, sourcePreviewUrl, batchPreviewUrl;
    private string? runtimeHours, runtimeMinutes, runtimeSeconds, draftHours, draftMinutes, draftSeconds, draftError;
    private bool metadataConflict, compact, panelCollapsed;
    private ProgramMetadata? filenameCandidate;
    private SheetForm sheet = new(), draftSheet = new();
    private ProgramForm program = new(), draftProgram = new();
    private OrderingMode ordering = OrderingMode.WithinEachCopy, draftOrdering;
    private readonly Dictionary<string, SvgAssignment> mappings = new();
    private ElementReference workArea, splitter;
    private MappingPanel? mappingPanel;
    private WorkspaceDialog runtimeDialog = default!, sheetDialog = default!, programDialog = default!, hiddenDialog = default!, glowforgeDialog = default!, issuesDialog = default!, helpDialog = default!;
    private IJSObjectReference? workspace;
    private DotNetObjectReference<Index>? reference;
    private bool EditingMetadata => workMode == "EditMetadata";
    private ProgramMetadata SourceProgram => new(program.PartName, program.OrderNumber, program.Batch, program.Yield);
    private ProgramMetadata OutputProgram => EditingMetadata ? SourceProgram : ProgramNameParser.Total(SourceProgram, sheet.Copies);
    private long? Runtime => EstimatedRuntime.Parse(runtimeHours, runtimeMinutes, runtimeSeconds);
    private bool MetadataBlocked => analysis == null || sourceNameError != null || metadataConflict;
    private BatchSettings Settings => SettingsFor(sheet, ordering);
    private static BatchSettings SettingsFor(SheetForm value, OrderingMode order) => new(value.Width, value.Height, value.HorizontalMargin, value.VerticalMargin, value.Spacing, value.Copies, order);
    private IReadOnlyDictionary<string, SvgAssignment?> Resolved => analysis == null ? new Dictionary<string, SvgAssignment?>() : SvgBatchService.Resolve(analysis, mappings);
    private int IncludedCount => Resolved.Count(p => p.Value != null && p.Value != SvgAssignment.Ignore);
    private int UnresolvedCount => Resolved.Count(p => p.Value == null);
    private string? BlockingReason => analysis == null ? "Open an SVG to begin." : sourceNameError != null ? "Correct the program fields: " + sourceNameError : metadataConflict ? "Filename and embedded metadata disagree. Open Issues to choose a source." : EditingMetadata ? null : analysis.Errors.FirstOrDefault() ?? (UnresolvedCount > 0 ? $"Assign operations to {UnresolvedCount} unresolved production elements." : IncludedCount == 0 ? "Include at least one production element." : null);
    private IEnumerable<string> Warnings => (analysis?.Warnings ?? Array.Empty<string>()).Concat(rendering?.Warnings ?? Array.Empty<string>()).Distinct();
    private int IssueCount => (analysis?.Errors.Count ?? 0) + Warnings.Count() + (metadataConflict ? 1 : 0) + (sourceNameError != null ? 1 : 0) + (error != null ? 1 : 0);
    private string Status => string.Join(" ", new[] { error, analysis != null ? BlockingReason : null, notice }.Where(s => s != null).Distinct()) is string message && message.Length > 0 ? message : EditingMetadata ? "Metadata only · artwork and machine settings are preserved." : rendering != null ? "Batch ready to download." : analysis == null ? "Open an SVG to begin." : "Source view · render when your assignments and sheet are ready.";
    private string FilenamePreview { get { try { return ProgramNameParser.Format(OutputProgram); } catch (Exception ex) when (ex is FormatException or OverflowException) { return ex is OverflowException ? "Total yield exceeds 2147483647." : ex.Message; } } }
    private static string ImageUrl(XDocument doc) => "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(SvgSerializer.Serialize(doc)));
    protected override async Task OnAfterRenderAsync(bool firstRender) {
        if (!firstRender) return;
        workspace = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./workspace.js"); reference = DotNetObjectReference.Create(this);
        compact = await workspace.InvokeAsync<bool>("initializeShell", workArea, splitter, reference); StateHasChanged();
    }
    [JSInvokable] public Task SetCompact(bool value) { compact = value; return InvokeAsync(StateHasChanged); }
    public async ValueTask DisposeAsync() { if (workspace != null) { await workspace.InvokeVoidAsync("disposeShell", workArea); await workspace.DisposeAsync(); } reference?.Dispose(); }
    private async Task ToggleMapping() { if (compact && mappingPanel != null) await mappingPanel.OpenDrawerAsync(); else panelCollapsed = !panelCollapsed; }
    private void Invalidate() { rendering = null; batchPreviewUrl = null; canvasView = "Source"; error = null; }
    private void MetadataChanged() { sourceNameError = null; metadataConflict = false; Invalidate(); }
    private void BatchChanged() {
        if (Runtime != null) { SetRuntime(null); notice = "Estimated runtime was cleared because the batch changed. Enter a new estimate for the complete output job."; } Invalidate();
    }
    private void SetRuntime(long? value) {
        runtimeHours = value == null ? null : (value / 3600).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        runtimeMinutes = value == null ? null : (value / 60 % 60).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        runtimeSeconds = value == null ? null : (value % 60).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private void UseProgram(ProgramMetadata value) { program = new() { PartName = value.PartName, OrderNumber = value.OrderNumber, Batch = value.Batch, Yield = value.Yield }; MetadataChanged(); }
    private void ModeChanged(ChangeEventArgs args) {
        workMode = args.Value?.ToString() == "EditMetadata" && analysis?.Metadata != null ? "EditMetadata" : "CreateBatch";
        SetRuntime(EditingMetadata ? analysis?.Metadata?.EstimatedRuntimeSeconds : null);
        notice = EditingMetadata ? null : "A new batch needs its own runtime estimate. The source estimate is not carried forward."; Invalidate();
    }
    private void SetMapping(MappingChange change) { if (Enum.TryParse<SvgAssignment>(change.Value, out var value)) mappings[change.Key] = value; else mappings.Remove(change.Key); BatchChanged(); }
    private async Task HandleFileUpload(InputFileChangeEventArgs args) {
        Invalidate(); analysis = null; sourcePreviewUrl = null; mappings.Clear(); sourceNameError = null; metadataConflict = false; filenameCandidate = null; notice = null;
        workMode = "CreateBatch"; SetRuntime(null); program = new();
        try {
            sourceFilename = args.File.Name; using var stream = args.File.OpenReadStream(10 * 1024 * 1024);
            analysis = SvgBatchService.Analyze(await new StreamReader(stream).ReadToEndAsync());
            var source = SvgMetadataService.SelectProgram(sourceFilename, analysis.Metadata);
            UseProgram(source.Program); filenameCandidate = source.FilenameCandidate; metadataConflict = source.Conflict; sourceNameError = source.Error;
            workMode = analysis.Metadata != null ? "EditMetadata" : "CreateBatch";
            if (EditingMetadata) SetRuntime(analysis.Metadata!.EstimatedRuntimeSeconds);
            // Passive, validated SVGs enter previews. Metadata-only updates are independent.
            if (!analysis.Errors.Any()) sourcePreviewUrl = ImageUrl(analysis.Document);
        } catch (XmlException ex) { error = $"Could not load SVG: invalid XML near line {ex.LineNumber}, column {ex.LinePosition}. Check that all elements and attributes are properly closed."; }
        catch (Exception ex) { error = "Could not load SVG: " + ex.Message; }
    }
    private void CalculateFillSheet() { try { if (analysis != null) { var count = SvgBatchService.Capacity(analysis, Settings); if (count < 1) throw new FormatException("No copies fit within the sheet margins."); sheet.Copies = count; BatchChanged(); } } catch (Exception ex) { error = ex.Message; } }
    private void RenderGrid() {
        Invalidate();
        try { if (BlockingReason != null) return; rendering = SvgBatchService.Build(analysis!, sourceFilename, SourceProgram, Settings, mappings, Runtime); batchPreviewUrl = ImageUrl(rendering.Document); canvasView = "Batch"; }
        catch (OverflowException) { error = "The yield or copy capacity exceeds 2147483647. Reduce yield or copies."; }
        catch (Exception ex) { error = ex.Message; }
    }
    private async Task SaveBatch() {
        try {
            if (MetadataBlocked) return;
            BatchResult? output;
            if (EditingMetadata) output = SvgMetadataService.Update(analysis!.SourceDocument, SourceProgram, Runtime);
            else { if (rendering == null) return; RenderGrid(); output = rendering; }
            if (output == null) return;
            await JSRuntime.InvokeVoidAsync("window.saveAsFile", output.Filename, Convert.ToBase64String(Encoding.UTF8.GetBytes(SvgSerializer.Serialize(output.Document))));
        } catch (Exception ex) { error = "Could not download SVG: " + ex.Message; }
    }
    private async Task OpenRuntime() { draftHours = runtimeHours; draftMinutes = runtimeMinutes; draftSeconds = runtimeSeconds; draftError = null; await runtimeDialog.OpenAsync(); }
    private async Task ApplyRuntime() {
        try { var value = EstimatedRuntime.Parse(draftHours, draftMinutes, draftSeconds); if (value != Runtime) { SetRuntime(value); notice = null; Invalidate(); } await runtimeDialog.CloseAsync(); }
        catch (FormatException ex) { draftError = ex.Message; }
    }
    private async Task OpenSheet() { draftSheet = sheet.Copy(); draftOrdering = ordering; draftError = null; await sheetDialog.OpenAsync(); }
    private void FillDraft() { try { if (analysis != null) { var count = SvgBatchService.Capacity(analysis, SettingsFor(draftSheet, draftOrdering)); if (count < 1) throw new FormatException("No copies fit within the sheet margins."); draftSheet.Copies = count; draftError = null; } } catch (Exception ex) { draftError = ex.Message; } }
    private async Task ApplySheet() {
        try {
            if (analysis != null) _ = SvgBatchService.Capacity(analysis, SettingsFor(draftSheet, draftOrdering));
            else if (draftSheet.Width <= 0 || draftSheet.Height <= 0 || draftSheet.Copies < 1 || draftSheet.HorizontalMargin < 0 || draftSheet.VerticalMargin < 0 || draftSheet.Spacing < 0) throw new FormatException("Sheet dimensions and copies must be positive; margins and spacing must be nonnegative.");
            if (Settings != SettingsFor(draftSheet, draftOrdering)) { sheet = draftSheet.Copy(); ordering = draftOrdering; BatchChanged(); } await sheetDialog.CloseAsync();
        } catch (Exception ex) { draftError = ex.Message; }
    }
    private async Task OpenProgram() { draftProgram = program.Copy(); draftError = null; await programDialog.OpenAsync(); }
    private async Task ApplyProgram() {
        try { var value = new ProgramMetadata(draftProgram.PartName, draftProgram.OrderNumber, draftProgram.Batch, draftProgram.Yield); _ = ProgramNameParser.Format(EditingMetadata ? value : ProgramNameParser.Total(value, sheet.Copies)); UseProgram(value); await programDialog.CloseAsync(); }
        catch (Exception ex) when (ex is FormatException or OverflowException) { draftError = ex.Message; }
    }
    private string HiddenXml(SvgItem item) {
        if (analysis == null) return ""; var node = analysis.Document.Root!;
        foreach (var index in item.Key.Split('/').Skip(1)) node = node.Elements().ElementAt(int.Parse(index)); return node.ToString();
    }
}
