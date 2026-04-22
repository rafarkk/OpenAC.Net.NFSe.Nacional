using System;
using System.Linq;

namespace OpenAC.Net.NFSe.Nacional.Indexador
{
    public enum EstruturaZip
    {
        /// <summary>
        /// Todos os arquivos soltos na raiz do ZIP, sem subpastas.
        /// </summary>
        Plana = 0,

        /// <summary>
        /// Arquivos organizados em pastas por prestador e tipo (prestador/NFSe ou prestador/Rps).
        /// </summary>
        PorPrestadorETipo = 1
    }
}
