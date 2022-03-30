using ContainerPacking.CromulentBisgetti.Entities;
using ContainerPacking.DemoApp.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

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
            List<List<Item>> listItemsToPack = new List<List<Item>>();
            string[] filePaths = Directory.GetFiles(Path.Combine(_env.WebRootPath, "xlsx/"));

            DataTableCollection sheets = ExcelDataContext.GetInstance(filePaths[0]).Sheets;

            foreach (DataTable table in sheets)
            {
                listItemsToPack.Add(GetItemsToPack(table));
            }

            int count = 0;
            foreach (List<Item> itemsToPack in listItemsToPack)
            {
                //if (count == 0)
                //{
                //    LoadedPallets(itemsToPack);
                //}

                LoadedPallets(itemsToPack);

                count++;
            }


            return View(listItemsToPack);
        }

        private List<Item> LoadedPallets(List<Item> itemsToPack)
        {
            int containerFloor = itemsToPack.FirstOrDefault().ContainerFloor;
            int containerHeigh = itemsToPack.FirstOrDefault().ContainerHeight;
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
                                pack.IsLoad = true;
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
                            pack.IsLoad = true;
                            loadedItems.Add(pack);
                            totalLoadedFloor = totalLoadedFloor + floor;
                        }
                        else
                        {
                            totalPackHeigh = height;
                            totalLoadedFloor = totalLoadedFloor + floor;

                            pack.IsLoad = true;
                            loadedItems.Add(pack);
                        }

                        if (totalLoadedFloor > containerFloor)
                        {
                            itemsToPack.ForEach(p => p.TotalLoadedFloor = totalLoadedFloor);
                            return itemsToPack;//Return
                        }

                        count++;
                    }
                }

                int outPacketCount = outPacket.Count;
                if (outPacketCount == 1)
                {
                    Item pack = outPacket.FirstOrDefault();
                    pack.IsOut = true;

                    decimal newFloor = (containerHeigh / pack.Dim3) * pack.Floor;

                    totalLoadedFloor = totalLoadedFloor + newFloor;

                    if (totalLoadedFloor > containerFloor)
                    {
                        totalLoadedFloor = totalLoadedFloor - newFloor;

                        itemsToPack.ForEach(p => p.TotalLoadedFloor = totalLoadedFloor);
                        return itemsToPack;//Return
                    }

                    pack.Floor = newFloor;
                    pack.IsLoad = true;
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

                                outPack.IsLoad = true;
                                outPack.IsOut = true;
                                loadedItems.Add(outPack);
                            }
                            else
                            {
                                outPack.IsLoad = true;
                                outPack.IsOut = true;
                                loadedItems.Add(outPack);

                                totalLoadedFloor = totalLoadedFloor + floor;
                            }

                            break;
                        }

                        if (totalPackHeigh <= containerHeigh)
                        {
                            outPack.IsLoad = true;
                            outPack.IsOut = true;
                            loadedItems.Add(outPack);

                            totalLoadedFloor = totalLoadedFloor + floor;
                        }
                        else
                        {
                            totalPackHeigh = height;
                            totalLoadedFloor = totalLoadedFloor + floor;

                            outPack.IsLoad = true;
                            outPack.IsOut = true;
                            loadedItems.Add(outPack);
                        }

                        if (totalLoadedFloor > containerFloor)
                        {
                            itemsToPack.ForEach(p => p.TotalLoadedFloor = totalLoadedFloor);
                            return itemsToPack;//Return
                        }
                        outCount++;
                    }
                }
            }

            itemsToPack.ForEach(p => p.TotalLoadedFloor = totalLoadedFloor);

            return itemsToPack;
        }

        private List<Item> GetItemsToPack(DataTable table)
        {
            List<Item> itemsToPack = new List<Item>();

            foreach (DataRow row in table.Rows)
            {
                Item item = new Item();

                item.No = Convert.ToInt32(row["No"]);
                item.SupplierId = Convert.ToInt32(row["SupplierId"]);
                item.Dim1 = Convert.ToInt32(row["PalletWidth"]);
                item.Dim2 = Convert.ToInt32(row["PalletLength"]);
                item.Dim3 = Convert.ToInt32(row["PalletHeight"]);
                item.Floor = Convert.ToDecimal(row["PalletFloor"]);

                item.ContainerHeight = Convert.ToInt32(row["ContainerHeight"]);
                item.ContainerFloor = Convert.ToInt32(row["ContainerFloor"]);

                itemsToPack.Add(item);
            }
            return itemsToPack;
        }
    }
}
