namespace RRDA.Plugins.Common
{
    public class RangeData
    {
        private string[][] _data = [];
        private int _row_count = 0;
        private int _column_count = 0;
        public int RowCount { get => _row_count; }
        public int ColumnCount { get => _column_count; }
        public string[] Rows => [.. _data.SelectMany(r => r)];
        public string[][] Data
        {
            get => _data;
            set
            {
                _data = value;
                _row_count = _data.Length;
                _column_count = _data.FirstOrDefault()?.Length ?? 0;
            }
        }
    }
}