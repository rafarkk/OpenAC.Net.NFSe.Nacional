using Microsoft.Data.Sqlite;
using OpenAC.Net.Core.Logging;
using OpenAC.Net.NFSe.Nacional.Common;
using OpenAC.Net.NFSe.Nacional.Common.Types;
using OpenAC.Net.NFSe.Nacional.Indexador.Model;
using OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;



/// <summary>
/// Serviço responsável pela indexação, busca, atualização e exportação de documentos fiscais (NFS-e) 
/// utilizando um banco de dados SQLite para armazenamento de metadados.
/// </summary>
public class IndexadorDocumentos : IOpenLog
{
    private readonly NFSeArquivoConfig _arquivoConfig;
    private readonly IStorageProvider? _storageCustomizado;

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
    private const int MaxChavesPermitidas = 50;

    private volatile bool _bancoInicializado = false;
    private readonly object _lockInit = new();

    /// <summary>
    /// Inicializa uma nova instância da classe <see cref="IndexadorDocumentos"/>.
    /// </summary>
    /// <param name="arquivoConfig">Representa a configuração para arquivos NFSe.</param>
    /// <param name="storage">Provedor de armazenamento customizado opcional.</param>
    public IndexadorDocumentos(NFSeArquivoConfig arquivoConfig, IStorageProvider? storage = null)
    {
        _arquivoConfig = arquivoConfig;
        _storageCustomizado = storage;
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
                    tipo_evento, documento_prestador, data_documento, numero_sequencial
                )
                VALUES (
                    $chave, $nome, $caminho, $tipo, $evento, $prestador, $data, $nseq
                )
                ON CONFLICT(caminho_relativo, nome_arquivo) DO UPDATE SET
                    chave_referencia = excluded.chave_referencia,
                    tipo_evento = excluded.tipo_evento,
                    data_documento = excluded.data_documento,
                    numero_sequencial = excluded.numero_sequencial
                RETURNING *;
            """;

            cmd.Parameters.AddWithValue("$chave", referenciaDoc.ChaveReferencia);
            cmd.Parameters.AddWithValue("$nome", referenciaDoc.NomeArquivo);
            cmd.Parameters.AddWithValue("$caminho", referenciaDoc.CaminhoRelativo);
            cmd.Parameters.AddWithValue("$tipo", referenciaDoc.TipoDocumentoFiscal.ToString());
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
    public DocumentoExportResult? ExportarDocumentos(
    List<string> chaves,
    EstruturaZip estruturaZip,
    TipoDocumento? tipoDocumento = null)
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
    }

    #region auxiliares

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
            if (!Enum.TryParse<TipoDocumento>(tipoDocStr, out var tipoDoc))
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
            TipoDocumento.DPS => "Rps",
            TipoDocumento.PEDIDO_REGISTRO_EVENTO => "Rps",
            TipoDocumento.NFSE => "NFSe",
            TipoDocumento.EVENTO => "NFSe",
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
    #endregion
}