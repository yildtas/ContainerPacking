using System;
using System.Collections.Generic;
using System.Text;

namespace ContainerPacking.Sharp3DBinPacking.Internal
{
    public enum ShelfChoiceHeuristic
    {
        ShelfNextFit, // Yeni küboidi her zaman son açık rafa koyarız.
        ShelfFirstFit, // Her bir küboidi sırayla her rafa karşı test ediyoruz ve ilk sığacağı yere paketliyoruz.
    }
}
