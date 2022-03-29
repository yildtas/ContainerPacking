using ContainerPacking.CromulentBisgetti.Algorithms;
using ContainerPacking.CromulentBisgetti.Entities;
using ContainerPacking.Sharp3DBinPacking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ContainerPacking.CromulentBisgetti
{
    /// <summary>
    /// The container packing service.
    /// </summary>
    public static class PackingService
    {
        /// <summary>
        /// Attempts to pack the specified containers with the specified items using the specified algorithms.
        /// </summary>
        /// <param name="containers">The list of containers to pack.</param>
        /// <param name="itemsToPack">The items to pack.</param>
        /// <param name="algorithmTypeIDs">The list of algorithm type IDs to use for packing.</param>
        /// <returns>A container packing result with lists of the packed and unpacked items.</returns>
        public static List<ContainerPackingResult> Pack(List<Container> containers, List<Item> itemsToPack, List<int> algorithmTypeIDs)
        {
            /*
          X => Width
          Y => Height
          Z => Length or Depthn
          */

            List<ContainerPackingResult> results = new List<ContainerPackingResult>();

            for (int containerIndex = 0; containerIndex < containers.Count; containerIndex++)
            {
                List<ContainerPackingResult> subResults = new List<ContainerPackingResult>();

                foreach (ContainerHorizontalRotateStates containerHorizontalRotateState in Enum.GetValues(typeof(ContainerHorizontalRotateStates)))
                {
                    var items = itemsToPack.Select(item => item.CopyItem()).ToList();

                    var packingStartedDate = DateTime.Now;

                    var container = containers[containerIndex];

                    var containerResult = new AlgorithmPackingResult();
                    containerResult.PackedItems = new List<Item>();
                    containerResult.UnpackedItems = new List<Item>();
                    containerResult.PercentContainerVolumePacked = 0;
                    containerResult.PercentItemVolumePacked = 0;
                    containerResult.IsCompletePack = false;

                    var totalContainerItemsCount = items.Count;
                    var totalContainerItemsVolumes = items.Sum(p => p.Volume);


                    // Define the size of bin
                    var binHeight = container.Height;
                    var binWidth = container.Width;
                    var binDepth = container.Length;

                    //Rotation state configuration
                    if (containerHorizontalRotateState == ContainerHorizontalRotateStates.Rotated)
                    {
                        binDepth = container.Width;
                        binWidth = container.Length;
                    }

                    // Define the cuboids to pack
                    Cuboid[] cuboids = items.Select(item => new Cuboid()
                    {
                        Height = item.Dim3,
                        Width = item.Dim1,
                        Depth = item.Dim2,
                        Tag = item
                    }).ToArray();

                    var parameter = new BinPackParameter(binWidth, binHeight, binDepth, 0, false, cuboids);

                    var packResult = BinPacker.GetDefault(BinPackerVerifyOption.BestOnly).Pack(parameter).BestResult;

                    //Results for this container
                    var resultForThisContainer = packResult.FirstOrDefault();
                    if (resultForThisContainer != null)
                    {
                        containerResult.PackedItems = resultForThisContainer.Select(cuboid =>
                        {
                            Item item = (Item)cuboid.Tag;
                            item.CoordX = (containerHorizontalRotateState == ContainerHorizontalRotateStates.Default) ? cuboid.X : cuboid.Z;
                            item.CoordY = cuboid.Y;
                            item.CoordZ = (containerHorizontalRotateState == ContainerHorizontalRotateStates.Default) ? cuboid.Z : cuboid.X;
                            item.PackDimX = cuboid.Width;
                            item.PackDimY = cuboid.Height;
                            item.PackDimZ = cuboid.Depth;
                            item.IsPacked = true;
                            return item;
                        }).ToList();

                        var totalContainerPackedItemsVolumes = containerResult.PackedItems.Sum(p => p.Volume);
                        var totalContainerPackedItemsCount = containerResult.PackedItems.Count;
                        var packingEndDate = DateTime.Now;

                        if (totalContainerItemsVolumes > 0)
                        {
                            containerResult.PercentItemVolumePacked = 100 * (totalContainerPackedItemsVolumes / totalContainerItemsVolumes);
                        }

                        if (container.Volume > 0)
                        {
                            containerResult.PercentContainerVolumePacked = 100 * (totalContainerPackedItemsVolumes / container.Volume);
                        }

                        if (totalContainerPackedItemsCount > 0)
                        {
                            containerResult.PercentContainerPacked = 100 * (totalContainerPackedItemsCount / items.Count);
                        }

                        containerResult.IsCompletePack = totalContainerItemsCount == containerResult.PackedItems.Count;

                        containerResult.PackTimeInMilliseconds = (packingEndDate - packingStartedDate).Milliseconds;

                        //Results for other containers
                        if (packResult.Count > 1)
                        {
                            var resultsForOthers = packResult.Skip(1).SelectMany(p => p).ToList();
                            items = resultsForOthers.Select(cuboid =>
                            {
                                Item item = (Item)cuboid.Tag;
                                item.CoordX = (containerHorizontalRotateState == ContainerHorizontalRotateStates.Default) ? cuboid.X : cuboid.Z;
                                item.CoordY = cuboid.Y;
                                item.CoordZ = (containerHorizontalRotateState == ContainerHorizontalRotateStates.Default) ? cuboid.Z : cuboid.X;
                                item.PackDimX = cuboid.Width;
                                item.PackDimY = cuboid.Height;
                                item.PackDimZ = cuboid.Depth;
                                item.IsPacked = false;
                                return item;
                            }).ToList();

                            //If in last container, unpacked items adding in this container result
                            if (containerIndex == containers.Count - 1)
                            {
                                containerResult.UnpackedItems = items;
                            }
                        }
                    }
                    else
                    {
                        items = new List<Item>();
                    }

                    ContainerPackingResult containerPackingResult = new ContainerPackingResult();
                    containerPackingResult.AlgorithmPackingResults = new List<AlgorithmPackingResult> { containerResult };
                    containerPackingResult.ContainerID = container.ID;

                    subResults.Add(containerPackingResult);
                }

                results.Add(subResults.OrderByDescending(p => p.AlgorithmPackingResults.First().PercentContainerPacked).First());
            }

            return results;
        }

        /// <summary>
        /// Gets the packing algorithm from the specified algorithm type ID.
        /// </summary>
        /// <param name="algorithmTypeID">The algorithm type ID.</param>
        /// <returns>An instance of a packing algorithm implementing AlgorithmBase.</returns>
        /// <exception cref="System.Exception">Invalid algorithm type.</exception>
        public static IPackingAlgorithm GetPackingAlgorithmFromTypeID(int algorithmTypeID)
        {
            switch (algorithmTypeID)
            {
                case (int)AlgorithmType.EB_AFIT:
                    return new EB_AFIT();

                default:
                    throw new Exception("Invalid algorithm type.");
            }
        }

        public static Item CopyItem(this Item item)
        {
            Item res = new Item()
            {
                CoordX = item.CoordX,
                CoordY = item.CoordY,
                CoordZ = item.CoordZ,
                Dim1 = item.Dim1,
                Dim2 = item.Dim2,
                Dim3 = item.Dim3,
                ID = item.ID,
                IsPacked = item.IsPacked,
                PackDimX = item.PackDimX,
                PackDimY = item.PackDimY,
                PackDimZ = item.PackDimZ,
                Quantity = item.Quantity
            };
            return res;
        }

        enum ContainerHorizontalRotateStates
        {
            /// <summary>
            /// Default rotation
            /// </summary>
            Default,

            //Changed eachother Length and Width
            Rotated
        }
    }
}
