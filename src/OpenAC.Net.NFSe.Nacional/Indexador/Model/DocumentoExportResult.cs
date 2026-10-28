using OpenAC.Net.DFe.Core.Common;
using OpenAC.Net.NFSe.Nacional.Common.Converter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Indexador.Model
{
    public class DocumentoExportResult
    {
        [JsonPropertyName("arquivoGZipB64")]
        [JsonConverter(typeof(ByteArrayGzipJsonConverter))]
        public byte[] ArquivoBytes { get; set; } = [];
        public string NomeArquivo { get; set; } = "";
        public string ContentType { get; set; }
        public long TamanhoBytes => ArquivoBytes.Length;


        //[JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        //public DFeTipoAmbiente? Ambiente { get; set; }

        //[JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        //public bool? Cancelada { get; set; }
    }
}
