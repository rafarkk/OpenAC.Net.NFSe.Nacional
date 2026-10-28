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
public class TestIndexadorDocumentos
{
    // =========================================================================
    // ! Não é necessário usar o Indexador manualmente como mostrado abaixo se a opção Configuracoes.Arquivos.UsarIndexadorDocumentos estiver como 'true'.
    // * Se a opção Configuracoes.Arquivos.UsarIndexadorDocumentos estiver como 'true' o próprio OpenNFSe fará a indexação de forma automática durante os requests utilizando as configurações de arquivos.
    // =========================================================================

    #region ViaOpenNFSe
    // =========================================================================
    // ! Usar o Indexador via OpenNFSe é a melhor opção pois as configurações de path serão utilizadas.
    // =========================================================================
    [TestMethod]
    public void Indexador_ViaOpenNFSe_Padrao_UsaDiscoLocal()
    {
        //o Indexador vai usar o disco local automaticamente.
        var open = new OpenNFSeNacional();
        SetupOpenNFSeNacional.ConfiguracaoModeloAtual(open);

        var doc = open.IndexadorDocs.Indexar(new ReferenciaDocumento { ChaveReferencia = "123", NomeArquivo = "a.xml", CaminhoRelativo = "Docs", DocumentoPrestador = "1", DataDocumento = DateTime.Now });

        Assert.IsNotNull(doc);
        Assert.IsFalse(open.IndexadorDocs.PossuiStorageCustomizado);
    }

    [TestMethod]
    public void Indexador_ViaOpenNFSe_Custom_UsaGCS()
    {
        //o Indexador vai usar o provedor GCS customizado na injeção do construtor
        //para criar novos providers basta implementar a interface IStorageProvider
        var credential = CredentialFactory.FromFile<ServiceAccountCredential>("D:\\Credentials\\GCS\\credential-812-89c7184.json").ToGoogleCredential();
        var gcsClient = StorageClient.Create(credential);

        var gcsProvider = new GoogleCloudStorageProvider(
            gcsClient,
            "emissor-nfse",
            "documentos"
        );
        
        var open = new OpenNFSeNacional(gcsProvider);

        SetupOpenNFSeNacional.ConfiguracaoModeloAtual(open);

        var doc = open.IndexadorDocs.Indexar(new ReferenciaDocumento { ChaveReferencia = "123", NomeArquivo = "a.xml", CaminhoRelativo = "Docs", DocumentoPrestador = "1", DataDocumento = DateTime.Now });

        Assert.IsNotNull(doc);
        Assert.IsTrue(open.IndexadorDocs.PossuiStorageCustomizado);
    }
    #endregion ViaOpenNFSe

    #region standalone
    // =========================================================================
    // ! Ao usar o Indexador standalone as configurações de path serão ignoradas.
    // A possibilidade de usar dessa forma serve apenas paraque seja possivel acessar o indexador sem precisar instanciar um OpenNFSeNacional só pra isso.
    // =========================================================================
    //[TestMethod]
    //public void Indexador_Standalone_Padrao_UsaDiscoLocal()
    //{
    //    //instancia o Indexador standalone
    //    var arquivoConfig = new NFSeArquivoConfig { PathSalvar = "C:\\Temp\\NFSe" };
    //    var indexador = new IndexadorDocumentos(arquivoConfig);

    //    var doc = indexador.Indexar(new ReferenciaDocumento { ChaveReferencia = "123", NomeArquivo = "a.xml", CaminhoRelativo = "Docs", DocumentoPrestador = "1", DataDocumento = DateTime.Now });

    //    Assert.IsNotNull(doc);
    //    Assert.IsFalse(indexador.PossuiStorageCustomizado);
    //}

    //[TestMethod]
    //public void Indexador_Standalone_CustomProvider_UsaGCS()
    //{
    //    //instancia o Indexador standalone, mas injetando o storage do GCS
    //    var arquivoConfig = new NFSeArquivoConfig { PathSalvar = "C:\\Temp\\NFSe" };
    //    var gcsProvider = new GoogleCloudStorageProvider(null, "meu-bucket");

    //    var indexador = new IndexadorDocumentos(arquivoConfig, storageCustomizado: gcsProvider);

    //    var doc = indexador.Indexar(new ReferenciaDocumento { ChaveReferencia = "123", NomeArquivo = "a.xml", CaminhoRelativo = "Docs", DocumentoPrestador = "1", DataDocumento = DateTime.Now });

    //    Assert.IsNotNull(doc);
    //    Assert.IsTrue(indexador.PossuiStorageCustomizado);
    //}
    #endregion standalone
}