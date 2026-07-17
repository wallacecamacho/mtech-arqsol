using CashFlow.Consolidated.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CashFlow.Consolidated.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "merchant-only")]
public class ConsolidatedController : ControllerBase
{
    private readonly IMediator _mediator;

    public ConsolidatedController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{date:datetime}")]
    [ProducesResponseType(typeof(DailyBalanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetDailyBalance([FromRoute] DateTime date, CancellationToken cancellationToken)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var query = new GetDailyBalanceQuery(merchantId, date);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        if (result.Value is null)
            return NotFound(new { message = $"No balance found for {date:yyyy-MM-dd}" });

        return Ok(result.Value);
    }

    [HttpGet]
    [ProducesResponseType(typeof(DailyBalanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentBalance(CancellationToken cancellationToken)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var query = new GetDailyBalanceQuery(merchantId, DateTime.UtcNow.Date);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        if (result.Value is null)
            return Ok(new DailyBalanceDto(merchantId, DateTime.UtcNow.Date, 0, 0, 0, "BRL"));

        return Ok(result.Value);
    }

    private Guid GetMerchantId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    [HttpGet("history")]
    [ProducesResponseType(typeof(IEnumerable<DailyBalanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken cancellationToken)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var query  = new GetDailyBalanceRangeQuery(merchantId, from, to);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }
}
