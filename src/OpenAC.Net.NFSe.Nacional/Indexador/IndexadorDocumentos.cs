using Microsoft.Data.Sqlite;
using OpenAC.Net.Core.Logging;
using OpenAC.Net.DFe.Core.Common;
using OpenAC.Net.NFSe.Nacional.Common;
using OpenAC.Net.NFSe.Nacional.Common.Types;
using OpenAC.Net.NFSe.Nacional.Danfse;
using OpenAC.Net.NFSe.Nacional.Danfse.Providers;
using OpenAC.Net.NFSe.Nacional.Indexador.Model;
using OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider;
using OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;



/// <summary>
/// Serviço responsável pela indexação, busca, atualização e exportação de documentos fiscais (NFS-e) 
/// utilizando um banco de dados SQLite para armazenamento de metadados.
/// </summary>
public class IndexadorDocumentos : IOpenLog
{
    private readonly NFSeArquivoConfig _arquivoConfig;
    private readonly IStorageProvider? _storageCustomizado;

    /// <summary>
    /// Obtém o provedor responsável pela geração do DANFSE em PDF.
    /// </summary>
    internal INFSeDanfseProvider GeradorPdf { get; }

    /// <summary>
    /// Obtém o provedor de armazenamento em uso. 
    /// Retorna o provedor customizado, se injetado; caso contrário, utiliza o <see cref="LocalStorageProvider"/> padrão.
    /// </summary>
    public IStorageProvider Storage => _storageCustomizado ?? new LocalStorageProvider(_arquivoConfig.PathSalvar);

    /// <summary>
    /// Indica se a instância está utilizando um provedor de armazenamento customizado em vez do armazenamento local padrão.
    /// </summary>
    public bool PossuiStorageCustomizado => !(Storage is LocalStorageProvider);

    /// <summary>
    /// Número máximo de chaves permitidas por requisição no método de baixar documentos do inexador.
    /// </summary>
    private const int MaxChavesPermitidas = 10;

    private volatile bool _bancoInicializado = false;
    private readonly object _lockInit = new();


    //public IndexadorDocumentos(NFSeArquivoConfig arquivoConfig, IStorageProvider? storageCustomizado = null)
    //    : this(arquivoConfig, storageCustomizado, NFSeDanfseFactory.Create())
    //{
    //}

    /// <summary>
    /// Inicializa uma nova instância da classe <see cref="IndexadorDocumentos"/>.
    /// </summary>
    /// <param name="arquivoConfig">Representa a configuração para arquivos NFSe.</param>
    /// <param name="storageCustomizado">Provedor de armazenamento customizado opcional.</param>
    /// <param name="geradorPdf">Provedor responsável pela geração do DANFSE em PDF.</param>
    internal IndexadorDocumentos(NFSeArquivoConfig arquivoConfig, IStorageProvider? storageCustomizado, INFSeDanfseProvider geradorPdf)
    {
        _arquivoConfig = arquivoConfig;
        _storageCustomizado = storageCustomizado;
        GeradorPdf = geradorPdf;
    }

    /// <summary>
    /// Inicializa a estrutura do banco de dados SQLite, configurando pragmas de performance (WAL) 
    /// e criando as tabelas e índices necessários caso não existam.
    /// </summary>
    /// <param name="con">Conexão aberta com o banco de dados SQLite.</param>
    private void InicializarBancoEstrutura(SqliteConnection con)
    {
        using var cmd = con.CreateCommand();

        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=10000;";
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS arquivos (
                id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                criado_em             TEXT    NOT NULL DEFAULT (datetime('now','utc')),
                documento_prestador   TEXT    NOT NULL,
                chave_referencia      TEXT    NOT NULL,
                caminho_relativo      TEXT    NOT NULL,
                nome_arquivo          TEXT    NOT NULL,
                tipo_documento_fiscal TEXT    NOT NULL,
                ambiente              TEXT    NOT NULL,
                data_documento        TEXT    NOT NULL,
                tipo_evento           TEXT,
                numero_sequencial     INTEGER,
                UNIQUE(caminho_relativo, nome_arquivo)
            );
            CREATE INDEX IF NOT EXISTS idx_chave_referencia      ON arquivos(chave_referencia);
            CREATE INDEX IF NOT EXISTS idx_tipo_documento_fiscal ON arquivos(tipo_documento_fiscal);
            CREATE INDEX IF NOT EXISTS idx_tipo_evento           ON arquivos(tipo_evento);
            CREATE INDEX IF NOT EXISTS idx_documento_prestador   ON arquivos(documento_prestador);
            CREATE INDEX IF NOT EXISTS idx_data_documento        ON arquivos(data_documento);
            CREATE INDEX IF NOT EXISTS idx_numero_sequencial     ON arquivos(numero_sequencial);
            CREATE INDEX IF NOT EXISTS idx_ambiente ON arquivos(ambiente);
        """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Insere um novo registro no índice do banco de dados. 
    /// Se já existir um registro com o mesmo caminho e nome de arquivo, os dados são atualizados (Upsert).
    /// </summary>
    /// <param name="referenciaDoc">Os dados do documento a ser indexado.</param>
    /// <returns>Retorna o <see cref="ReferenciaDocumento"/> atualizado com o ID gerado pelo banco, ou null em caso de falha/timeout.</returns>
    public ReferenciaDocumento? Indexar(ReferenciaDocumento referenciaDoc)
    {
        try
        {
            using var con = Conectar();
            if (con == null) return null;

            using var cmd = con.CreateCommand();

            cmd.CommandText = """
                INSERT INTO arquivos (
                    chave_referencia, nome_arquivo, caminho_relativo, tipo_documento_fiscal,
                    ambiente, tipo_evento, documento_prestador, data_documento, numero_sequencial
                )
                VALUES (
                    $chave, $nome, $caminho, $tipo, $ambiente, $evento, $prestador, $data, $nseq
                )
                ON CONFLICT(caminho_relativo, nome_arquivo) DO UPDATE SET
                    chave_referencia = excluded.chave_referencia,
                    ambiente = excluded.ambiente,
                    tipo_evento = excluded.tipo_evento,
                    data_documento = excluded.data_documento,
                    numero_sequencial = excluded.numero_sequencial
                RETURNING *;
            """;

            cmd.Parameters.AddWithValue("$chave", referenciaDoc.ChaveReferencia);
            cmd.Parameters.AddWithValue("$nome", referenciaDoc.NomeArquivo);
            cmd.Parameters.AddWithValue("$caminho", referenciaDoc.CaminhoRelativo);
            cmd.Parameters.AddWithValue("$tipo", referenciaDoc.TipoDocumentoFiscal.ToString());
            cmd.Parameters.AddWithValue("$ambiente", referenciaDoc.Ambiente.ToString());
            cmd.Parameters.AddWithValue("$evento", referenciaDoc.TipoEvento?.ToString() ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$prestador", referenciaDoc.DocumentoPrestador ?? "");
            cmd.Parameters.AddWithValue("$data", referenciaDoc.DataDocumento.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$nseq", referenciaDoc.NumeroSequencial ?? (object)DBNull.Value);

            return LerResultados(cmd).First();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
        {
            this.Log().Warn($"[Aviso] Timeout ao indexar arquivo referência {referenciaDoc.ChaveReferencia}.");
            return null;
        }
        catch (Exception ex)
        {
            this.Log().Error($"[Indexador] Erro ao indexar: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Realiza uma busca paginada de documentos no índice com base nos critérios especificados no filtro.
    /// </summary>
    /// <param name="filtro">Objeto contendo os parâmetros de filtro (chave, prestador, datas, etc.) e dados de paginação.</param>
    /// <returns>Retorna um <see cref="ResultadoPaginado{DocumentoIndexado}"/> contendo o total de itens e a lista da página atual.</returns>
    public ResultadoPaginado<ReferenciaDocumento> BuscarPorFiltro(FiltroReferenciaDocumento filtro)
    {
        using var con = Conectar(lancarExcecoes: true);
        using var cmd = con.CreateCommand();

        var where = new List<string>();

        if (!string.IsNullOrWhiteSpace(filtro.ChaveReferencia))
        {
            where.Add("chave_referencia = $chaveReferencia");
            cmd.Parameters.AddWithValue("$chaveReferencia", filtro.ChaveReferencia);
        }
        if (!string.IsNullOrWhiteSpace(filtro.DocumentoPrestador))
        {
            where.Add("documento_prestador = $documentoPrestador");
            cmd.Parameters.AddWithValue("$documentoPrestador", filtro.DocumentoPrestador);
        }
        if (filtro.TipoDocumentoFiscal is not null)
        {
            where.Add("tipo_documento_fiscal = $tipoDocumento");
            cmd.Parameters.AddWithValue("$tipoDocumento", filtro.TipoDocumentoFiscal.ToString());
        }
        if (filtro.Ambiente is not null)
        {
            where.Add("ambiente = $ambiente");
            cmd.Parameters.AddWithValue("$ambiente", filtro.Ambiente.ToString());
        }
        if (filtro.DataDe is not null)
        {
            where.Add("data_documento >= $dataDe");
            cmd.Parameters.AddWithValue("$dataDe", filtro.DataDe.Value.ToString("yyyy-MM-dd 00:00:00", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (filtro.DataAte is not null)
        {
            where.Add("data_documento <= $dataAte");
            cmd.Parameters.AddWithValue("$dataAte", filtro.DataAte.Value.ToString("yyyy-MM-dd 23:59:59", System.Globalization.CultureInfo.InvariantCulture));
        }

        var clausulaWhere = where.Count > 0 ? $"WHERE {string.Join(" AND ", where)}" : "";

        cmd.CommandText = $"SELECT COUNT(*) FROM arquivos {clausulaWhere}";
        var totalItems = Convert.ToInt32(cmd.ExecuteScalar());

        var resultado = new ResultadoPaginado<ReferenciaDocumento>
        {
            TotalItems = totalItems,
            Pagina = filtro.Pagina,
            TamanhoPagina = filtro.TamanhoPagina,
            Items = new List<ReferenciaDocumento>()
        };

        if (totalItems > 0)
        {
            var offset = (filtro.Pagina - 1) * filtro.TamanhoPagina;

            cmd.CommandText = $"SELECT * FROM arquivos {clausulaWhere} ORDER BY id DESC LIMIT $limite OFFSET $offset";
            cmd.Parameters.AddWithValue("$limite", filtro.TamanhoPagina);
            cmd.Parameters.AddWithValue("$offset", offset);

            resultado.Items = LerResultados(cmd);
        }

        return resultado;
    }

    /// <summary>
    /// Atualiza integralmente os dados de um documento existente no índice, localizando-o pelo ID.
    /// </summary>
    /// <param name="arquivo">Objeto contendo o ID e os novos dados do documento.</param>
    /// <returns>Retorna o <see cref="ReferenciaDocumento"/> atualizado salvo no banco, ou null em caso de erro.</returns>
    public ReferenciaDocumento? Atualizar(ReferenciaDocumento arquivo)
    {
        try
        {
            using var con = Conectar();
            if (con == null) return null;

            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                UPDATE arquivos SET
                    chave_referencia = $chave,
                    caminho_relativo = $caminho,
                    nome_arquivo = $nome,
                    tipo_documento_fiscal = $tipo,
                    ambiente = $ambiente,
                    tipo_evento = $evento,
                    documento_prestador = $prestador,
                    data_documento = $data,
                    numero_sequencial = $nseq
                WHERE id = $id
            """;

            cmd.Parameters.AddWithValue("$id", arquivo.Id);
            cmd.Parameters.AddWithValue("$chave", arquivo.ChaveReferencia);
            cmd.Parameters.AddWithValue("$nome", arquivo.NomeArquivo);
            cmd.Parameters.AddWithValue("$caminho", arquivo.CaminhoRelativo);
            cmd.Parameters.AddWithValue("$tipo", arquivo.TipoDocumentoFiscal.ToString());
            cmd.Parameters.AddWithValue("$ambiente", arquivo.Ambiente.ToString());
            cmd.Parameters.AddWithValue("$evento", arquivo.TipoEvento?.ToString() ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$prestador", arquivo.DocumentoPrestador ?? "");
            cmd.Parameters.AddWithValue("$data", arquivo.DataDocumento.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$nseq", arquivo.NumeroSequencial ?? (object)DBNull.Value);

            cmd.ExecuteNonQuery();

            return BuscarPorId(arquivo.Id)!;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
        {
            this.Log().Warn($"[Aviso] Timeout ao atualizar o arquivo ID {arquivo.Id}.");
            return null;
        }
        catch (Exception ex)
        {
            this.Log().Error($"[Indexador] Erro ao atualizar arquivo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Atualiza um único campo específico de um documento indexado. Suporta apenas campos liberados.
    /// </summary>
    /// <param name="id">O identificador único do documento.</param>
    /// <param name="nomeCampo">O nome da coluna a ser atualizada no banco de dados.</param>
    /// <param name="valor">O novo valor do campo.</param>
    /// <returns>Retorna o <see cref="ReferenciaDocumento"/> atualizado, ou null em caso de erro.</returns>
    /// <exception cref="ArgumentException">Lançada caso o <paramref name="nomeCampo"/> não esteja na lista de campos permitidos.</exception>
    public ReferenciaDocumento? AtualizarCampo(int id, string nomeCampo, object valor)
    {
        try
        {
            using var con = Conectar();
            if (con == null) return null;

            using var cmd = con.CreateCommand();

            var camposPermitidos = new[] { "chave_referencia", "caminho_relativo", "nome_arquivo", "tipo_evento" };
            if (!camposPermitidos.Contains(nomeCampo.ToLower()))
                throw new ArgumentException($"O campo '{nomeCampo}' não é permitido para atualização individual.");

            cmd.CommandText = $"UPDATE arquivos SET {nomeCampo} = $valor WHERE id = $id";
            cmd.Parameters.AddWithValue("$valor", valor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id);

            cmd.ExecuteNonQuery();

            return BuscarPorId(id)!;
        }
        catch (Exception ex)
        {
            this.Log().Error($"[Indexador] Erro ao atualizar campo {nomeCampo}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Remove permanentemente um documento do índice através de seu identificador (ID).
    /// </summary>
    /// <param name="id">O identificador único do documento a ser deletado.</param>
    public void Remover(int id)
    {
        try
        {
            using var con = Conectar();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM arquivos WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
        {
            this.Log().Warn($"[Aviso] Não foi possível remover o registro {id} (Banco Ocupado).");
        }
        catch (Exception ex)
        {
            this.Log().Error($"[Indexador] Erro ao remover registro {id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Exporta os documentos físicos associados às chaves de referência fornecidas. 
    /// Retorna diretamente o arquivo caso seja apenas um, ou gera um arquivo ZIP consolidado para múltiplos.
    /// </summary>
    /// <param name="chaves">Lista de chaves de referência dos documentos desejados.</param>
    /// <param name="estruturaZip">Define se a estrutura do arquivo ZIP será plana ou organizada por diretórios.</param>
    /// <param name="tipoDocumento">Opcional. Filtra adicionalmente a extração por um tipo de documento fiscal específico.</param>
    /// <returns>Retorna um <see cref="DocumentoExportResult"/> contendo os bytes do arquivo e seu content-type correspondente, ou null se nada for encontrado.</returns>
    /// <exception cref="ArgumentException">Lançada caso nenhuma chave seja informada, se o limite máximo for excedido, ou se todas as chaves forem inválidas.</exception>
    /*public DocumentoExportResult? ExportarDocumentos(
        List<string> chaves,
        EstruturaZip estruturaZip,
        TipoRegistroDocumento? tipoDocumento = null,
        INFSeDanfseProvider geradorPdf = null
    )
    {
        if (chaves == null || chaves.Count == 0)
            throw new ArgumentException("Informe ao menos uma chave.");

        if (chaves.Count > MaxChavesPermitidas)
            throw new ArgumentException($"Número máximo de chaves permitidas por requisição é {MaxChavesPermitidas}. Recebido: {chaves.Count}.");

        var chavesFiltradas = chaves.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();

        if (chavesFiltradas.Count == 0)
            throw new ArgumentException("Nenhuma chave válida foi informada.");

        var documentos = BuscarPorChaves(chavesFiltradas, tipoDocumento);

        if (documentos.Count == 0)
            return null;

        var rootPath = _arquivoConfig.PathSalvar;
        var arquivosParaZipar = documentos
            .Select(d => new
            {
                Documento = d,
                CaminhoFisico = Path.Combine(rootPath, d.CaminhoRelativo, d.NomeArquivo),//TODO: averiguar
                PastaNoZip = ObterPastaNoZip(d)
            })
            .Where(x => Storage.Existe(x.Documento.CaminhoRelativo, x.Documento.NomeArquivo))
            .ToList();

        if (arquivosParaZipar.Count == 0)
            return null;

        //unico
        if (arquivosParaZipar.Count == 1)
        {
            var item = arquivosParaZipar[0];
            return new DocumentoExportResult
            {
                ArquivoBytes = Storage.Ler(item.Documento.CaminhoRelativo, item.Documento.NomeArquivo),
                NomeArquivo = item.Documento.NomeArquivo,
                ContentType = "application/xml"
            };
        }

        //multiplos
        byte[] conteudo;
        using (var memoryStream = new MemoryStream())
        {
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var item in arquivosParaZipar)
                {
                    var entryPath = estruturaZip == EstruturaZip.Plana
                        ? item.Documento.NomeArquivo
                        : Path.Combine(
                            item.Documento.DocumentoPrestador,
                            item.PastaNoZip,
                            item.Documento.NomeArquivo
                          ).Replace('\\', '/');

                    try
                    {
                        var entry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
                        using var entryStream = entry.Open();
                        var fileBytes = Storage.Ler(item.Documento.CaminhoRelativo, item.Documento.NomeArquivo);
                        using var fileStream = new MemoryStream(fileBytes);
                        fileStream.CopyTo(entryStream);
                    }
                    catch (Exception ex)
                    {
                        this.Log().Warn($"[ExportarDocumentos] Arquivo inacessível: {item.CaminhoFisico} — {ex.Message}");
                    }
                }
            }

            conteudo = memoryStream.ToArray();
        }

        return new DocumentoExportResult
        {
            ArquivoBytes = conteudo,
            NomeArquivo = $"documentos_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            ContentType = "application/zip"
        };
    }*/

    public DocumentoExportResult? ExportarDocumentos(
        List<string> chaves,
        EstruturaZip? estruturaZip = EstruturaZip.Plana,
        TipoRegistroDocumento? tipoDocumento = null
    )
    {
        if (chaves == null || chaves.Count == 0) throw new ArgumentException("Informe ao menos uma chave.");
        if (chaves.Count > MaxChavesPermitidas) throw new ArgumentException($"Número máximo de chaves permitidas por requisição é {MaxChavesPermitidas}. Recebido: {chaves.Count}.");

        var chavesFiltradas = chaves.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        if (chavesFiltradas.Count == 0) throw new ArgumentException("Nenhuma chave válida foi informada.");

        var todosDocumentos = BuscarPorChaves(chavesFiltradas, tipoDocumento: null);
        if (todosDocumentos.Count == 0) return null;

        bool requerPdf = tipoDocumento == null || tipoDocumento == TipoRegistroDocumento.PDF;
        if (requerPdf && GeradorPdf != null)
        {
            GarantirPdfsGerados(chavesFiltradas, todosDocumentos);
        }

        var documentosValidos = todosDocumentos
            .Where(d => tipoDocumento == null || d.TipoDocumentoFiscal.ToString() == tipoDocumento.ToString())
            .Where(d => Storage.Existe(d.CaminhoRelativo, d.NomeArquivo))
            .ToList();

        if (documentosValidos.Count == 0) return null;

        return EmpacotarDocumentos(documentosValidos, estruturaZip);
    }

    /// <summary>
    /// Exporta um documento físico no formato PDF associados à chave de referência fornecida. 
    /// </summary>
    /// <param name="chave">Chave de referência do documento desejado.</param>
    /// <param name="geradorPdf">Provedor responsável por abstrair a geração do DANFSE em PDF.</param>
    /// <returns>Retorna um <see cref="DocumentoExportResult"/> contendo os bytes do arquivo e seu content-type correspondente, ou null se nada for encontrado.</returns>
    /// <exception cref="ArgumentException">Lançada caso nenhuma chave seja informada, se o limite máximo for excedido, ou se todas as chaves forem inválidas.</exception>
    public DocumentoExportResult? ExportarDanfse(
    string chave
    )
    {
        return ExportarDocumentos(new List<string> { chave }, tipoDocumento: TipoRegistroDocumento.PDF);
    }

    /// <summary>
    /// Localiza os documentos associados à chave, identifica o estado (emitido/cancelado) 
    /// e gera o PDF do DANFSe com a sinalização correta. É necessário que o XML de origem esteja indexado.
    /// </summary>
    /// <param name="chave">Chave de referência da NFSe.</param>
    /// <param name="isCancelada">Indica se a nota fiscal está cancelada.</param>
    /// <returns>Array de bytes do PDF gerado.</returns>
    public DocumentoExportResult GerarIndexarPdfByChave(string chave, bool isCancelada)
    {
        if (string.IsNullOrWhiteSpace(chave))
            throw new ArgumentException("A chave não pode ser nula ou vazia.");

        var documentos = BuscarPorChaves(new List<string> { chave }, tipoDocumento: null);

        var docReferenciaXml = documentos.FirstOrDefault(d => d.TipoDocumentoFiscal == TipoRegistroDocumento.NFSE);

        if (docReferenciaXml == null || !Storage.Existe(docReferenciaXml.CaminhoRelativo, docReferenciaXml.NomeArquivo))
            throw new FileNotFoundException($"XML da NFSe não encontrado para a chave {chave}.");

        try
        {
            var xmlBytes = Storage.Ler(docReferenciaXml.CaminhoRelativo, docReferenciaXml.NomeArquivo);
            var xmlString = System.Text.Encoding.UTF8.GetString(xmlBytes);

            var refDocPdf = new ReferenciaDocumento
            {
                ChaveReferencia = chave,
                NomeArquivo = $"{chave}_danfse.pdf",
                TipoDocumentoFiscal = TipoRegistroDocumento.PDF,
                Ambiente = docReferenciaXml.Ambiente,
                DocumentoPrestador = docReferenciaXml.DocumentoPrestador,
                DataDocumento = docReferenciaXml.DataDocumento,
            };

            return GerarIndexarPdfByRefDocumento(refDocPdf, xmlString, isCancelada);
        }
        catch (Exception ex)
        {
            this.Log().Error($"Erro ao gerar PDF para a chave {chave}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Localiza os documentos associados à chave, identifica o estado (emitido/cancelado) 
    /// e gera o PDF do DANFSe com a sinalização correta.
    /// </summary>
    /// <param name="docReferencia">Chave de referência da NFSe.</param>
    /// <param name="xmlNfse">XML da NFSe.</param>
    /// <param name="isCancelada">Indica se a nota fiscal está cancelada.</param>
    /// <returns>Array de bytes do PDF gerado.</returns>
    public DocumentoExportResult GerarIndexarPdfByRefDocumento(ReferenciaDocumento docReferencia, string xmlNfse, bool isCancelada)
    {
        try
        {
            var pdfBytes = GeradorPdf.GerarPdf(xmlNfse, docReferencia.Ambiente, isCancelada);
            //Storage.Salvar(docReferencia.CaminhoRelativo, nomeArquivoPdf, pdfBytes);

            //var novoDocPdf = Indexar(docReferencia);
            var novoDocPdf = GravarArquivoEmDisco(pdfBytes, docReferencia.NomeArquivo, docReferencia.DocumentoPrestador, docReferencia.DataDocumento, false, docReferencia);

            if (novoDocPdf == null)
            {
                throw new InvalidOperationException($"Falha ao salvar/indexar o PDF da chave {docReferencia.ChaveReferencia}");
            }

            return new DocumentoExportResult
            {
                ArquivoBytes = Storage.Ler(novoDocPdf.CaminhoRelativo, novoDocPdf.NomeArquivo) ?? [],
                NomeArquivo = novoDocPdf.NomeArquivo,
                ContentType = novoDocPdf.TipoDocumentoFiscal == TipoRegistroDocumento.PDF ? "application/pdf" : "application/xml",
                //Ambiente = novoDocPdf.Ambiente,
                //Cancelada = novoDocPdf.TipoEvento == TipoEvento.CANCELAMENTO
            };
        }
        catch (Exception ex)
        {
            this.Log().Error(message: $"Erro ao gerar PDF para a chave {docReferencia.ChaveReferencia}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gera o PDF do DANFSe a partir de um XML válido.
    /// </summary>
    /// <param name="xmlNfse">XML da NFSe.</param>
    /// <param name="isCancelada">Indica se a nota fiscal está cancelada.</param>
    /// <returns>Array de bytes do PDF gerado.</returns>
    public DocumentoExportResult GerarIndexarPdfByXml(string xmlNfse, bool isCancelada)
    {
        var dadosXml = ParseXMLNfse(xmlNfse);

        try
        {
            var refDocPdf = new ReferenciaDocumento
            {
                ChaveReferencia = dadosXml.Chave,
                NomeArquivo = $"{dadosXml.Chave}_danfse.pdf",
                TipoDocumentoFiscal = TipoRegistroDocumento.PDF,
                Ambiente = (DFeTipoAmbiente)dadosXml.Ambiente,
                DocumentoPrestador = dadosXml.DocumentoPrestador,
                DataDocumento = dadosXml.DataEmissao.Value,
            };

            return GerarIndexarPdfByRefDocumento(refDocPdf, xmlNfse, isCancelada);
        }
        catch (Exception ex)
        {
            this.Log().Error($"Erro ao gerar PDF para a chave {dadosXml.Chave}: {ex.Message}");
            throw;
        }
    }

    #region auxiliares

    /// <summary>
    /// Grava o arquivo no disco conforme o tipo especificado.
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

    private ReferenciaDocumento? GravarArquivoEmDisco(string conteudoArquivo, string nomeArquivo, string? documento, DateTime? data = null, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        var diretorioAbsoluto = Path.Combine(_arquivoConfig.GetPathDps(data ?? DateTime.Today, documento ?? string.Empty));

        var caminhoRelativo = GetRelativePath(_arquivoConfig.PathSalvar, diretorioAbsoluto);
        var motorNuvem = Storage;

        var nomeFinal = !incrementarNome ? GerarNomeUnico(caminhoRelativo, nomeArquivo, motorNuvem) : nomeArquivo;
        var caminhoAbsolutoFinal = Path.Combine(diretorioAbsoluto, nomeFinal);

        motorNuvem.Salvar(caminhoRelativo, nomeFinal, conteudoArquivo);

        if (_arquivoConfig.Indexador.GarantirCopiaLocal && PossuiStorageCustomizado)
        {
            if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
            File.WriteAllText(caminhoAbsolutoFinal, conteudoArquivo, Encoding.UTF8);
        }

        referenciaDoc.CaminhoRelativo = caminhoRelativo;
        referenciaDoc.NomeArquivo = nomeFinal;
        return Indexar(referenciaDoc);
    }

    /// <summary>
    /// Grava o arquivo no disco conforme o tipo especificado.
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

    private ReferenciaDocumento? GravarArquivoEmDisco(byte[] conteudoArquivo, string nomeArquivo, string? documento, DateTime? data = null, bool incrementarNome = false, ReferenciaDocumento referenciaDoc = null)
    {
        var diretorioAbsoluto = Path.Combine(_arquivoConfig.GetPathNFSe(data ?? DateTime.Today, documento ?? string.Empty));

        var caminhoRelativo = GetRelativePath(_arquivoConfig.PathSalvar, diretorioAbsoluto);
        var motorNuvem = Storage;

        var nomeFinal = incrementarNome ? GerarNomeUnico(caminhoRelativo, nomeArquivo, motorNuvem) : nomeArquivo;
        var caminhoAbsolutoFinal = Path.Combine(diretorioAbsoluto, nomeFinal);

        motorNuvem.Salvar(caminhoRelativo, nomeFinal, conteudoArquivo);

        if (_arquivoConfig.Indexador.GarantirCopiaLocal && PossuiStorageCustomizado)
        {
            if (!Directory.Exists(diretorioAbsoluto)) Directory.CreateDirectory(diretorioAbsoluto);
            File.WriteAllBytes(caminhoAbsolutoFinal, conteudoArquivo);
        }

        referenciaDoc.CaminhoRelativo = caminhoRelativo;
        referenciaDoc.NomeArquivo = nomeFinal;
        return Indexar(referenciaDoc);
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
    /// Verifica se os PDFs solicitados existem fisicamente. Caso não existam, gera a partir do XML base,
    /// salva no Storage e indexa no banco de dados.
    /// </summary>
    private void GarantirPdfsGerados(List<string> chaves, List<ReferenciaDocumento> documentos)
    {
        foreach (var chave in chaves)
        {
            var docPdf = documentos.FirstOrDefault(d => d.ChaveReferencia == chave && d.TipoDocumentoFiscal == TipoRegistroDocumento.PDF);
            bool pdfFisicoExiste = docPdf != null && Storage.Existe(docPdf.CaminhoRelativo, docPdf.NomeArquivo);

            if (!pdfFisicoExiste)
            {
                var docXml = documentos.FirstOrDefault(d => d.ChaveReferencia == chave && d.TipoDocumentoFiscal == TipoRegistroDocumento.NFSE);

                if (docXml != null && Storage.Existe(docXml.CaminhoRelativo, docXml.NomeArquivo))
                {
                    try
                    {
                        bool isCancelado = documentos.Any(d => d.ChaveReferencia == chave && d.TipoEvento == TipoEvento.CANCELAMENTO);

                        var xmlBytes = Storage.Ler(docXml.CaminhoRelativo, docXml.NomeArquivo);
                        var xmlString = System.Text.Encoding.UTF8.GetString(xmlBytes);

                        var pdfBytes = GeradorPdf.GerarPdf(xmlString, docXml.Ambiente, isCancelado);
                        var nomeArquivoPdf = $"{chave}_danfse.pdf";

                        Storage.Salvar(docXml.CaminhoRelativo, nomeArquivoPdf, pdfBytes);

                        var novoDocPdf = new ReferenciaDocumento
                        {
                            ChaveReferencia = chave,
                            NomeArquivo = nomeArquivoPdf,
                            CaminhoRelativo = docXml.CaminhoRelativo,
                            TipoDocumentoFiscal = TipoRegistroDocumento.PDF,
                            Ambiente = docXml.Ambiente,
                            TipoEvento = isCancelado ? TipoEvento.CANCELAMENTO : docXml.TipoEvento,
                            DocumentoPrestador = docXml.DocumentoPrestador,
                            DataDocumento = docXml.DataDocumento,
                            NumeroSequencial = docXml.NumeroSequencial
                        };

                        var docIndexado = Indexar(novoDocPdf);
                        if (docIndexado != null)
                            documentos.Add(docIndexado);
                    }
                    catch (Exception ex)
                    {
                        this.Log().Warn($"[ExportarDocumentos] Falha ao gerar PDF dinâmico para a chave {chave}: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processa a lista final de documentos válidos, retornando o arquivo direto se for apenas um, 
    /// ou gerando um MemoryStream com um ZipArchive se forem múltiplos.
    /// </summary>
    private DocumentoExportResult EmpacotarDocumentos(List<ReferenciaDocumento> documentos, EstruturaZip? estruturaZip = EstruturaZip.PorPrestadorETipo)
    {
        // Tratamento para arquivo único
        if (documentos.Count == 1)
        {
            var doc = documentos[0];
            return new DocumentoExportResult
            {
                ArquivoBytes = Storage.Ler(doc.CaminhoRelativo, doc.NomeArquivo) ?? [],
                NomeArquivo = doc.NomeArquivo,
                ContentType = doc.TipoDocumentoFiscal == TipoRegistroDocumento.PDF ? "application/pdf" : "application/xml",
                //Ambiente = doc.Ambiente,
                //Cancelada = doc.TipoEvento == TipoEvento.CANCELAMENTO
            };
        }

        // Tratamento para múltiplos arquivos (ZIP)
        byte[] conteudoZip;
        using (var memoryStream = new MemoryStream())
        {
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var doc in documentos)
                {
                    var entryPath = estruturaZip == EstruturaZip.Plana
                        ? doc.NomeArquivo
                        : Path.Combine(doc.DocumentoPrestador, ObterPastaNoZip(doc), doc.NomeArquivo).Replace('\\', '/');

                    try
                    {
                        var entry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
                        using var entryStream = entry.Open();
                        var fileBytes = Storage.Ler(doc.CaminhoRelativo, doc.NomeArquivo);

                        if (fileBytes != null)
                        {
                            entryStream.Write(fileBytes, 0, fileBytes.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.Log().Warn($"[ExportarDocumentos] Arquivo inacessível: {doc.CaminhoRelativo}/{doc.NomeArquivo} — {ex.Message}");
                    }
                }
            }
            conteudoZip = memoryStream.ToArray();
        }

        return new DocumentoExportResult
        {
            ArquivoBytes = conteudoZip,
            NomeArquivo = $"documentos_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            ContentType = "application/zip"
        };
    }

    /// <summary>
    /// Busca um documento indexado pelo seu identificador primário interno.
    /// </summary>
    /// <param name="id">ID do documento no banco.</param>
    /// <returns>Retorna o <see cref="ReferenciaDocumento"/> correspondente, ou null se não for encontrado.</returns>
    private ReferenciaDocumento? BuscarPorId(int id)
    {
        using var con = Conectar(lancarExcecoes: true);
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT * FROM arquivos WHERE id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);
        return LerResultados(cmd).FirstOrDefault();
    }

    /// <summary>
    /// Busca múltiplos documentos indexados baseados em uma lista de chaves de referência exatas.
    /// </summary>
    /// <param name="chaves">Lista de chaves de referência.</param>
    /// <param name="tipoDocumento">Opcional. Filtro de tipo de documento fiscal específico.</param>
    /// <returns>Lista de <see cref="ReferenciaDocumento"/> localizados.</returns>
    private List<ReferenciaDocumento> BuscarPorChaves(List<string> chaves, TipoDocumento? tipoDocumento)
    {
        using var con = Conectar(lancarExcecoes: true);
        using var cmd = con.CreateCommand();

        var placeholders = chaves.Select((_, i) => $"$c{i}");
        var sql = $"SELECT * FROM arquivos WHERE chave_referencia IN ({string.Join(", ", placeholders)})";

        if (tipoDocumento is not null)
            sql += " AND tipo_documento_fiscal = $tipo";

        cmd.CommandText = sql;
        for (int i = 0; i < chaves.Count; i++)
            cmd.Parameters.AddWithValue($"$c{i}", chaves[i]);

        if (tipoDocumento is not null)
            cmd.Parameters.AddWithValue("$tipo", tipoDocumento.ToString());

        return LerResultados(cmd);
    }

    /// <summary>
    /// Processa o <see cref="SqliteDataReader"/> gerado pela execução do comando e o converte 
    /// em uma lista de objetos do domínio <see cref="ReferenciaDocumento"/>.
    /// </summary>
    /// <param name="cmd">Comando SQLite recém-configurado e pronto para leitura.</param>
    /// <returns>Lista materializada de documentos mapeados.</returns>
    private List<ReferenciaDocumento> LerResultados(SqliteCommand cmd)
    {
        var lista = new List<ReferenciaDocumento>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var tipoDocStr = reader.GetString(reader.GetOrdinal("tipo_documento_fiscal"));
            if (!Enum.TryParse<TipoRegistroDocumento>(tipoDocStr, out var tipoDoc))
            {

            }

            var ambienteStr = reader.GetString(reader.GetOrdinal("ambiente"));
            if (!Enum.TryParse<DFeTipoAmbiente>(ambienteStr, out var ambienteDoc))
            {

            }

            TipoEvento? tipoEvento = null;
            var ordinalEvento = reader.GetOrdinal("tipo_evento");

            if (!reader.IsDBNull(ordinalEvento))
            {
                var tipoEventoStr = reader.GetString(ordinalEvento);
                if (Enum.TryParse<TipoEvento>(tipoEventoStr, out var ev))
                {
                    tipoEvento = ev;
                }
            }

            lista.Add(new ReferenciaDocumento
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                CriadoEm = DateTime.Parse(reader.GetString(reader.GetOrdinal("criado_em")), null, System.Globalization.DateTimeStyles.RoundtripKind),
                ChaveReferencia = reader.GetString(reader.GetOrdinal("chave_referencia")),
                NomeArquivo = reader.GetString(reader.GetOrdinal("nome_arquivo")),
                CaminhoRelativo = reader.GetString(reader.GetOrdinal("caminho_relativo")),
                DocumentoPrestador = reader.GetString(reader.GetOrdinal("documento_prestador")),
                DataDocumento = DateTime.Parse(reader.GetString(reader.GetOrdinal("data_documento")), null, System.Globalization.DateTimeStyles.RoundtripKind),

                TipoDocumentoFiscal = tipoDoc,
                Ambiente = ambienteDoc,
                TipoEvento = tipoEvento,

                NumeroSequencial = reader.IsDBNull(reader.GetOrdinal("numero_sequencial"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("numero_sequencial"))
            });
        }
        return lista;
    }

    /// <summary>
    /// Mapeia o tipo de documento fiscal para o nome de uma pasta padrão a ser utilizada na estruturação do arquivo ZIP exportado.
    /// </summary>
    /// <param name="referenciaDoc">O registro do documento indexado avaliado.</param>
    /// <returns>Nome do diretório para organização ("Rps", "NFSe" ou "Outros").</returns>
    private string ObterPastaNoZip(ReferenciaDocumento referenciaDoc) =>
        referenciaDoc.TipoDocumentoFiscal switch
        {
            TipoRegistroDocumento.DPS => "Rps",
            TipoRegistroDocumento.PEDIDO_REGISTRO_EVENTO => "Rps",
            TipoRegistroDocumento.NFSE => "NFSe",
            TipoRegistroDocumento.EVENTO => "NFSe",
            TipoRegistroDocumento.PDF => "NFSe",
            _ => "Outros"
        };

    /// <summary>
    /// Gerencia e estabelece a conexão principal com o banco de dados SQLite local, 
    /// realizando a injeção do timeout e garantindo a inicialização da estrutura das tabelas usando um controle de lock.
    /// </summary>
    /// <param name="lancarExcecoes">Determina se as exceções de banco devem ser relançadas (throw) ou silenciadas (retornando null).</param>
    /// <returns>Instância aberta de <see cref="SqliteConnection"/> pronta para uso, ou null em caso de falha não-fatal.</returns>
    private SqliteConnection? Conectar(bool lancarExcecoes = false)
    {
        try
        {
            var rootDbPath = _arquivoConfig.Indexador.PathIndexadorDb;
            var dbPath = Path.Combine(rootDbPath, "indexador_docs.db");

            if (!Directory.Exists(rootDbPath))
                Directory.CreateDirectory(rootDbPath);

            //var connectionString = $"Data Source={dbPath};Default Timeout=10;";
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 10
            }.ConnectionString;

            var con = new SqliteConnection(connectionString);
            con.Open();

            if (!_bancoInicializado)
            {
                lock (_lockInit)
                {
                    if (!_bancoInicializado)
                    {
                        InicializarBancoEstrutura(con);
                        _bancoInicializado = true;
                    }
                }
            }
            return con;
        }
        catch (Exception ex)
        {
            this.Log().Error($"[Indexador] Erro inesperado no banco: {ex.Message}");

            if (lancarExcecoes)
                throw;

            return null;
        }
    }

    /// <summary>
    /// Faz o parse do XML da NFS-e e retorna os campos principais. 
    /// Retorna null nas propriedades caso a tag não exista no XML.
    /// </summary>
    private (string? Chave, DFeTipoAmbiente? Ambiente, string? DocumentoPrestador, DateTime? DataEmissao) ParseXMLNfse(string xml)
    {
        string? chave = null;
        DFeTipoAmbiente? ambiente = null;
        string? documentoPrestador = null;
        DateTime? dataEmissao = null;

        if (string.IsNullOrWhiteSpace(xml))
            return (chave, ambiente, documentoPrestador, dataEmissao);


        var doc = XDocument.Parse(xml);

        var valorAtributoId = doc.Descendants()
                                 .FirstOrDefault(x => x.Name.LocalName == "infNFSe")?
                                 .Attribute("Id")?.Value;

        if (!string.IsNullOrEmpty(valorAtributoId))
            chave = new string(valorAtributoId.Where(char.IsDigit).ToArray());

        string strAmbiente = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "tpAmb")?.Value;
        if (!string.IsNullOrEmpty(strAmbiente) && int.TryParse(strAmbiente, out int ambInt))
            ambiente = (DFeTipoAmbiente)ambInt;

        string strDataEmi = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "dhEmi")?.Value;
        if (!string.IsNullOrEmpty(strDataEmi) && DateTime.TryParse(strDataEmi, out DateTime dataConvertida))
            dataEmissao = dataConvertida;

        var tagPrestador = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "prest");
        var cpf = tagPrestador?.Descendants().FirstOrDefault(x => x.Name.LocalName == "CPF")?.Value;
        var cnpj = tagPrestador?.Descendants().FirstOrDefault(x => x.Name.LocalName == "CNPJ")?.Value;
        documentoPrestador = cpf ?? cnpj ?? string.Empty;

        return (chave, ambiente, documentoPrestador, dataEmissao);
    }

    #endregion
}