using FitnessApp.DTO.GympassDTOs;
using FitnessApp.DTO.TrainerDTOs;
using FitnessApp.Filters;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

public class GympassController : ControllerBase
{
    private readonly IGympassService _service;
    
    public GympassController(IGympassService service)
    {
        _service = service;
    }

    [HttpPost]
    public IActionResult GetAll([FromBody] GympassFilterModel filters)
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
    public IActionResult Create([FromBody] GympassEditDTO pass)
    {
        var result = _service.Create(pass);

        if (result == null)
        {
            return BadRequest();
        }
        
        return Created();
    }

    [HttpPut]
    public IActionResult Update([FromBody] GympassEditDTO pass)
    {
        var result = _service.Update(pass);

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