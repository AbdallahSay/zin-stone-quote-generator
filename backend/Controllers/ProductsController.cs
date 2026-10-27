using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FactoryQuoteApi.Data;
using FactoryQuoteApi.Models;
using FactoryQuoteApi.Services;

namespace FactoryQuoteApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ISecurityService _security;

    public ProductsController(ApplicationDbContext db, ISecurityService security)
    {
        _db = db;
        _security = security;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
        var (valid, _, role) = await _security.ValidateUserTokenAsync(token);
        if (!valid || role is not ("employee" or "admin" or "owner")) return Unauthorized();
        var products = await _db.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.ProductName)
            .ToListAsync();
        return Ok(products);
    }
}
