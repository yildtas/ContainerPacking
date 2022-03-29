using ContainerPacking.CromulentBisgetti.Entities;
using System.Collections.Generic;

namespace ContainerPacking.CromulentBisgetti.Algorithms
{
	public abstract class AlgorithmBase
	{
		public abstract ContainerPackingResult Run(Container container, List<Item> items);
	}
}