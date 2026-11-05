using System.Collections.Generic;
using System.Windows.Forms;

namespace Drex;

/// <summary>
/// Simple animation helper for smooth UI transitions.
/// </summary>
public static class Anim
{
    sealed class AnimHandle
    {
        public readonly Control Control;
        public readonly int Duration;
        public readonly float From;
        public readonly float To;
        public readonly System.Action<float> Apply;
        public readonly System.Action? OnComplete;
        public readonly long Start = System.Environment.TickCount64;

        public AnimHandle(Control c, int dur, float from, float to, System.Action<float> apply, System.Action? onComplete)
        {
            Control = c; Duration = dur; From = from; To = to; Apply = apply; OnComplete = onComplete;
        }
    }

    static readonly List<AnimHandle> _running = new();
    static readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 }; // ~60fps, single shared instance
    static bool _timerStarted = false;

    /// <summary>
    /// Animates a float property on a control over a duration (ms).
    /// </summary>
    public static void Float(Control c, float from, float to, int durationMs, System.Action<float> apply, System.Action? onComplete = null)
    {
        if (c == null || c.IsDisposed) return;
        if (durationMs <= 0) { apply(to); onComplete?.Invoke(); return; }
        _running.Add(new AnimHandle(c, durationMs, from, to, apply, onComplete));
        if (!_timerStarted)
        {
            _timer.Tick += Tick;
            _timer.Start();
            _timerStarted = true;
        }
    }

    static void Tick(object? s, System.EventArgs e)
    {
        if (_running.Count == 0)
        {
            // Keep timer running for future animations (avoid recreate overhead)
            return;
        }

        var now = System.Environment.TickCount64;
        var done = new List<AnimHandle>();

        foreach (var a in _running)
        {
            if (a.Control.IsDisposed) { done.Add(a); continue; }
            var elapsed = (float)(now - a.Start);
            float t = Math.Clamp(elapsed / a.Duration, 0f, 1f);
            // Ease-out cubic for natural feel
            t = 1f - (1f - t) * (1f - t) * (1f - t);
            float val = a.From + (a.To - a.From) * t;
            a.Apply(val);
            if (t >= 1f) done.Add(a);
        }

        foreach (var d in done)
        {
            _running.Remove(d);
            d.OnComplete?.Invoke();
        }
    }
}