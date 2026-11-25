namespace DiskCleaner.Core.Scanning;

/// <summary>
/// Runs folder-size jobs on at most two threads. The most recently opened level goes first:
/// a job from an older level stops between directories as soon as a newer level has work waiting,
/// and resumes from where it stopped once that work is done.
/// </summary>
internal sealed class SizeCalculator
{
    public const int MaxConcurrency = 2;

    private readonly object _lock = new();
    private readonly List<Level> _priority = [];
    private readonly Dictionary<Level, LinkedList<FolderJob>> _queues = [];
    private readonly Func<FolderJob, bool> _shouldYield;
    private int _workers;
    private bool _paused;

    public SizeCalculator()
    {
        _shouldYield = ShouldYield;
    }

    public void Prioritize(Level level)
    {
        lock (_lock)
        {
            _priority.Remove(level);
            _priority.Insert(0, level);
        }
    }

    public void Enqueue(Level level, IEnumerable<FolderJob> jobs)
    {
        lock (_lock)
        {
            if (!_priority.Contains(level))
            {
                _priority.Add(level);
            }

            if (!_queues.TryGetValue(level, out var queue))
            {
                queue = new LinkedList<FolderJob>();
                _queues[level] = queue;
            }

            foreach (var job in jobs)
            {
                queue.AddLast(job);
            }

            StartWorkers();
        }
    }

    public void Cancel(FolderJob job)
    {
        job.Cancel();
        lock (_lock)
        {
            if (_queues.TryGetValue(job.Level, out var queue))
            {
                queue.Remove(job);
            }
        }
    }

    /// <summary>Drops a level that is complete or has been thrown away.</summary>
    public void Forget(Level level)
    {
        lock (_lock)
        {
            _priority.Remove(level);
            if (_queues.Remove(level, out var queue))
            {
                foreach (var job in queue)
                {
                    job.Cancel();
                }
            }
        }
    }

    /// <summary>Running jobs stop at the next directory boundary and keep their progress.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            _paused = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _paused = false;
            StartWorkers();
        }
    }

    private void StartWorkers()
    {
        if (_paused)
        {
            return;
        }

        int queued = 0;
        foreach (var queue in _queues.Values)
        {
            queued += queue.Count;
        }

        while (_workers < MaxConcurrency && queued > 0)
        {
            _workers++;
            queued--;
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Folder size",
                Priority = ThreadPriority.BelowNormal,
            };
            thread.Start();
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            FolderJob? job;
            lock (_lock)
            {
                job = _paused ? null : TakeNext();
                if (job is null)
                {
                    _workers--;
                    return;
                }
            }

            bool finished;
            try
            {
                finished = job.Run(_shouldYield);
            }
            catch (Exception ex)
            {
                job.Level.FailEntry(job.Index, ErrorDescriptions.Describe(0), ex.Message);
                finished = true;
            }

            if (!finished)
            {
                lock (_lock)
                {
                    if (_queues.TryGetValue(job.Level, out var queue))
                    {
                        queue.AddFirst(job);
                    }
                }
            }
        }
    }

    private FolderJob? TakeNext()
    {
        foreach (var level in _priority)
        {
            if (!_queues.TryGetValue(level, out var queue))
            {
                continue;
            }

            while (queue.First is { } node)
            {
                queue.RemoveFirst();
                if (!node.Value.IsCancelled)
                {
                    return node.Value;
                }
            }
        }

        return null;
    }

    private bool ShouldYield(FolderJob job)
    {
        lock (_lock)
        {
            if (_paused)
            {
                return true;
            }

            foreach (var level in _priority)
            {
                if (level == job.Level)
                {
                    return false;
                }

                if (_queues.TryGetValue(level, out var queue) && queue.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
