namespace RRDA.Core
{
    public record ImportProgress(int Current, int Total, string Message)
    {
        public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;
    }
}
