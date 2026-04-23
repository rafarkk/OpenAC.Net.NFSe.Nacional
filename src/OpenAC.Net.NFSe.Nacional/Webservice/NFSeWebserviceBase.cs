using OpenAC.Net.Core.Logging;
using OpenAC.Net.DFe.Core;
using OpenAC.Net.DFe.Core.Extensions;
using OpenAC.Net.NFSe.Nacional.Common;
using OpenAC.Net.NFSe.Nacional.Common.Model;
using OpenAC.Net.NFSe.Nacional.Common.Types;
using OpenAC.Net.NFSe.Nacional.Indexador.Model;
using OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Schema;

namespace OpenAC.Net.NFSe.Nacional.Webservice;

/// <summary>
/// Classe base para comunicação com os webservices de NFS-e (Nacional).
/// Contém funcionalidades comuns usadas pelos webservices, como envio HTTP,
/// validação de XML via schema e gravação de arquivos em disco.
/// Implementa <see cref="IOpenLog"/> para integração com o sistema de logging.
/// </summary>
public abstract class NFSeWebserviceBase : IOpenLog
{
    #region Internal Types

    /// <summary>
    /// Tipos de arquivos manipulados pelo webservice.
    /// </summary>
    protected enum TipoArquivo
    {
        /// <summary>
        /// Arquivo de comunicação com o webservice.
        /// </summary>
        Webservice,
        /// <summary>
        /// Arquivo de RPS (Recibo Provisório de Serviços).
        /// </summary>
        Rps,
        /// <summary>
        /// Arquivo de NFS-e (Nota Fiscal de Serviço eletrônica).
        /// </summary>
        NFSe
    }

    #endregion Internal Types

    #region Constructors

    /// <summary>
    /// Inicializa uma nova instância da classe <see cref="NFSeWebserviceBase"/>.
    /// </summary>
    /// <param name="configuracaoNFSe">Configuração da NFSe.</param>
    /// <param name="serviceInfo"></param>
    /// <param name="indexadorDocs">Serviço responsável por indexar e localizar os documentos no sistema de arquivos.</param>
    protected NFSeWebserviceBase(ConfiguracaoNFSe configuracaoNFSe, NFSeServiceInfo serviceInfo, IndexadorDocumentos indexadorDocs)
    {
        Configuracao = configuracaoNFSe;
        ServiceInfo = serviceInfo;
        IndexadorDocs = indexadorDocs;
    }

    /// <summary>
    /// Inicializa uma nova instância da classe <see cref="NFSeWebserviceBase"/>.
    /// </summary>
    /// <param name="configuracaoNFSe">Configuração da NFSe.</param>
    /// <param name="serviceInfo"></param>
    protected NFSeWebserviceBase(ConfiguracaoNFSe configuracaoNFSe, NFSeServiceInfo serviceInfo)
    {
        Configuracao = configuracaoNFSe;
        ServiceInfo = serviceInfo;
    }
    #endregion Constructors

    #region Properties

    /// <summary>
    /// Configuração da NFSe utilizada pelo webservice.
    /// </summary>
    protected ConfiguracaoNFSe Configuracao { get; }

    /// <summary>
    /// Informações do serviço (endpoints, timeout e metadados) utilizadas pelo webservice.
    /// </summary>
    public NFSeServiceInfo ServiceInfo { get; }

    /// <summary>
    /// Instância do serviço de índice utilizada para catalogar os arquivos gerados e resolver seus caminhos físicos de armazenamento.
    /// </summary>
    protected IndexadorDocumentos IndexadorDocs { get; }


    #endregion Properties

    #region Methods

    /// <summary>
    /// Retorna o DANFSe de uma NFS-e a partir de sua chave de acesso.
    /// </summary>
    /// <param name="chave">Chave de acesso da NFS-e.</param>
    /// <returns>Array de bytes contendo o DANFSe.</returns>
    public abstract Task<byte[]> DownloadDANFSeAsync(string chave);

    /// <summary>
    /// Distribui os DF-e para contribuintes relacionados à NFS-e.
    /// </summary>
    /// <param name="nsu">Número NSU.</param>
    /// <returns>Resposta da consulta contendo os DF-e.</returns>
    public abstract Task<NFSeResponse<RespostaConsultaDFe>> ConsultaNsuAsync(int nsu);

    /// <summary>
    /// Distribui os DF-e vinculados à chave de acesso informada.
    /// </summary>
    /// <param name="chave">Chave de acesso da NFS-e.</param>
    /// <returns>Resposta da consulta contendo os DF-e.</returns>
    public abstract Task<NFSeResponse<RespostaConsultaDFe>> ConsultaChaveAsync(string chave);

    /// <summary>
    /// Retorna a chave de acesso da NFS-e a partir do identificador do DPS.
    /// </summary>
    /// <param name="id">Identificação do DPS.</param>
    /// <returns>Resposta da consulta contendo a chave de acesso.</returns>
    public abstract Task<NFSeResponse<RespostaConsultaChaveDps>> ConsultaChaveDpsAsync(string id);

    /// <summary>
    /// Verifica se uma NFS-e foi emitida a partir do Id do DPS.
    /// </summary>
    /// <param name="id">Identificação do DPS.</param>
    /// <returns>True se existir, caso contrário false.</returns>
    public abstract Task<bool> ConsultaExisteDpsAsync(string id);

    /// <summary>
    /// Recepciona o Pedido de Registro de Evento e gera Eventos de NFS-e, crédito, débito e apuração.
    /// </summary>
    /// <param name="evento">Evento a ser enviado.</param>
    /// <returns>Resposta do envio do evento.</returns>
    public abstract Task<NFSeResponse<RespostaEnvioEvento>> EnviarEventoAsync(PedidoRegistroEvento evento);

    /// <summary>
    /// Recepciona a DPS e gera a NFS-e de forma síncrona.
    /// </summary>
    /// <param name="dps">DPS a ser enviada.</param>
    /// <returns>Resposta do envio da DPS.</returns>
    public abstract Task<NFSeResponse<RespostaEnvioDps>> EnviarAsync(Dps dps);

    /// <summary>
    /// Envia uma requisição HTTP para o webservice.
    /// </summary>
    /// <param name="content">Conteúdo da requisição.</param>
    /// <param name="method">Método HTTP.</param>
    /// <param name="url">URL do serviço.</param>
    /// <returns>Resposta HTTP.</returns>
    protected virtual async Task<HttpResponseMessage> SendAsync(HttpContent? content, HttpMethod method, string url)
    {
        var handler = new HttpClientHandler();

        handler.SslProtocols = (SslProtocols)Configuracao.WebServices.Protocolos;
        handler.ClientCertificates.Add(Configuracao.Certificados.ObterCertificado());
        var client = new HttpClient(handler);

        var request = new HttpRequestMessage(method, url);

        var assemblyName = GetType().Assembly.GetName();
        var productValue = new ProductInfoHeaderValue("OpenAC.Net.NFSe.Nacional", assemblyName!.Version!.ToString());
        var commentValue = new ProductInfoHeaderValue("(+https://github.com/OpenAC-Net/OpenAC.Net.NFSe.Nacional)");

        request.Headers.UserAgent.Add(productValue);
        request.Headers.UserAgent.Add(commentValue);
        request.Content = content;

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Valida o XML de acordo com o schema.
    /// </summary>
    /// <param name="schema">O schema que será usado na verificação.</param>
    /// <param name="xml">Conteúdo XML a ser validado.</param>
    /// <param name="versao">Versão do Schema a ser carregado</param>
    /// <exception cref="XmlSchemaException">Lançada se o arquivo de schema não for encontrado.</exception>
    /// <exception cref="XmlSchemaValidationException">Lançada se houver erros de validação.</exception>
    protected virtual void ValidarSchema(SchemaNFSe schema, string xml, VersaoNFSe versao = VersaoNFSe.Ve100)
    {
        if (!Configuracao.WebServices.ValidarSchemas)
            return;

        Configuracao.Arquivos.VersaoSchema = versao;
        var schemaFile = Configuracao.Arquivos.GetSchema(schema);
        if (!File.Exists(schemaFile))
            throw new XmlSchemaException("Nao encontrou o arquivo schema do xml => " + schemaFile);

        if (XmlSchemaValidation.ValidarXml(xml, schemaFile, out var errosSchema, out _)) return;

        throw new XmlSchemaValidationException("Erros gerado ao validar o schema do xml" + Environment.NewLine +
                                               string.Join(Environment.NewLine, errosSchema));
    }

    /// <summary>
    /// Grava o xml da Dps no disco.
    /// </summary>
    /// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    /// <param name="nomeArquivo">Nome do arquivo.</param>
    /// <param name="documento">Documento do prestador.</param>
    /// <param name="data">Data de emissão (opcional).</param>
    /// <param name="incrementarNome">
    /// Indica se o nome do arquivo deve ser incrementado caso já exista,
    /// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    /// </param>
    /// <param name="referenciaDoc">Representa o registo de um documento no índice do indexador.</param>
    /// <returns>
    /// Uma tupla contendo:
    /// - <c>caminhoCompleto</c>: caminho físico final onde o arquivo foi salvo.
    /// - <c>documentoIndexadoId</c>: identificador do registro no banco de dados (gerado ou atualizado),
    ///   ou <c>null</c> caso a indexação não tenha ocorrido.
    /// </returns>
    protected virtual (string? caminhoCompletoArquivo, int? documentoIndexadoId) GravarDpsEmDisco(string conteudoArquivo, string nomeArquivo, string? documento, DateTime data, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        if (Configuracao.Arquivos.Salvar == false) return (null, null);

        return GravarArquivoEmDisco(TipoArquivo.Rps, conteudoArquivo, nomeArquivo, documento, data, incrementarNome, referenciaDoc);
    }

    /// <summary>
    /// Grava o xml da NFSe no disco.
    /// </summary>
    /// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    /// <param name="nomeArquivo">Nome do arquivo.</param>
    /// <param name="documento">Documento do prestador.</param>
    /// <param name="data">Data de emissão (opcional).</param>
    /// <param name="incrementarNome">
    /// Indica se o nome do arquivo deve ser incrementado caso já exista,
    /// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    /// </param>
    /// <param name="referenciaDoc">Representa o registo de um documento no índice do indexador.</param>
    /// <returns>
    /// Uma tupla contendo:
    /// - <c>caminhoCompleto</c>: caminho físico final onde o arquivo foi salvo.
    /// - <c>documentoIndexadoId</c>: identificador do registro no banco de dados (gerado ou atualizado),
    ///   ou <c>null</c> caso a indexação não tenha ocorrido.
    /// </returns>
    protected virtual (string? caminhoCompletoArquivo, int? documentoIndexadoId) GravarNFSeEmDisco(string conteudoArquivo, string nomeArquivo, string? documento, DateTime data, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        if (Configuracao.Arquivos.Salvar == false) return (null, null);

        return GravarArquivoEmDisco(TipoArquivo.NFSe, conteudoArquivo, nomeArquivo, documento, data, incrementarNome, referenciaDoc);
    }

    /// <summary>
    /// Grava o xml de comunicação com o webservice no disco.
    /// </summary>
    /// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    /// <param name="nomeArquivo">Nome do arquivo.</param>
    /// <param name="documento">Documento do prestador.</param>
    protected virtual void GravarArquivoEmDisco(string conteudoArquivo, string nomeArquivo, string? documento)
    {
        if (!Configuracao.Geral.Salvar) return;

        GravarArquivoEmDisco(TipoArquivo.Webservice, conteudoArquivo, nomeArquivo, documento);
    }

    /// <summary>
    /// Grava o arquivo no disco conforme o tipo especificado.
    /// </summary>
    /// <param name="tipo">Tipo do arquivo.</param>
    /// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    /// <param name="nomeArquivo">Nome do arquivo.</param>
    /// <param name="documento">Documento do prestador.</param>
    /// <param name="data">Data de emissão (opcional).</param>
    /// <param name="incrementarNome">
    /// Indica se o nome do arquivo deve ser incrementado caso já exista,
    /// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    /// </param>
    /// <param name="referenciaDoc">Representa o registo de um documento no índice do indexador.</param>
    /// <returns>
    /// Uma tupla contendo:
    /// - <c>caminhoCompleto</c>: caminho físico final onde o arquivo foi salvo.
    /// - <c>documentoIndexadoId</c>: identificador do registro no banco de dados (gerado ou atualizado),
    ///   ou <c>null</c> caso a indexação não tenha ocorrido.
    /// </returns>

    protected virtual (string? caminhoCompletoArquivo, int? documentoIndexadoId) GravarArquivoEmDisco(TipoArquivo tipo, string conteudoArquivo, string nomeArquivo, string? documento, DateTime? data = null, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        var diretorioAbsoluto = tipo switch
        {
            TipoArquivo.Webservice => Path.Combine(Configuracao.Arquivos.GetPathEnvio(data ?? DateTime.Today, documento ?? string.Empty)),
            TipoArquivo.Rps => Path.Combine(Configuracao.Arquivos.GetPathDps(data ?? DateTime.Today, documento ?? string.Empty)),
            TipoArquivo.NFSe => Path.Combine(Configuracao.Arquivos.GetPathNFSe(data ?? DateTime.Today, documento ?? string.Empty)),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
        };

        var indexavel = tipo is TipoArquivo.NFSe or TipoArquivo.Rps && Configuracao.Arquivos.UsarIndexadorDocumentos && referenciaDoc != null;

        if (indexavel)
        {
            var caminhoRelativo = GetRelativePath(Configuracao.Arquivos.PathSalvar, diretorioAbsoluto);
            var motorNuvem = IndexadorDocs.Storage;

            var nomeFinal = !incrementarNome ? GerarNomeUnico(caminhoRelativo, nomeArquivo, motorNuvem) : nomeArquivo;
            var caminhoAbsolutoFinal = Path.Combine(diretorioAbsoluto, nomeFinal);

            motorNuvem.Salvar(caminhoRelativo, nomeFinal, conteudoArquivo);

            if (Configuracao.Arquivos.Indexador.ManterCopiaLocal && IndexadorDocs.PossuiStorageCustomizado)
            {
                if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
                File.WriteAllText(caminhoAbsolutoFinal, conteudoArquivo, Encoding.UTF8);
            }

            referenciaDoc.CaminhoRelativo = caminhoRelativo;
            referenciaDoc.NomeArquivo = nomeFinal;
            var docSalvo = IndexadorDocs.Indexar(referenciaDoc);

            return (caminhoAbsolutoFinal, docSalvo?.Id);
        }
        else
        {
            var caminhoFinal = !incrementarNome ? GerarPathUnicoLocal(diretorioAbsoluto, nomeArquivo) : Path.Combine(diretorioAbsoluto, nomeArquivo);

            if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
            File.WriteAllText(caminhoFinal, conteudoArquivo, Encoding.UTF8);
            return (caminhoFinal, null);
        }
    }

    //protected virtual (string? caminhoCompletoArquivo, int? documentoIndexadoId) GravarArquivoEmDisco(TipoArquivo tipo, string conteudoArquivo, string nomeArquivo, string? documento, DateTime? data = null, bool incrementarNome = false, DocumentoIndexado docIndexado = null)
    //{
    //    var diretorio = tipo switch
    //    {
    //        TipoArquivo.Webservice => Path.Combine(Configuracao.Arquivos.GetPathEnvio(data ?? DateTime.Today, documento ?? string.Empty)),
    //        TipoArquivo.Rps => Path.Combine(Configuracao.Arquivos.GetPathDps(data ?? DateTime.Today, documento ?? string.Empty)),
    //        TipoArquivo.NFSe => Path.Combine(Configuracao.Arquivos.GetPathNFSe(data ?? DateTime.Today, documento ?? string.Empty)),
    //        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
    //    };

    //    var caminhoFinal = !incrementarNome ? GerarPathUnico(diretorio, nomeArquivo) : Path.Combine(diretorio, nomeArquivo);

    //    File.WriteAllText(caminhoFinal, conteudoArquivo, Encoding.UTF8);

    //    if (!Configuracao.Arquivos.IndexarDocumentosSalvos || docIndexado == null || tipo is not (TipoArquivo.NFSe or TipoArquivo.Rps))
    //        return (caminhoFinal, null);

    //    //arquivoIndex.CaminhoRelativo = diretorio;
    //    docIndexado.CaminhoRelativo = GetRelativePath(Configuracao.Arquivos.PathSalvar, diretorio);
    //    return (caminhoFinal, IndexadorDocs.Indexar(docIndexado)?.Id);
    //}

    private string GerarNomeUnico(string caminhoRelativo, string nomeArquivo, IStorageProvider motor)
    {
        if (!motor.Existe(caminhoRelativo, nomeArquivo)) return nomeArquivo;

        var semExtensao = Path.GetFileNameWithoutExtension(nomeArquivo);
        var extensao = Path.GetExtension(nomeArquivo);
        var contador = 1;
        string novoNome;
        do
        {
            novoNome = $"{semExtensao}_{contador++}{extensao}";
        } while (motor.Existe(caminhoRelativo, novoNome));

        return novoNome;
    }

    private static string GerarPathUnicoLocal(string diretorio, string nomeArquivo)
    {
        var caminho = Path.Combine(diretorio, nomeArquivo);
        if (!File.Exists(caminho)) return caminho;

        var semExtensao = Path.GetFileNameWithoutExtension(nomeArquivo);
        var extensao = Path.GetExtension(nomeArquivo);
        var contador = 1;
        do
        {
            caminho = Path.Combine(diretorio, $"{semExtensao}_{contador++}{extensao}");
        } while (File.Exists(caminho));

        return caminho;
    }

    /// <summary>
    /// Verifica se o arquivo já existe antes de escrever, e incrementa um sufixo numérico caso exista.
    /// </summary>
    /// <param name="diretorio">Diretório onde o arquivo será salvo.</param>
    /// <param name="nomeArquivo">Nome do arquivo.</param>
    private static string GerarPathUnico(string diretorio, string nomeArquivo)
    {
        var caminho = Path.Combine(diretorio, nomeArquivo);

        if (!File.Exists(caminho))
            return caminho;

        var semExtensao = Path.GetFileNameWithoutExtension(nomeArquivo);
        var extensao = Path.GetExtension(nomeArquivo);
        var contador = 1;

        do
        {
            var novoNome = $"{semExtensao}_{contador++}{extensao}";
            caminho = Path.Combine(diretorio, novoNome);
        }
        while (File.Exists(caminho));

        return caminho;
    }

    private static string GetRelativePath(string relativeTo, string path)
    {
        var root = relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString())
                   ? relativeTo : relativeTo + Path.DirectorySeparatorChar;

        Uri rootUri = new Uri(root);
        Uri fullUri = new Uri(path);

        Uri relativeUri = rootUri.MakeRelativeUri(fullUri);

        return Uri.UnescapeDataString(relativeUri.ToString())
                  .Replace('/', Path.DirectorySeparatorChar);
    }

    #endregion Methods
}