namespace RRDA.Core.Validator
{
    public class RowRule
    {
        public string Name { get; set; } = "";
        public string ConditionExpression { get; set; } = ""; // espressione in DSL (vedi sotto)
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "error";
    }
}
