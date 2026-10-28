using OpenAC.Net.DFe.Core.Common;
using OpenAC.Net.NFSe.Nacional.Common.Types;
using System;

namespace OpenAC.Net.NFSe.Nacional.Indexador.Model
{
    /// <summary>
    /// Define o tipo do documento registrado.
    /// </summary>
    public enum TipoRegistroDocumento
    {
        /// <summary>
        /// Documento de Prestação de Serviço (DPS).
        /// </summary>
        DPS,

        /// <summary>
        /// Pedido de Registro de Evento.
        /// </summary>
        PEDIDO_REGISTRO_EVENTO,

        /// <summary>
        /// Nota Fiscal de Serviço Eletrônica (NFS-e).
        /// </summary>
        NFSE,

        /// <summary>
        /// Evento relacionado ao documento.
        /// </summary>
        EVENTO,

        /// <summary>
        /// PDF gerado a partir de um XML.
        /// </summary>
        PDF
    }

    /// <summary>
    /// Representa o registro de um documento eletrônico (XML, JSON, etc.) no índice de localização.
    /// </summary>
    public class ReferenciaDocumento
    {
        /// <summary>
        /// Identificador único do registro no banco de dados local.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Data e hora em que o registro foi criado no índice.
        /// </summary>
        public DateTime CriadoEm { get; set; }

        /// <summary>
        /// CPF ou CNPJ do prestador de serviços dono do documento.
        /// </summary>
        public string DocumentoPrestador { get; set; } = "";

        /// <summary>
        /// Chave de ligação do documento. Pode ser o ID da DPS (antes do envio) 
        /// ou a Chave de Acesso da NFSe (após a emissão).
        /// </summary>
        public string ChaveReferencia { get; set; } = "";

        /// <summary>
        /// Caminho da pasta onde o arquivo está armazenado, relativo à raiz de documentos.
        /// </summary>
        public string CaminhoRelativo { get; set; } = "";

        /// <summary>
        /// Nome físico do arquivo no disco (ex: chave_nfse.xml).
        /// </summary>
        public string NomeArquivo { get; set; } = "";

        /// <summary>
        /// Data de emissão ou competência presente no corpo do documento fiscal.
        /// </summary>
        public DateTime DataDocumento { get; set; }

        /// <summary>
        /// Tipo do documento (NFSe, DPS, etc.).
        /// </summary>
        public TipoRegistroDocumento TipoDocumentoFiscal { get; set; }

        /// <summary>
        /// Ambiente em que a operação foi realizada e o documento foi gerado.
        /// </summary>
        public DFeTipoAmbiente Ambiente { get; set; }

        /// <summary>
        /// Tipo de evento relacionado ao documento (ex: Cancelamento, Substituição), se aplicável.
        /// </summary>
        public TipoEvento? TipoEvento { get; set; }

        /// <summary>
        /// Número sequencial do evento.
        /// </summary>
        public int? NumeroSequencial { get; set; }
    }
}
