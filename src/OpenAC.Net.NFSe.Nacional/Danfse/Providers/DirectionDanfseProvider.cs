
using Direction.NFSe.Danfe;
using OpenAC.Net.DFe.Core.Common;
using System;
using System.IO;
using System.Xml.Serialization;

namespace OpenAC.Net.NFSe.Nacional.Danfse.Providers
{
    internal class DirectionDanfseProvider : INFSeDanfseProvider
    {
        public byte[] GerarPdf(string xmlString, DFeTipoAmbiente ambiente, bool cancelada)
        {
            if (string.IsNullOrWhiteSpace(xmlString))
                throw new ArgumentException("O conteúdo do XML não pode ser vazio.", nameof(xmlString));

            var serializer = new XmlSerializer(typeof(NFSeSchema));
            using var reader = new StringReader(xmlString);
            var nfse = serializer.Deserialize(reader) as NFSeSchema
                       ?? throw new InvalidDataException("Falha ao desserializar o XML.");

            var danfeService = new DanfeService();

            var ambienteLib = ambiente == DFeTipoAmbiente.Producao
                ? DanfeEnvironment.Production
                : DanfeEnvironment.Restricted;

            var result = danfeService.Generate(nfse, ambienteLib, cancelada);

            if (result.PdfBytes == null || result.PdfBytes.Length == 0)
                throw new InvalidOperationException("A biblioteca retornou bytes vazios na geração do PDF.");

            return result.PdfBytes;
        }
    }
}
