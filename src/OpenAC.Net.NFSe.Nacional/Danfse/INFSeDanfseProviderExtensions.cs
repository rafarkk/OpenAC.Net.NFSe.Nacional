//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace OpenAC.Net.NFSe.Nacional.Danfse
//{
//    /// <summary>
//    /// Métodos facilitadores para a geração de DANFSE.
//    /// </summary>
//    public static class INFSeDanfseProviderExtensions
//    {
//        /// <summary>
//        /// Localiza a NFS-e pelo indexador, recupera seus metadados e conteúdo, e gera o DANFSe localmente.
//        /// </summary>
//        public static byte[] GerarPdfPelaChave(
//            this INFSeDanfseProvider gerador,
//            string chave,
//            IndexadorDocumentos indexador)
//        {
//            var doc = indexador.ExportarDocumentoPorChave(chave);

//            if (doc == null || doc.ArquivoBytes.Length == 0)
//                throw new FileNotFoundException($"Documento não encontrado para a chave {chave}.");

//            var xmlString = System.Text.Encoding.UTF8.GetString(doc.ArquivoBytes);

//            return gerador.GerarPdf(xmlString, doc.Ambiente!.Value, doc.Cancelada!.Value);
//        }
//    }
//}
