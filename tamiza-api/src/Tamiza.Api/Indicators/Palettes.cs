using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Indicators;

/// <summary>
/// The palettes a dashboard can have and the sequential scales a map or a heat map can be drawn on (design D2 of dashboard palettes).
/// Only ids live here: the colors are the page's, by the same ids (<c>chart-colors.ts</c>). The palette ids are also the ones the
/// <c>ck_dashboards_palette</c> check of script 0019 lists, and a test keeps the two together.
/// </summary>
public static class Palettes
{
    public const string Tamiza = "tamiza";
    public const string Institutional = "institutional";
    public const string Earth = "earth";
    public const string Vivid = "vivid";

    /// <summary>The palettes, as stored in <c>dashboards.palette</c> and sent as <c>palette</c>.</summary>
    public static readonly IReadOnlyList<string> All = [Tamiza, Institutional, Earth, Vivid];

    /// <summary>The sequential scales a map or a heat map can be drawn on, as sent in a widget's <c>display.ramp</c>.</summary>
    public static readonly IReadOnlyList<string> Scales = ["blue", "teal", "green", "orange", "red", "purple", "gray"];

    /// <summary>The categorical colors of a palette, numbered 1 to this.</summary>
    public const int ColorCount = 8;

    /// <summary>The most colors a dashboard keeps by value.</summary>
    public const int MaxValueColors = 64;

    public static bool IsKnown(string? palette) => palette is not null && All.Contains(palette);

    public static bool IsScale(string? scale) => scale is not null && Scales.Contains(scale);

    /// <summary>Whether <paramref name="color"/> is the number of one of a palette's colors.</summary>
    public static bool IsColor(int? color) => color is >= 1 and <= ColorCount;

    /// <summary>The scale a map or a heat map is drawn on when its widget names none: the palette's own.</summary>
    public static string DefaultScale(string palette) => palette switch
    {
        Tamiza => "blue",
        Institutional => "teal",
        Earth => "orange",
        Vivid => "purple",
        _ => throw new ArgumentOutOfRangeException(nameof(palette), palette, "Not a palette."),
    };
}

/// <summary>
/// A color a dashboard keeps for a value of a column, as stored in <c>dashboards.value_colors</c> and returned with the dashboard
/// (design D3). Not checked against any table: an entry whose value no widget shows changes nothing.
/// </summary>
/// <param name="Column">A published column's name; a value is kept at most once per column, and so is a color.</param>
/// <param name="Value">The value's text, matched exactly, case included; may be empty, since an empty text is a group value.</param>
/// <param name="Color">The number, 1 to <see cref="Palettes.ColorCount"/>, of one of the dashboard palette's colors.</param>
public sealed record ValueColor(string Column, string Value, int Color);

/// <summary>
/// An entry as the request to save a dashboard's colors carries it; <see cref="DashboardValidation.Colors"/> checks it. The description
/// and range on the fields are what the MCP tools' input schema shows of them, and validate nothing.
/// </summary>
public sealed record ValueColorInput(
    [property: Description("A column's name: lowercase letters, digits and underscores, not starting with a digit, at most 63 characters.")] string? Column,
    [property: Description("The value's text, matched exactly, case and spaces included; an empty text is a value too.")] string? Value,
    [property: Description("A place in the dashboard's palette, 1 to 8, never a color code."), Range(1, Palettes.ColorCount)] int? Color);

/// <summary>The body that replaces a dashboard's palette and colors kept by value (design D5): both, whole.</summary>
/// <param name="Palette">One of <see cref="Palettes.All"/>.</param>
/// <param name="ValueColors">At most <see cref="Palettes.MaxValueColors"/> entries, in the order to keep them in.</param>
public sealed record DashboardColorsRequest(string? Palette, IReadOnlyList<ValueColorInput?>? ValueColors);

/// <summary>A dashboard's palette and colors kept by value, as saved, and the answer to saving them (design D5).</summary>
public sealed record DashboardColors(string Palette, IReadOnlyList<ValueColor> ValueColors);
