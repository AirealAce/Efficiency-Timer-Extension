using System.Text.Json;
using ReflectionTimer.Core;

internal static partial class Program
{
    private static void TestSafeDelivery()
    {
        Test("protected uploads retry the same immutable entry after backoff", () => {
            var f = new Fixture(); var id = f.Queue(); var first = f.Engine.BeginUpload(true)!;
            Is(first.RetryProtected); f.Engine.FinishUpload(id, false, "timeout", retryable: true);
            Is(f.Engine.BeginUpload(true) is null); f.Move(15); var second = f.Engine.BeginUpload(true)!;
            Equal(id, second.Id); Equal(first.SubmittedAt, second.SubmittedAt); Equal(first.Message, second.Message); Equal(2, second.Attempts);
            f.Engine.FinishUpload(id, true, tab: "test"); Is(f.Engine.BeginUpload(true) is null);
        });
        Test("protected interrupted uploads recover with delay and original ID", () => {
            var f = new Fixture(); var id = f.Queue(); f.Engine.BeginUpload(true); var e = f.Restart();
            Equal(DeliveryStatus.Pending, e.Snapshot.Outbox.Single().Status); Is(e.BeginUpload(true) is null);
            f.Move(15); Equal(id, e.BeginUpload(true)!.Id);
        });
        Test("legacy attempts never gain automatic retry protection retroactively", () => {
            var f = new Fixture(); var id = f.Queue(); f.Engine.BeginUpload(); f.Engine.FinishUpload(id, false, "timeout", retryable: true);
            Equal(DeliveryStatus.NeedsReview, f.Engine.Snapshot.Outbox.Single().Status);
            f.Engine.RetryUpload(id); Is(!f.Engine.BeginUpload(true)!.RetryProtected);
        });
        Test("partial writes and receiver capability downgrade are held", () => {
            var f = new Fixture(); var id = f.Queue(); f.Engine.BeginUpload(true); f.Engine.FinishUpload(id, false, "write_uncertain");
            Equal(DeliveryStatus.NeedsReview, f.Engine.Snapshot.Outbox.Single().Status);
            f.Engine.RetryUpload(id); Is(f.Engine.Snapshot.Outbox.Single().Id != id);
            f.Engine.BeginUpload(true); var next = f.Engine.Snapshot.Outbox.Single().Id;
            f.Engine.FinishUpload(next, false, "network", retryable: true); f.Move(20);
            Is(f.Engine.BeginUpload(false) is null); Equal("receiver_changed", f.Engine.Snapshot.Outbox.Single().ErrorKind);
        });
        Test("retry budget is finite and delays are capped", () => {
            var f = new Fixture(); var id = f.Queue();
            for (var attempt = 1; attempt <= 8; attempt++) {
                Equal(attempt, f.Engine.BeginUpload(true)!.Attempts); f.Engine.FinishUpload(id, false, "network", retryable: true);
                var next = f.Engine.Snapshot.Outbox.Single();
                if (attempt < 8) { Is(next.NextAttemptAt <= f.Engine.Now + 300000); f.Move(300); }
                else Equal(DeliveryStatus.NeedsReview, next.Status);
            }
        });
        Test("changing receiver cannot reroute a queued reflection", () => {
            var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true); f.Queue();
            Throws<InvalidOperationException>(() => f.Engine.SaveSettings(Connection with { WebAppUrl = "https://script.google.com/macros/s/different/exec" }, true, false, true));
            Equal(Connection, f.Engine.Snapshot.Connection);
            // Old imported/migrated state still gets the delivery-time backstop.
            f.Store.Data = f.Engine.Snapshot; f.Store.Data.Connection = Connection with { WebAppUrl = "https://script.google.com/macros/s/different/exec" };
            var restarted = f.Restart(); Is(restarted.BeginUpload(true) is null); Equal("receiver_changed", restarted.Snapshot.Outbox.Single().ErrorKind);
        });
        Test("failed upload-state commit keeps the prior sending state recoverable", () => {
            var f = new Fixture(); var id = f.Queue(); f.Engine.BeginUpload(true); var before = JsonSerializer.Serialize(f.Engine.Snapshot);
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.FinishUpload(id, false, "timeout", retryable: true));
            Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
        });
    }
}
