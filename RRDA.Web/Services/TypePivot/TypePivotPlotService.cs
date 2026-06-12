using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Areas.Data.Controllers;
using System.Globalization;

namespace RRDA.Web.Services.TypePivot;

public sealed class TypePivotPlotService(
    RRDADbContext db,
    ITypePivotDatasetService datasetService) : ITypePivotPlotService
{
    public async Task<TypePivotPlotResult> BuildAsync(
        TypePivotPlotRequest request,
        CancellationToken cancellationToken = default)
    {
        var dataset = await datasetService.GetAsync(request.Filter, cancellationToken);
        if (dataset is null)
            return new(TypePivotPlotStatus.NotFound, null);

        var metadata = dataset.Metadata;
        var allowedSeriesFields = metadata.VisibleHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedSeriesFields = request.SeriesFields
            .Where(f => !string.IsNullOrWhiteSpace(f) && allowedSeriesFields.Contains(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedSeriesFields.Count == 0)
        {
            return new(TypePivotPlotStatus.BadRequest, new
            {
                message = "Selezionare almeno una grandezza numerica valida.",
                availableSeriesFields = metadata.VisibleHeaders
            });
        }

        return string.Equals(request.ChartType, "pdf", StringComparison.OrdinalIgnoreCase)
            ? await BuildPdfAsync(dataset, selectedSeriesFields, request.SelectedFileIds, cancellationToken)
            : await BuildScatterAsync(dataset, selectedSeriesFields, request.XField, request.SelectedFileIds, cancellationToken);
    }

    private async Task<TypePivotPlotResult> BuildScatterAsync(
        TypePivotDataset dataset,
        List<string> selectedSeriesFields,
        string? xField,
        IReadOnlyCollection<int> selectedFileIds,
        CancellationToken cancellationToken)
    {
        var selectedFileIdSet = selectedFileIds.ToHashSet();
        var plotFileIds = selectedFileIdSet.Count == 0
            ? dataset.FileIds
            : [.. dataset.FileIds.Where(selectedFileIdSet.Contains)];
        var files = await LoadFilesAsync(plotFileIds, cancellationToken);
        var fileOrder = dataset.FileIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);
        files = [.. files.OrderBy(f => fileOrder.TryGetValue(f.Id, out var index) ? index : int.MaxValue)];

        var fileIds = files.Select(f => f.Id).ToList();
        var pairs = await LoadPairsAsync(fileIds, selectedSeriesFields, cancellationToken);
        var subjectKeysByFileId = BuildSubjectKeys(pairs);
        var measureValuesByFileId = pairs
            .Where(p => !p.IsSubjectKey)
            .GroupBy(p => p.FileId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        keyGroup => keyGroup.Key,
                        keyGroup => keyGroup.LastOrDefault()?.Value,
                        StringComparer.OrdinalIgnoreCase));

        var metadata = dataset.Metadata;
        var resolvedXField = ResolveXAxisField(xField, metadata.HasSubjectKey);
        var markerSymbols = new[]
        {
            "circle", "square", "diamond", "cross", "x",
            "triangle-up", "triangle-down", "star"
        };
        var traces = selectedSeriesFields.Select((field, index) =>
        {
            var xValues = new List<object?>();
            var yValues = new List<double?>();
            var subjectKeyValues = new List<object?>();

            foreach (var file in files)
            {
                if (!measureValuesByFileId.TryGetValue(file.Id, out var values)
                    || !values.TryGetValue(field, out var rawY)
                    || TryParseDouble(rawY) is not double y)
                    continue;

                xValues.Add(GetXAxisValue(resolvedXField, file, dataset.BatchNames, subjectKeysByFileId));
                yValues.Add(y);
                subjectKeyValues.Add(subjectKeysByFileId.TryGetValue(file.Id, out var subjectKey)
                    ? FormatSubjectKeyValue(subjectKey)
                    : null);
            }

            return new
            {
                name = FormatHeaderLabel(field, metadata.HeaderUnits),
                type = "scatter",
                mode = "markers",
                marker = new { symbol = markerSymbols[index % markerSymbols.Length], size = 8 },
                x = xValues,
                y = yValues,
                customdata = subjectKeyValues,
                hovertemplate = $"{metadata.SubjectKeyLabel}: %{{customdata}}<br>X: %{{x}}<br>Y: %{{y}}<extra>%{{fullData.name}}</extra>"
            };
        }).ToList();

        return new(TypePivotPlotStatus.Success, new
        {
            reportTypeId = dataset.ReportType.Id,
            reportTypeKey = dataset.ReportType.Key,
            rowCount = files.Count,
            selectedFileIds = fileIds,
            availableXFields = metadata.PlotXAxisFields,
            availableSeriesFields = metadata.VisibleHeaders,
            layout = new
            {
                title = $"Scatter - {dataset.ReportType.Key}",
                xaxis = new { title = new { text = GetXAxisTitle(resolvedXField, metadata.SubjectKeyLabel) } },
                yaxis = new { title = new { text = GetYAxisTitle(selectedSeriesFields, metadata.HeaderUnits) } },
                hovermode = "closest"
            },
            traces
        });
    }

    private async Task<TypePivotPlotResult> BuildPdfAsync(
        TypePivotDataset dataset,
        List<string> selectedSeriesFields,
        IReadOnlyCollection<int> selectedFileIds,
        CancellationToken cancellationToken)
    {
        var pairs = await LoadPairsAsync(dataset.FileIds, selectedSeriesFields, cancellationToken);
        var subjectKeysByFileId = BuildSubjectKeys(pairs);
        var selectedFileIdSet = selectedFileIds.ToHashSet();
        var colors = new[] { "#1f77b4", "#d62728", "#2ca02c", "#ff7f0e", "#9467bd", "#17becf", "#8c564b", "#e377c2" };
        var traces = new List<object>();
        var skippedFields = new List<string>();
        var maxDensity = 0d;

        foreach (var field in selectedSeriesFields)
        {
            var color = colors[selectedSeriesFields.IndexOf(field) % colors.Length];
            var valuesByFileId = pairs
                .Where(p => !p.IsSubjectKey && string.Equals(p.Key, field, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => p.FileId)
                .Select(g => new PdfSample(g.Key, TryParseDouble(g.LastOrDefault()?.Value)))
                .Where(x => x.Value.HasValue)
                .Select(x => new PdfSample(x.FileId, x.Value!.Value))
                .OrderBy(x => x.Value)
                .ToList();

            if (valuesByFileId.Count < 2)
            {
                skippedFields.Add(field);
                continue;
            }

            var values = valuesByFileId.Select(x => x.Value!.Value).ToList();
            var mean = values.Average();
            var variance = values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1);
            var stdDev = Math.Sqrt(variance);
            if (stdDev <= 0)
            {
                skippedFields.Add(field);
                continue;
            }

            var min = values.Min();
            var max = values.Max();
            var binWidth = stdDev / 2;
            var binStart = mean - Math.Ceiling((mean - min) / binWidth) * binWidth;
            var binEnd = mean + Math.Ceiling((max - mean) / binWidth) * binWidth;
            var curveStart = Math.Min(min, mean - 4 * stdDev);
            var curveEnd = Math.Max(max, mean + 4 * stdDev);
            var curveStep = (curveEnd - curveStart) / 160;
            if (curveStep <= 0)
                curveStep = stdDev / 20;

            var curveX = new List<double>();
            var curveY = new List<double>();
            for (var x = curveStart; x <= curveEnd; x += curveStep)
            {
                curveX.Add(x);
                curveY.Add(NormalPdf(x, mean, stdDev));
            }

            var histogramBins = BuildHistogramBins(
                valuesByFileId,
                subjectKeysByFileId,
                dataset.Metadata.SubjectKeyLabel,
                binStart,
                binEnd + binWidth,
                binWidth);
            var selectedValues = valuesByFileId
                .Where(x => selectedFileIdSet.Contains(x.FileId))
                .Select(x => new
                {
                    x.FileId,
                    Value = x.Value!.Value,
                    Density = NormalPdf(x.Value.Value, mean, stdDev),
                    SubjectKey = subjectKeysByFileId.TryGetValue(x.FileId, out var subjectKey)
                        ? FormatSubjectKeyValue(subjectKey)
                        : null
                })
                .ToList();
            var seriesMaxDensity = Math.Max(
                selectedValues.Count == 0 || curveY.Count == 0 ? 0 : curveY.Max(),
                histogramBins.Count == 0 ? 0 : histogramBins.Max(b => b.Density));
            maxDensity = Math.Max(maxDensity, seriesMaxDensity);
            var displayName = FormatHeaderLabel(field, dataset.Metadata.HeaderUnits);

            traces.Add(new
            {
                name = $"{displayName} istogramma",
                type = "bar",
                opacity = 0.45,
                x = histogramBins.Select(b => b.Center).ToList(),
                y = histogramBins.Select(b => b.Density).ToList(),
                width = histogramBins.Select(_ => binWidth).ToList(),
                customdata = histogramBins.Select(b => new object[] { b.RangeLabel, b.Count, b.SubjectKeysSummary }).ToList(),
                marker = new { color = ToRgba(color, 0.55), line = new { color, width = 1 } },
                hovertemplate = $"Intervallo: %{{customdata[0]}}<br>Campioni: %{{customdata[1]}}<br>{dataset.Metadata.SubjectKeyLabel}: %{{customdata[2]}}<br>Densita: %{{y}}<extra>%{{fullData.name}}</extra>"
            });

            var lineHeight = seriesMaxDensity * 1.1;
            traces.Add(BuildVerticalLine($"{displayName} media", mean, lineHeight, color, 3, null, $"Media: {mean:G6}"));
            foreach (var sigmaValue in new[] { mean - stdDev, mean + stdDev })
                traces.Add(BuildVerticalLine($"{displayName} sigma", sigmaValue, lineHeight, color, 2, "dash", $"Sigma: {sigmaValue:G6}"));

            if (selectedValues.Count > 0)
            {
                traces.Add(new
                {
                    name = $"{displayName} gaussiana",
                    type = "scatter",
                    mode = "lines",
                    x = curveX,
                    y = curveY,
                    line = new { color, width = 2 },
                    hovertemplate = "Valore: %{x}<br>Densita: %{y}<extra>%{fullData.name}</extra>"
                });
                traces.Add(new
                {
                    name = $"{displayName} selezionati",
                    type = "scatter",
                    mode = "markers",
                    x = selectedValues.Select(x => x.Value).ToList(),
                    y = selectedValues.Select(x => x.Density).ToList(),
                    customdata = selectedValues.Select(x => x.SubjectKey).ToList(),
                    marker = new { color, size = 10, symbol = "circle", line = new { color = "#000000", width = 1 } },
                    hovertemplate = $"{dataset.Metadata.SubjectKeyLabel}: %{{customdata}}<br>Valore: %{{x}}<br>Densita: %{{y}}<extra>%{{fullData.name}}</extra>"
                });
            }
        }

        if (traces.Count == 0)
        {
            return new(TypePivotPlotStatus.BadRequest, new
            {
                message = "Non ci sono abbastanza campioni numerici per generare la densita di probabilita.",
                skippedFields
            });
        }

        return new(TypePivotPlotStatus.Success, new
        {
            reportTypeId = dataset.ReportType.Id,
            reportTypeKey = dataset.ReportType.Key,
            chartType = "pdf",
            rowCount = dataset.FileIds.Count,
            skippedFields,
            layout = new
            {
                title = $"PDF - {dataset.ReportType.Key}",
                xaxis = new { title = new { text = GetYAxisTitle(selectedSeriesFields, dataset.Metadata.HeaderUnits) } },
                yaxis = new { title = new { text = "Probability Density Function" }, range = new[] { 0, maxDensity * 1.25 } },
                hovermode = "closest",
                barmode = "overlay"
            },
            traces
        });
    }

    private async Task<List<PlotFile>> LoadFilesAsync(List<int> fileIds, CancellationToken cancellationToken) =>
        fileIds.Count == 0 ? [] : await db.ReportFiles
            .AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .Select(f => new PlotFile(f.Id, f.FileName, f.FileLastModify, f.ReportBatchId))
            .ToListAsync(cancellationToken);

    private async Task<List<PivotPair>> LoadPairsAsync(
        List<int> fileIds,
        List<string> seriesFields,
        CancellationToken cancellationToken) =>
        fileIds.Count == 0 ? [] : await db.ReportEntities
            .AsNoTracking()
            .Where(e => fileIds.Contains(e.ReportFileId))
            .SelectMany(e => e.Properties
                .Where(p => p.Name == "value" && (p.IsSubjectKey || seriesFields.Contains(e.Key)))
                .Select(p => new PivotPair
                {
                    FileId = e.ReportFileId,
                    Key = e.Key,
                    Value = p.Value,
                    IsSubjectKey = p.IsSubjectKey,
                    DataType = p.DataType,
                    Unit = p.Unit
                }))
            .ToListAsync(cancellationToken);

    private static Dictionary<int, string?> BuildSubjectKeys(List<PivotPair> pairs) =>
        pairs.Where(p => p.IsSubjectKey)
            .GroupBy(p => p.FileId)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Value);

    private static object BuildVerticalLine(
        string name, double value, double height, string color, int width, string? dash, string hover) =>
        new
        {
            name,
            type = "scatter",
            mode = "lines",
            x = new[] { value, value },
            y = new[] { 0, height },
            line = new { color, width, dash },
            showlegend = false,
            hovertemplate = $"{hover}<extra>{name}</extra>"
        };

    private static string ResolveXAxisField(string? xField, bool hasSubjectKey)
    {
        if (string.Equals(xField, "FileName", StringComparison.OrdinalIgnoreCase)) return "FileName";
        if (string.Equals(xField, "LastModified", StringComparison.OrdinalIgnoreCase)) return "LastModified";
        if (string.Equals(xField, "Batch", StringComparison.OrdinalIgnoreCase)) return "Batch";
        return hasSubjectKey ? "SubjectKey" : "FileName";
    }

    private static object? GetXAxisValue(
        string xField,
        PlotFile file,
        Dictionary<int, string> batchNames,
        Dictionary<int, string?> subjectKeys) =>
        xField switch
        {
            "SubjectKey" => subjectKeys.TryGetValue(file.Id, out var key) ? FormatSubjectKeyValue(key) : null,
            "LastModified" => file.LastModified.ToString("O", CultureInfo.InvariantCulture),
            "Batch" => file.BatchId.HasValue && batchNames.TryGetValue(file.BatchId.Value, out var name) ? name : null,
            _ => file.FileName
        };

    private static object? FormatSubjectKeyValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;
        if (long.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var integerValue)) return integerValue;
        if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue)) return doubleValue;
        return rawValue;
    }

    private static string GetXAxisTitle(string xField, string subjectKeyLabel) =>
        xField switch
        {
            "SubjectKey" => subjectKeyLabel,
            "LastModified" => "Ultima modifica",
            "Batch" => "Batch",
            _ => "Nome file"
        };

    internal static string FormatHeaderLabel(string header, Dictionary<string, string?> units) =>
        units.TryGetValue(header, out var unit) && !string.IsNullOrWhiteSpace(unit) ? $"{header} [{unit}]" : header;

    private static string GetYAxisTitle(List<string> fields, Dictionary<string, string?> units)
    {
        var distinctUnits = fields.Select(field => units.TryGetValue(field, out var unit) ? unit : null)
            .Where(unit => !string.IsNullOrWhiteSpace(unit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fields.Count == 1) return FormatHeaderLabel(fields[0], units);
        return distinctUnits.Count == 1 ? $"Valore [{distinctUnits[0]}]" : $"Valore [{string.Join(" / ", distinctUnits)}]";
    }

    private static double NormalPdf(double x, double mean, double standardDeviation)
    {
        var z = (x - mean) / standardDeviation;
        return Math.Exp(-0.5 * z * z) / (standardDeviation * Math.Sqrt(2 * Math.PI));
    }

    private static List<PdfHistogramBin> BuildHistogramBins(
        List<PdfSample> samples,
        Dictionary<int, string?> subjectKeys,
        string subjectKeyLabel,
        double start,
        double end,
        double binWidth)
    {
        var binCount = Math.Max(1, (int)Math.Ceiling((end - start) / binWidth));
        var bins = Enumerable.Range(0, binCount).Select(_ => new List<PdfSample>()).ToList();
        foreach (var sample in samples)
        {
            var index = Math.Min(binCount - 1, Math.Max(0, (int)Math.Floor((sample.Value!.Value - start) / binWidth)));
            bins[index].Add(sample);
        }

        return bins.Select((binSamples, index) =>
        {
            var binStart = start + index * binWidth;
            var binEnd = binStart + binWidth;
            var keys = binSamples
                .Select(sample => subjectKeys.TryGetValue(sample.FileId, out var key) ? FormatSubjectKeyValue(key)?.ToString() : null)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new PdfHistogramBin(
                binStart + binWidth / 2,
                binSamples.Count / (samples.Count * binWidth),
                binSamples.Count,
                FormattableString.Invariant($"{binStart:G6} - {binEnd:G6}"),
                FormatSubjectKeySummary(keys, subjectKeyLabel));
        }).ToList();
    }

    private static string FormatSubjectKeySummary(List<string?> keys, string label)
    {
        const int maxItems = 12;
        if (keys.Count == 0) return $"{label} non disponibile";
        var suffix = keys.Count > maxItems ? $" (+{keys.Count - maxItems})" : string.Empty;
        return $"{string.Join(", ", keys.Take(maxItems))}{suffix}";
    }

    private static string ToRgba(string hexColor, double alpha)
    {
        var value = hexColor.TrimStart('#');
        if (value.Length != 6) return hexColor;
        var red = int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = int.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = int.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return FormattableString.Invariant($"rgba({red},{green},{blue},{alpha})");
    }

    private static double? TryParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private sealed record PlotFile(int Id, string FileName, DateTime LastModified, int? BatchId);
    private sealed record PdfSample(int FileId, double? Value);
    private sealed record PdfHistogramBin(double Center, double Density, int Count, string RangeLabel, string SubjectKeysSummary);
}
