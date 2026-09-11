using System.Text.Json;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;

static class ReflectionSeparatorTests
{
    internal static void Run(Action<bool,string> check)
    {
        check(AppState.CreateDefault().ReflectionSeparator == ReflectionSeparator.Newline
            && JsonSerializer.Deserialize<AppState>("{}")!.ReflectionSeparator == ReflectionSeparator.Newline,
            "New and existing profiles default saved reflection continuation to Newline");
        foreach (var (option, separator) in new[] {
            (ReflectionSeparator.None,""), (ReflectionSeparator.Comma,", "),
            (ReflectionSeparator.Bullet,"\n- "), (ReflectionSeparator.Newline,"\n") }) {
            var store = new MemoryStore(); var session = new PreviewSession(store, isolatedProfile:true);
            session.Engine.SetReflectionSeparator(option);
            var id = session.Engine.TestPrompt(); session.Engine.SaveDraft(id,"First response");
            check(Editing(session,id)=="First response",option+": background draft saves do not add separators");
            Save(session,id,"First response");
            session = new PreviewSession(store, isolatedProfile:true);
            check(session.Engine.Snapshot.ReflectionSeparator==option && Editing(session,id)=="First response"+separator,
                option+": setting and saved continuation survive restart");
            session.Engine.SaveDraft(id,Editing(session,id));
            Save(session,id,Editing(session,id));
            check(Editing(session,id)=="First response"+separator && session.Engine.Snapshot.Outbox.Count==0,
                option+": reopening, flushing and saving unchanged text cannot duplicate separators or send it");
            var combined=Editing(session,id)+"Second response";
            session.Engine.SaveDraft(id,combined); Save(session,id,combined);
            check(session.Engine.Snapshot.Prompts.Single().Draft==combined && Editing(session,id)==combined+separator,
                option+": a second response keeps the separator between responses and prepares the next continuation");
            session.Execute("queue",Data(new{id,text=Editing(session,id),reason=""}));
            check(session.Engine.Snapshot.Outbox.Single().Message==combined,
                option+": Save and send includes written content without an unused trailing separator");
            var blank=session.Engine.TestPrompt(); Save(session,blank,"");
            check(Editing(session,blank)=="",option+": saving a blank response keeps the editor empty");
            var maximum=new string('x',5000); Save(session,blank,maximum);
            check(Editing(session,blank)==maximum,option+": continuation never truncates a full 5,000-character response");
            var existing="Already separated"+separator; Save(session,blank,existing);
            check(Editing(session,blank)==existing,option+": an existing delimiter is not doubled");
            session.Engine.AutoSendReflection(blank,localOnly:true);
            check(session.Engine.Snapshot.Outbox.Last().Message==existing.Trim(),option+": auto-send preserves authored content");
        }
        var memory=new MemoryStore();var now=DateTimeOffset.Now;var current=new PreviewSession(memory,()=>now,isolatedProfile:true);
        current.Engine.SetReflectionSeparator(ReflectionSeparator.Bullet);
        current.Engine.Start(60,false,0);var checkIn=current.Engine.CheckIn();
        Save(current,checkIn,"Session note","Reason unchanged");now=now.AddSeconds(60);current.Tick();
        check(Editing(current,checkIn)=="Session note\n- " && current.Engine.Snapshot.Prompts.Single().EarlyEndReason=="Reason unchanged",
            "A saved check-in keeps its continuation when promoted to the session-end prompt; reasons are not delimited");
        current.AutoSendReflection(checkIn);
        check(current.Engine.Snapshot.Outbox.Single().Message=="Session note","Auto-send omits a prepared bullet when the saved response was never continued");
        var pending=current.Engine.TestPrompt();current.Engine.SaveDraft(pending,"Retain this");
        var before=JsonSerializer.Serialize(current.Engine.Snapshot);memory.Fail=true;
        try {Save(current,pending,"Newest edit");throw new Exception("Failed persistence accepted");}catch(IOException){}
        check(JsonSerializer.Serialize(current.Engine.Snapshot)==before,"Failed Save leaves the existing response and continuation state intact");
        memory.Fail=false;
        try {current.Engine.SetReflectionSeparator((ReflectionSeparator)99);throw new Exception("Invalid option accepted");}catch(ArgumentException){}
        check(JsonSerializer.Serialize(current.Engine.Snapshot)==before,"Invalid separator settings cannot change saved state");
    }
    private static JsonElement Data(object value)=>JsonSerializer.SerializeToElement(value,PreviewSession.Json);
    private static void Save(PreviewSession session,Guid id,string text,string reason="") {
        if(!session.Execute("saveForLater",Data(new{id,text,reason})).Close)throw new Exception("Save should close after persistence");
    }
    private static string Editing(PreviewSession session,Guid id)=>Data(session.View()).GetProperty("prompts").EnumerateArray()
        .Single(p=>p.GetProperty("id").GetGuid()==id).GetProperty("draft").GetString()!;
}
