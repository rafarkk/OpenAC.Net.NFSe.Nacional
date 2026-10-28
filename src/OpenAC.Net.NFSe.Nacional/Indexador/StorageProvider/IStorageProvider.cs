using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider
{
    /// <summary>
    /// Define a interface para provedores de armazenamento de documentos (XML, PDF, etc.).
    /// Permite abstrair o local de salvamento, seja ele Local, Cloud (GCS, AWS) ou FTP.
    /// </summary>
    public interface IStorageProvider
    {
        /// <summary>
        /// Salva um conteúdo de texto (como XML) no armazenamento.
        /// </summary>
        /// <param name="caminhoRelativo">O diretório ou prefixo da chave dentro do storage.</param>
        /// <param name="nomeArquivo">O nome do arquivo com extensão.</param>
        /// <param name="conteudo">O conteúdo de texto a ser gravado (será codificado em UTF-8).</param>
        /// <param name="contentType">O MIME type do arquivo (padrão: application/xml).</param>
        void Salvar(string caminhoRelativo, string nomeArquivo, string conteudo, string contentType = "application/xml");

        /// <summary>
        /// Salva um conteúdo binário (como PDF ou imagens) no armazenamento.
        /// </summary>
        /// <param name="caminhoRelativo">O diretório ou prefixo da chave dentro do storage.</param>
        /// <param name="nomeArquivo">O nome do arquivo com extensão.</param>
        /// <param name="conteudo">O array de bytes brutos do arquivo.</param>
        /// <param name="contentType">O MIME type do arquivo (padrão: application/pdf).</param>
        void Salvar(string caminhoRelativo, string nomeArquivo, byte[] conteudo, string contentType = "application/pdf");

        /// <summary>
        /// Lê o conteúdo de um arquivo do armazenamento e retorna como array de bytes.
        /// </summary>
        /// <param name="caminhoRelativo">O diretório ou prefixo da chave.</param>
        /// <param name="nomeArquivo">O nome do arquivo.</param>
        /// <returns>O array de bytes do arquivo lido.</returns>
        byte[] Ler(string caminhoRelativo, string nomeArquivo);

        /// <summary>
        /// Verifica se um arquivo existe no armazenamento.
        /// </summary>
        /// <param name="caminhoRelativo">O diretório ou prefixo da chave.</param>
        /// <param name="nomeArquivo">O nome do arquivo.</param>
        /// <returns>Verdadeiro se o arquivo existir; caso contrário, falso.</returns>
        bool Existe(string caminhoRelativo, string nomeArquivo);
    }
}
