namespace SolarBmsMonitor.App.ViewModels;

/// <summary>
/// A single cell voltage prepared for display: numbered so a card can be tied
/// back to a physical cell, and flagged when it sits at either end of the pack
/// spread.
/// </summary>
/// <param name="Number">One-based position of the cell inside the pack.</param>
/// <param name="VoltageText">Voltage already formatted for display.</param>
/// <param name="IsMinimum">The cell holds the lowest voltage in the pack.</param>
/// <param name="IsMaximum">The cell holds the highest voltage in the pack.</param>
public sealed record CellReading(
    int Number,
    string VoltageText,
    bool IsMinimum,
    bool IsMaximum)
{
    public string NumberText => $"Celda {Number}";

    public bool IsExtreme => IsMinimum || IsMaximum;
}
