using CashFlow.Entries.Application.Commands;
using CashFlow.Entries.Application.Queries;
using CashFlow.Entries.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CashFlow.Entries.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EntriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateEntryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateEntry([FromBody] CreateEntryRequest request, CancellationToken cancellationToken)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var command = new CreateEntryCommand(
            request.Amount,
            request.Currency ?? "BRL",
            request.Type,
            request.Description,
            request.EntryDate,
            merchantId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(GetEntries),
            new { date = request.EntryDate.ToString("yyyy-MM-dd") },
            new CreateEntryResponse(result.Value));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<EntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetEntries([FromQuery] DateTime? date, CancellationToken cancellationToken)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var queryDate = date?.Date ?? DateTime.UtcNow.Date;
        var query = new GetEntriesByDateQuery(merchantId, queryDate);
        var result = await _mediator.Send(query, cancellationToken);

        return Ok(result.Value);
    }

    private Guid GetMerchantId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public record CreateEntryRequest(
    decimal Amount,
    string? Currency,
    EntryType Type,
    string Description,
    DateTime EntryDate
);

public record CreateEntryResponse(Guid Id);
