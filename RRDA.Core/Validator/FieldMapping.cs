namespace RRDA.Core.Validator
{
    public class FieldMapping
    {
        public string DefinedName { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;

        // Metadata UI/analytics (opzionali)
        public bool? Filterable { get; set; }
        public bool? VisibleInPivot { get; set; }
        public bool? StatEnabled { get; set; }
        public string? StatMenu { get; set; }
        public string? UiDataType { get; set; }
    }
}
