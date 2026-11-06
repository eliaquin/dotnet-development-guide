using Store.Application;
using Store.Domain;

namespace Store.UnitTests;

public sealed class StoreServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateCategoryPersistsNormalizedNameAndInjectedUtcTime()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        var category = service.CreateCategory("  Accesorios  ");

        Assert.Equal("Accesorios", category.Name);
        Assert.Equal(Now, category.CreatedUtc);
        Assert.Equal(TimeSpan.Zero, category.CreatedUtc.Offset);
        Assert.Equal(1, repository.WriteCount);
    }

    [Fact]
    public void InvalidCategoryNeverReachesPersistence()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        Assert.Throws<DomainRuleException>(() => service.CreateCategory("   "));
        Assert.Equal(0, repository.WriteCount);
    }

    [Fact]
    public void CreateGadgetPersistsValidatedDetailsAndInjectedUtcTime()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        var gadget = service.CreateGadget("  Ratón  ", "  Inalámbrico  ", 35.49m, 1);

        Assert.Equal("Ratón", gadget.Name);
        Assert.Equal("Inalámbrico", gadget.Description);
        Assert.Equal(3549, gadget.PriceCents);
        Assert.Equal(1, gadget.CategoryId);
        Assert.Equal(Now, gadget.CreatedUtc);
        Assert.Equal(TimeSpan.Zero, gadget.CreatedUtc.Offset);
        Assert.Equal(1, repository.WriteCount);
    }

    [Fact]
    public void InvalidGadgetNeverReachesPersistence()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        Assert.Throws<DomainRuleException>(() => service.CreateGadget("Ratón", null, -1m, 1));
        Assert.Equal(0, repository.WriteCount);
    }

    [Fact]
    public void GadgetCannotBeCreatedInMissingCategory()
    {
        var repository = new RecordingRepository { Category = null };
        var service = CreateService(repository);

        Assert.Throws<MissingRecordException>(() => service.CreateGadget("Ratón", null, 10m, 9));
        Assert.Equal(0, repository.WriteCount);
    }

    [Fact]
    public void UpdatePassesNormalizedDetailsAndExpectedVersionToRepository()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        var updated = service.UpdateGadget(7, "  Ratón  ", "  Nuevo  ", 12.25m, 1, 3);

        Assert.Equal(7, repository.LastGadgetId);
        Assert.Equal(3, repository.LastExpectedVersion);
        Assert.Equal("Ratón", repository.LastDetails?.Name);
        Assert.Equal("Nuevo", repository.LastDetails?.Description);
        Assert.Equal(1225, repository.LastDetails?.PriceCents);
        Assert.Equal(7, updated.Id);
        Assert.Equal(1, repository.WriteCount);
    }

    [Fact]
    public void InvalidUpdateNeverReachesPersistence()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        Assert.Throws<DomainRuleException>(() => service.UpdateGadget(7, "", null, 10m, 1, 1));
        Assert.Equal(0, repository.WriteCount);
    }

    [Fact]
    public void UpdateRequiresAnExistingCategory()
    {
        var repository = new RecordingRepository { Category = null };
        var service = CreateService(repository);

        Assert.Throws<MissingRecordException>(() => service.UpdateGadget(7, "Ratón", null, 10m, 9, 1));
        Assert.Equal(0, repository.WriteCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateRequiresAPositiveExpectedVersion(long expectedVersion)
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        Assert.Throws<DomainRuleException>(() => service.UpdateGadget(7, "Ratón", null, 10m, 1, expectedVersion));
        Assert.Equal(0, repository.WriteCount);
    }

    [Fact]
    public void DeletePassesIdentityAndExpectedVersionToRepository()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        service.DeleteGadget(7, 3);

        Assert.Equal(7, repository.LastGadgetId);
        Assert.Equal(3, repository.LastExpectedVersion);
        Assert.Equal(1, repository.WriteCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DeleteRejectsInvalidVersionBeforePersistence(long expectedVersion)
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        Assert.Throws<DomainRuleException>(() => service.DeleteGadget(7, expectedVersion));
        Assert.Equal(0, repository.WriteCount);
    }

    [Fact]
    public void MissingCategoryIsReportedAsAnApplicationError()
    {
        var service = CreateService(new RecordingRepository { Category = null });

        Assert.Throws<MissingRecordException>(() => service.GetCategory(8));
    }

    [Fact]
    public void MissingGadgetIsReportedAsAnApplicationError()
    {
        var service = CreateService(new RecordingRepository());

        Assert.Throws<MissingRecordException>(() => service.GetGadget(8));
    }

    [Fact]
    public void ReadsReturnTheRepositoryResult()
    {
        var category = new Category(1, "Accesorios", Now);
        var gadget = new Gadget(2, "Ratón", "", 10, 1, 1, Now);
        var service = CreateService(new RecordingRepository { Category = category, Gadget = gadget });

        Assert.Same(category, service.GetCategory(1));
        Assert.Same(gadget, service.GetGadget(2));
    }

    [Fact]
    public void ConcurrentUpdateConflictPropagatesToCaller()
    {
        var conflict = new ConflictException("La versión cambió.");
        var service = CreateService(new RecordingRepository { WriteError = conflict });

        var actual = Assert.Throws<ConflictException>(() => service.UpdateGadget(7, "Ratón", null, 10m, 1, 2));

        Assert.Same(conflict, actual);
    }

    [Fact]
    public void DeleteOfMissingGadgetPropagatesToCaller()
    {
        var missing = new MissingRecordException("El producto no existe.");
        var service = CreateService(new RecordingRepository { WriteError = missing });

        var actual = Assert.Throws<MissingRecordException>(() => service.DeleteGadget(7, 2));

        Assert.Same(missing, actual);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 101)]
    public void InvalidPaginationNeverReachesRepository(int offset, int limit)
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        Assert.Throws<DomainRuleException>(() => service.GetCategories(offset, limit));
        Assert.Throws<DomainRuleException>(() => service.GetGadgets(null, offset, limit));
        Assert.Equal(0, repository.ListCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 100)]
    [InlineData(5, 10)]
    public void ValidPaginationAndCategoryFilterReachRepository(int offset, int limit)
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        service.GetCategories(offset, limit);
        Assert.Equal((offset, limit), repository.LastPage);

        service.GetGadgets(9, offset, limit);
        Assert.Equal((offset, limit), repository.LastPage);
        Assert.Equal(9, repository.LastCategoryId);
        Assert.Equal(2, repository.ListCount);
    }

    [Fact]
    public void DefaultPaginationIsBounded()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        service.GetCategories();
        Assert.Equal((0, 50), repository.LastPage);

        service.GetGadgets();
        Assert.Equal((0, 50), repository.LastPage);
        Assert.Null(repository.LastCategoryId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidCategoryFilterNeverReachesRepository(long categoryId)
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository);

        Assert.Throws<DomainRuleException>(() => service.GetGadgets(categoryId));
        Assert.Equal(0, repository.ListCount);
    }

    private static StoreService CreateService(RecordingRepository repository) => new(repository, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // A recording fake checks the service boundary; database behavior belongs to integration tests.
    private sealed class RecordingRepository : IStoreRepository
    {
        public Category? Category { get; init; } = new(1, "Accesorios", Now);
        public Gadget? Gadget { get; init; }
        public Exception? WriteError { get; init; }
        public int WriteCount { get; private set; }
        public int ListCount { get; private set; }
        public long LastGadgetId { get; private set; }
        public long LastExpectedVersion { get; private set; }
        public GadgetDetails? LastDetails { get; private set; }
        public (int Offset, int Limit) LastPage { get; private set; }
        public long? LastCategoryId { get; private set; }

        public IReadOnlyList<Category> GetCategories(int offset, int limit)
        {
            ListCount++;
            LastPage = (offset, limit);
            return Category is null ? [] : [Category];
        }

        public Category? GetCategory(long id) => Category?.Id == id ? Category : null;

        public Category AddCategory(string name, DateTimeOffset createdUtc)
        {
            WriteCount++;
            return new Category(2, name, createdUtc);
        }

        public IReadOnlyList<Gadget> GetGadgets(long? categoryId, int offset, int limit)
        {
            ListCount++;
            LastPage = (offset, limit);
            LastCategoryId = categoryId;
            return Gadget is null ? [] : [Gadget];
        }

        public Gadget? GetGadget(long id) => Gadget?.Id == id ? Gadget : null;

        public Gadget AddGadget(GadgetDetails details, DateTimeOffset createdUtc)
        {
            WriteCount++;
            return new Gadget(2, details.Name, details.Description, details.PriceCents, details.CategoryId, 1, createdUtc);
        }

        public Gadget UpdateGadget(long id, GadgetDetails details, long expectedVersion)
        {
            RecordWrite(id, expectedVersion);
            LastDetails = details;
            return new Gadget(id, details.Name, details.Description, details.PriceCents, details.CategoryId, expectedVersion + 1, Now);
        }

        public void DeleteGadget(long id, long expectedVersion) => RecordWrite(id, expectedVersion);

        private void RecordWrite(long id, long expectedVersion)
        {
            WriteCount++;
            LastGadgetId = id;
            LastExpectedVersion = expectedVersion;
            if (WriteError is not null)
            {
                throw WriteError;
            }
        }
    }
}
