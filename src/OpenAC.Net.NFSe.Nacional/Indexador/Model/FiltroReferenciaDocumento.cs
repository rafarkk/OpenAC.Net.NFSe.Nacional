using OpenAC.Net.NFSe.Nacional.Common.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Indexador.Model
{
    /// <summary>
    /// Define como os arquivos serão organizados dentro do arquivo ZIP gerado na exportação de documentos.
    /// </summary>
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

    /// <summary>
    /// Parâmetros de busca para localização de documentos indexados.
    /// </summary>
    public class FiltroReferenciaDocumento : ResultadoPaginadoBase
    {
        //public int Id { get; set; }
        //public DateTime CriadoEm { get; set; }

        /// <summary>
        /// Filtra documentos por um prestador específico (CPF/CNPJ).
        /// </summary>
        public string? DocumentoPrestador { get; set; }

        /// <summary>
        /// Filtra por chave de referência (ID da DPS ou Chave de Acesso).
        /// </summary>
        public string? ChaveReferencia { get; set; } = "";

        //public string CaminhoRelativo { get; set; } = "";
        //public string NomeArquivo { get; set; } = "";

        /// <summary>
        /// Data inicial para o intervalo de busca baseado na data do documento.
        /// </summary>
        public DateTime? DataDe { get; set; }

        /// <summary>
        /// Data final para o intervalo de busca baseado na data do documento.
        /// </summary>
        public DateTime? DataAte { get; set; }

        /// <summary>
        /// Filtra pelo tipo específico de documento (ex: apenas NFSe).
        /// </summary>
        public TipoDocumento? TipoDocumentoFiscal { get; set; }

        /// <summary>
        /// Filtra por um evento específico (ex: apenas documentos cancelados).
        /// </summary>
        public TipoEvento? TipoEvento { get; set; }

        /// <summary>
        /// Filtra pelo número exato do documento fiscal.
        /// </summary>
        public int? NumeroSequencial { get; set; }
    }
}
