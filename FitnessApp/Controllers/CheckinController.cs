using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Service.Interfaces;
using FitnessApp.Service;
using FitnessApp.Data;
using FitnessApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

public class CheckinController(ICheckinService service, AppDbContext db, CurrentUser currentUser) : ControllerBase
{
    private readonly ICheckinService _service = service;

    [HttpPost]
    public IActionResult GetAll([FromBody] CheckinFilterModel filters)
    {
        filters.OwnerId = currentUser.IsAdmin ? null : currentUser.Id;
        filters.Page = Math.Clamp(filters.Page, 1, 100000);
        filters.PageSize = Math.Clamp(filters.PageSize, 1, 100);
        var result = _service.GetAll(filters);

        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetOne([FromQuery] int id, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdmin && !await db.Checkins.AnyAsync(
            x => x.Checkinid == id && x.Customerpass.Customerid == currentUser.Id, cancellationToken)) return NotFound();
        var result = _service.GetOne(id);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CheckinEditDTO checkin, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdmin && !await db.Customerpasses.AnyAsync(
            x => x.Customerpassid == checkin.CustomerPassId && x.Customerid == currentUser.Id, cancellationToken)) return NotFound();
        var result = _service.Create(checkin);

        if (result == null)
        {
            return BadRequest();
        }

        return Created();
    }

    [HttpPut, Authorize(Roles = UserRoles.Admin)]
    public IActionResult Update([FromBody] CheckinEditDTO checkin)
    {
        var result = _service.Update(checkin);

        if (!result)
        {
            return BadRequest();
        }

        return Ok();
    }

    [HttpDelete, Authorize(Roles = UserRoles.Admin)]
    public IActionResult Delete([FromQuery] int id)
    {
        var result = _service.Delete(id);

        if (!result)
        {
            return NotFound();
        }

        return Ok();
    }
}
