using System.IO;
using System.Text.Json;

namespace LmuRaceEngineerCS;

public class LayoutState
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "layout.json");

    // Outer grid — content row heights in pixels (rows 0, 2, 4, 6)
    public double[] OuterRows { get; set; } = [];

    // Row 0 — race strip content column widths (columns 0,2,4,6,8,10)
    public double[] Row0Cols { get; set; } = [];

    // Row 2 — delta strip content column widths (columns 0,2,4,6,8)
    public double[] Row2Cols { get; set; } = [];

    // Row 4 — main panels content column widths (columns 0,2,4 = fuel,tire,pitwindow)
    public double[] Row4Cols { get; set; } = [];

    // Row 6 — strategy outer content column widths (columns 0,2)
    public double[] Row6Cols { get; set; } = [];

    // Row 6 left column — content row heights (rows 0,2 = session,stint)
    public double[] Row6LeftRows { get; set; } = [];

    // Row 6 right column — content row heights (rows 0,2 = driveavg,fuelstrat)
    public double[] Row6RightRows { get; set; } = [];

    // StrategyPage — OuterGrid row heights (rows 0,2,4,6,8)
    public double[] StrategyRows { get; set; } = [];

    // LapsPage — row heights for chart row (4) and table row (6)
    public double[] LapsRows { get; set; } = [];

    // Edit-Layout mode — per-widget data-box scale factor (1.0 = design size): WidgetId → scale
    public Dictionary<string, double> WidgetScales { get; set; } = new();

    // Floating overlay windows — bounds by title: title → [left, top, width, height]
    public Dictionary<string, double[]> OverlayBounds { get; set; } = new();

    public static LayoutState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(FilePath)) ?? new LayoutState();
        }
        catch { }
        return new LayoutState();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
