using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FitnessApp.Filters;

// Read the form before model binding so its size errors are returned as 413,
// including on hosts that turn multipart binding exceptions into a generic 400.
public class ProfilePictureUploadLimitFilter : IAsyncResourceFilter
{
    public const long MaxMultipartBytes = 5 * 1024 * 1024 + 64 * 1024;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.ContentLength > MaxMultipartBytes)
        {
            context.Result = TooLarge();
            return;
        }
        if (!request.HasFormContentType)
        {
            await next();
            return;
        }

        var original = request.Body;
        using var bounded = new BoundedUploadStream(original);
        request.Body = bounded;
        try
        {
            await request.ReadFormAsync(context.HttpContext.RequestAborted);
            await next();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            context.Result = TooLarge();
        }
        catch (IOException)
        {
            context.Result = bounded.Exceeded ? TooLarge() :
                new BadRequestObjectResult(new { error = "The multipart upload is invalid or incomplete." });
        }
        finally
        {
            request.Body = original;
        }
    }

    private static ObjectResult TooLarge() => new(new { error = "Profile pictures must be 5 MB or smaller." })
        { StatusCode = StatusCodes.Status413PayloadTooLarge };

    private sealed class BoundedUploadStream(Stream inner) : Stream
    {
        private long bytesRead;
        public bool Exceeded { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => bytesRead; set => throw new NotSupportedException(); }

        private int ReadLength(int requested) => (int)Math.Min(requested, MaxMultipartBytes - bytesRead + 1);
        private int Count(int count)
        {
            bytesRead += count;
            if (bytesRead > MaxMultipartBytes)
            {
                Exceeded = true;
                throw new BadHttpRequestException("The upload exceeds the request size limit.", StatusCodes.Status413PayloadTooLarge);
            }
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, ReadLength(count)));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer[..ReadLength(buffer.Length)]));
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            Count(await inner.ReadAsync(buffer.AsMemory(offset, ReadLength(count)), ct));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Count(await inner.ReadAsync(buffer[..ReadLength(buffer.Length)], ct));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
