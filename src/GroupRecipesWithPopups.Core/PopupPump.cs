using System;
using System.Collections.Generic;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>
    /// Time based dosage of a plan: one step becomes available every <c>intervalSeconds</c> of accumulated
    /// <see cref="Tick"/> time. Silent steps at the head of the queue can be drained without waiting.
    /// </summary>
    public sealed class PopupPump
    {
        private const double Epsilon = 1e-6;

        private readonly Queue<PopupStep> _queue = new Queue<PopupStep>();
        private readonly double _interval;
        private double _elapsed;

        public PopupPump(PopupPlan plan, double intervalSeconds)
            : this(intervalSeconds)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Enqueue(plan);
        }

        public PopupPump(double intervalSeconds)
        {
            _interval = Math.Max(0.0, intervalSeconds);
        }

        /// <summary>Number of steps that were not handed out yet.</summary>
        public int Remaining => _queue.Count;

        public bool IsEmpty => _queue.Count == 0;

        /// <summary>Appends the steps of another plan behind the queued ones.</summary>
        public void Enqueue(PopupPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            foreach (PopupStep step in plan.Steps)
            {
                _queue.Enqueue(step);
            }
        }

        public void Clear()
        {
            _queue.Clear();
            _elapsed = 0.0;
        }

        /// <summary>Advances the pacing clock; time only accumulates while steps are waiting.</summary>
        public void Tick(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0 || _queue.Count == 0)
            {
                return;
            }

            _elapsed += deltaSeconds;
        }

        /// <summary>Hands out the next step when the interval has elapsed.</summary>
        public bool TryDequeue(out KnowledgeEntry entry, out bool showPopup)
        {
            entry = default;
            showPopup = false;
            if (_queue.Count == 0 || _elapsed + Epsilon < _interval)
            {
                return false;
            }

            PopupStep step = _queue.Dequeue();
            entry = step.Entry;
            showPopup = step.ShowPopup;
            // Keep at most one interval of carry-over so that a long frame does not cause a burst afterwards.
            _elapsed = Math.Min(Math.Max(0.0, _elapsed - _interval), _interval);
            if (_queue.Count == 0)
            {
                _elapsed = 0.0;
            }

            return true;
        }

        /// <summary>Hands out the next step immediately, but only if it is a silent one (no popup, no pacing).</summary>
        public bool TryDequeueSilent(out KnowledgeEntry entry)
        {
            entry = default;
            if (_queue.Count == 0 || _queue.Peek().ShowPopup)
            {
                return false;
            }

            entry = _queue.Dequeue().Entry;
            if (_queue.Count == 0)
            {
                _elapsed = 0.0;
            }

            return true;
        }
    }
}
