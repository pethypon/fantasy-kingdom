using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>Main-thread cooperative work. Nested iterators are disposed inside-out on cancellation.</summary>
public sealed class AISlicedWork : IDisposable
{
    readonly Stack<IEnumerator> pending = new Stack<IEnumerator>(32);
    readonly Stopwatch activeTime;
    readonly Stopwatch slice = new Stopwatch();
    Action onFinished;
    public double LastSliceMs { get; private set; }
    public double MaxSliceMs { get; private set; }
    public int SliceCount { get; private set; }

    public AISlicedWork(IEnumerator root, Stopwatch activeTime, Action onFinished = null)
    {
        pending.Push(root ?? throw new ArgumentNullException(nameof(root)));
        this.activeTime = activeTime;
        this.onFinished = onFinished;
    }

    // A checkpoint cannot preempt an individual operation (e.g. a board clone).
    // Callers must place checkpoints inside long loops, not only between root moves.
    public bool Step(double milliseconds = 3)
    {
        if (pending.Count == 0) return false;
        if (double.IsNaN(milliseconds) || milliseconds <= 0) milliseconds = 3;
        activeTime?.Start();
        slice.Restart();
        try
        {
            do
            {
                var current = pending.Peek();
                if (!current.MoveNext())
                {
                    pending.Pop();
                    (current as IDisposable)?.Dispose();
                }
                else if (current.Current is IEnumerator child) pending.Push(child);
            }
            while (pending.Count > 0 && slice.Elapsed.TotalMilliseconds < milliseconds);
            if (pending.Count == 0) Finish();
            return pending.Count > 0;
        }
        catch { Dispose(); throw; }
        finally
        {
            slice.Stop();
            activeTime?.Stop();
            LastSliceMs = slice.Elapsed.TotalMilliseconds;
            MaxSliceMs = Math.Max(MaxSliceMs, LastSliceMs);
            SliceCount++;
        }
    }

    public void Dispose()
    {
        Exception failure = null;
        try
        {
            while (pending.Count > 0)
            {
                try { (pending.Pop() as IDisposable)?.Dispose(); }
                catch (Exception e) { if (failure == null) failure = e; }
            }
        }
        finally { activeTime?.Stop(); Finish(); }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    void Finish() { var callback = onFinished; onFinished = null; callback?.Invoke(); }
}
