using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;

// Opt-in native WebView smoke test. Only this test's in-memory session and its
// disposable WebView profile are used; no production windows or input injection.
static class NativeReflectionSmoke
{
    internal static void Run()
    {
        Exception? failure=null;var passed=0;
        void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        var thread=new Thread(()=>{
            PreviewApplication? app=null;
            try {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
                var now=DateTimeOffset.Now;
                var state=new AppState{ShowFloatingTimer=false,LoggingEnabled=false,Theme=AppColorTheme.Glamour,Timer=new(){Volume=0}};
                var session=new PreviewSession(new MemoryStore{State=state},()=>now,isolatedProfile:true);
                session.Engine.Start(60,false,0,lowTime:new(){Enabled=false});
                var id=session.Engine.CheckIn();session.Engine.SaveDraft(id,"Saved native-window draft","Native reason draft");
                var directory=Path.Combine(Path.GetTempPath(),"ReflectionTimer-NativeSmoke-"+Guid.NewGuid().ToString("N"));
                app=new PreviewApplication(session,directory,startInTray:true,profileName:"native-smoke");
                _=app.MainForm!.Handle;
                app.MainForm.BeginInvoke(async()=>{
                    try {
                        app.Open("reflection",id);
                        var window=Windows(app).Single(w=>w.View=="reflection");
                        Check(!window.Visible,"New reflection stays hidden while its editor is initializing");
                        await Until(()=>window.Visible);
                        var first=await Read(window);
                        Check(first.GetProperty("draft").GetString()=="Saved native-window draft"&&first.GetProperty("theme").GetString()=="3","First native display has the saved response and Glamour theme already loaded");
                        Check(first.GetProperty("reasonVisible").GetBoolean()&&first.GetProperty("reason").GetString()=="Native reason draft","Pre-completion native prompt shows its saved reason");
                        await Script(window,"document.querySelector('#later').focus()");
                        ((IReflectionShortcutTarget)window).FocusOrSaveDraft();
                        await UntilAsync(async()=>(await Read(window)).GetProperty("focused").GetString()=="reflection-text");
                        Check(!window.IsDisposed,"Comma from a non-text control focuses the first box without closing");
                        await Script(window,"document.querySelector('#reflection-text').value='Latest native draft';document.querySelector('#reflection-text').dispatchEvent(new Event('input',{bubbles:true}))");
                        ((IReflectionShortcutTarget)window).FocusOrSaveDraft();
                        await Until(()=>window.IsDisposed);
                        Check(session.Engine.Snapshot.Prompts.Single().Draft=="Latest native draft"&&session.Engine.Snapshot.Outbox.Count==0,"Native Save shortcut persists the latest text and closes without submission");
                        app.Open("reflection",id);var reopened=Windows(app).Single(w=>w.View=="reflection");await Until(()=>reopened.Visible);
                        Check((await Read(reopened)).GetProperty("draft").GetString()=="Latest native draft","Recreated native prompt restores the saved draft before display");
                        await Script(reopened,"document.querySelector('#reflection-text').value='Typed just before zero';document.querySelector('#reflection-text').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#early-reason').focus()");
                        now=now.AddSeconds(60);session.Tick();app.Open("reflection",id);
                        await UntilAsync(async()=>!(await Read(reopened)).GetProperty("reasonVisible").GetBoolean());
                        var completed=await Read(reopened);
                        Check(ReferenceEquals(reopened,Windows(app).Single(w=>w.View=="reflection"))&&completed.GetProperty("draft").GetString()=="Typed just before zero","Natural completion updates the existing native editor without blanking in-flight text");
                        Check(completed.GetProperty("focused").GetString()=="reflection-text"&&!completed.GetProperty("reasonVisible").GetBoolean(),"Natural completion hides the reason and leaves focus in the response box");
                        ((IReflectionShortcutTarget)reopened).FocusOrSaveDraft();await Until(()=>reopened.IsDisposed);
                        Check(session.Engine.Snapshot.Prompts.Single() is {IsCheckIn:false,EndedEarly:false,Draft:"Typed just before zero"}&&session.Engine.Snapshot.Outbox.Count==0,"Completed native draft saves under the original ID without a duplicate entry");
                        session.Engine.SetAutoSendIncompleteReflections(false);
                        session.Engine.Start(60,false,0,lowTime:new(){Enabled=false});now=now.AddSeconds(5);
                        var result=session.Execute("startOrEnd",JsonSerializer.SerializeToElement(new{}));
                        var earlyId=result.OpenReflection!.Value;
                        Check(result.SessionCompleted&&session.Engine.Snapshot.Prompts.Single(p=>p.Id==earlyId).EndedEarly,"Backtick ending before zero identifies a genuine early-ended reflection");
                        app.Open("reflection",earlyId,sessionCompleted:result.SessionCompleted);
                        await Until(()=>Windows(app).Any(w=>w.PromptId==earlyId&&w.Visible));
                        var earlyWindow=Windows(app).Single(w=>w.PromptId==earlyId);
                        Check((await Read(earlyWindow)).GetProperty("reasonVisible").GetBoolean(),"Native early-ended popup shows the smaller reason-for-ending-early box");
                        await Script(earlyWindow,"document.querySelector('#reflection-text').value='Early saved response';document.querySelector('#reflection-text').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#early-reason').value='Needed a break';document.querySelector('#early-reason').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#reflection-prev').click()");
                        await Until(()=>earlyWindow.IsDisposed&&Windows(app).Any(w=>w.PromptId==id&&w.Visible));
                        var previous=Windows(app).Single(w=>w.PromptId==id);
                        Check(Windows(app).Count(w=>w.View=="reflection"&&w.Visible)==1&&(await Read(previous)).GetProperty("draft").GetString()=="Typed just before zero","Prev displays only one native reflection and restores the older saved response");
                        Check(session.Engine.Snapshot.Prompts.Single(p=>p.Id==earlyId) is {Draft:"Early saved response",EarlyEndReason:"Needed a break"}&&session.Engine.Snapshot.Outbox.Count==0,"Native navigation flushes both newly typed fields without submitting them");
                        Check(!(await Read(previous)).GetProperty("reasonVisible").GetBoolean(),"Prev to a naturally completed session hides only that session's reason box");
                        await Script(previous,"document.querySelector('#reflection-next').click()");
                        await Until(()=>previous.IsDisposed&&Windows(app).Any(w=>w.PromptId==earlyId&&w.Visible));
                        var next=Windows(app).Single(w=>w.PromptId==earlyId);var restored=await Read(next);
                        Check(Windows(app).Count(w=>w.View=="reflection"&&w.Visible)==1&&restored.GetProperty("reasonVisible").GetBoolean()&&restored.GetProperty("reason").GetString()=="Needed a break"&&restored.GetProperty("draft").GetString()=="Early saved response","Next restores the early-ended response and reason already populated in a single visible window");
                        ((IReflectionShortcutTarget)next).FocusOrSaveDraft();await Until(()=>next.IsDisposed);
                        app.Open("reflection",session.ReflectionForShortcut());
                        var reload=Windows(app).Single(w=>w.PromptId==earlyId);
                        Check(!reload.Visible,"Comma reopening an early-ended draft does not expose an empty editor");
                        await Until(()=>reload.Visible);var reloaded=await Read(reload);
                        Check(reloaded.GetProperty("draft").GetString()=="Early saved response"&&reloaded.GetProperty("reasonVisible").GetBoolean()&&reloaded.GetProperty("reason").GetString()=="Needed a break","Reopened early-ended popup has both saved text fields at its first native display");
                        Check(session.Engine.Snapshot.Connection.WebAppUrl==""&&session.Engine.Snapshot.Timer.Volume==0,"Native smoke test stays muted and disconnected throughout");
                    } catch(Exception error){failure=error;}
                    finally {await app.CloseMainAsync();}
                });
                Application.Run(app);
            } catch(Exception error){failure=error;}
            finally {app?.Dispose();}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(70)))throw new TimeoutException("Native smoke did not finish; inspect its isolated process.");
        if(failure is not null)throw new Exception("Native reflection smoke failed",failure);
        Console.WriteLine($"{passed} native smoke checks passed.");
    }
    private static List<PreviewWindow> Windows(PreviewApplication app)=>(List<PreviewWindow>)typeof(PreviewApplication).GetField("windows",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)!;
    private static Task<string> Script(PreviewWindow window,string script)=>((WebView2)typeof(PreviewWindow).GetField("browser",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).CoreWebView2.ExecuteScriptAsync(script);
    private static async Task<JsonElement> Read(PreviewWindow window)=>JsonDocument.Parse(await Script(window,"JSON.parse(JSON.stringify({draft:document.querySelector('#reflection-text').value,reason:document.querySelector('#early-reason').value,reasonVisible:!document.querySelector('#reason-group').hidden,theme:document.documentElement.dataset.theme,focused:document.activeElement.id}))")).RootElement.Clone();
    private static async Task Until(Func<bool> predicate){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(!predicate())await Task.Delay(25,timeout.Token);}
    private static async Task UntilAsync(Func<Task<bool>> predicate){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(!await predicate())await Task.Delay(25,timeout.Token);}
}
