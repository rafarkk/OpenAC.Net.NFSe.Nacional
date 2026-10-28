using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAC.Net.NFSe.Nacional.Common.Converter
{
    /// <summary>
    /// Conversor JSON para dados binários (PDF) compactados em GZip e codificados em Base64.
    /// </summary>
    public class ByteArrayGzipJsonConverter : JsonConverter<byte[]>
    {
        /// <summary>
        /// Lê uma string codificada em Base64 e compactada em GZip de JSON, descompacta e retorna os dados binários (PDF).
        /// </summary>
        /// <param name="reader">Leitor JSON.</param>
        /// <param name="typeToConvert">Tipo a ser convertido.</param>
        /// <param name="options">Opções do serializador JSON.</param>
        /// <returns>Array de bytes contendo os dados binários descompactados.</returns>
        public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            var compressedBytes = reader.GetBytesFromBase64();

            if (compressedBytes.Length == 0)
                return Array.Empty<byte>();

            using var memoryStream = new MemoryStream(compressedBytes);
            using var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
            using var memoryStreamOutput = new MemoryStream();

            gZipStream.CopyTo(memoryStreamOutput);

            return memoryStreamOutput.ToArray();
        }

        /// <summary>
        /// Escreve os dados binários (PDF) como Base64 compactado em GZip no JSON.
        /// </summary>
        /// <param name="writer">Gravador JSON.</param>
        /// <param name="value">Dados binários a serem compactados e escritos.</param>
        /// <param name="options">Opções do serializador JSON.</param>
        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            if (value == null || value.Length == 0)
            {
                writer.WriteNullValue();
                return;
            }

            using var memoryStream = new MemoryStream();

            using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress))
            {
                gZipStream.Write(value, 0, value.Length);
            }

            var compressedBytes = memoryStream.ToArray();
            writer.WriteBase64StringValue(compressedBytes);
        }
    }
}
