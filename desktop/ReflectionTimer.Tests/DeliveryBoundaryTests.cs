using System.Diagnostics;
using System.Net;
using System.Text;
using ReflectionTimer.Core;

internal static partial class Program
{
    private static async Task TestDeliveryBoundaries()
    {
        await TestAsync("public health success does not pass an authenticated connection check", async () => {
            using var client = new SheetsClient(new FakeHttp(_ => Json("{\"success\":true,\"service\":\"Reflection Timer\"}")));
            var reply = await client.Ping(Connection); Is(!reply.Success); Equal("invalid_response", reply.ErrorKind);
        });
        await TestAsync("bounded UTF-8 JSON with a BOM is still accepted", async () => {
            var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("{\"success\":true,\"target\":\"Synthetic / test\"}")).ToArray();
            using var client = new SheetsClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
            Is((await client.Ping(Connection)).Success);
        });
        await TestAsync("same deployment spelling keeps queued delivery and original entry data", async () => {
            var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true); f.Queue();
            var before = f.Engine.Snapshot.Outbox.Single();
            var canonical = Connection with { WebAppUrl = Connection.WebAppUrl.Replace("https://script.google.com", "https://SCRIPT.GOOGLE.COM") + "/" };
            f.Engine.SaveSettings(canonical, true, false, true);
            var entry = f.Engine.BeginUpload(true)!; Is(entry is not null); Equal(before.ReceiverUrl, entry!.ReceiverUrl);
            using var client = new SheetsClient(new FakeHttp(_ => Json("{\"success\":true,\"sheet\":\"test\",\"deliveryProtocol\":\"request-id-v1\"}")));
            var reply = await client.Upload(canonical, entry); Is(reply.Success);
            f.Engine.FinishUpload(entry.Id, reply.Success, reply.ErrorKind, reply.Tab);
            Equal(DeliveryStatus.Sent, f.Engine.Snapshot.Outbox.Single().Status); Equal(before.Message, entry.Message); Equal(before.SubmittedAt, entry.SubmittedAt);
        });
        foreach (var body in new[] { "{\"success\":true}", "{\"success\":true,\"sheet\":\"test\"}", "{\"success\":true,\"sheet\":\"  \",\"deliveryProtocol\":\"request-id-v1\"}" })
            await TestAsync("incomplete acknowledgement never marks a protected reflection sent: " + body, async () => {
                var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true); f.Queue(); var entry = f.Engine.BeginUpload(true)!;
                using var client = new SheetsClient(new FakeHttp(_ => Json(body)));
                var reply = await client.Upload(Connection, entry); Is(!reply.Success); Equal("invalid_response", reply.ErrorKind); Is(!reply.Retryable);
                f.Engine.FinishUpload(entry.Id, reply.Success, reply.ErrorKind, reply.Tab, reply.Retryable);
                Equal(DeliveryStatus.NeedsReview, f.Engine.Snapshot.Outbox.Single().Status); Equal(entry.Message, f.Engine.Snapshot.Outbox.Single().Message);
            });
        await TestAsync("legacy upload also rejects a public health success without a destination", async () => {
            using var client = new SheetsClient(new FakeHttp(_ => Json("{\"success\":true,\"service\":\"Reflection Timer\"}")));
            Is(!(await client.Upload(Connection, new OutboxItem { Message = "Synthetic", SheetUrl = Connection.SheetUrl })).Success);
        });
        await TestAsync("oversized declared responses are rejected without reading their body", async () => {
            var reads = 0;
            var stream = new ScriptedResponseStream((_, _) => { reads++; throw new Exception("Must not read an oversized body."); });
            var content = new StreamContent(stream); content.Headers.ContentLength = SheetsClient.MaxResponseBytes + 1;
            using var client = new SheetsClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = content }));
            Equal("invalid_response", (await client.Ping(Connection)).ErrorKind); Equal(0, reads);
        });
        await TestAsync("unbounded chunked responses stop at the byte limit", async () => {
            var readBytes = 0;
            var stream = new ScriptedResponseStream((buffer, _) => { buffer.Span.Fill((byte)'x'); readBytes += buffer.Length; return ValueTask.FromResult(buffer.Length); });
            using var client = new SheetsClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
            Equal("invalid_response", (await client.Ping(Connection)).ErrorKind); Equal(SheetsClient.MaxResponseBytes + 1, readBytes);
        });
        await TestAsync("a stalled body uses the response deadline after headers arrive", async () => {
            var stream = new ScriptedResponseStream(async (_, cancellation) => { await Task.Delay(Timeout.Infinite, cancellation); return 0; });
            using var client = new SheetsClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stream) }), TimeSpan.FromMilliseconds(150));
            var reply = await client.Ping(Connection); Equal("timeout", reply.ErrorKind); Is(reply.Retryable);
        });
        await TestAsync("all redirects share one deadline instead of renewing the timeout", async () => {
            var calls = 0;
            using var client = new SheetsClient(new AsyncResponseHandler(async (_, cancellation) => {
                calls++; await Task.Delay(100, cancellation); return Redirect("https://script.googleusercontent.com/macros/echo?synthetic=1");
            }), TimeSpan.FromMilliseconds(150));
            Equal("timeout", (await client.Ping(Connection)).ErrorKind); Is(calls <= 2);
        });
        await TestAsync("explicit cancellation is not advertised as automatically retryable", async () => {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            using var client = new SheetsClient(new FakeHttp(_ => Json("{\"success\":true}")));
            var reply = await client.Ping(Connection, cancellation.Token); Equal("timeout", reply.ErrorKind); Is(!reply.Retryable);
        });
        await TestAsync("interrupted body reads produce a safe retryable network error", async () => {
            var stream = new ScriptedResponseStream((_, _) => throw new IOException("Synthetic private server detail"));
            using var client = new SheetsClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
            var reply = await client.Ping(Connection); Equal("network", reply.ErrorKind); Is(reply.Retryable); Is(!reply.DisplayMessage.Contains("private"));
        });
    }
    private sealed class AsyncResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class ScriptedResponseStream(Func<Memory<byte>, CancellationToken, ValueTask<int>> read) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => read(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
