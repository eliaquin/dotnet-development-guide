using Store.Domain;

namespace Store.Application;

public interface IStoreRepository
{
    IReadOnlyList<Category> GetCategories(int offset, int limit);
    Category? GetCategory(long id);
    Category AddCategory(string name, DateTimeOffset createdUtc);
    IReadOnlyList<Gadget> GetGadgets(long? categoryId, int offset, int limit);
    Gadget? GetGadget(long id);
    Gadget AddGadget(GadgetDetails details, DateTimeOffset createdUtc);
    Gadget UpdateGadget(long id, GadgetDetails details, long expectedVersion);
    void DeleteGadget(long id, long expectedVersion);
}
