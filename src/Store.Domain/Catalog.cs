namespace Store.Domain;

public sealed record Category(long Id, string Name, DateTimeOffset CreatedUtc);

public sealed record Gadget(
    long Id,
    string Name,
    string Description,
    long PriceCents,
    long CategoryId,
    long Version,
    DateTimeOffset CreatedUtc);

public sealed class DomainRuleException(string message) : Exception(message);

public sealed record GadgetDetails
{
    public string Name { get; }
    public string Description { get; }
    public long PriceCents { get; }
    public long CategoryId { get; }

    private GadgetDetails(string name, string description, long priceCents, long categoryId)
    {
        Name = name;
        Description = description;
        PriceCents = priceCents;
        CategoryId = categoryId;
    }

    public static GadgetDetails Create(string name, string? description, decimal price, long categoryId)
    {
        var cleanName = CatalogRules.Name(name);
        var cleanDescription = description?.Trim() ?? string.Empty;
        if (cleanDescription.Length > 500)
        {
            throw new DomainRuleException("La descripción admite como máximo 500 caracteres.");
        }

        if (price < 0 || price > 1_000_000 || decimal.Round(price, 2) != price)
        {
            throw new DomainRuleException("El precio debe estar entre 0 y 1000000, con hasta dos decimales.");
        }

        if (categoryId <= 0)
        {
            throw new DomainRuleException("La categoría debe ser un identificador positivo.");
        }

        return new GadgetDetails(cleanName, cleanDescription, checked((long)(price * 100)), categoryId);
    }
}

public static class CatalogRules
{
    public static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 50)
        {
            throw new DomainRuleException("El nombre es obligatorio y admite hasta 50 caracteres.");
        }

        return name.Trim();
    }
}
