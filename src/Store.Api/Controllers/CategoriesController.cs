using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Store.Api.Security;
using Store.Application;

namespace Store.Api.Controllers;

[ApiController]
[Authorize(Policy = StorePolicies.ReadCatalog)]
[RequestSizeLimit(16_384)]
[Route("api/categories")]
public sealed class CategoriesController(StoreService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CategoryResponse>> GetAll(int offset = 0, int limit = 50) =>
        Ok(service.GetCategories(offset, limit).Select(CategoryResponse.From).ToArray());

    [HttpGet("{id:long}")]
    public ActionResult<CategoryResponse> Get(long id) => Ok(CategoryResponse.From(service.GetCategory(id)));

    [HttpPost]
    [Authorize(Policy = StorePolicies.WriteCatalog)]
    public ActionResult<CategoryResponse> Create(CreateCategoryRequest request)
    {
        var category = service.CreateCategory(request.Name);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, CategoryResponse.From(category));
    }
}
