using System.Text.Json;

namespace IndigoMovieManager.Thumbnail.Ipc
{
    // 長さ付きUTF-8 JSONへ固定し、client/serverで同じフレーミングを使う。
    public static class NamedPipeMessageFraming
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
        };

        public static async Task WriteAsync<T>(
            Stream stream,
            T payload,
            CancellationToken cancellationToken
        )
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            byte[] lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
            await stream.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(jsonBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            byte[] lengthBytes = await ReadExactAsync(stream, sizeof(int), cancellationToken)
                .ConfigureAwait(false);
            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length < 0)
            {
                throw new InvalidOperationException("negative message length.");
            }

            byte[] jsonBytes = await ReadExactAsync(stream, length, cancellationToken)
                .ConfigureAwait(false);
            T payload = JsonSerializer.Deserialize<T>(jsonBytes, JsonOptions);
            if (payload == null)
            {
                throw new InvalidOperationException("pipe message deserialize failed.");
            }

            return payload;
        }

        private static async Task<byte[]> ReadExactAsync(
            Stream stream,
            int length,
            CancellationToken cancellationToken
        )
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream
                    .ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("pipe message ended unexpectedly.");
                }

                offset += read;
            }

            return buffer;
        }
    }
}
