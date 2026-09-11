namespace ReflectionTimer.Core;

public enum ReflectionSeparator { None = 0, Comma = 1, Bullet = 2, Newline = 3 }

public static class ReflectionDrafts
{
    // Keep a continuation separate from the response until the user writes after
    // it. Reopening/recovery cannot accumulate separators or send an empty bullet.
    public static string ForEditing(ReflectionPrompt prompt)
    {
        var separator = prompt.ContinuationSeparator switch {
            ReflectionSeparator.Comma => ", ", ReflectionSeparator.Bullet => "\n- ",
            ReflectionSeparator.Newline => "\n", _ => ""
        };
        return separator.Length > 0 && !string.IsNullOrWhiteSpace(prompt.Draft)
            && !prompt.Draft.EndsWith(separator, StringComparison.Ordinal)
            && prompt.Draft.Length + separator.Length <= 5000 ? prompt.Draft + separator : prompt.Draft;
    }

    public static string Content(ReflectionPrompt prompt, string text) =>
        text == ForEditing(prompt) ? prompt.Draft : text;
}
