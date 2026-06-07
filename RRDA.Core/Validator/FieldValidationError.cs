namespace RRDA.Core.Validator
{
    public class FieldValidationError
    {
        public required string Field { get; set; }
        public required string Message { get; set; }
        public required string Severity { get; set; }
    }
}
