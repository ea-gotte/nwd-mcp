using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bimwright.Nwd.Shared.Infrastructure;

/// <summary>RGB colour with channels 0..1 (Navisworks' convention), parsed from "#RRGGBB", "RRGGBB", "r,g,b" (0-255) or a basic name.</summary>
public readonly struct ColorSpec
{
    public double R { get; }
    public double G { get; }
    public double B { get; }

    public ColorSpec(double r, double g, double b)
    {
        R = Clamp(r); G = Clamp(g); B = Clamp(b);
    }

    public string Hex => "#" + Byte(R).ToString("X2", CultureInfo.InvariantCulture) + Byte(G).ToString("X2", CultureInfo.InvariantCulture) + Byte(B).ToString("X2", CultureInfo.InvariantCulture);

    private static readonly Dictionary<string, ColorSpec> Named = new Dictionary<string, ColorSpec>(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = new ColorSpec(1, 0, 0), ["green"] = new ColorSpec(0, 0.6, 0), ["blue"] = new ColorSpec(0, 0, 1),
        ["yellow"] = new ColorSpec(1, 1, 0), ["orange"] = new ColorSpec(1, 0.55, 0), ["purple"] = new ColorSpec(0.5, 0, 0.5),
        ["cyan"] = new ColorSpec(0, 1, 1), ["magenta"] = new ColorSpec(1, 0, 1), ["white"] = new ColorSpec(1, 1, 1),
        ["black"] = new ColorSpec(0, 0, 0), ["grey"] = new ColorSpec(0.5, 0.5, 0.5), ["gray"] = new ColorSpec(0.5, 0.5, 0.5),
    };

    public static bool TryParse(string? raw, out ColorSpec color, out string error)
    {
        color = default; error = "";
        var s = (raw ?? "").Trim();
        if (s.Length == 0) { error = "colour is empty"; return false; }
        if (Named.TryGetValue(s, out color)) return true;

        var hex = s.StartsWith("#", StringComparison.Ordinal) ? s.Substring(1) : s;
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            color = new ColorSpec(((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
            return true;
        }

        var parts = s.Split(',');
        if (parts.Length == 3)
        {
            var c = new double[3];
            for (int i = 0; i < 3; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ch) || ch < 0 || ch > 255)
                { error = $"colour '{raw}': r,g,b channels must be 0-255"; return false; }
                else c[i] = ch / 255.0;
            color = new ColorSpec(c[0], c[1], c[2]);
            return true;
        }

        error = $"colour '{raw}' must be #RRGGBB, r,g,b (0-255) or a basic name (red, green, blue, yellow, orange, purple, cyan, magenta, white, black, grey)";
        return false;
    }

    /// <summary>
    /// <paramref name="count"/> distinct, deterministic colours: hues spaced by the golden ratio, alternating
    /// saturation/value bands so neighbours in a legend stay distinguishable.
    /// </summary>
    public static IReadOnlyList<ColorSpec> Palette(int count)
    {
        var list = new List<ColorSpec>(Math.Max(0, count));
        const double golden = 0.618033988749895;
        double h = 0.07;
        for (int i = 0; i < count; i++)
        {
            h = (h + golden) % 1.0;
            double s = (i / 3) % 2 == 0 ? 0.75 : 0.55;
            double v = i % 3 == 2 ? 0.7 : 0.95;
            list.Add(FromHsv(h, s, v));
        }
        return list;
    }

    public static ColorSpec FromHsv(double h, double s, double v)
    {
        h = (h % 1.0 + 1.0) % 1.0 * 6.0;
        int i = (int)Math.Floor(h);
        double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        switch (i % 6)
        {
            case 0: return new ColorSpec(v, t, p);
            case 1: return new ColorSpec(q, v, p);
            case 2: return new ColorSpec(p, v, t);
            case 3: return new ColorSpec(p, q, v);
            case 4: return new ColorSpec(t, p, v);
            default: return new ColorSpec(v, p, q);
        }
    }

    private static double Clamp(double x) => double.IsNaN(x) ? 0 : Math.Max(0, Math.Min(1, x));
    private static int Byte(double x) => (int)Math.Round(Clamp(x) * 255);
}
