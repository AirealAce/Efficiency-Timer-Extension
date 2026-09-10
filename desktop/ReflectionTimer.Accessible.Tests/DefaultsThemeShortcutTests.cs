using System.Text.Json;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

static class DefaultsThemeShortcutTests
{
    internal static void Run(Action<bool,string> check)
    {
        var store=new MemoryStore();
        var session=new PreviewSession(store);
        var state=session.Engine.Snapshot;
        check(state.Timer.DurationSeconds==900&&state.Timer.RemainingSeconds==900&&!state.Timer.IsRunning&&!state.Timer.AutoRestart&&state.Timer.AutoRestartUntil is null,"New profile starts ready at 15 minutes with repeat and cutoff off");
        check(state.ShowFloatingTimer&&state.FloatingPlacement==FloatingTimerPlacement.BottomLeft&&state.PopupPosition==ReflectionPopupPosition.BottomRight,"New profile enables Compact at bottom left and reflections at bottom right");
        check(state.Timer.LowTime.Enabled&&state.Timer.LowTime.ThresholdSeconds is null&&AudioSettings.From(state).LowTimeThresholdSeconds==15,"New profile enables the inherited 15-second low-time warning");
        check(state.Timer.Volume==50&&state.Theme==AppColorTheme.Dark&&state.LoggingEnabled&&!state.StartAtLogin,"New profile keeps the original volume, Dark theme, diagnostics, and startup defaults");
        check(state.ScheduleOverlap==ScheduleOverlapPolicy.EndWithReflection&&state.Schedules.Count==0&&state.Outbox.Count==0&&state.Prompts.Count==0,"New profile has the original overlap policy and no demonstration entries");
        foreach(var (kind,file) in new[]{(SoundEvent.SessionEnd,"popup.mp3"),(SoundEvent.Success,"pokemon-level-up.mp3"),(SoundEvent.Failure,"kirby-out-of-health.mp3"),(SoundEvent.LowTime,"pokemon-battle-trainer.mp3")}){
            var sound=AudioSettings.From(state).For(kind);
            var path=SoundLibrary.Resolve(kind,sound)!;
            check(Path.GetFileName(path)==file&&File.Exists(path)&&sound.Behavior==SoundBehavior.Disruptive&&sound.Volume==100&&!sound.FadeOutEnabled&&sound.FadeOutAfterSeconds==10,"Bundled MP3 and original playback defaults for "+kind);
        }
        foreach(var track in SoundLibrary.Tracks){
            var path=Path.Combine(AppContext.BaseDirectory,SoundLibrary.FileName(track));
            check(Mp3AudioBackend.ValidateCustomFile(path)==path,"Packaged audio decodes: "+SoundLibrary.FileName(track));
        }
        check(SoundLibrary.Tracks.Count()==8,"All eight MP3 library tracks ship with the preview");
        foreach(var theme in Enum.GetValues<AppColorTheme>()){
            session.Engine.SetTheme(theme);
            check(new PreviewSession(store).Engine.Snapshot.Theme==theme,"Theme preference survives restart: "+theme);
            var palette=PreviewTheme.Palette(theme);
            check(palette.Text!=palette.Background&&palette.SelectionText!=palette.Selection&&palette.Accent!=palette.Raised,"Native frame and menu palette has distinct text/selection colors: "+theme);
        }
        var contrast=PreviewTheme.Palette(AppColorTheme.Glamour,true);
        check(contrast.Background==SystemColors.Control&&contrast.Selection==SystemColors.Highlight,"Windows contrast overrides decorative native colors");
        session.Engine.SetFloatingTimer(false);session.Engine.SetAppVolume(31);
        session.Engine.SetSound(SoundEvent.Success,new(){Track=LibrarySound.PokemonHealed,Volume=42});
        var retained=new PreviewSession(store).Engine.Snapshot;
        check(!retained.ShowFloatingTimer&&retained.Timer.Volume==31&&AudioSettings.From(retained).Success.Track==LibrarySound.PokemonHealed&&AudioSettings.From(retained).Success.Volume==42,"Reopening preserves explicit saved preferences instead of resetting defaults");

        Exception? failure=null;
        var thread=new Thread(()=>{
            try {
                var backend=new Registration();var calls=new int[5];
                backend.Blocked.Add(GlobalShortcut.CompactId);
                using var keys=new PreviewShortcuts(Enumerable.Range(0,5).Select(i=>(Action)(()=>calls[i]++)).ToArray(),backend:backend);
                check(backend.Requests.Select(r=>r.Key).SequenceEqual(new uint[]{0x54,0xC0,0xBF,0xBE,0xBC})&&backend.Requests.All(r=>r.Modifiers==(0x0002|0x0001|0x4000)),"All five original global chords register with Ctrl+Alt and no key-repeat");
                var states=JsonSerializer.SerializeToElement(keys.Status,PreviewSession.Json);
                check(states.EnumerateArray().Select(s=>s.GetProperty("available").GetBoolean()).SequenceEqual(new[]{true,true,false,true,true}),"A shortcut conflict reports the correct unavailable chord");
                foreach(var chord in PreviewShortcuts.Chords)keys.Dispatch(GlobalShortcut.HotKeyMessage,chord.Id);
                check(calls.SequenceEqual(new[]{1,1,0,1,1}),"Registered shortcut messages invoke exactly their matching action");
                var attempts=backend.Requests.Count;keys.RetryUnavailable();
                check(backend.Requests.Count==attempts+1&&!keys.Dispatch(GlobalShortcut.HotKeyMessage,GlobalShortcut.CompactId),"Only unavailable chords retry while another app owns them");
                backend.Blocked.Clear();check(keys.RetryUnavailable(),"Releasing a competing shortcut recovers without restarting");
                keys.Dispatch(GlobalShortcut.HotKeyMessage,GlobalShortcut.CompactId);
                check(calls[2]==1&&!keys.Dispatch(0,GlobalShortcut.HotKeyId)&&!keys.Dispatch(GlobalShortcut.HotKeyMessage,-1),"Recovered shortcut works and unrelated messages are ignored");
                keys.Dispose();check(backend.Removed.Count==5&&!keys.Dispatch(GlobalShortcut.HotKeyMessage,GlobalShortcut.HotKeyId),"Exit releases every owned shortcut and stops dispatch");
                var clock=new Clock();var pairs=new ConsecutiveShortcutPresses(clock);
                var first=pairs.Press();clock.Ticks+=TimeSpan.FromMilliseconds(799).Ticks;
                check(!first&&pairs.Press()&&!pairs.Press(),"Period double press selects App once and consumes the pair");
                clock.Ticks+=TimeSpan.FromMilliseconds(801).Ticks;check(!pairs.Press(),"A slow period press starts a new Compact action");
                pairs.Reset();check(!pairs.Press(),"Another shortcut clears the pending period pair");
            }catch(Exception error){failure=error;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)throw failure;
    }
    private sealed class Registration : IHotKeyRegistration
    {
        public HashSet<int> Blocked=[];public List<(uint Key,uint Modifiers)> Requests=[];public List<int> Removed=[];
        public bool Register(nint window,int id,uint modifiers,uint key){Requests.Add((key,modifiers));return !Blocked.Contains(id);}
        public bool Unregister(nint window,int id){Removed.Add(id);return true;}
    }
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long GetTimestamp()=>Ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
    }
}
