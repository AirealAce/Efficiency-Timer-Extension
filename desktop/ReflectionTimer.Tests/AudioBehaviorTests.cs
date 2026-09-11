using System.Threading.Channels;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

static class AudioBehaviorTests
{
    internal static async Task Run(Action<bool,string> check)
    {
        foreach(var ending in new[]{"deadline","early","scheduled"})foreach(var behavior in Enum.GetValues<SoundBehavior>()) {
            var directory=Path.Combine(Path.GetTempPath(),"ReflectionTimer-AudioMix-"+Guid.NewGuid().ToString("N"));
            var now=DateTimeOffset.Now;var engine=new TimerEngine(new MemoryStore(),()=>now);
            engine.SetSound(SoundEvent.LowTime,new(){Behavior=SoundBehavior.Polite});
            engine.SetSound(SoundEvent.SessionEnd,new(){Behavior=behavior});
            engine.Start(20,ending=="deadline",80,lowTime:new(){Enabled=true,ThresholdSeconds=10});
            var prompt=engine.CheckIn();engine.SaveDraft(prompt,"Draft retained through sound transition");
            if(ending=="scheduled")engine.SaveSchedule(null,now.AddSeconds(18),60,false,80,lowTime:new(){Enabled=false});
            var backend=new HoldingAudio();
            using var services=new PreviewServices(engine,directory,audio:backend);
            try {
                now=now.AddSeconds(10);engine.Advance();var low=await backend.Next();
                check(low.Level.Gain==.8f&&!low.Token.IsCancellationRequested,"Low-time sound begins at its configured volume: "+ending+" / "+behavior);
                now=now.AddSeconds(ending=="deadline"?10:8);
                if(ending=="early")engine.EndEarly();else engine.Advance();
                var end=await backend.Next();
                engine.SaveDraft(prompt,"Updated completion draft"); // An unrelated state update must not stop retained audio.
                check(end.Level.Gain==.8f&&engine.Snapshot.Prompts.Single().Id==prompt,"Promoted session draft still triggers the session-end sound exactly once: "+ending+" / "+behavior);
                if(behavior==SoundBehavior.Disruptive)check(low.Token.IsCancellationRequested,"Disruptive completion stops low-time playback: "+ending);
                else {
                    check(!low.Token.IsCancellationRequested&&low.Level.Gain==(behavior==SoundBehavior.Assertive?.2f:.8f),behavior+" completion mixes with low-time audio without cancelling it: "+ending);
                    end.Complete.TrySetResult();await Until(()=>low.Level.Gain==.8f);
                    check(!low.Token.IsCancellationRequested&&low.Level.Gain==.8f,"Low-time audio retains playback and its normal volume after completion audio: "+ending+" / "+behavior);
                }
            } finally {
                services.Dispose();await backend.FinishAll();
                foreach(var file in new[]{"diagnostics.dat","diagnostics.dat.bak"})File.Delete(Path.Combine(directory,file));
                if(Directory.Exists(directory))Directory.Delete(directory);
            }
        }
        var audio=new HoldingAudio();using var player=new AlertSoundPlayer(audio);
        var politeTask=player.PlayAsync("polite.mp3",80,SoundBehavior.Polite,SoundEvent.LowTime);var polite=await audio.Next();
        var firstTask=player.PlayAsync("first.mp3",80,SoundBehavior.Assertive,SoundEvent.SessionEnd);var first=await audio.Next();
        var lastTask=player.PlayAsync("last.mp3",80,SoundBehavior.Assertive,SoundEvent.Success);var last=await audio.Next();
        check(polite.Level.Gain==.2f&&first.Level.Gain==.2f&&last.Level.Gain==.8f,"Newest assertive sound has priority; older audio is ducked, not cancelled");
        last.Complete.TrySetResult();await lastTask;
        check(first.Level.Gain==.8f&&polite.Level.Gain==.2f,"Earlier assertive sound regains priority when the newer one finishes");
        first.Complete.TrySetResult();await firstTask;
        check(polite.Level.Gain==.8f&&!polite.Token.IsCancellationRequested,"Polite sound returns to its original level after all assertive sounds end");
        await player.PlayAsync("muted.mp3",0,SoundBehavior.Disruptive,SoundEvent.Failure);
        check(!polite.Token.IsCancellationRequested,"A muted disruptive sound cannot interrupt audible playback");
        var disruptiveTask=player.PlayAsync("stop.mp3",80,SoundBehavior.Disruptive,SoundEvent.Failure);var disruptive=await audio.Next();
        check(await politeTask==AlertSoundResult.Cancelled&&polite.Token.IsCancellationRequested,"Disruptive stops existing sounds instead of pausing them");
        disruptive.Complete.TrySetResult();await disruptiveTask;
        check(polite.Token.IsCancellationRequested,"Previously interrupted sounds do not restart after disruptive playback");
        await audio.FinishAll();
    }
    private static async Task Until(Func<bool> condition){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));while(!condition())await Task.Delay(10,timeout.Token);}
    private sealed record Playback(AudioLevel Level,CancellationToken Token)
    {
        public TaskCompletionSource Complete=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Done=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class HoldingAudio:IAlertAudioBackend
    {
        private readonly Channel<Playback> started=Channel.CreateUnbounded<Playback>();
        private readonly List<Playback> all=[];
        public async Task PlayAsync(string path,AudioLevel level,CancellationToken cancellationToken)
        {
            var playback=new Playback(level,cancellationToken);lock(all)all.Add(playback);started.Writer.TryWrite(playback);
            try{await playback.Complete.Task.WaitAsync(cancellationToken);}finally{playback.Done.TrySetResult();}
        }
        public async Task<Playback> Next()=>await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
        public async Task FinishAll(){Playback[] voices;lock(all)voices=all.ToArray();foreach(var voice in voices)voice.Complete.TrySetResult();await Task.WhenAll(voices.Select(v=>v.Done.Task)).WaitAsync(TimeSpan.FromSeconds(4));}
    }
}
