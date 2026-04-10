namespace RRDA.Core.Validator
{
    public class RowValidationResult
    {
        public bool IsValid => !Errors.Any(e => e.Severity == "error");
        public List<FieldValidationError> Errors { get; set; } = [];
        public Dictionary<string, object> NormalizedValues { get; set; } = [];
    }
}
