using OpenAC.Net.DFe.Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Danfse
{
    /// <summary>
    /// Provedor responsável por abstrair a geração do DANFSE em PDF.
    /// </summary>
    public interface INFSeDanfseProvider
    {
        /// <summary>
        /// Gera o arquivo PDF do DANFSE a partir do conteúdo XML da NFS-e Nacional.
        /// </summary>
        /// <param name="xmlString">O conteúdo em string do XML da NFS-e.</param>
        /// <param name="ambiente">O ambiente de emissão (Produção ou Homologação/Restrita) para adequação do layout.</param>
        /// <param name="cancelada">Indica se a nota fiscal está cancelada (aplica a marca d'água de cancelamento no documento).</param>
        /// <returns>Um array de bytes representando o arquivo PDF gerado.</returns>
        byte[] GerarPdf(string xmlString, DFeTipoAmbiente ambiente, bool cancelada);
    }
}
