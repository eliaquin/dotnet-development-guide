using System.ComponentModel.DataAnnotations;
using Store.Domain;

namespace Store.Api;

public sealed class CreateCategoryRequest
{
    private string _name = string.Empty;

    [Required, StringLength(50)]
    public string Name { get => _name; init => _name = value?.Trim() ?? string.Empty; }
}

public sealed class CreateGadgetRequest
{
    private string _name = string.Empty;
    private string? _description;

    [Required, StringLength(50)]
    public string Name { get => _name; init => _name = value?.Trim() ?? string.Empty; }

    [StringLength(500)]
    public string? Description { get => _description; init => _description = value?.Trim(); }

    [Required]
    public decimal? Price { get; init; }

    [Required]
    public long? CategoryId { get; init; }
}

public sealed class UpdateGadgetRequest
{
    private string _name = string.Empty;
    private string? _description;

    [Required, StringLength(50)]
    public string Name { get => _name; init => _name = value?.Trim() ?? string.Empty; }

    [StringLength(500)]
    public string? Description { get => _description; init => _description = value?.Trim(); }

    [Required]
    public decimal? Price { get; init; }

    [Required]
    public long? CategoryId { get; init; }

    [Required]
    public long? Version { get; init; }
}

public sealed record CategoryResponse(long Id, string Name, DateTimeOffset CreatedUtc)
{
    public static CategoryResponse From(Category category) => new(category.Id, category.Name, category.CreatedUtc);
}

public sealed record GadgetResponse(
    long Id, string Name, string Description, decimal Price, long CategoryId, long Version, DateTimeOffset CreatedUtc)
{
    public static GadgetResponse From(Gadget gadget) => new(
        gadget.Id, gadget.Name, gadget.Description, gadget.PriceCents / 100m,
        gadget.CategoryId, gadget.Version, gadget.CreatedUtc);
}
