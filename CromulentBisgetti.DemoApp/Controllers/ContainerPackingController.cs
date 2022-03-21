﻿using System.Collections.Generic;
using CromulentBisgetti.ContainerPacking;
using CromulentBisgetti.ContainerPacking.Entities;
using CromulentBisgetti.DemoApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace CromulentBisgetti.DemoApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContainerPackingController : ControllerBase
    {
        // POST api/values
        [HttpPost]
        public ActionResult<List<ContainerPackingResult>> Post([FromBody] ContainerPackingRequest request)
        {
            List<ContainerPackingResult> result = PackingService.Pack(request.Containers, request.ItemsToPack, request.AlgorithmTypeIDs);


            return result;

        }
    }
}