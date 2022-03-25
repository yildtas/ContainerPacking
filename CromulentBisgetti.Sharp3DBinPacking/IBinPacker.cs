using System;
using System.Collections.Generic;
using System.Text;

namespace CromulentBisgetti.Sharp3DBinPacking
{
    public interface IBinPacker
    {
        BinPackResult Pack(BinPackParameter parameter);
    }
}
