using OpenAC.Net.NFSe.Nacional.Danfse.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Danfse
{
    /// <summary>
    /// Fábrica para criação do gerador de PDF de forma independente.
    /// </summary>
    public static class NFSeDanfseFactory
    {
        /// <summary>
        /// Cria uma nova instância do gerador de PDF padrão.
        /// Retorna a interface INFSeDanfseProvider para manter o desacoplamento.
        /// </summary>
        public static INFSeDanfseProvider Create()
        {
            //se no futuro precisar trocar o provider padrão é só retorná-lo aqui
            return new DirectionDanfseProvider();
        }
    }
}
