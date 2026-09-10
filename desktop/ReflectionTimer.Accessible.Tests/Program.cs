using System.Text.Json;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
JsonElement Data(object value) => JsonSerializer.SerializeToElement(value, PreviewSession.Json);
var now = DateTimeOffset.Now;
var store = new MemoryStore { State = PreviewSession.SampleState(now) };
var session = new PreviewSession(store, () => now);
Check(session.Engine.Snapshot.Timer.DurationSeconds == 900 && session.Engine.Snapshot.Timer.LowTime.Enabled, "Fresh timer and low-time defaults");
Check(session.Engine.Snapshot.FloatingPlacement == FloatingTimerPlacement.BottomLeft && session.Engine.Snapshot.PopupPosition == ReflectionPopupPosition.BottomRight, "Fresh window positions");
session.Execute("toggle", Data(new { seconds = 20, threshold = 15, repeat = false, lowTime = true }));
Check(session.Engine.Snapshot.Timer.IsRunning, "Real engine starts from bridge command");
now = now.AddSeconds(3); session.Execute("toggle", Data(new { }));
Check(!session.Engine.Snapshot.Timer.IsRunning && TimerEngine.Remaining(session.Engine.Snapshot.Timer, session.Engine.Now) == 17, "Pause retains elapsed time");
session.Execute("toggle", Data(new { seconds = 20, threshold = 15, repeat = false, lowTime = true }));
Check(TimerEngine.Remaining(session.Engine.Snapshot.Timer, session.Engine.Now) == 17, "Resume keeps remaining duration");
var announcements = new List<string>(); session.Announcement += announcements.Add;
now = now.AddSeconds(2); session.Tick(); session.Tick();
Check(announcements.Count(a => a.StartsWith("Low time")) == 1, "Low-time event announced once");
now = now.AddSeconds(15); session.Tick(); session.Tick();
Check(announcements.Count(a => a.StartsWith("Session finished")) == 1 && session.Engine.Snapshot.Prompts.Count == 1, "Completion produces one prompt and announcement");
var prompt = session.Engine.Snapshot.Prompts[0];
session.Execute("draft", Data(new { id = prompt.Id, text = "A draft that should survive.", reason = "" }));
var reopened = new PreviewSession(store, () => now);
Check(reopened.Engine.Snapshot.Prompts[0].Draft == "A draft that should survive.", "Draft survives session recreation");
session.Execute("queue", Data(new { id = prompt.Id, text = "Saved local reflection.", reason = "" }));
Check(session.Engine.Snapshot.Prompts.Count == 0 && session.Engine.Snapshot.Outbox.Last().Message == "Saved local reflection.", "Queue atomically moves draft into local outbox");
session.Execute("simulate", Data(new { id = prompt.Id }));
Check(session.Engine.Snapshot.Outbox.Last().Status == DeliveryStatus.Sent, "Local delivery simulation changes the same record");
var serialized = JsonSerializer.Serialize(session.View(), PreviewSession.Json);
Check(serialized.Contains("Simulated success") && !serialized.Contains("apiToken") && !serialized.Contains("receiverUrl") && !serialized.Contains("sheetUrl"), "View exposes simulation truthfully and excludes connection fields");
var start = now.AddHours(1).ToLocalTime().ToString("yyyy-MM-ddTHH:mm");
session.Execute("schedule", Data(new { start, seconds = 60 }));
Check(session.Engine.Snapshot.Schedules.Count == 3, "Schedule saved through engine");
var scheduleId = session.Engine.Snapshot.Schedules.First(s => s.DurationSeconds == 60).Id;
session.Execute("removeSchedule", Data(new { id = scheduleId }));
Check(session.Engine.Snapshot.Schedules.All(s => s.Id != scheduleId), "Schedule removed by stable ID");
var before = JsonSerializer.Serialize(session.Engine.Snapshot);
try { session.Execute("toggle", Data(new { seconds = -10 })); throw new Exception("Invalid duration accepted"); } catch (ArgumentException) { }
Check(JsonSerializer.Serialize(session.Engine.Snapshot) == before, "Invalid bridge input leaves state untouched");
store.Fail = true;
try { session.Execute("reset", Data(new { seconds = 10 })); throw new Exception("Failed save accepted"); } catch (IOException) { }
Check(JsonSerializer.Serialize(session.Engine.Snapshot) == before, "Failed persistence does not change live state");
store.Fail = false;
Check(PreviewWindow.Allowed("https://reflection-timer.invalid/index.html?view=main"), "Local UI origin allowed");
Check(new[] { "https://evil.example/index.html", "file:///C:/private.txt", "https://reflection-timer.invalid:4430/index.html", "https://reflection-timer.invalid/private.txt", "https://reflection-timer.invalid@evil.example/index.html", "http://reflection-timer.invalid/app.js" }.All(address => !PreviewWindow.Allowed(address)), "Unexpected schemes, origins, ports and paths rejected");
var directory = Path.Combine(Path.GetTempPath(), "ReflectionTimer-AccessibleTest-" + Guid.NewGuid().ToString("N"));
try {
    var encrypted = new EncryptedStore(directory); encrypted.Save(store.State);
    Check(!File.ReadAllText(Path.Combine(directory, "state.dat")).Contains("Saved local reflection."), "Preview uses encrypted storage");
    Check(new PreviewSession(encrypted).Engine.Snapshot.Outbox.Last().Message == "Saved local reflection.", "Encrypted preview data survives reopening");
} finally { File.Delete(Path.Combine(directory, "state.dat")); if (Directory.Exists(directory)) Directory.Delete(directory); }
Console.WriteLine($"{passed} tests passed.");

sealed class MemoryStore : IStateStore
{
    public AppState State = new(); public bool Fail;
    public AppState Load() => DataJson.Clone(State);
    public void Save(AppState state) { if (Fail) throw new IOException("Simulated disk failure"); State = DataJson.Clone(state); }
}
