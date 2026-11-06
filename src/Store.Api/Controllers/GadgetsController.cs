using Microsoft.AspNetCore.Mvc;
using Store.Application;

namespace Store.Api.Controllers;

[ApiController]
[Route("api/gadgets")]
public sealed class GadgetsController(StoreService service) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<GadgetResponse>> GetAll(long? categoryId = null, int offset = 0, int limit = 50) =>
        Ok(service.GetGadgets(categoryId, offset, limit).Select(GadgetResponse.From).ToArray());

    [HttpGet("{id:long}")]
    public ActionResult<GadgetResponse> Get(long id) => Ok(GadgetResponse.From(service.GetGadget(id)));

    [HttpPost]
    public ActionResult<GadgetResponse> Create(CreateGadgetRequest request)
    {
        var gadget = service.CreateGadget(request.Name, request.Description, request.Price!.Value, request.CategoryId!.Value);
        return CreatedAtAction(nameof(Get), new { id = gadget.Id }, GadgetResponse.From(gadget));
    }

    [HttpPut("{id:long}")]
    public ActionResult<GadgetResponse> Update(long id, UpdateGadgetRequest request) => Ok(GadgetResponse.From(
        service.UpdateGadget(id, request.Name, request.Description, request.Price!.Value, request.CategoryId!.Value, request.Version!.Value)));

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id, [FromQuery] long version)
    {
        service.DeleteGadget(id, version);
        return NoContent();
    }
}
