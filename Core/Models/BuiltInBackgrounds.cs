using System.Collections.Generic;

namespace Iris.Core.Models;

/// <summary>The six gradient backgrounds that ship with the app (IRIS_SPEC §9). Church pictures come on top of these.</summary>
public static class BuiltInBackgrounds
{
    public static IReadOnlyList<ProjectionBackground> All { get; } =
    [
        new("aurora", "Aurora", ["#2A1658", "#4E2A8C", "#131E5C"], true),
        new("brasa", "Brasa", ["#3A1E08", "#8C3A1E", "#3D1235"], false),
        new("oceano", "Océano", ["#06283D", "#0E5E6F", "#0A1931"], true),
        new("olivo", "Olivo", ["#0F2417", "#2F5233", "#111A12"], false),
        new("alba", "Alba", ["#5B2A3C", "#C0694E", "#2B1A3A"], false),
        new("medianoche", "Medianoche", ["#07070B", "#15151F", "#07070B"], false),
    ];
}
