namespace RRDA.Web.Areas.Data.Controllers
{
    public class TypePivotColumnStatistics
    {
        public int Count { get; set; }
        public double? Mean { get; set; }
        public double? Median { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }   
        public double? StandardDeviation { get; set; }
    }
}
