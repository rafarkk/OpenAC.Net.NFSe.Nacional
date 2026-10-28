using OpenAC.Net.Core.Logging;
using System;
using System.IO;
using System.Net.Mime;
using System.Text;

namespace OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider.Providers
{
    public class LocalStorageProvider : IStorageProvider, IOpenLog
    {
        private readonly string _rootPath;

        public LocalStorageProvider(string? rootPath = null)
        {
            _rootPath = rootPath ?? string.Empty;
        }

        public void Salvar(string caminhoRelativo, string nomeArquivo, string conteudo, string contentType = "application/xml")
        {
            try
            {
                var caminhoFinal = PrepararPath(caminhoRelativo, nomeArquivo);

                File.WriteAllText(caminhoFinal, conteudo, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.Log().Error($"[LocalStorageProvider] Erro ao salvar o arquivo '{nomeArquivo}' em '{caminhoRelativo}': {ex.Message}");
                throw;
            }
        }

        public void Salvar(string caminhoRelativo, string nomeArquivo, byte[] conteudo, string contentType = "application/pdf")
        {
            try
            {
                var caminhoFinal = PrepararPath(caminhoRelativo, nomeArquivo);

                File.WriteAllBytes(caminhoFinal, conteudo);
            }
            catch (Exception ex)
            {
                this.Log().Error($"[LocalStorageProvider] Erro ao salvar o arquivo binário '{nomeArquivo}' em '{caminhoRelativo}': {ex.Message}");
                throw;
            }
        }
        
        public byte[] Ler(string caminhoRelativo, string nomeArquivo)
        {
            try
            {
                return File.ReadAllBytes(Path.Combine(_rootPath, caminhoRelativo, nomeArquivo));
            }
            catch (Exception ex)
            {
                this.Log().Error($"[LocalStorageProvider] Erro ao ler o arquivo '{nomeArquivo}' de '{caminhoRelativo}': {ex.Message}");
                throw;
            }
        }

        public bool Existe(string caminhoRelativo, string nomeArquivo)
        {
            try
            {
                return File.Exists(Path.Combine(_rootPath, caminhoRelativo, nomeArquivo));
            }
            catch (Exception ex)
            {
                this.Log().Error($"[LocalStorageProvider] Erro ao verificar a existência do arquivo '{nomeArquivo}' em '{caminhoRelativo}': {ex.Message}");
                return false;
            }
        }

        private string PrepararPath(string caminhoRelativo, string nomeArquivo)
        {
            return PrepararPath(Path.Combine(caminhoRelativo, nomeArquivo));
        }

        private string PrepararPath(string caminhoCompleto)
        {
            var caminhoArquivoFinal = Path.Combine(_rootPath, caminhoCompleto);

            var diretorio = Path.GetDirectoryName(caminhoArquivoFinal);

            if (!string.IsNullOrEmpty(diretorio) && !Directory.Exists(diretorio))
            {
                Directory.CreateDirectory(diretorio);
            }

            return caminhoArquivoFinal;
        }
    }
}