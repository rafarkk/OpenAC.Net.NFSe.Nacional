using OpenAC.Net.Core.Logging;
using OpenAC.Net.DFe.Core;
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

    ///// <summary>
    ///// Grava o arquivo no disco conforme o tipo especificado.
    ///// </summary>
    ///// <param name="tipo">Tipo do arquivo.</param>
    ///// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    ///// <param name="nomeArquivo">Nome do arquivo.</param>
    ///// <param name="documento">Documento do prestador.</param>
    ///// <param name="data">Data de emissão (opcional).</param>
    ///// <param name="incrementarNome">
    ///// Indica se o nome do arquivo deve ser incrementado caso já exista,
    ///// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    ///// </param>
    ///// <param name="referenciaDoc">Representa o registo de um documento no índice do indexador.</param>
    ///// <returns>
    ///// Uma tupla contendo:
    ///// - <c>caminhoCompleto</c>: caminho físico final onde o arquivo foi salvo.
    ///// - <c>documentoIndexadoId</c>: identificador do registro no banco de dados (gerado ou atualizado),
    /////   ou <c>null</c> caso a indexação não tenha ocorrido.
    ///// </returns>

    //protected virtual (string? caminhoCompletoArquivo, int? documentoIndexadoId) GravarArquivoEmDisco(TipoArquivo tipo, string conteudoArquivo, string nomeArquivo, string? documento, DateTime? data = null, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    //{
    //    var diretorioAbsoluto = tipo switch
    //    {
    //        TipoArquivo.Webservice => Path.Combine(Configuracao.Arquivos.GetPathEnvio(data ?? DateTime.Today, documento ?? string.Empty)),
    //        TipoArquivo.Rps => Path.Combine(Configuracao.Arquivos.GetPathDps(data ?? DateTime.Today, documento ?? string.Empty)),
    //        TipoArquivo.NFSe => Path.Combine(Configuracao.Arquivos.GetPathNFSe(data ?? DateTime.Today, documento ?? string.Empty)),
    //        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
    //    };

    //    var indexavel = tipo is TipoArquivo.NFSe or TipoArquivo.Rps && Configuracao.Arquivos.IndexarAutomaticamente && referenciaDoc != null;

    //    if (indexavel)
    //    {
    //        var caminhoRelativo = GetRelativePath(Configuracao.Arquivos.PathSalvar, diretorioAbsoluto);
    //        var motorNuvem = IndexadorDocs.Storage;

    //        var nomeFinal = incrementarNome ? GerarNomeUnico(caminhoRelativo, nomeArquivo, motorNuvem) : nomeArquivo;
    //        var caminhoAbsolutoFinal = Path.Combine(diretorioAbsoluto, nomeFinal);

    //        motorNuvem.Salvar(caminhoRelativo, nomeFinal, conteudoArquivo);

    //        if (Configuracao.Arquivos.Indexador.GarantirCopiaLocal && IndexadorDocs.PossuiStorageCustomizado)
    //        {
    //            if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
    //            File.WriteAllText(caminhoAbsolutoFinal, conteudoArquivo, Encoding.UTF8);
    //        }

    //        referenciaDoc.CaminhoRelativo = caminhoRelativo;
    //        referenciaDoc.NomeArquivo = nomeFinal;
    //        var docSalvo = IndexadorDocs.Indexar(referenciaDoc);

    //        return (caminhoAbsolutoFinal, docSalvo?.Id);
    //    }
    //    else
    //    {
    //        var caminhoFinal = incrementarNome ? GerarPathUnicoLocal(diretorioAbsoluto, nomeArquivo) : Path.Combine(diretorioAbsoluto, nomeArquivo);

    //        if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
    //        File.WriteAllText(caminhoFinal, conteudoArquivo, Encoding.UTF8);
    //        return (caminhoFinal, null);
    //    }
    //}

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
    protected virtual void GravarDpsEmDisco(string conteudoArquivo, string nomeArquivo, string? documento, DateTime data, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        if (Configuracao.Arquivos.Salvar == false) return;

        GravarArquivoEmDisco(TipoArquivo.Rps, conteudoArquivo, nomeArquivo, documento, data, incrementarNome, referenciaDoc);
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
    protected virtual void GravarNFSeEmDisco(string conteudoArquivo, string nomeArquivo, string? documento, DateTime data, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        if (Configuracao.Arquivos.Salvar == false) return;

        GravarArquivoEmDisco(TipoArquivo.NFSe, conteudoArquivo, nomeArquivo, documento, data, incrementarNome, referenciaDoc);
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
    /// <param name="referenciaDoc">Representa o registo de um documento no índice do indexador.</param>
    /// Indica se o nome do arquivo deve ser incrementado caso já exista,
    /// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    /// </param>
    protected virtual void GravarArquivoEmDisco(TipoArquivo tipo, string conteudoArquivo, string nomeArquivo, string? documento, DateTime? data = null, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        var diretorioAbsoluto = tipo switch
        {
            TipoArquivo.Webservice => Path.Combine(Configuracao.Arquivos.GetPathEnvio(data ?? DateTime.Today, documento ?? string.Empty)),
            TipoArquivo.Rps => Path.Combine(Configuracao.Arquivos.GetPathDps(data ?? DateTime.Today, documento ?? string.Empty)),
            TipoArquivo.NFSe => Path.Combine(Configuracao.Arquivos.GetPathNFSe(data ?? DateTime.Today, documento ?? string.Empty)),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
        };

        var caminhoFinal = incrementarNome ? GerarPathUnicoLocal(diretorioAbsoluto, nomeArquivo) : Path.Combine(diretorioAbsoluto, nomeArquivo);

        if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
        File.WriteAllText(caminhoFinal, conteudoArquivo, Encoding.UTF8);

        if (referenciaDoc != null && Configuracao.Arquivos.IndexarAutomaticamente)
            IndexarArquivo(diretorioAbsoluto, conteudoArquivo, nomeArquivo, referenciaDoc, incrementarNome);
    }

    ///// <summary>
    ///// Grava o xml da Dps no disco, gerenciando a criação do objeto de indexação automaticamente.
    ///// </summary>
    ///// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    ///// <param name="nomeArquivo">Nome do arquivo.</param>
    ///// <param name="documento">Documento do prestador.</param>
    ///// <param name="data">Data de emissão (opcional).</param>
    ///// <param name="chaveReferencia">ID da DPS (antes do envio) ou a Chave de Acesso da NFSe (após a emissão).</param>
    ///// <param name="incrementarNome">
    ///// Indica se o nome do arquivo deve ser incrementado caso já exista,
    ///// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    ///// </param>
    //protected virtual int? GravarDpsEmDisco(string conteudoArquivo, string nomeArquivo, string? documento, DateTime data, string chaveReferencia, bool incrementarNome = false)
    //{
    //    ReferenciaDocumento? refDoc = null;
    //    if (Configuracao.Arquivos.IndexarAutomaticamente)
    //    {
    //        refDoc = new ReferenciaDocumento
    //        {
    //            CriadoEm = DateTime.Now,
    //            DocumentoPrestador = documento ?? "",
    //            ChaveReferencia = chaveReferencia,
    //            NomeArquivo = nomeArquivo,
    //            DataDocumento = data,
    //            TipoDocumentoFiscal = TipoRegistroDocumento.DPS,
    //            Ambiente = Configuracao.WebServices.Ambiente
    //        };
    //    }

    //    // Chama o método original da biblioteca (Retrocompatibilidade garantida)
    //    GravarDpsEmDisco(conteudoArquivo, nomeArquivo, documento, data, incrementarNome, refDoc);

    //    return refDoc?.Id > 0 ? refDoc.Id : null;
    //}

    ///// <summary>
    ///// Grava o xml da NFSe no disco, gerenciando a criação do objeto de indexação automaticamente.
    ///// </summary>
    ///// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    ///// <param name="nomeArquivo">Nome do arquivo.</param>
    ///// <param name="documento">Documento do prestador.</param>
    ///// <param name="data">Data de emissão (opcional).</param>
    ///// <param name="chaveReferencia">ID da DPS (antes do envio) ou a Chave de Acesso da NFSe (após a emissão).</param>
    ///// <param name="incrementarNome">
    ///// Indica se o nome do arquivo deve ser incrementado caso já exista,
    ///// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    ///// </param>
    //protected virtual int? GravarNFSeEmDisco(string conteudoArquivo, string nomeArquivo, string? documento, DateTime data, string chaveReferencia, bool incrementarNome = false)
    //{
    //    ReferenciaDocumento? refDoc = null;
    //    if (Configuracao.Arquivos.IndexarAutomaticamente)
    //    {
    //        refDoc = new ReferenciaDocumento
    //        {
    //            CriadoEm = DateTime.Now,
    //            DocumentoPrestador = documento ?? "",
    //            ChaveReferencia = chaveReferencia,
    //            NomeArquivo = nomeArquivo,
    //            DataDocumento = data,
    //            TipoDocumentoFiscal = TipoRegistroDocumento.NFSE,
    //            Ambiente = Configuracao.WebServices.Ambiente
    //        };
    //    }

    //    GravarNFSeEmDisco(conteudoArquivo, nomeArquivo, documento, data, incrementarNome, refDoc);

    //    return refDoc?.Id > 0 ? refDoc.Id : null;
    //}

    ///// <summary>
    ///// Grava o Pedido de Registro de Evento no disco e indexa automaticamente.
    ///// </summary>
    //protected virtual int? GravarPedidoEventoEmDisco(string xml, string nomeArquivo, string? documento, DateTime data, string chaveNfse, TipoEvento tipoEvento, bool incrementarNome = false)
    //{
    //    ReferenciaDocumento? refDoc = null;
    //    if (Configuracao.Arquivos.IndexarAutomaticamente)
    //    {
    //        refDoc = new ReferenciaDocumento
    //        {
    //            CriadoEm = DateTime.Now,
    //            DocumentoPrestador = documento ?? "",
    //            ChaveReferencia = chaveNfse,
    //            NomeArquivo = nomeArquivo,
    //            DataDocumento = data,
    //            TipoDocumentoFiscal = TipoRegistroDocumento.PEDIDO_REGISTRO_EVENTO,
    //            Ambiente = Configuracao.WebServices.Ambiente,
    //            TipoEvento = tipoEvento
    //        };
    //    }

    //    // Chama o método original da lib para manter compatibilidade
    //    GravarDpsEmDisco(xml, nomeArquivo, documento, data, incrementarNome, refDoc);

    //    return refDoc?.Id > 0 ? refDoc.Id : null;
    //}

    ///// <summary>
    ///// Grava o Evento confirmado retornado pela SEFAZ e indexa automaticamente.
    ///// </summary>
    //protected virtual int? GravarEventoEmDisco(string xml, string nomeArquivo, string? documento, DateTime data, string chaveNfse, TipoEvento tipoEvento, int sequencial, bool incrementarNome = false)
    //{
    //    ReferenciaDocumento? refDoc = null;
    //    if (Configuracao.Arquivos.IndexarAutomaticamente)
    //    {
    //        refDoc = new ReferenciaDocumento
    //        {
    //            CriadoEm = DateTime.Now,
    //            DocumentoPrestador = documento ?? "",
    //            ChaveReferencia = chaveNfse,
    //            NomeArquivo = nomeArquivo,
    //            DataDocumento = data,
    //            TipoDocumentoFiscal = TipoRegistroDocumento.EVENTO,
    //            Ambiente = Configuracao.WebServices.Ambiente,
    //            TipoEvento = tipoEvento,
    //            NumeroSequencial = sequencial
    //        };
    //    }

    //    // Chama o método original de gravar NFSe da lib
    //    GravarNFSeEmDisco(xml, nomeArquivo, documento, data, incrementarNome, refDoc);

    //    return refDoc?.Id > 0 ? refDoc.Id : null;
    //}

    /// <summary>
    /// Constrói o objeto de referência para o Indexador apenas se a configuração de indexação automática estiver ativa.
    /// </summary>
    protected ReferenciaDocumento? CriarReferenciaIndexacao(
        TipoRegistroDocumento tipoDoc, string chave, string nomeArquivo, string documento, DateTime data, TipoEvento? evento = null, int? seq = null)
    {
        if (!Configuracao.Arquivos.IndexarAutomaticamente) return null;

        return new ReferenciaDocumento
        {
            CriadoEm = DateTime.Now,
            DocumentoPrestador = documento ?? string.Empty,
            ChaveReferencia = chave,
            NomeArquivo = nomeArquivo,
            DataDocumento = data,
            TipoDocumentoFiscal = tipoDoc,
            Ambiente = Configuracao.WebServices.Ambiente,
            TipoEvento = evento,
            NumeroSequencial = seq
        };
    }

    /// <summary>
    /// Grava o arquivo no disco conforme o tipo especificado.
    /// </summary>
    /// <param name="diretorioAbsoluto">Diretório absoluto onde o arquivo foi salvo.</param>
    /// <param name="conteudoArquivo">Conteúdo do arquivo.</param>
    /// <param name="nomeArquivo">Nome do arquivo.</param>
    /// <param name="incrementarNome">
    /// <param name="referenciaDoc">Representa o registo de um documento no índice do indexador.</param>
    /// Indica se o nome do arquivo deve ser incrementado caso já exista,
    /// adicionando um sufixo numérico (_1, _2, etc.) para evitar sobrescrita.
    /// </param>
    protected virtual void IndexarArquivo(string diretorioAbsoluto, string conteudoArquivo, string nomeArquivo, ReferenciaDocumento referenciaDoc, bool incrementarNome = false)
    {
        bool permiteIndexar = referenciaDoc.TipoDocumentoFiscal switch
        {
            TipoRegistroDocumento.DPS => Configuracao.Arquivos.Indexador.IndexacaoAutomatica.IndexarDps,
            TipoRegistroDocumento.PEDIDO_REGISTRO_EVENTO => Configuracao.Arquivos.Indexador.IndexacaoAutomatica.IndexarPedidoEvento,
            TipoRegistroDocumento.NFSE => Configuracao.Arquivos.Indexador.IndexacaoAutomatica.IndexarNFSe,
            TipoRegistroDocumento.EVENTO => Configuracao.Arquivos.Indexador.IndexacaoAutomatica.IndexarEvento,
            _ => false
        };

        if (!permiteIndexar) return;

        var caminhoRelativo = GetRelativePath(Configuracao.Arquivos.PathSalvar, diretorioAbsoluto);
        var motorNuvem = IndexadorDocs.Storage;

        var nomeFinal = incrementarNome ? GerarNomeUnico(caminhoRelativo, nomeArquivo, motorNuvem) : nomeArquivo;
        var caminhoAbsolutoFinal = Path.Combine(diretorioAbsoluto, nomeFinal);

        motorNuvem.Salvar(caminhoRelativo, nomeFinal, conteudoArquivo);

        if (Configuracao.Arquivos.Indexador.GarantirCopiaLocal && IndexadorDocs.PossuiStorageCustomizado)
        {
            if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
            File.WriteAllText(caminhoAbsolutoFinal, conteudoArquivo, Encoding.UTF8);
        }

        referenciaDoc.CaminhoRelativo = caminhoRelativo;
        referenciaDoc.NomeArquivo = nomeFinal;
        
        var docSalvo = IndexadorDocs.Indexar(referenciaDoc);
        if (docSalvo != null)
        {
            referenciaDoc.Id = docSalvo.Id;
        }
    }

    /// <summary>
    /// Gera um nome de arquivo único para um provedor de armazenamento, adicionando um sufixo numérico (ex: _1, _2) caso o arquivo já exista.
    /// </summary>
    /// <param name="caminhoRelativo">O caminho relativo ou diretório alvo no provedor.</param>
    /// <param name="nomeArquivo">O nome original do arquivo com sua extensão.</param>
    /// <param name="motor">O provedor de armazenamento (<see cref="IStorageProvider"/>) responsável por verificar se o arquivo já existe.</param>
    /// <returns>Um nome de arquivo único, preservando a extensão original.</returns>
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

    /// <summary>
    /// Gera um caminho físico local único, adicionando um sufixo numérico (ex: _1, _2) ao nome do arquivo caso ele já exista no diretório especificado.
    /// </summary>
    /// <param name="diretorio">O caminho do diretório local onde o arquivo será salvo.</param>
    /// <param name="nomeArquivo">O nome original do arquivo com sua extensão.</param>
    /// <returns>O caminho completo e único para o arquivo no sistema de arquivos local.</returns>
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
    /// Calcula o caminho relativo entre um diretório base absoluto e um caminho de destino absoluto.
    /// </summary>
    /// <param name="relativeTo">O caminho absoluto que servirá como base (ponto de partida).</param>
    /// <param name="path">O caminho absoluto de destino.</param>
    /// <returns>Uma string representando o caminho relativo do diretório base até o destino.</returns>
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

    /// <summary>
    /// Verifica se o arquivo já existe antes de escrever, e incrementa um sufixo numérico (ex: _1, _2) caso exista, gerando um caminho único.
    /// </summary>
    /// <param name="diretorio">Diretório onde o arquivo será salvo.</param>
    /// <param name="nomeArquivo">Nome original do arquivo com sua extensão.</param>
    /// <returns>O caminho completo e único para o arquivo no sistema de arquivos local.</returns>
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

    #endregion Methods
}