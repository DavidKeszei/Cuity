using Kinesis.Core;
using Kinesis.Native;
using Kinesis.Core.Rendering;
using Kinesis.UI;
using Kinesis.UI.Components;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Kinesis.Core.Utils;

namespace Kinesis;

/// <summary>
/// Represent the heart of the library: this connects all systems to one class.
/// </summary>
public sealed class KinesisEngine: ISystemProvider {
    private readonly Renderer m_renderer = null!;
    private readonly InputSystem m_input = null!;

    private readonly JobSystem m_worker = null!;
    private readonly NavigationSystem m_navigator = null!;

    private readonly LayoutSystem m_layoutSystem = null!;
    private readonly List<SystemInvocationInfo> m_customSystems = null!;

    private readonly State<LayoutInfo> m_layoutInfo = null!;
    private readonly State<WorkerSystemState> m_workSyncState = null!;

    private readonly PlatformConsoleInfo m_consoleSourceInfoProvider = default!;
    private readonly string m_title = string.Empty;

    /// <summary>
    /// Create a new <see cref="KinesisEngine"/> instance.
    /// </summary>
    /// <param name="title">Title of the window.</param>
    /// <exception cref="PlatformNotSupportedException"/>
    public KinesisEngine(string? title = null!, int x = -1, int y = -1) {
        Console.Out.Write(value: AnsiCommand.EnableAlternateBuffering);
        Console.Out.Write(value: AnsiCommand.WrapDisable);

        m_title = m_title == null ? $"\e]0;Untitled\a" : $"\e]0;{title}\a";
        m_consoleSourceInfoProvider = new PlatformConsoleInfo();

        m_layoutInfo = new RefState<LayoutInfo>();
        m_workSyncState = new ValueState<WorkerSystemState>(@default: WorkerSystemState.WAIT_FOR_RENDERER);

        m_input = new InputSystem(provider: m_consoleSourceInfoProvider);
        m_layoutSystem = new LayoutSystem(provider: m_consoleSourceInfoProvider, state: m_layoutInfo, scale: new Vec2(x == -1 ? Console.BufferWidth : x, y == -1 ? Console.BufferHeight : y));

        m_worker = JobSystem.Current;
        m_navigator = new NavigationSystem(provider: this);

        m_renderer = new Renderer(workState: m_workSyncState, layoutState: m_layoutInfo);
        m_customSystems = new List<SystemInvocationInfo>();

        m_customSystems.Add(new SystemInvocationInfo(null!, m_navigator, SystemInvocationTime.ON_CALL));
        RegisterBuiltInComponents();
    }
    
    public T? GetSystem<T>() where T: class, ISystem {
        for (int i = 0; i < m_customSystems.Count; ++i) {
            if (m_customSystems[i].When == SystemInvocationTime.ON_CALL && m_customSystems[i].System is T) {

                if (m_customSystems[i].System == null!)
                    m_customSystems[i] = m_customSystems[i] with { System = m_customSystems[i].Creation(this) };

                return m_customSystems[i].System as T;
            }
        }

        return default!;
    }

    public bool RegisterComponent<T>() where T: Component, IStaticType
        => ComponentRegistry.RegisterComponent<T>(name: T.TypeName);

    /// <summary>
    /// Add a system to the engine.
    /// </summary>
    /// <param name="action">The system itself.</param>
    /// <param name="when">Time of the invocation of the system.</param>
    public void RegisterSystem(SystemInvocationTime when, Func<ISystemProvider, ISystem> action)
        => m_customSystems.Add(item: new SystemInvocationInfo(action, null, when));

    /// <summary>
    /// Register a named route with a <paramref name="onCreate"/> method.
    /// </summary>
    /// <param name="name">Name of the route.</param>
    /// <param name="onCreate">Creation method of the <see cref="Island"/>.</param>
    /// <returns>Returns <see langword="true"/>, if the route is successfully registered, otherwise returns <see langword="false"/>.</returns>
    public bool RegisterIsland<T>(string name, Func<ISystemProvider, T> onCreate) where T: Island
        => m_navigator.Register(name, onCreate);

    /// <summary>
    /// Start the <see cref="KinesisEngine"/> instance with the systems.
    /// </summary>
    public async Task Start(CancellationToken token = default) {
        bool firstRun = true;

        /* Run the starter systems. */
        await Run(invocation: SystemInvocationTime.ON_BEGIN);
        JobSystem.Current.AddRenderSync(sync: m_workSyncState);

        /* Start main parts of the engine on different threads. (Input, Workers) */
        _ = Task.Run(action: () => m_worker.Run(), token);

        //TODO: Try to remove unneccessary VSD overhead
        _ = Task.Run(action: () => m_input.Run(), token);

        _ = Task.Run(action: () => m_layoutSystem.Run(), token);
        while(!token.IsCancellationRequested) {

            /* Render the frame to the screen/terminal window. */
            m_renderer.Run(calls: m_navigator.Current.Get<DrawCalls>()!);

            if (!firstRun) {
                Vec2 safeArea = m_layoutInfo.Value.Scale - 1; // This helps the outer entities for calculate transforms in the good dimension
                m_worker.AddRenderMessage(message: new RenderMessage(m_renderer.Time, (int)m_renderer.FPS, safeArea));
            }
            else {
                Console.Out.Write(m_title);
                firstRun = false;
            }
        }

        /* Run the shutdown systems. */
        await Run(invocation: SystemInvocationTime.ON_END);
        Console.Out.Write(AnsiCommand.DisableAlternateBuffering);
    }

    private Task Run(SystemInvocationTime invocation) {
        foreach (SystemInvocationInfo systemInfo in m_customSystems.Where(x => x.When == invocation)) {
            ISystem system = systemInfo.System ?? systemInfo.Creation(this);

            if (system.Behavior == SystemBehavior.DYNAMIC && systemInfo.System is IDynamicSystem dynamic)
                dynamic.Run();
        }

        return Task.CompletedTask;
    }

    private void RegisterBuiltInComponents() {
        this.RegisterComponent<RenderComponent>();
        this.RegisterComponent<DrawCalls>();

        this.RegisterComponent<Position>();
        this.RegisterComponent<Scale>();

        this.RegisterComponent<Hierarchy>();
        this.RegisterComponent<Style>();

        this.RegisterComponent<JobComponent>();
        this.RegisterComponent<RebuildContent>();
    }
}