using Google;
using Google.Cloud.Storage.V1;
using OpenAC.Net.Core.Logging;
using System;
using System.IO;
using System.Net;
using System.Text;

namespace OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider.Providers
{
    public class GoogleCloudStorageProvider : IStorageProvider, IOpenLog
    {
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;
        private readonly string _prefixo;

        /// <summary>
        /// Construtor do provedor nativo do Google Cloud Storage (GCS).
        /// </summary>
        /// <param name="storageClient">Cliente nativo do Google Cloud</param>
        /// <param name="bucketName">Nome do Bucket (ex: "meu-bucket-nfse")</param>
        /// <param name="prefixo">Prefixo opcional que simula a pasta raiz (ex: "documentos")</param>
        public GoogleCloudStorageProvider(StorageClient storageClient, string bucketName, string prefixo = "")
        {
            _storageClient = storageClient;
            _bucketName = bucketName;
            _prefixo = prefixo ?? string.Empty;
        }

        public void Salvar(string caminhoRelativo, string nomeArquivo, string conteudo, string contentType = "application/xml")
        {
            var key = ObterGcsKey(caminhoRelativo, nomeArquivo);

            try
            {
                var bytes = Encoding.UTF8.GetBytes(conteudo);
                using var memoryStream = new MemoryStream(bytes);

                _storageClient.UploadObject(
                    bucket: _bucketName,
                    objectName: key,
                    contentType: contentType,
                    source: memoryStream
                );
            }
            catch (Exception ex)
            {
                this.Log().Error($"[GoogleCloudStorageProvider] Erro ao salvar o arquivo '{nomeArquivo}' no GCS (Bucket: {_bucketName}, Key: {key}): {ex.Message}");
                throw;
            }
        }

        public void Salvar(string caminhoRelativo, string nomeArquivo, byte[] conteudoBytes, string contentType = "application/pdf")
        {
            var key = ObterGcsKey(caminhoRelativo, nomeArquivo);

            try
            {
                using var memoryStream = new MemoryStream(conteudoBytes);
                memoryStream.Position = 0;

                _storageClient.UploadObject(
                    bucket: _bucketName,
                    objectName: key,
                    contentType: contentType, // Será application/pdf
                    source: memoryStream
                );
            }
            catch (Exception ex)
            {
                this.Log().Error($"[GoogleCloudStorageProvider] Erro ao salvar o arquivo binário '{nomeArquivo}' no GCS (Bucket: {_bucketName}, Key: {key}): {ex.Message}");
                throw;
            }
        }

        public byte[] Ler(string caminhoRelativo, string nomeArquivo)
        {
            var key = ObterGcsKey(caminhoRelativo, nomeArquivo);

            try
            {
                using var memoryStream = new MemoryStream();

                _storageClient.DownloadObject(_bucketName, key, memoryStream);
                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                this.Log().Error($"[GoogleCloudStorageProvider] Erro ao ler o arquivo '{nomeArquivo}' do GCS (Bucket: {_bucketName}, Key: {key}): {ex.Message}");
                throw;
            }
        }

        public bool Existe(string caminhoRelativo, string nomeArquivo)
        {
            var key = ObterGcsKey(caminhoRelativo, nomeArquivo);

            try
            {
                _storageClient.GetObject(_bucketName, key);
                return true;
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                this.Log().Error($"[GoogleCloudStorageProvider] Erro inesperado ao verificar existência do arquivo '{nomeArquivo}' no GCS (Bucket: {_bucketName}, Key: {key}): {ex.Message}");
                return false;
            }
        }

        private string ObterGcsKey(string caminhoRelativo, string nomeArquivo)
        {
            var key = Path.Combine(_prefixo, caminhoRelativo, nomeArquivo);
            return key.Replace("\\", "/").TrimStart('/');
        }
    }
}
