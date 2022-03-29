using ContainerPacking.CromulentBisgetti.Entities;
using ContainerPacking.DemoApp.Models;
using ExcelDataReader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ContainerPacking.DemoApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _env;

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public IActionResult Index()
        {
            List<Item> itemsToPack = new List<Item>();

            string[] filePaths = Directory.GetFiles(Path.Combine(_env.WebRootPath, "xlsx/"));

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            using (var stream = System.IO.File.Open(filePaths[0], FileMode.Open, FileAccess.Read))
            {
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    while (reader.Read()) //Each row of the file
                    {

                        string id = reader.GetValue(0).ToString();

                    }
                }


            }


            int containerFloor = 33;
            int containerHeigh = 3000;
            List<ContainerPackingResult> result = new List<ContainerPackingResult>();

            var groupedItemsToPacks = itemsToPack.GroupBy(ip => ip.SupplierId);
            List<Item> loadedItems = new List<Item>();
            decimal totalLoadedFloor = 0;

            foreach (var groupedItemsToPack in groupedItemsToPacks)
            {
                var groupByFloorItemsToPacks = groupedItemsToPack
                    .GroupBy(op => op.Dim1 * op.Dim2 * op.Dim3)
                    .OrderByDescending(p => p.Key);

                decimal totalPackHeigh = 0;
                List<Item> outPacket = new List<Item>();

                foreach (var groupByFloorItemsToPack in groupByFloorItemsToPacks)
                {
                    int count = 1;
                    int itemsPackCount = groupByFloorItemsToPack.Count();

                    foreach (var pack in groupByFloorItemsToPack)
                    {
                        int supplierId = pack.SupplierId;
                        decimal height = pack.Dim3;
                        decimal floor = pack.Floor;

                        totalPackHeigh = totalPackHeigh + height;

                        if (count == itemsPackCount)
                        {
                            if (totalPackHeigh <= containerHeigh)
                            {
                                loadedItems.Add(pack);
                                totalLoadedFloor = totalLoadedFloor + floor;
                            }
                            else
                            {
                                outPacket.Add(pack);
                                totalPackHeigh = 0;
                            }
                            break;
                        }

                        if (totalPackHeigh <= containerHeigh)
                        {
                            loadedItems.Add(pack);
                            totalLoadedFloor = totalLoadedFloor + floor;
                        }
                        else
                        {
                            totalPackHeigh = height;
                            totalLoadedFloor = totalLoadedFloor + floor;
                            loadedItems.Add(pack);
                        }

                        if (totalLoadedFloor > containerFloor)
                        {
                            //return result;
                        }

                        count++;
                    }
                }

                int outPacketCount = outPacket.Count;
                if (outPacketCount == 1)
                {
                    Item pack = outPacket.FirstOrDefault();
                    decimal newFloor = containerHeigh / pack.Dim3 * pack.Floor;
                    pack.Floor = newFloor;

                    totalLoadedFloor = totalLoadedFloor + newFloor;
                    loadedItems.Add(pack);
                }
                else
                {
                    int outCount = 1;

                    foreach (Item outPack in outPacket.OrderByDescending(p => p.Dim3))
                    {
                        int supplierId = outPack.SupplierId;
                        decimal height = outPack.Dim3;
                        decimal floor = outPack.Floor;

                        totalPackHeigh = totalPackHeigh + height;

                        if (outCount == outPacketCount)
                        {
                            if (totalPackHeigh > containerHeigh)
                            {
                                decimal newFloor = containerHeigh / outPack.Dim3 * outPack.Floor;
                                outPack.Floor = newFloor;

                                totalLoadedFloor = totalLoadedFloor + newFloor;
                                loadedItems.Add(outPack);
                            }
                            else
                            {
                                loadedItems.Add(outPack);
                                totalLoadedFloor = totalLoadedFloor + floor;
                            }

                            break;
                        }

                        if (totalPackHeigh <= containerHeigh)
                        {
                            loadedItems.Add(outPack);
                            totalLoadedFloor = totalLoadedFloor + floor;
                        }
                        else
                        {
                            totalPackHeigh = height;
                            totalLoadedFloor = totalLoadedFloor + floor;
                            loadedItems.Add(outPack);
                        }

                        if (totalLoadedFloor > containerFloor)
                        {
                            //return result;
                        }
                        outCount++;
                    }
                }

            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
