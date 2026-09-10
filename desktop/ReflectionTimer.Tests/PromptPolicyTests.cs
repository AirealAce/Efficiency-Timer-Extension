using System.Text.Json;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;

static class PromptPolicyTests
{
    internal static async Task Run(Action<bool,string> check)
    {
        var migrated=JsonSerializer.Deserialize<AppState>("{}",DataJson.Options)!;
        check(migrated.CompactAlwaysOnTop&&migrated.TimeOnlyAlwaysOnTop&&migrated.PromptAlwaysOnTop,"Existing and fresh profiles default all three viewers to always on top");
        var store=new MemoryStore{State=migrated};var session=new PreviewSession(store,isolatedProfile:true);
        session.Engine.SetAlwaysOnTop(false,true,false);
        var reopened=new PreviewSession(store).Engine.Snapshot;
        check(!reopened.CompactAlwaysOnTop&&reopened.TimeOnlyAlwaysOnTop&&!reopened.PromptAlwaysOnTop,"Independent window layering choices survive reopening");
        var first=session.Engine.TestPrompt();var second=session.Engine.TestPrompt();var third=session.Engine.TestPrompt();
        store.State.Prompts=store.State.Prompts.Select(p=>p.Id==first?p with{EndedEarly=true,ActualDurationSeconds=17,EarlyEndReason="Old reason"}:p).ToList();
        session=new PreviewSession(store,isolatedProfile:true);
        var windows=new List<FakeWindow>();var shown=new List<Guid>();int queued=0,maxEnds=0;
        FakeWindow Window(Guid id){var w=new FakeWindow(id,()=>windows.RemoveAll(x=>x.ReflectionId==id));windows.Add(w);return w;}
        var old=Window(first);var released=new TaskCompletionSource();
        old.Prepare=async()=>{await released.Task;session.Engine.SaveDraft(first,"Latest unsubmitted keystroke","Final reason");};
        var coordinator=new ReflectionPromptCoordinator(session,()=>windows.Cast<IReflectionPromptWindow>().ToArray(),(id,activate)=>{
            if(windows.All(w=>w.ReflectionId!=id))Window(id);shown.Add(id);
            maxEnds=Math.Max(maxEnds,windows.Count(w=>session.Engine.Snapshot.Prompts.Any(p=>p.Id==w.ReflectionId&&!p.IsCheckIn)));
        },()=>queued++);
        var openSecond=coordinator.OpenAsync(second,false);var openThird=coordinator.OpenAsync(third,false);
        check(shown.Count==0&&windows.Single()==old,"Concurrent prompt arrivals wait for the previous browser draft to flush");
        released.SetResult();await Task.WhenAll(openSecond,openThird);
        var sent=session.Engine.Snapshot.Outbox.Single(o=>o.Id==first);
        check(sent.AutoSent&&sent.EndedEarly&&sent.Message=="Latest unsubmitted keystroke"&&sent.EarlyEndReason=="Final reason"&&sent.ActualDurationSeconds==17,"Auto-send preserves newest text, early-end reason, elapsed time and both flags");
        check(session.Engine.Snapshot.Outbox.Single(o=>o.Id==second) is {AutoSent:true,Message:"",LocalOnly:true,Status:DeliveryStatus.Pending},"A blank replaced prompt is queued with auto-sent status without inventing a response");
        check(maxEnds==1&&windows.Single().ReflectionId==third&&queued==2,"Rapid arrivals leave exactly one session-end window and queue each replaced prompt once");
        await coordinator.OpenAsync(third,true);
        check(queued==2,"Refocusing the current prompt does not auto-send it");
        var next=session.Engine.TestPrompt();var current=windows.Single();
        current.Prepare=()=>throw new IOException("Synthetic browser save failure");
        try{await coordinator.OpenAsync(next,false);throw new Exception("Failed flush accepted");}catch(IOException){}
        check(windows.Single()==current&&current.Resumed==1&&session.Engine.Snapshot.Prompts.Any(p=>p.Id==next),"Failed browser flush keeps the previous window editable and the new prompt pending");
        current.Prepare=()=>Task.CompletedTask;store.Fail=true;
        try{await coordinator.OpenAsync(next,false);throw new Exception("Failed disk save accepted");}catch(IOException){}
        store.Fail=false;
        check(windows.Single()==current&&current.Resumed==2&&session.Engine.Snapshot.Outbox.Count==2,"Failed atomic queue retains the previous prompt without a duplicate or lost draft");
        await coordinator.OpenAsync(next,false);
        session.Engine.Start(900,false,50);var checkIn=session.Engine.CheckIn();await coordinator.OpenAsync(checkIn,true);
        var last=session.Engine.TestPrompt();await coordinator.OpenAsync(last,false);
        check(windows.Count==2&&windows.Any(w=>w.ReflectionId==checkIn)&&windows.Any(w=>w.ReflectionId==last)&&!session.Engine.Snapshot.Outbox.Any(o=>o.Id==checkIn),"Session-end replacements leave a separate check-in intact");
        var final=session.Engine.TestPrompt();windows.Single(w=>w.ReflectionId==last).Prepare=()=>{session.Engine.QueueReflection(last,"Submitted manually",localOnly:true);return Task.CompletedTask;};
        await coordinator.OpenAsync(final,false);
        check(session.Engine.Snapshot.Outbox.Count(o=>o.Id==last)==1&&!session.Engine.Snapshot.Outbox.Single(o=>o.Id==last).AutoSent,"A completed manual submit wins a race with auto-send without duplicating the entry");
        var stop=session.Engine.TestPrompt();await coordinator.OpenAsync(stop,false,()=>true);
        check(windows.Any(w=>w.ReflectionId==final)&&windows.All(w=>w.ReflectionId!=stop),"Shutdown prevents queued arrivals from reopening prompt windows");
        var persisted=new PreviewSession(store).Engine.Snapshot.Outbox.Single(o=>o.Id==first);
        check(persisted.AutoSent&&persisted.EndedEarly&&persisted.Message==sent.Message,"Auto-send metadata and original text survive restart");
    }
    private sealed class FakeWindow(Guid id,Action close):IReflectionPromptWindow
    {
        public Guid ReflectionId=>id;
        internal Func<Task> Prepare=()=>Task.CompletedTask;
        internal int Resumed;
        public Task PrepareAutoSendAsync()=>Prepare();
        public void ResumeEditing()=>Resumed++;
        public void CloseAfterSave()=>close();
    }
}
