using OpenAC.Net.Core.Logging;
using System;
using System.IO;
using System.Text;

namespace OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider
{
    public class LocalStorageProvider : IStorageProvider, IOpenLog
    {
        private readonly string _rootPath;

        public LocalStorageProvider(string rootPath)
        {
            _rootPath = rootPath;
        }

        public void Salvar(string caminhoRelativo, string nomeArquivo, string conteudo)
        {
            try
            {
                var caminhoFinal = Path.Combine(_rootPath, caminhoRelativo);
                if (!Directory.Exists(caminhoFinal)) Directory.CreateDirectory(caminhoFinal);

                File.WriteAllText(Path.Combine(caminhoFinal, nomeArquivo), conteudo, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.Log().Error($"[LocalStorageProvider] Erro ao salvar o arquivo '{nomeArquivo}' em '{caminhoRelativo}': {ex.Message}");
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
    }
}