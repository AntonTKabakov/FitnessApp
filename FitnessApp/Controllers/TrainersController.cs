using Microsoft.AspNetCore.Authorization;
using FitnessApp.DTO.TrainerDTOs;
using FitnessApp.Filters;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

[Authorize(Roles = FitnessApp.Models.UserRoles.Admin)]
public class TrainersController : ControllerBase
{
    private readonly ITrainerService _service;
    
    public TrainersController(ITrainerService service)
    {
        _service = service;
    }

    [HttpPost]
    public IActionResult GetAll([FromBody] TrainerFilterModel filters)
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
    public IActionResult Create([FromBody] TrainerEditDTO trainer)
    {
        var result = _service.Create(trainer);

        if (result == null)
        {
            return BadRequest();
        }
        
        return Created();
    }

    [HttpPut]
    public IActionResult Update([FromBody] TrainerEditDTO trainer)
    {
        var result = _service.Update(trainer);

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
