using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenAC.Net.NFSe.Nacional.Indexador.StorageProvider
{
    public interface IStorageProvider
    {
        void Salvar(string caminhoRelativo, string nomeArquivo, string conteudo);
        byte[] Ler(string caminhoRelativo, string nomeArquivo);
        bool Existe(string caminhoRelativo, string nomeArquivo);
    }
}
