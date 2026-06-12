using RRDA.Core.Validator;
using System.Text;
using Xunit;

namespace RRDA.Core.Tests;

public sealed class ValidationConfigTests
{
    [Fact]
    public void Load_ValidatesAndReadsRootSubjectKeyField()
    {
        using var stream = Xml("""
            <ValidationConfig subjectKeyField="Serial" culture="it-IT">
              <FieldRules>
                <Field definedName="Serial" type="int" required="true" />
              </FieldRules>
            </ValidationConfig>
            """);

        var config = ValidationConfig.Load(stream);

        Assert.Equal("Serial", config.SubjectKeyField);
        Assert.Equal("it-IT", config.Culture.Name);
    }

    [Fact]
    public void Load_RejectsSubjectKeyFieldOnFieldRules()
    {
        using var stream = Xml("""
            <ValidationConfig>
              <FieldRules subjectKeyField="Serial">
                <Field definedName="Serial" type="int" />
              </FieldRules>
            </ValidationConfig>
            """);

        var exception = Assert.Throws<InvalidDataException>(() => ValidationConfig.Load(stream));

        Assert.Contains("ValidationConfig", exception.Message);
    }

    [Fact]
    public void Load_RejectsMalformedXml()
    {
        using var stream = Xml("<ValidationConfig>");

        Assert.Throws<InvalidDataException>(() => ValidationConfig.Load(stream));
    }

    [Fact]
    public void Load_ReadsReferenceMetadata()
    {
        using var stream = Xml("""
            <ValidationConfig subjectKeyField="Serial">
              <FieldRules>
                <Field definedName="SN_ALI"
                       referenceReportType="ALI"
                       referenceKeyField="Serial" />
              </FieldRules>
            </ValidationConfig>
            """);

        var rule = Assert.Single(ValidationConfig.Load(stream).FieldRules);

        Assert.Equal("ALI", rule.ReferenceReportType);
        Assert.Equal("Serial", rule.ReferenceKeyField);
    }

    [Fact]
    public void Load_RejectsIncompleteReferenceMetadata()
    {
        using var stream = Xml("""
            <ValidationConfig subjectKeyField="Serial">
              <FieldRules>
                <Field definedName="SN_ALI" referenceReportType="ALI" />
              </FieldRules>
            </ValidationConfig>
            """);

        var exception = Assert.Throws<InvalidDataException>(() => ValidationConfig.Load(stream));

        Assert.Contains("entrambi", exception.Message);
    }

    [Fact]
    public void Load_RejectsDtd()
    {
        using var stream = Xml("""
            <!DOCTYPE ValidationConfig [<!ENTITY xxe SYSTEM "file:///does-not-exist">]>
            <ValidationConfig subjectKeyField="Serial">
              <FieldRules>
                <Field definedName="Serial" />
              </FieldRules>
            </ValidationConfig>
            """);

        Assert.Throws<InvalidDataException>(() => ValidationConfig.Load(stream));
    }

    private static MemoryStream Xml(string value) =>
        new(Encoding.UTF8.GetBytes(value));
}
