using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// Turns the per-body files <see cref="PartingScorecard"/> writes into the tables a change is judged
/// by: one summary of a run, and one comparison of two runs.
///
/// <para>
/// Kept apart from the measuring so the tables can be rebuilt, or two old runs compared, without
/// cutting a single mould. The comparison is the reason this exists at all: a change to the parting
/// split is only acceptable if no body got worse, and that is a claim about all eight bodies and all
/// three sweeps at once, which nobody can hold in their head from two sets of log lines.
/// </para>
/// </summary>
internal static class PartingScorecardReport
{
    private static readonly string[] Sweeps = ["MouldLoft", "SurfaceSweep", "SkinBisector"];

    private static string Short(string sweep) => sweep switch
    {
        "MouldLoft" => "Loft",
        "SurfaceSweep" => "Sweep",
        "SkinBisector" => "Bisector",
        _ => sweep,
    };

    /// <summary>The smallest share of the mould a piece may be and still count as a half.</summary>
    public const double LeastHalfSharePct = 5.0;

    /// <summary>What a run's gates add up to for one body and sweep.</summary>
    /// <param name="Refusals">Reasons the split must not be returned at all.</param>
    /// <param name="Warnings">Reasons it may be returned, but not without saying so.</param>
    public sealed record Verdict(IReadOnlyList<string> Refusals, IReadOnlyList<string> Warnings)
    {
        public string Word => Refusals.Count > 0 ? "REFUSE" : Warnings.Count > 0 ? "WARN" : "PASS";

        public string Reasons => string.Join("; ", Refusals.Concat(Warnings));
    }

    /// <summary>
    /// Sorts one sweep's measurements into refusals and warnings.
    ///
    /// <para>
    /// The split is the policy settled for this work, not something the measurements imply. A split
    /// that did not sever, left specks, came back open, lost volume, changed the cavity, breached its
    /// seal or was cut with a cutter that crosses itself or is not a clean closed solid is refused. A flange that
    /// loops back - not single-valued over the parting plane, or halves that foul when pulled - is a
    /// warning: on larynx_large nothing parts the mould without one, and a mould that has to be
    /// worked apart is still more use than none.
    /// </para>
    /// </summary>
    public static Verdict Judge(JsonNode? sweep)
    {
        var refusals = new List<string>();
        var warnings = new List<string>();

        if (sweep?["pieces"] is null)
        {
            refusals.Add(Text(sweep, "outcome") is { Length: > 1 } outcome ? outcome : Text(sweep, "threw"));
            return new Verdict(refusals, warnings);
        }

        int substantial = (int)(Number(sweep, "pieces", "substantial") ?? 0);
        var shares = Numbers(sweep, "pieces", "sharesPct");
        if (substantial != 2) refusals.Add($"{substantial} piece(s)");
        else if (shares.Any(share => share < LeastHalfSharePct)) refusals.Add($"a half is {shares.Min():0.#}% of the mould");

        if ((Number(sweep, "pieces", "specksAfterClearing") ?? 0) > 0) refusals.Add("specks");
        if (!All(sweep, "pieces", "watertight") || !All(sweep, "pieces", "manifold")) refusals.Add("a half is not watertight and manifold");
        if (Flag(sweep, "seal", "applies") == true && (Number(sweep, "seal", "breached") ?? 0) > 0) refusals.Add("seal breached");
        if ((Number(sweep, "cutter", "selfIntersections") ?? 0) > 0) refusals.Add("cutter crosses itself");
        if (Flag(sweep, "cutter", "watertight") == false) refusals.Add("cutter not a clean solid");
        if (Math.Abs(Number(sweep, "volume", "errorMm3") ?? double.NaN) is not < 1.0) refusals.Add("volume not conserved");
        if (Flag(sweep, "unchanged", "passes") != true) refusals.Add("mould surface changed");

        if (Flag(sweep, "singleValued", "passes") != true) warnings.Add("flange doubles back");
        if (Flag(sweep, "pullApart", "passes") != true) warnings.Add("halves foul when pulled");

        return new Verdict(refusals, warnings);
    }

    // ==================================================================== one run

    /// <summary>Writes <c>scorecard.md</c> from every body file in <paramref name="directory"/>.</summary>
    public static void WriteSummary(string directory)
    {
        var cards = Load(directory);
        if (cards.Count == 0) return;

        var md = new StringBuilder();
        md.AppendLine("# Parting split scorecard").AppendLine();
        md.AppendLine("Every body in tests/files/3mf against each sweep the view offers, under the view's own recipe.");
        md.AppendLine("Slopes and dihedrals are over the mating face: the flange outside the body and inside the mould.");

        md.AppendLine().AppendLine("## Parting line").AppendLine();
        Row(md, "body", "loops / wall rims", "points", "turn max / RMS °", "steer max / RMS °", "height range mm",
            "steepest climb °", "off-centre median / max", "% past 0.25", "crease-to-line ÷ wall", "back-steps", "footprint crossings");
        Rule(md, 12);
        foreach (var (body, card) in cards)
        {
            var line = card["line"];
            if (line?["perLoop"] is not JsonArray loops)
            {
                Row(md, body, "line failed: " + Text(line, "error"));
                continue;
            }

            for (int i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];
                Row(md, i == 0 ? body : $"↳ loop {i}",
                    $"{Text(line, "loops")} / {Text(line, "wallRims")}", Text(loop, "points"),
                    $"{F1(loop, "turnMaxDeg")} / {F1(loop, "turnRmsDeg")}", $"{F1(loop, "steerMaxDeg")} / {F1(loop, "steerRmsDeg")}",
                    F1(loop, "heightRangeMm"), F0(loop, "steepestClimbDeg"),
                    $"{F2(loop, "offCentreMedian")} / {F2(loop, "offCentreMax")}", F1(loop, "pctBeyondQuarter"),
                    F2(loop, "creaseToLineOverWall"), Text(loop, "backwardSteps"), Text(loop, "footprintCrossings"));
            }
        }

        md.AppendLine().AppendLine("## Line consistency: median / max distance from the first trace, mm").AppendLine();
        string[] retraces = ["runTwice", "openedAgain", "savedAndReopened", "rotated30", "rotated60", "rotated90"];
        Row(md, "body", "run twice", "opened again", "saved and reopened", "turned 30°", "turned 60°", "turned 90°");
        Rule(md, 7);
        foreach (var (body, card) in cards)
            Row(md, [body, .. retraces.Select(name => Retrace(card["line"]?["consistency"]?[name]))]);

        md.AppendLine().AppendLine("## Flange flatness (whole flange in brackets)").AppendLine();
        Row(md, "body", "sweep", "median °", "p90 °", "max °", "% area past 45°", "steep area by cause, mm²");
        Rule(md, 7);
        Each(cards, (body, sweep, result) =>
        {
            if (result["slopeMating"] is null) { Row(md, body, Short(sweep), Judge(result).Reasons); return; }

            string Pair(string key) => $"{F1(result["slopeMating"], key)} ({F1(result["slopeWhole"], key)})";
            string causes = result["steep"]?["areaByCauseMm2"] is JsonObject byCause
                ? string.Join(", ", byCause.Select(cause => $"{cause.Key} {cause.Value}"))
                : "";
            Row(md, body, Short(sweep), Pair("medianDeg"), Pair("p90Deg"), Pair("maxDeg"), Pair("pctPast45"), causes);
        });

        md.AppendLine().AppendLine("## Flange smoothness: dihedral between neighbouring mating faces, median / p99 / max °").AppendLine();
        Row(md, "body", "sweep", "ripple (faces side by side round the loop)", "bend (one face further out than the other)");
        Rule(md, 4);
        Each(cards, (body, sweep, result) =>
        {
            if (result["dihedral"] is null) return;

            string Triple(string family) =>
                $"{F1(result["dihedral"]![family], "medianDeg")} / {F1(result["dihedral"]![family], "p99Deg")} / {F1(result["dihedral"]![family], "maxDeg")}";
            Row(md, body, Short(sweep), Triple("rippleAcrossRadialEdges"), Triple("bendAcrossLoopEdges"));
        });

        md.AppendLine().AppendLine("## Gates").AppendLine();
        Row(md, "body", "sweep", "pieces, % of mould", "specks first cut → cleared", "halves watertight and manifold", "seal breached",
            "cutter crossings", "cutter a clean solid", "volume error mm³", "mould surface unchanged", "turned over mm²", "doubled over mm²",
            "overlap when pulled mm³", "verdict", "Apply returns");
        Rule(md, 15);
        Each(cards, (body, sweep, result) =>
        {
            var verdict = Judge(result);
            if (result["pieces"] is null) { Row(md, body, Short(sweep), verdict.Reasons); return; }

            bool sealApplies = Flag(result, "seal", "applies") == true;
            Row(md, body, Short(sweep),
                $"{Text(result, "pieces", "substantial")} ({string.Join(" / ", Numbers(result, "pieces", "sharesPct").Select(share => share.ToString("0.#", CultureInfo.InvariantCulture)))})",
                $"{Text(result, "pieces", "specksFirstCut")} → {Text(result, "pieces", "specksAfterClearing")}",
                Tick(All(result, "pieces", "watertight") && All(result, "pieces", "manifold")),
                sealApplies ? Text(result, "seal", "breached") : "n/a",
                Text(result, "cutter", "selfIntersections"), Tick(Flag(result, "cutter", "watertight") == true),
                F2(result["volume"], "errorMm3"), Tick(Flag(result, "unchanged", "passes") == true),
                F1(result["singleValued"], "againstAxisMm2"), F1(result["singleValued"], "doubledOverMm2"),
                result["pullApart"]?["worstMm3"] is null ? Text(result, "pullApart", "reason") : F1(result["pullApart"], "worstMm3"),
                verdict.Word, Applied(result));
        });

        md.AppendLine().AppendLine("## Where the verdict and what Apply returns disagree, and why each fails").AppendLine();
        Each(cards, (body, sweep, result) =>
        {
            var verdict = Judge(result);
            if (verdict.Word == "PASS") return;
            md.AppendLine($"- **{body} / {Short(sweep)}** - {verdict.Word}: {verdict.Reasons}. Apply: {Applied(result)}. " +
                          $"Best other direction leaves {F1(result["bestPull"], "againstMm2")} mm² facing away, {F1(result["bestPull"], "tiltFromAxisDeg")}° off the axis.");
        });

        md.AppendLine().AppendLine("## Timing, ms").AppendLine();
        Row(md, ["body", "trace", .. Sweeps.Select(sweep => $"{Short(sweep)} surface / cut and gates")]);
        Rule(md, 2 + Sweeps.Length);
        foreach (var (body, card) in cards)
            Row(md, [body, Text(card["line"], "traceMs"),
                .. Sweeps.Select(sweep => $"{Text(card["sweeps"]?[sweep], "surfaceMs")} / {Text(card["sweeps"]?[sweep], "cutMs")}")]);

        File.WriteAllText(Path.Combine(directory, "scorecard.md"), md.ToString());
    }

    // ==================================================================== two runs

    /// <summary>
    /// The same tables for two runs side by side, a cell reading <c>before → after</c> wherever the two
    /// differ and marked better or worse. Every quantity compared is one where less is better, so the
    /// mark needs no per-column rule.
    /// </summary>
    public static string Compare(string beforeDirectory, string afterDirectory)
    {
        var before = Load(beforeDirectory).ToDictionary(card => card.Body, card => card.Card);
        var after = Load(afterDirectory);

        var md = new StringBuilder();
        md.AppendLine("# Parting split scorecard: before → after").AppendLine();
        md.AppendLine("A cell shows one number where nothing moved. ▼ is better, ▲ is worse.");

        md.AppendLine().AppendLine("## Gates and flange").AppendLine();
        Row(md, "body", "sweep", "verdict", "Apply returns", "slope median °", "slope p90 °", "slope max °", "% past 45°",
            "ripple p99 °", "bend p99 °", "turned over mm²", "doubled over mm²", "overlap when pulled mm³", "cutter crossings");
        Rule(md, 14);

        int better = 0, worse = 0;
        foreach (var (body, card) in after)
        {
            before.TryGetValue(body, out var old);
            foreach (string sweep in Sweeps)
            {
                var now = card["sweeps"]?[sweep];
                var was = old?["sweeps"]?[sweep];
                if (now is null && was is null) continue;

                string Moved(Func<JsonNode?, double?> read) => Delta(read(was), read(now), ref better, ref worse);

                Row(md, body, Short(sweep),
                    Changed(Judge(was).Word, Judge(now).Word), Changed(Applied(was), Applied(now)),
                    Moved(n => Number(n, "slopeMating", "medianDeg")), Moved(n => Number(n, "slopeMating", "p90Deg")),
                    Moved(n => Number(n, "slopeMating", "maxDeg")), Moved(n => Number(n, "slopeMating", "pctPast45")),
                    Moved(n => Number(n, "dihedral", "rippleAcrossRadialEdges", "p99Deg")),
                    Moved(n => Number(n, "dihedral", "bendAcrossLoopEdges", "p99Deg")),
                    Moved(n => Number(n, "singleValued", "againstAxisMm2")), Moved(n => Number(n, "singleValued", "doubledOverMm2")),
                    Moved(n => Number(n, "pullApart", "worstMm3")), Moved(n => Number(n, "cutter", "selfIntersections")));
            }
        }

        md.AppendLine().AppendLine("## Parting line").AppendLine();
        Row(md, "body", "loop", "points", "steer max °", "steer RMS °", "turn RMS °", "height range mm", "off-centre max",
            "back-steps", "footprint crossings", "turned 30° max mm", "turned 60° max mm");
        Rule(md, 12);
        foreach (var (body, card) in after)
        {
            before.TryGetValue(body, out var old);
            if (card["line"]?["perLoop"] is not JsonArray loops) continue;

            for (int i = 0; i < loops.Count; i++)
            {
                var now = loops[i];
                var was = old?["line"]?["perLoop"] is JsonArray oldLoops && i < oldLoops.Count ? oldLoops[i] : null;
                var nowTurned = card["line"]?["consistency"];
                var wasTurned = old?["line"]?["consistency"];

                string Moved(string key) => Delta(Number(was, key), Number(now, key), ref better, ref worse);
                string Turned(string key) => i > 0 ? "" : Delta(Number(wasTurned, key, "maxMm"), Number(nowTurned, key, "maxMm"), ref better, ref worse);

                Row(md, body, i.ToString(CultureInfo.InvariantCulture), Changed(Text(was, "points"), Text(now, "points")),
                    Moved("steerMaxDeg"), Moved("steerRmsDeg"), Moved("turnRmsDeg"), Moved("heightRangeMm"), Moved("offCentreMax"),
                    Moved("backwardSteps"), Moved("footprintCrossings"), Turned("rotated30"), Turned("rotated60"));
            }
        }

        md.AppendLine().AppendLine($"{better} cell(s) better, {worse} worse.");
        return md.ToString();
    }

    // ==================================================================== helpers

    private static List<(string Body, JsonNode Card)> Load(string directory)
    {
        var cards = new List<(string, JsonNode)>();
        if (!Directory.Exists(directory)) return cards;

        foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            // Another row of the theory may be part-way through writing its own file.
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is { } card && card["body"] is not null)
                    cards.Add((card["body"]!.GetValue<string>(), card));
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
            {
            }
        }

        return cards;
    }

    private static void Each(List<(string Body, JsonNode Card)> cards, Action<string, string, JsonNode> visit)
    {
        foreach (var (body, card) in cards)
            foreach (string sweep in Sweeps)
                if (card["sweeps"]?[sweep] is { } result) visit(body, sweep, result);
    }

    private static string Applied(JsonNode? sweep)
    {
        var applied = sweep?["applied"];
        if (applied is null) return "-";
        if (Flag(applied, "succeeded") != true)
            return applied["code"] is null ? "error" : $"refused: {Text(applied, "code")}";

        string shares = string.Join(" / ", Numbers(applied, "componentSharesPct").Take(3)
            .Select(share => share.ToString("0.#", CultureInfo.InvariantCulture)));
        string warned = applied["warnings"] is JsonArray { Count: > 0 } warnings ? $", {warnings.Count} warning(s)" : "";
        return $"split {shares}{warned}";
    }

    private static string Retrace(JsonNode? retrace) =>
        retrace is null ? "-"
        : retrace["error"] is not null ? "failed"
        : $"{F3(retrace, "medianMm")} / {F3(retrace, "maxMm")}" + (Flag(retrace, "sameLoops") == false ? " (loop count differs)" : "");

    private static string Changed(string before, string after) => before == after || before == "-" ? after : $"{before} → {after}";

    private static string Delta(double? before, double? after, ref int better, ref int worse)
    {
        if (after is not { } now || !double.IsFinite(now)) return before is null ? "-" : $"{Fmt(before.Value)} → -";
        if (before is not { } was || !double.IsFinite(was)) return Fmt(now);

        // Under a tenth of a unit is the run-to-run noise of a boolean or a percentile, not a change.
        if (Math.Abs(now - was) < 0.1) return Fmt(now);

        if (now < was) better++; else worse++;
        return $"{Fmt(was)} → {Fmt(now)} {(now < was ? "▼" : "▲")}";
    }

    private static string Fmt(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static JsonNode? At(JsonNode? node, params string[] path)
    {
        foreach (string key in path)
        {
            if (node is not JsonObject) return null;
            node = node[key];
        }
        return node;
    }

    private static double? Number(JsonNode? node, params string[] path)
    {
        if (At(node, path) is not JsonValue value) return null;
        if (value.TryGetValue(out double number)) return number;
        return value.TryGetValue(out string? text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static List<double> Numbers(JsonNode? node, params string[] path) =>
        At(node, path) is JsonArray array
            ? array.Select(item => item is JsonValue value && value.TryGetValue(out double number) ? number : double.NaN).ToList()
            : [];

    private static bool? Flag(JsonNode? node, params string[] path) =>
        At(node, path) is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;

    private static bool All(JsonNode? node, params string[] path) =>
        At(node, path) is JsonArray { Count: > 0 } array && array.All(item => item?.GetValue<bool>() == true);

    private static string Text(JsonNode? node, params string[] path) => At(node, path)?.ToString() ?? "-";

    private static string Format(JsonNode? node, string key, string format)
    {
        if (Number(node, key) is not { } number || !double.IsFinite(number)) return "-";

        // A hair under zero rounds to "-0", which reads as a sign where there is none.
        string text = number.ToString(format, CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    private static string F0(JsonNode? node, string key) => Format(node, key, "0");

    private static string F1(JsonNode? node, string key) => Format(node, key, "0.#");

    private static string F2(JsonNode? node, string key) => Format(node, key, "0.##");

    private static string F3(JsonNode? node, string key) => Format(node, key, "0.000");

    private static string Tick(bool ok) => ok ? "yes" : "NO";

    private static void Row(StringBuilder md, params string[] cells) => md.Append("| ").AppendJoin(" | ", cells).AppendLine(" |");

    private static void Rule(StringBuilder md, int columns) => md.Append('|').Insert(md.Length, "---|", columns).AppendLine();
}
