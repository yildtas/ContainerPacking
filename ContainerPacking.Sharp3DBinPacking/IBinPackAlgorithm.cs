using System;
using System.Collections.Generic;
using System.Text;

namespace ContainerPacking.Sharp3DBinPacking
{
    public interface IBinPackAlgorithm
    {
        void Insert(IEnumerable<Cuboid> cuboids);
    }
}
