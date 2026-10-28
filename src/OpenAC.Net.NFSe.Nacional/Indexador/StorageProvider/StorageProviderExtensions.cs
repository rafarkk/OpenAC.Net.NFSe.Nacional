using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider
{
    /// <summary>
    /// Fornece métodos práticos (sobrecargas) para qualquer IStorageProvider, 
    /// facilitando o uso sem obrigar implementações complexas.
    /// </summary>
    public static class StorageProviderExtensions
    {
        /// <summary>
        /// Salva um conteúdo de texto no armazenamento utilizando um caminho completo ou chave única.
        /// </summary>
        /// <param name="caminhoCompletoOuChave">O caminho absoluto, completo ou a chave (Object Key) do arquivo.</param>
        /// <param name="conteudo">O conteúdo de texto a ser gravado.</param>
        /// <param name="contentType">O MIME type do arquivo (padrão: application/xml).</param>
        public static void Salvar(this IStorageProvider provider, string caminhoCompletoOuChave, string conteudo, string contentType = "application/xml")
        {
            provider.Salvar(string.Empty, caminhoCompletoOuChave, conteudo, contentType);
        }

        /// <summary>
        /// Salva um conteúdo binário no armazenamento utilizando um caminho completo ou chave única.
        /// </summary>
        /// <param name="caminhoCompletoOuChave">O caminho absoluto, completo ou a chave (Object Key) do arquivo.</param>
        /// <param name="conteudo">O array de bytes brutos do arquivo.</param>
        /// <param name="contentType">O MIME type do arquivo (padrão: application/pdf).</param>
        public static void Salvar(this IStorageProvider provider, string caminhoCompletoOuChave, byte[] conteudo, string contentType = "application/pdf")
        {
            provider.Salvar(string.Empty, caminhoCompletoOuChave, conteudo, contentType);
        }

        /// <summary>
        /// Lê o conteúdo de um arquivo do armazenamento utilizando o caminho completo ou chave.
        /// </summary>
        /// <param name="caminhoCompletoOuChave">O caminho absoluto, completo ou a chave (Object Key) do arquivo.</param>
        /// <returns>O array de bytes do arquivo lido.</returns>
        public static byte[] Ler(this IStorageProvider provider, string caminhoCompletoOuChave)
        {
            return provider.Ler(string.Empty, caminhoCompletoOuChave);
        }

        /// <summary>
        /// Verifica se um arquivo existe no armazenamento utilizando o caminho completo ou chave.
        /// </summary>
        /// <param name="caminhoCompletoOuChave">O caminho absoluto, completo ou a chave (Object Key) do arquivo.</param>
        /// <returns>Verdadeiro se o arquivo existir; caso contrário, falso.</returns>
        public static bool Existe(this IStorageProvider provider, string caminhoCompletoOuChave)
        {
            return provider.Existe(string.Empty, caminhoCompletoOuChave);
        }
    }
}
