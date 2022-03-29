using ContainerPacking.CromulentBisgetti.Entities;
using System.Collections.Generic;

namespace ContainerPacking.CromulentBisgetti.Algorithms
{
	/// <summary>
	/// Interface for the packing algorithms in this project.
	/// </summary>
	public interface IPackingAlgorithm
	{
		/// <summary>
		/// Runs the algorithm on the specified container and items.
		/// </summary>
		/// <param name="container">The container.</param>
		/// <param name="items">The items to pack.</param>
		/// <returns>The algorithm packing result.</returns>
		AlgorithmPackingResult Run(Container container, List<Item> items);
	}
}