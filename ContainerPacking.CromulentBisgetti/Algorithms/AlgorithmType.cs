using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ContainerPacking.CromulentBisgetti.Algorithms
{
	[DataContract]
	public enum AlgorithmType
	{
		/// <summary>
		/// The EB-AFIT packing algorithm type.
		/// </summary>
		[DataMember]
		EB_AFIT = 1
	}
}
