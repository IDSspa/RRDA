using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Web.Security;
using RRDA.Data;
using System.Globalization;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public class StatisticsController(RRDADbContext db) : Controller
    {
        public IActionResult Subject(int reportTypeId)
        {
            ViewBag.ReportTypeId = reportTypeId;
            return View();
        }

        public async Task<IActionResult> Distribution(int reportTypeId, string field, int? batchId, DateTime? uploadedFrom, DateTime? uploadedTo)
        {
            if (string.IsNullOrWhiteSpace(field)) return BadRequest("Field is required.");

            var filesQuery = db.ReportFiles.AsNoTracking().Where(f => f.ReportTypeId == reportTypeId);
            if (batchId.HasValue) filesQuery = filesQuery.Where(f => f.ReportBatchId == batchId.Value);
            if (uploadedFrom.HasValue) filesQuery = filesQuery.Where(f => f.UploadedAt >= uploadedFrom.Value);
            if (uploadedTo.HasValue) filesQuery = filesQuery.Where(f => f.UploadedAt <= uploadedTo.Value);

            var fileIds = await filesQuery.Select(f => f.Id).ToListAsync();
            var rawValues = fileIds.Count == 0 ? [] : await db.ReportEntities
                .AsNoTracking()
                .Where(e => fileIds.Contains(e.ReportFileId) && e.Key == field)
                .SelectMany(e => e.Properties.Where(p => p.Name == "value").Select(p => p.Value))
                .ToListAsync();

            var values = rawValues
                .Select(v => TryParse(v))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            var model = BuildHistogramModel(reportTypeId, field, values, batchId, uploadedFrom, uploadedTo);
            return View(model);
        }

        private static double? TryParse(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) return x;
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.CurrentCulture, out x)) return x;
            return null;
        }

        private static DistributionViewModel BuildHistogramModel(int reportTypeId, string field, List<double> values, int? batchId, DateTime? uploadedFrom, DateTime? uploadedTo)
        {
            var model = new DistributionViewModel
            {
                ReportTypeId = reportTypeId,
                Field = field,
                BatchId = batchId,
                UploadedFrom = uploadedFrom,
                UploadedTo = uploadedTo,
                Samples = values.Count
            };

            if (values.Count == 0) return model;

            var min = values.Min();
            var max = values.Max();
            var bins = 10;
            var width = Math.Abs(max - min) < double.Epsilon ? 1d : (max - min) / bins;

            for (int i = 0; i < bins; i++)
            {
                var from = min + i * width;
                var to = i == bins - 1 ? max : from + width;
                var count = i == bins - 1
                    ? values.Count(v => v >= from && v <= to)
                    : values.Count(v => v >= from && v < to);

                model.Bins.Add(new HistogramBin { From = from, To = to, Count = count });
            }

            return model;
        }
    }

    public class DistributionViewModel
    {
        public int ReportTypeId { get; set; }
        public string Field { get; set; } = string.Empty;
        public int? BatchId { get; set; }
        public DateTime? UploadedFrom { get; set; }
        public DateTime? UploadedTo { get; set; }
        public int Samples { get; set; }
        public List<HistogramBin> Bins { get; set; } = [];
    }

    public class HistogramBin
    {
        public double From { get; set; }
        public double To { get; set; }
        public int Count { get; set; }
    }
}
