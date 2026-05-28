namespace RRDA.Web.Areas.Data.Controllers
{
    // ViewModels

    public class TabularPreviewRow
    {
        public int EntityId { get; set; }
        public string EntityKey { get; set; } = string.Empty;
        public string ReportSheet { get; set; } = string.Empty;
        public int PropertiesCount { get; set; }
    }
}
