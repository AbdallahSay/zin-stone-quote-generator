using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FactoryQuoteApi.Data;
using FactoryQuoteApi.Models;
using FactoryQuoteApi.Services;

namespace FactoryQuoteApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ISecurityService _security;

    public ClientsController(ApplicationDbContext db, ISecurityService security)
    {
        _db = db;
        _security = security;
    }

    [HttpGet]
    public async Task<IActionResult> GetClients()
    {
        var (valid, user, role) = await _security.ValidateUserTokenAsync(Bearer());
        if (!valid || role is not ("employee" or "admin" or "owner")) return Unauthorized();
        var clients = await _db.Clients
            .Where(c => role != "employee" || user != null && c.CreatedById == user.Id)
            .OrderBy(c => c.ClientName)
            .Select(c => new { c.Id, c.ClientName, c.Phone, c.Email, c.ContactPerson, c.Address, c.CreatedAt })
            .ToListAsync();
        return Ok(clients);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetClient(int id)
    {
        var (valid, user, role) = await _security.ValidateUserTokenAsync(Bearer());
        if (!valid || role is not ("employee" or "admin" or "owner")) return Unauthorized();
        var client = await _db.Clients.Where(c => c.Id == id && (role != "employee" || user != null && c.CreatedById == user.Id))
            .Select(c => new { c.Id, c.ClientName, c.Phone, c.Email, c.ContactPerson, c.Address, c.CreatedAt }).FirstOrDefaultAsync();
        if (client == null) return NotFound();
        return Ok(client);
    }

    public sealed class CreateClientRequest
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(200)]
        public string ClientName { get; set; } = "";
        [System.ComponentModel.DataAnnotations.MaxLength(50)] public string Phone { get; set; } = "";
        [System.ComponentModel.DataAnnotations.MaxLength(150)] public string Email { get; set; } = "";
        [System.ComponentModel.DataAnnotations.MaxLength(150)] public string ContactPerson { get; set; } = "";
        [System.ComponentModel.DataAnnotations.MaxLength(250)] public string Address { get; set; } = "";
    }

    [HttpPost]
    public async Task<IActionResult> CreateClient(CreateClientRequest request)
    {
        var (valid, user, role) = await _security.ValidateUserTokenAsync(Bearer());
        if (!valid || user is null || role is not ("employee" or "admin" or "owner")) return Unauthorized();
        var client = new Client { ClientName = request.ClientName.Trim(), Phone = request.Phone,
            Email = request.Email, ContactPerson = request.ContactPerson, Address = request.Address, CreatedById = user.Id };
        _db.Clients.Add(client);
        _db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "ClientCreated", EntityId = client.ClientName[..Math.Min(100, client.ClientName.Length)], Timestamp = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetClient), new { id = client.Id }, new { client.Id, client.ClientName });
    }
    private string? Bearer()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }
}
