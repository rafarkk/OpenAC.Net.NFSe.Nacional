using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Indexador.Model
{
    /// <summary>
    /// Representa a estrutura base para paginação.
    /// Inclui o número da página atual e o número máximo de registros por página.
    /// </summary>
    public abstract class ResultadoPaginadoBase
    {
        /// <summary>
        /// Número da página atual.
        /// </summary>
        public int Pagina { get; set; } = 1;

        /// <summary>
        /// Quantidade máxima de registros por página.
        /// </summary>
        public int TamanhoPagina { get; set; } = 50;
    }

    /// <summary>
    /// Representa o resultado de uma consulta paginada,
    /// incluindo a lista de itens e o total de registros disponíveis.
    /// </summary>
    /// <typeparam name="T">Tipo dos elementos que compõem a lista de resultados.</typeparam>
    public class ResultadoPaginado<T> : ResultadoPaginadoBase
    {
        /// <summary>
        /// Quantidade total de páginas calculada automaticamente
        /// com base em TotalItems e PageSize.
        /// </summary>
        [JsonIgnore]
        public int TotalPaginas => TamanhoPagina > 0 ? (int)Math.Ceiling((double)TotalItems / TamanhoPagina) : 0;


        /// <summary>
        /// Quantidade total de registros que satisfazem a consulta,
        /// independentemente da paginação.
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// Lista contendo os registros da página atual.
        /// </summary>
        [JsonPropertyOrder(100)]
        public List<T> Items { get; set; } = new();
    }
}
