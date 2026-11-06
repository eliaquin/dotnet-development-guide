using Microsoft.AspNetCore.Mvc;
using Store.Application;

namespace Store.Api.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(StoreService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CategoryResponse>> GetAll(int offset = 0, int limit = 50) =>
        Ok(service.GetCategories(offset, limit).Select(CategoryResponse.From).ToArray());

    [HttpGet("{id:long}")]
    public ActionResult<CategoryResponse> Get(long id) => Ok(CategoryResponse.From(service.GetCategory(id)));

    [HttpPost]
    public ActionResult<CategoryResponse> Create(CreateCategoryRequest request)
    {
        var category = service.CreateCategory(request.Name);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, CategoryResponse.From(category));
    }
}
