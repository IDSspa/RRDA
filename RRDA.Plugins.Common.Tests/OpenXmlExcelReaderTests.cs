using DocumentFormat.OpenXml.Spreadsheet;
using RRDA.Core.Validator;
using Xunit;

namespace RRDA.Plugins.Common.Tests;

public sealed class OpenXmlExcelReaderTests
{
    [Theory]
    [InlineData("foglio!$B$3", "foglio", "B3")]
    [InlineData("'Foglio Uno'!$C$10", "Foglio Uno", "C10")]
    [InlineData("Sheet1!A1", "Sheet1", "A1")]
    [InlineData("Dati!$D$20", "Dati", "D20")]
    public void ParseCellCoordinate_ValidFormat_ReturnsSheetAndCell(
        string coordinate,
        string expectedSheet,
        string expectedCell)
    {
        var (sheet, cell) = OpenXmlExcelReader.ParseCellCoordinate(coordinate);

        Assert.Equal(expectedSheet, sheet);
        Assert.Equal(expectedCell.ToUpperInvariant(), cell);
    }

    [Theory]
    [InlineData("C3")]  // Nessun foglio
    [InlineData("Sheet!")]  // Foglio ma nessuna cella
    [InlineData("")]  // Vuoto
    [InlineData("   ")]  // Solo whitespace
    public void ParseCellCoordinate_InvalidFormat_ThrowsArgumentException(string coordinate)
    {
        if (string.IsNullOrWhiteSpace(coordinate))
        {
            Assert.Throws<ArgumentException>(() =>
                OpenXmlExcelReader.ParseCellCoordinate(coordinate));
        }
        else
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                OpenXmlExcelReader.ParseCellCoordinate(coordinate));
            Assert.Contains("non contiene il nome del foglio", ex.Message);
        }
    }

    [Fact]
    public void BuildDefinedNamesIndexWithDirectCells_EmptyWorkbook_ReturnsEmptyIndex()
    {
        var workbook = new Workbook();
        var cells = new List<CellMapping>();

        var index = OpenXmlExcelReader.BuildDefinedNamesIndexWithDirectCells(workbook, cells);

        Assert.Empty(index);
    }

    [Fact]
    public void BuildDefinedNamesIndexWithDirectCells_AddsVirtualEntries()
    {
        var workbook = new Workbook();
        var cells = new List<CellMapping>
        {
            new CellMapping
            {
                Coordinate = "Sheet1!$A$1",
                DefinedNameAlias = "CellA1"
            },
            new CellMapping
            {
                Coordinate = "Sheet1!$B$2",
                DefinedNameAlias = "CellB2"
            }
        };

        var index = OpenXmlExcelReader.BuildDefinedNamesIndexWithDirectCells(workbook, cells);

        Assert.Contains("CellA1", index.Keys);
        Assert.Contains("CellB2", index.Keys);
        Assert.Equal(("Sheet1", "A1"), index["CellA1"]);
        Assert.Equal(("Sheet1", "B2"), index["CellB2"]);
    }

    [Fact]
    public void BuildDefinedNamesIndexWithDirectCells_DoesNotOverwriteRealDefinedNames()
    {
        var workbook = new Workbook();
        var definedNames = new DefinedNames(
            new DefinedName
            {
                Name = "RealName",
                Text = "Sheet1!$C$1"
            }
        );
        workbook.DefinedNames = definedNames;

        var cells = new List<CellMapping>
        {
            new CellMapping
            {
                Coordinate = "Sheet1!$A$1",
                DefinedNameAlias = "RealName"  // Tentativo di sovrascrivere
            }
        };

        var index = OpenXmlExcelReader.BuildDefinedNamesIndexWithDirectCells(workbook, cells);

        // Deve mantenere il DefinedName reale, non sovrascrivere
        Assert.Equal(("Sheet1", "C1"), index["RealName"]);
    }

    [Fact]
    public void BuildDefinedNamesIndexWithDirectCells_SkipsInvalidCoordinates()
    {
        var workbook = new Workbook();
        var cells = new List<CellMapping>
        {
            new CellMapping
            {
                Coordinate = "Sheet1!$A$1",
                DefinedNameAlias = "ValidCell"
            },
            new CellMapping
            {
                Coordinate = "C3",  // Formato non valido (nessun foglio)
                DefinedNameAlias = "InvalidCell"
            }
        };

        var index = OpenXmlExcelReader.BuildDefinedNamesIndexWithDirectCells(workbook, cells);

        // Deve contenere solo la cella valida
        Assert.Contains("ValidCell", index.Keys);
        Assert.DoesNotContain("InvalidCell", index.Keys);
    }

    [Fact]
    public void BuildDefinedNamesIndexWithDirectCells_HandlesNullCellsList()
    {
        var workbook = new Workbook();

        var index = OpenXmlExcelReader.BuildDefinedNamesIndexWithDirectCells(workbook, null);

        Assert.Empty(index);
    }

    [Fact]
    public void BuildDefinedNamesIndexWithDirectCells_CombinesRealAndVirtualDefinedNames()
    {
        var workbook = new Workbook();
        var definedNames = new DefinedNames(
            new DefinedName
            {
                Name = "Serial",
                Text = "Dati!$B$5"
            },
            new DefinedName
            {
                Name = "TestDate",
                Text = "Dati!$C$10"
            }
        );
        workbook.DefinedNames = definedNames;

        var cells = new List<CellMapping>
        {
            new CellMapping
            {
                Coordinate = "Dati!$D$3",
                DefinedNameAlias = "DirectCell1"
            }
        };

        var index = OpenXmlExcelReader.BuildDefinedNamesIndexWithDirectCells(workbook, cells);

        // Deve contenere sia i DefinedNames reali che quelli virtuali
        Assert.Contains("Serial", index.Keys);
        Assert.Contains("TestDate", index.Keys);
        Assert.Contains("DirectCell1", index.Keys);
        Assert.Equal(3, index.Count);
    }

    [Fact]
    public void BuildDefinedNamesIndexWithDirectCells_NormalizesCoordinates()
    {
        var workbook = new Workbook();
        var cells = new List<CellMapping>
        {
            new CellMapping
            {
                Coordinate = "Sheet1!$a$1",  // Lowercase - deve normalizzare
                DefinedNameAlias = "LowercaseCell"
            }
        };

        var index = OpenXmlExcelReader.BuildDefinedNamesIndexWithDirectCells(workbook, cells);

        var (_, addr) = index["LowercaseCell"];
        Assert.Equal("A1", addr);  // Deve essere uppercase
    }
}