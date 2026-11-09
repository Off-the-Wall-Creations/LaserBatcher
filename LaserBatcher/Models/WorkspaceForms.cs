namespace LaserBatcher.Models;

public sealed record MappingChange(string Key, string? Value);

public sealed class SheetForm
{
    public double Width { get; set; } = 19;
    public double Height { get; set; } = 12;
    public double HorizontalMargin { get; set; }
    public double VerticalMargin { get; set; }
    public double Spacing { get; set; }
    public int Copies { get; set; } = 1;
    public SheetForm Copy() => (SheetForm)MemberwiseClone();
}

public sealed class ProgramForm
{
    public string? PartName { get; set; }
    public string? OrderNumber { get; set; }
    public string? Batch { get; set; }
    public int Yield { get; set; } = 1;
    public ProgramForm Copy() => (ProgramForm)MemberwiseClone();
}
