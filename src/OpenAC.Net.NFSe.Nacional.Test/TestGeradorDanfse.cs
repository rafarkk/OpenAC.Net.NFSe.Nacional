using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Newtonsoft.Json;
using OpenAC.Net.NFSe.Nacional.Common;
using OpenAC.Net.NFSe.Nacional.Common.Model;
using OpenAC.Net.NFSe.Nacional.Common.Types;
using OpenAC.Net.NFSe.Nacional.Danfse;
using OpenAC.Net.NFSe.Nacional.Danfse.Providers;
using OpenAC.Net.NFSe.Nacional.Indexador.Model;
using OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider;
using OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider.Providers;
using System.Data.SqlTypes;
using System.Diagnostics;

namespace OpenAC.Net.NFSe.Nacional.Test;

[TestClass]
public class TestGeradorDanfse
{
    #region ViaOpenNFSe
    // =========================================================================
    // ! A possibilidade de usar dessa forma Serve para acessar o gerador diretamente através de uma instância existente do OpenNFSeNacional, 
    // evitando instanciar um novo motor através da Factory desnecessariamente.
    // =========================================================================
    [TestMethod]
    public void GeradorPdf_ViaOpenNFSe_Padrao()
    {
        //usa o gerador que já vem embutido na classe principal
        var open = new OpenNFSeNacional();
        SetupOpenNFSeNacional.ConfiguracaoModeloAtual(open);

        string xmlMock = "<NFSe><infNFSe Id=\"1234\"><tpAmb>2</tpAmb></infNFSe></NFSe>";
        var pdf = open.GeradorPdf.GerarPdf(xmlMock, DFe.Core.Common.DFeTipoAmbiente.Homologacao, false);

        Assert.IsNotNull(pdf);
    }
    #endregion ViaOpenNFSe

    #region standalone
    [TestMethod]
    public void GeradorPdf_Standalone_Padrao()
    {
        //cria o gerador isolado direto da Factory, sem carregar o OpenNFSe
        var gerador = NFSeDanfseFactory.Create();

        string xmlMock = "<NFSe><infNFSe Id=\"1234\"><tpAmb>2</tpAmb></infNFSe></NFSe>";
        var pdf = gerador.GerarPdf(xmlMock, DFe.Core.Common.DFeTipoAmbiente.Homologacao, false);

        Assert.IsNotNull(pdf);
    }

    #endregion standalone



    // =========================================================================
    // ! Este método substitui a necessidade de usar o webservice oficial '/download-danfse'.
    // * Fluxo: Busca no índice -> Gera via XML local (documento precisa estar indexado) -> Consulta API (se não existir localmente) -> Gera via XML obtido.
    // =========================================================================
    [TestMethod]
    public async Task ExportarDanfse()
    {
        var openNFSeNacional = new OpenNFSeNacional();
        SetupOpenNFSeNacional.ConfiguracaoModeloAtual(openNFSeNacional);

        var chaveNFse = "35253002242250933000187000000000000724054029982347";

        //tenta buscar o PDF salvo
        var docPdf = openNFSeNacional.IndexadorDocs.ExportarDanfse(chaveNFse);

        //se não existir, faz o processo de consulta e geração
        if (docPdf == null)
        {
            var r = await openNFSeNacional.ConsultaChaveAsync(chaveNFse);

            Assert.IsTrue(r.Sucesso, "Falha ao consultar a chave na API.");

            bool isCancelada = r.Resultado.Lote.Any(dfe => dfe.TipoEvento == TipoEvento.CANCELAMENTO);
            var dfeOriginal = r.Resultado.Lote.FirstOrDefault(dfe => dfe.TipoDocumento == TipoDocumento.NFSE);

            Assert.IsNotNull(dfeOriginal, "XML da NFSe não encontrado no lote.");

            //gera e indexa (salva) o PDF
            var retorno = openNFSeNacional.IndexadorDocs.GerarIndexarPdfByXml(dfeOriginal.ArquivoXml, isCancelada);
            docPdf = retorno;
        }

        Assert.IsTrue(docPdf?.ArquivoBytes?.Length > 0);
    }
}