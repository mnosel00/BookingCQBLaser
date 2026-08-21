using System;
using System.Linq;
using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace BookingCQBLaser.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PackagesController : ControllerBase
{
    // Blocked duration is a pure lookup over the PackageType enum - no repository or other
    // dependency needed, so this stays a plain static endpoint rather than a full service.
    [HttpGet]
    public IActionResult GetPackages()
    {
        var packages = Enum.GetValues<PackageType>()
            .Select(p => new PackageDurationDto(p, p.GetBaseDurationMinutes()))
            .ToList();

        return Ok(packages);
    }
}
