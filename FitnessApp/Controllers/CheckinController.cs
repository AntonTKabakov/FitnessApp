using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

public class CheckinController : ControllerBase
{
    private readonly ICheckinService _service;

    public CheckinController(ICheckinService service)
    {
        _service = service;
    }

    [HttpPost]
    public IActionResult GetAll([FromBody] CheckinFilterModel filters)
    {
        var result = _service.GetAll(filters);

        return Ok(result);
    }

    [HttpGet]
    public IActionResult GetOne([FromQuery] int id)
    {
        var result = _service.GetOne(id);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost]
    public IActionResult Create([FromBody] CheckinEditDTO checkin)
    {
        var result = _service.Create(checkin);

        if (result == null)
        {
            return BadRequest();
        }

        return Created();
    }

    [HttpPut]
    public IActionResult Update([FromBody] CheckinEditDTO checkin)
    {
        var result = _service.Update(checkin);

        if (!result)
        {
            return BadRequest();
        }

        return Ok();
    }

    [HttpDelete]
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