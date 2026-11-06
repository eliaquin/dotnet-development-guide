using Store.Domain;

namespace Store.Application;

public sealed class MissingRecordException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);

public sealed class StoreService(IStoreRepository repository, TimeProvider clock)
{
    public IReadOnlyList<Category> GetCategories(int offset = 0, int limit = 50)
    {
        ValidatePage(offset, limit);
        return repository.GetCategories(offset, limit);
    }

    public Category GetCategory(long id) => repository.GetCategory(id)
        ?? throw new MissingRecordException("La categoría no existe.");

    public Category CreateCategory(string name) => repository.AddCategory(CatalogRules.Name(name), clock.GetUtcNow());

    public IReadOnlyList<Gadget> GetGadgets(long? categoryId = null, int offset = 0, int limit = 50)
    {
        ValidatePage(offset, limit);
        if (categoryId is <= 0)
        {
            throw new DomainRuleException("La categoría debe ser un identificador positivo.");
        }

        return repository.GetGadgets(categoryId, offset, limit);
    }

    public Gadget GetGadget(long id) => repository.GetGadget(id)
        ?? throw new MissingRecordException("El producto no existe.");

    public Gadget CreateGadget(string name, string? description, decimal price, long categoryId)
    {
        var details = GadgetDetails.Create(name, description, price, categoryId);
        _ = GetCategory(categoryId);
        return repository.AddGadget(details, clock.GetUtcNow());
    }

    public Gadget UpdateGadget(long id, string name, string? description, decimal price, long categoryId, long expectedVersion)
    {
        ValidateVersion(expectedVersion);
        var details = GadgetDetails.Create(name, description, price, categoryId);
        _ = GetCategory(categoryId);
        return repository.UpdateGadget(id, details, expectedVersion);
    }

    public void DeleteGadget(long id, long expectedVersion)
    {
        ValidateVersion(expectedVersion);
        repository.DeleteGadget(id, expectedVersion);
    }

    private static void ValidatePage(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100)
        {
            throw new DomainRuleException("offset debe ser positivo o cero y limit debe estar entre 1 y 100.");
        }
    }

    private static void ValidateVersion(long version)
    {
        if (version <= 0)
        {
            throw new DomainRuleException("La versión debe ser positiva.");
        }
    }
}
