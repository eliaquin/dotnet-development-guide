using Store.Domain;

namespace Store.UnitTests;

public sealed class CatalogRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void NameRejectsMissingValues(string? name)
    {
        Assert.Throws<DomainRuleException>(() => CatalogRules.Name(name!));
    }

    [Fact]
    public void NameTrimsWhitespaceBeforeCheckingLength()
    {
        var name = new string('a', 50);

        Assert.Equal(name, CatalogRules.Name($"  {name}  "));
    }

    [Fact]
    public void NameRejectsMoreThanFiftyCharacters()
    {
        Assert.Throws<DomainRuleException>(() => CatalogRules.Name(new string('a', 51)));
    }

    [Fact]
    public void DetailsNormalizeNameAndDescription()
    {
        var details = GadgetDetails.Create("  Teclado  ", "  Mecánico  ", 19.95m, 1);

        Assert.Equal("Teclado", details.Name);
        Assert.Equal("Mecánico", details.Description);
        Assert.Equal(1995, details.PriceCents);
        Assert.Equal(1, details.CategoryId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DetailsTreatMissingDescriptionAsEmpty(string? description)
    {
        var details = GadgetDetails.Create("Teclado", description, 1m, 1);

        Assert.Empty(details.Description);
    }

    [Fact]
    public void DescriptionAcceptsFiveHundredCharactersAfterTrimming()
    {
        var description = new string('a', 500);

        var details = GadgetDetails.Create("Teclado", $" {description} ", 1m, 1);

        Assert.Equal(description, details.Description);
    }

    [Fact]
    public void DescriptionRejectsMoreThanFiveHundredCharacters()
    {
        Assert.Throws<DomainRuleException>(() => GadgetDetails.Create("Teclado", new string('a', 501), 1m, 1));
    }

    public static TheoryData<decimal, long> ValidPrices => new()
    {
        { 0m, 0L },
        { 0.01m, 1L },
        { 0.29m, 29L },
        { 19.95m, 1995L },
        { 999999.99m, 99999999L },
        { 1000000m, 100000000L },
    };

    [Theory]
    [MemberData(nameof(ValidPrices))]
    public void PriceConvertsExactlyToIntegerCents(decimal price, long expectedCents)
    {
        var details = GadgetDetails.Create("Teclado", null, price, 1);

        Assert.Equal(expectedCents, details.PriceCents);
    }

    public static TheoryData<decimal> InvalidPrices => new()
    {
        -0.01m,
        0.001m,
        1.005m,
        1000000.01m,
        decimal.MinValue,
        decimal.MaxValue,
    };

    [Theory]
    [MemberData(nameof(InvalidPrices))]
    public void PriceRejectsInvalidValuesWithoutRoundingOrOverflow(decimal price)
    {
        Assert.Throws<DomainRuleException>(() => GadgetDetails.Create("Teclado", null, price, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void DetailsRequireAPositiveCategoryId(long categoryId)
    {
        Assert.Throws<DomainRuleException>(() => GadgetDetails.Create("Teclado", null, 1m, categoryId));
    }
}
