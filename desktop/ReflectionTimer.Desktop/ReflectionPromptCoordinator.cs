namespace ReflectionTimer.Accessible;

internal interface IReflectionPromptWindow
{
    Guid ReflectionId { get; }
    Task PrepareAutoSendAsync();
    void ResumeEditing();
    void CloseAfterSave();
}

// Serialize replacements so no second session-end window opens until the first
// draft is durably queued. Network delivery is independent of this handoff.
internal sealed class ReflectionPromptCoordinator(PreviewSession session,
    Func<IReadOnlyList<IReflectionPromptWindow>> openWindows, Action<Guid,bool> show, Action queued)
{
    private readonly SemaphoreSlim gate = new(1,1);
    internal async Task OpenAsync(Guid id, bool activate, Func<bool>? stopping = null)
    {
        await gate.WaitAsync();
        try {
            if(stopping?.Invoke()==true)return;
            var target=session.Engine.Snapshot.Prompts.SingleOrDefault(p=>p.Id==id);
            if(target is null)return;
            if(!target.IsCheckIn) {
                foreach(var previous in openWindows().Where(w=>w.ReflectionId!=id).ToArray()) {
                    if(session.Engine.Snapshot.Prompts.Any(p=>p.Id==previous.ReflectionId&&p.IsCheckIn))continue;
                    try {
                        await previous.PrepareAutoSendAsync();
                        if(session.AutoSendReflection(previous.ReflectionId))queued();
                        previous.CloseAfterSave();
                    }
                    catch { previous.ResumeEditing();throw; }
                }
            }
            if(stopping?.Invoke()!=true&&session.Engine.Snapshot.Prompts.Any(p=>p.Id==id))show(id,activate);
        }
        finally {gate.Release();}
    }
    internal async Task ExclusivelyAsync(Func<Task> action)
    {
        await gate.WaitAsync();
        try {await action();} finally {gate.Release();}
    }
}
