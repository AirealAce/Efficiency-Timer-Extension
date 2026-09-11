using System.Text.Json;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;

static class PromptPolicyTests
{
    internal static async Task Run(Action<bool,string> check)
    {
        var migrated=JsonSerializer.Deserialize<AppState>("{}",DataJson.Options)!;
        check(migrated.CompactAlwaysOnTop&&migrated.TimeOnlyAlwaysOnTop&&migrated.PromptAlwaysOnTop,"Existing and fresh profiles default all three viewers to always on top");
        check(migrated.AutoSendIncompleteReflections&&AppState.CreateDefault().AutoSendIncompleteReflections,"Incomplete-reflection auto-send defaults on for new and existing profiles");
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
            foreach(var previous in windows.Where(w=>w.ReflectionId!=id).ToArray())previous.CloseAfterSave();
            if(windows.All(w=>w.ReflectionId!=id))Window(id);shown.Add(id);
            maxEnds=Math.Max(maxEnds,windows.Count(w=>session.Engine.Snapshot.Prompts.Any(p=>p.Id==w.ReflectionId&&!p.IsCheckIn)));
            return Task.CompletedTask;
        },()=>queued++);
        var openSecond=coordinator.OpenAsync(second,false,sessionCompleted:true);var openThird=coordinator.OpenAsync(third,false,sessionCompleted:true);
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
        try{await coordinator.OpenAsync(next,false,sessionCompleted:true);throw new Exception("Failed flush accepted");}catch(IOException){}
        check(windows.Single()==current&&current.Resumed==1&&session.Engine.Snapshot.Prompts.Any(p=>p.Id==next),"Failed browser flush keeps the previous window editable and the new prompt pending");
        current.Prepare=()=>Task.CompletedTask;store.Fail=true;
        try{await coordinator.OpenAsync(next,false,sessionCompleted:true);throw new Exception("Failed disk save accepted");}catch(IOException){}
        store.Fail=false;
        check(windows.Single()==current&&current.Resumed==2&&session.Engine.Snapshot.Outbox.Count==2,"Failed atomic queue retains the previous prompt without a duplicate or lost draft");
        await coordinator.OpenAsync(next,false,sessionCompleted:true);
        session.Engine.Start(900,false,50);var checkIn=session.Engine.CheckIn();await coordinator.OpenAsync(checkIn,true);
        var last=session.Engine.TestPrompt();await coordinator.OpenAsync(last,false,sessionCompleted:true);
        check(windows.Count==1&&windows.Single().ReflectionId==last&&session.Engine.Snapshot.Prompts.Any(p=>p.Id==checkIn)&&!session.Engine.Snapshot.Outbox.Any(o=>o.Id==checkIn),"Session-end replacements retain a check-in without showing two windows or sending it");
        var final=session.Engine.TestPrompt();windows.Single(w=>w.ReflectionId==last).Prepare=()=>{session.Engine.QueueReflection(last,"Submitted manually",localOnly:true);return Task.CompletedTask;};
        await coordinator.OpenAsync(final,false,sessionCompleted:true);
        check(session.Engine.Snapshot.Outbox.Count(o=>o.Id==last)==1&&!session.Engine.Snapshot.Outbox.Single(o=>o.Id==last).AutoSent,"A completed manual submit wins a race with auto-send without duplicating the entry");
        var stop=session.Engine.TestPrompt();await coordinator.OpenAsync(stop,false,()=>true);
        check(windows.Any(w=>w.ReflectionId==final)&&windows.All(w=>w.ReflectionId!=stop),"Shutdown prevents queued arrivals from reopening prompt windows");
        var persisted=new PreviewSession(store).Engine.Snapshot.Outbox.Single(o=>o.Id==first);
        check(persisted.AutoSent&&persisted.EndedEarly&&persisted.Message==sent.Message,"Auto-send metadata and original text survive restart");
        await Navigation(check);
    }
    private static async Task Navigation(Action<bool,string> check)
    {
        var store=new MemoryStore();var session=new PreviewSession(store,isolatedProfile:true);
        session.Engine.SetAutoSendIncompleteReflections(false);
        check(!new PreviewSession(store).Engine.Snapshot.AutoSendIncompleteReflections,"Turning off auto-send survives app restart");
        var first=session.Engine.TestPrompt();var second=session.Engine.TestPrompt();var third=session.Engine.TestPrompt();
        var windows=new List<FakeWindow>();var shown=new List<Guid>();var queued=0;var failShow=false;
        var coordinator=new ReflectionPromptCoordinator(session,()=>windows.Cast<IReflectionPromptWindow>().ToArray(),(id,activate)=>{
            if(failShow)throw new IOException("Synthetic target load failure");
            foreach(var old in windows.Where(w=>w.ReflectionId!=id).ToArray())old.CloseAfterSave();
            if(windows.All(w=>w.ReflectionId!=id))windows.Add(new(id,()=>windows.RemoveAll(w=>w.ReflectionId==id)));
            shown.Add(id);return Task.CompletedTask;
        },()=>queued++);
        await coordinator.OpenAsync(first,true);
        windows.Single().Prepare=()=>{session.Engine.SaveDraft(first,"Last typed before Next");return Task.CompletedTask;};
        await coordinator.NavigateAsync(first,1);
        check(windows.Single().ReflectionId==second&&session.Engine.Snapshot.Prompts.Single(p=>p.Id==first).Draft=="Last typed before Next"&&queued==0,"Next flushes the newest draft, replaces the visible window, and never sends it");
        await coordinator.NavigateAsync(second,-1);
        check(windows.Single().ReflectionId==first&&session.Engine.Snapshot.Prompts.Count==3,"Prev restores a saved pending reflection without removing other drafts");
        var before=shown.Count;await coordinator.NavigateAsync(first,-1);await coordinator.NavigateAsync(Guid.NewGuid(),1);
        check(shown.Count==before&&queued==0,"Navigation at a boundary or from a stale prompt leaves the current window unchanged");
        await coordinator.OpenAsync(third,false,sessionCompleted:true);
        check(windows.Single().ReflectionId==third&&session.Engine.Snapshot.Prompts.Count==3&&queued==0,"Completion with auto-send off retains all older unfinished reflections");
        session.Engine.SetAutoSendIncompleteReflections(true);
        await coordinator.NavigateAsync(third,-1);await coordinator.OpenAsync(first,true);
        check(queued==0&&session.Engine.Snapshot.Prompts.Count==3,"Turning auto-send on does not send drafts while navigating or reopening saved responses");
        failShow=true;var current=windows.Single();
        try{await coordinator.NavigateAsync(first,1);throw new Exception("Target failure accepted");}catch(IOException){}
        check(windows.Single()==current&&current.Resumed>0,"A target editor load failure leaves the current draft editable and visible");
        failShow=false;current.Prepare=()=>throw new IOException("Synthetic navigation flush failure");
        try{await coordinator.NavigateAsync(first,1);throw new Exception("Flush failure accepted");}catch(IOException){}
        check(windows.Single()==current&&queued==0,"A failed navigation flush never closes or submits the current reflection");
        current.Prepare=()=>Task.CompletedTask;
        session.Engine.Start(60,false,0);var live=session.Engine.CheckIn();
        var fourth=session.Engine.TestPrompt();await coordinator.OpenAsync(fourth,false,sessionCompleted:true);
        check(queued==3&&session.Engine.Snapshot.Outbox.Count==3&&session.Engine.Snapshot.Prompts.Select(p=>p.Id).ToHashSet().SetEquals([live,fourth]),"Completion auto-sends all older matching session drafts, including closed ones, but not an active check-in or the new prompt");
        session.Engine.EndEarly();
        await coordinator.OpenAsync(live,false,sessionCompleted:true);
        check(session.Engine.Snapshot.Prompts.Any(p=>p.Id==fourth)&&!session.Engine.Snapshot.Outbox.Any(p=>p.Id==fourth),"A real completion never auto-sends practice reflections");
        var test=session.Engine.TestPrompt();await coordinator.OpenAsync(test,true);
        check(session.Engine.Snapshot.Prompts.Any(p=>p.Id==live)&&!session.Engine.Snapshot.Outbox.Any(p=>p.Id==live),"Opening a practice prompt never sends a real session reflection");
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
