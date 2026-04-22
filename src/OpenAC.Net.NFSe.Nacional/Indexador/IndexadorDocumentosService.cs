using Microsoft.Data.Sqlite;
using OpenAC.Net.Core.Logging;
using OpenAC.Net.NFSe.Nacional.Common;
using OpenAC.Net.NFSe.Nacional.Common.Types;
using OpenAC.Net.NFSe.Nacional.Indexador;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;



// Services/NfseIndexService.cs
public class IndexadorDocumentosService : IOpenLog
{
    private readonly NFSeArquivoConfig _arquivoConfig;

    /// <summary>
    /// Número máximo de chaves permitidas por requisição no método de baixar documentos do inexador.
    /// </summary>
    private const int MaxChavesPermitidas = 100;

    private volatile bool _bancoInicializado = false;
    private readonly object _lockInit = new();

    public IndexadorDocumentosService(NFSeArquivoConfig arquivoConfig)
    {
        _arquivoConfig = arquivoConfig;
    }

    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Escrita
    // -------------------------------------------------------------------------

    public DocumentoIndexado? Indexar(DocumentoIndexado arquivo)
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

            cmd.Parameters.AddWithValue("$chave", arquivo.ChaveReferencia);
            cmd.Parameters.AddWithValue("$nome", arquivo.NomeArquivo);
            cmd.Parameters.AddWithValue("$caminho", arquivo.CaminhoRelativo);
            cmd.Parameters.AddWithValue("$tipo", arquivo.TipoDocumentoFiscal.ToString());
            cmd.Parameters.AddWithValue("$evento", arquivo.TipoEvento?.ToString() ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$prestador", arquivo.DocumentoPrestador ?? "");
            cmd.Parameters.AddWithValue("$data", arquivo.DataDocumento.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$nseq", arquivo.NumeroSequencial ?? (object)DBNull.Value);

            // O LerResultados agora retorna a lista com o registro recém-criado/atualizado
            return LerResultados(cmd).First();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
        {
            this.Log().Warn($"[Aviso] Timeout ao indexar arquivo referência {arquivo.ChaveReferencia}.");
            return null;
        }
        catch (Exception ex)
        {
            this.Log().Error($"[Indexador] Erro ao indexar: {ex.Message}");
            return null;
        }
    }

    public List<DocumentoIndexado> BuscarPorFiltro(FiltroDocumentoIndexado filtro)
    {
        using var con = Conectar(lancarExcecao: true);
        using var cmd = con.CreateCommand();

        var where = new List<string>();
        var offset = (filtro.Pagina - 1) * filtro.TamanhoPagina;
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
        cmd.CommandText = $"SELECT * FROM arquivos {clausulaWhere} ORDER BY criado_em DESC LIMIT $limite OFFSET $offset";

        cmd.Parameters.AddWithValue("$limite", filtro.TamanhoPagina);
        cmd.Parameters.AddWithValue("$offset", offset);

        return LerResultados(cmd);
    }

    public DocumentoIndexado? Atualizar(DocumentoIndexado arquivo)
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

    public DocumentoIndexado? AtualizarCampo(int id, string nomeCampo, object valor)
    {
        try
        {
            using var con = Conectar();
            if (con == null) return null;

            using var cmd = con.CreateCommand();

            // Lista branca para evitar SQL Injection
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
        catch (Exception ex) // É bom ter um catch genérico aqui também
        {
            this.Log().Error($"[Indexador] Erro ao remover registro {id}: {ex.Message}");
        }
    }

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
                CaminhoFisico = Path.Combine(rootPath, d.CaminhoRelativo, d.NomeArquivo),
                PastaNoZip = ObterPastaNoZip(d)
            })
            .Where(x => File.Exists(x.CaminhoFisico))
            .ToList();

        if (arquivosParaZipar.Count == 0)
            return null;

        // Arquivo único — retorna direto sem zipar
        if (arquivosParaZipar.Count == 1)
        {
            var item = arquivosParaZipar[0];
            return new DocumentoExportResult
            {
                ArquivoBytes = File.ReadAllBytes(item.CaminhoFisico),
                NomeArquivo = item.Documento.NomeArquivo,
                ContentType = "application/xml"
            };
        }

        // Múltiplos arquivos — zipa
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
                        using var fileStream = File.OpenRead(item.CaminhoFisico);
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
    private DocumentoIndexado? BuscarPorId(int id)
    {
        using var con = Conectar(lancarExcecao: true);
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT * FROM arquivos WHERE id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);
        return LerResultados(cmd).FirstOrDefault();
    }

    //private DocumentoIndexado? BuscarPorCaminhoENome(string caminho, string nome)
    //{
    //    using var con = Conectar();
    //    using var cmd = con.CreateCommand();
    //    cmd.CommandText = "SELECT * FROM arquivos WHERE caminho_relativo = $caminho AND nome_arquivo = $nome LIMIT 1";
    //    cmd.Parameters.AddWithValue("$caminho", caminho);
    //    cmd.Parameters.AddWithValue("$nome", nome);
    //    return LerResultados(cmd).FirstOrDefault();
    //}

    private List<DocumentoIndexado> BuscarPorChaves(List<string> chaves, TipoDocumento? tipoDocumento)
    {
        using var con = Conectar(lancarExcecao: true);
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

    private List<DocumentoIndexado> LerResultados(SqliteCommand cmd)
    {
        var lista = new List<DocumentoIndexado>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            // 1. Tratamento para TipoDocumento (Obrigatório)
            var tipoDocStr = reader.GetString(reader.GetOrdinal("tipo_documento_fiscal"));
            if (!Enum.TryParse<TipoDocumento>(tipoDocStr, out var tipoDoc))
            {
                // Opcional: Logar aqui que um valor inválido foi encontrado no banco
                // tipoDoc assumirá o valor padrão (geralmente o primeiro item do Enum)
            }

            // 2. Tratamento para TipoEvento (Pode ser NULL no banco)
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

            lista.Add(new DocumentoIndexado
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                CriadoEm = DateTime.Parse(reader.GetString(reader.GetOrdinal("criado_em")), null, System.Globalization.DateTimeStyles.RoundtripKind),
                ChaveReferencia = reader.GetString(reader.GetOrdinal("chave_referencia")),
                NomeArquivo = reader.GetString(reader.GetOrdinal("nome_arquivo")),
                CaminhoRelativo = reader.GetString(reader.GetOrdinal("caminho_relativo")),
                DocumentoPrestador = reader.GetString(reader.GetOrdinal("documento_prestador")),
                DataDocumento = DateTime.Parse(reader.GetString(reader.GetOrdinal("data_documento")), null, System.Globalization.DateTimeStyles.RoundtripKind),

                // Usando os valores parseados com segurança
                TipoDocumentoFiscal = tipoDoc,
                TipoEvento = tipoEvento,

                NumeroSequencial = reader.IsDBNull(reader.GetOrdinal("numero_sequencial"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("numero_sequencial"))
            });
        }
        return lista;
    }

    private string ObterPastaNoZip(DocumentoIndexado documento) =>
        documento.TipoDocumentoFiscal switch
        {
            TipoDocumento.DPS => "Rps",
            TipoDocumento.PEDIDO_REGISTRO_EVENTO => "Rps",
            TipoDocumento.NFSE => "NFSe",
            TipoDocumento.EVENTO => "NFSe",
            _ => "Outros"
        };

    //public string? ResolverCaminho(DocumentoIndexado registro)
    //{
    //    var rootPath = _arquivoConfig.PathDocumentosDb; // Ou o caminho base configurado
    //    var esperado = Path.Combine(rootPath, registro.CaminhoRelativo, registro.NomeArquivo);

    //    if (File.Exists(esperado))
    //        return esperado;

    //    // Se não achou no local esperado, tenta buscar pelo nome em subpastas
    //    var encontrado = Directory
    //        .GetFiles(rootPath, registro.NomeArquivo, SearchOption.AllDirectories)
    //        .FirstOrDefault();

    //    if (encontrado is null)
    //        return null;

    //    // Atualiza o banco com o novo local encontrado
    //    var novoRelativo = Path.GetDirectoryName(Path.GetRelativePath(rootPath, encontrado)) ?? "";
    //    AtualizarCaminho(registro.NomeArquivo, novoRelativo);

    //    return encontrado;
    //}

    private SqliteConnection? Conectar(bool lancarExcecao = false)
    {
        try
        {
            var rootDbPath = _arquivoConfig.PathIndexadorDocsDb;
            var dbPath = Path.Combine(rootDbPath, "indexador_docs.db");

            if (!Directory.Exists(rootDbPath))
                Directory.CreateDirectory(rootDbPath);

            //var connectionString = $"Data Source={dbPath};Default Timeout=10;";
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 10 // Aqui está o segredo! Equivale ao busy_timeout (em segundos).
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

            if (lancarExcecao)
                throw;

            return null;
        }
    }
    #endregion
}