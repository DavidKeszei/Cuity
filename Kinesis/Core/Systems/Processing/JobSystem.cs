using Kinesis.Core.Rendering;
using Kinesis.Utils;
using Kinesis.UI;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

using RenderSyncContext = Kinesis.Core.State<Kinesis.Core.WorkerSystemState>;

namespace Kinesis.Core;

/// <summary>
/// Represents the smallest unit of work.
/// </summary>
/// <param name="Action">Callback of the work.</param>
/// <param name="Island">Container of the changed entities.</param>
/// <param name="Tag">Indicates the type of the target.</param>
/// <param name="Status">Connection middle-object of the target.</param>
/// <param name="IsFocusBased">Indicates the job is global or local based.</param>
internal record JobTarget(Delegate Action, Island Island, JobTag Tag, State<JobRequestIntent> Status, bool IsFocusBased);

/// <summary>
/// Represent a bunch of workers for different tasks.
/// </summary>
internal sealed class JobSystem: IDynamicSystem {
    #region PREDEFINES
    private const string DEDICATED_THREAD_NAME = "kinesis.tui::job_thread";
    private const string ERR_SYNC_NOT_FOUND = "The synchronization context/state wasn't found.";

    private const int MAX_MSG_RND_COUNT   = 1;
    private const int MAX_MSG_INPUT_COUNT = 32;

    private const int PRE_ALLOC_INTERACTION_COUNT = 2048;
    private const int POOLING_TIME = 8;
    #endregion

    private static JobSystem s_instance = null!;
    private readonly List<JobTarget> m_targets = null!;

    private readonly RingBuffer<InputMessage> m_inputMessages = null!;
    private readonly RingBuffer<RenderMessage> m_renderMessages = null!;

    private readonly ConcurrentQueue<JobTarget> m_addIntents = null!;
    private RenderSyncContext m_renderSync = null!;

    private readonly List<int> m_focusTargetIndexes = null!;
    private int m_focusIndex = 0;

    /// <summary>
    /// Indicates behavior of the <see cref="JobSystem"/>.
    /// </summary>
    public SystemBehavior Behavior { get => SystemBehavior.DYNAMIC; }

    /// <summary>
    /// Current instance of the <see cref="JobSystem"/>.
    /// </summary>
    public static JobSystem Current { get => s_instance ??= new JobSystem(); }

    private JobSystem() {
        m_targets = new List<JobTarget>(capacity: PRE_ALLOC_INTERACTION_COUNT);
        m_focusTargetIndexes = new List<int>();

        m_renderMessages = new RingBuffer<RenderMessage>(capacity: MAX_MSG_RND_COUNT);
        m_inputMessages = new RingBuffer<InputMessage>(capacity: MAX_MSG_INPUT_COUNT);

        m_addIntents = new ConcurrentQueue<JobTarget>();
    }

    /// <summary>
    /// Add synchronization context/state to the <see cref="JobSystem"/> from the <see cref="Renderer"/>.
    /// </summary>
    /// <param name="sync">Synchronization state of the <see cref="KinesisEngine"/>.</param>
    /// <remarks>Remarks: If the state wasn't set, then the <see cref="JobSystem.Run"/> throws <see cref="InvalidOperationException"/> in the first run.</remarks>
    public void AddRenderSync(RenderSyncContext sync) => m_renderSync ??= sync;

    /// <summary>
    /// Add new <see cref="InputMessage"/> to the workers.
    /// </summary>
    /// <param name="message">The message itself.</param>
    public void AddInputMessage(InputMessage message) {
        if (MoveFocusIndex(message)) return;

        if(m_inputMessages.Count < m_inputMessages.Capacity)
            m_inputMessages.Write(message);
    }

    /// <summary>
    /// Add new <see cref="RenderMessage"/> to the workers.
    /// </summary>
    /// <param name="message">The message itself.</param>
    public void AddRenderMessage(RenderMessage message) 
        => m_renderMessages.Write(message);

    /// <summary>
    /// Add <paramref name="work"/> to the queue.
    /// </summary>
    /// <param name="work">Current work item.</param>
    /// <param name="island">Root <see cref="Island"/> instance of the work.</param>
    /// <param name="isFocusBased">Indicates the job requires some focus-based behavior.</param>
    /// <returns>Returns a <see cref="State{T}"/> instance, which helps request and track state of the job.</returns>
    public State<JobRequestIntent> AddCallback<T>(Action<T> work, Island island, bool isFocusBased) where T: IJobMessage {
        if (work == null || island == null) return null!;

        JobTarget target = new JobTarget(work, island, T.Target, new ValueState<JobRequestIntent>(@default: JobRequestIntent.ACTIVE), isFocusBased);
        m_addIntents.Enqueue(target);

        return target.Status;
    }

    public void Run() {
        if (m_renderSync == null)
            throw new InvalidOperationException(message: ERR_SYNC_NOT_FOUND);

        Thread.CurrentThread.Name = DEDICATED_THREAD_NAME;
        
        while(true) {
            if (m_renderSync.Value != WorkerSystemState.OPEN_FOR_PROCESSING) {
                Thread.Sleep(millisecondsTimeout: POOLING_TIME);
                continue;
            }

            RemoveJobs();
            AddJobs();

            Send<InputMessage>(messages: m_inputMessages);
            Send<RenderMessage>(messages: m_renderMessages);

            m_renderSync.Value = WorkerSystemState.WAIT_FOR_RENDERER;
        }
    }

    private void Send<T>(RingBuffer<T> messages) where T: struct, IJobMessage {
        if (!messages.Read(out T message)) return;

        for(int i = 0; i < m_targets.Count; ++i) {

            if (!m_targets[i].Island.IsActive ||
                 m_targets[i].Status != JobRequestIntent.ACTIVE ||
                (m_targets[i].IsFocusBased && m_focusTargetIndexes[m_focusIndex] != i))
                continue;

            Delegate _ref = m_targets[i].Action;

            if (m_targets[i].Tag == T.Target)
                Unsafe.As<Delegate, Action<T>>(ref _ref)(message);
        }
    }
    
    private void RemoveJobs() {
        for (int i = m_targets.Count - 1; i >= 0; --i)
            if (m_targets[i].Status == JobRequestIntent.REMOVE) {
                JobTarget target = m_targets[i];

                if (m_targets[^1].IsFocusBased) {
                    if (IsFocused(target, i) && m_focusIndex - 1 >= 0)
                        --m_focusIndex;

                    m_focusTargetIndexes.RemoveAt(m_focusTargetIndexes.Count - 1);
                }

                (m_targets[i], m_targets[^1]) = (m_targets[^1], m_targets[i]);
                m_targets.RemoveAt(m_targets.Count - 1);
            }
    }

    private void AddJobs() {
        if (m_addIntents.IsEmpty) return;

        while (m_addIntents.TryDequeue(out JobTarget? target) && target != null) {
            if (target.IsFocusBased)
                m_focusTargetIndexes.Add(m_targets.Count);

            m_targets.Add(item: target);
        }
    }

    private bool MoveFocusIndex(InputMessage input) {
        if (input.IsPressed && input.Key == '\t' && input.Modifiers == InputModifier.L_SHIFT) {

            m_focusIndex = ++m_focusIndex % m_focusTargetIndexes.Count;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFocused(JobTarget target, int index) 
        => target.IsFocusBased && m_focusTargetIndexes[m_focusIndex] == index;
}

/// <summary>
/// Simple state representation between the <see cref="Renderer"/> and <see cref="JobSystem"/>.
/// </summary>
public enum WorkerSystemState: byte {
    /// <summary>
    /// Indicates the <see cref="JobSystem"/> can process one message from the queue.
    /// </summary>
    OPEN_FOR_PROCESSING,
    /// <summary>
    /// Indicates for the <see cref="JobSystem"/> wait to the <see cref="Renderer"/>.
    /// </summary>
    WAIT_FOR_RENDERER
}