using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Indexador
{
    public class DocumentoExportResult
    {
        public byte[] ArquivoBytes { get; set; } = [];
        public string NomeArquivo { get; set; } = "";
        public string ContentType { get; set; }
        public long Tamanho => ArquivoBytes.Length;
    }
}
