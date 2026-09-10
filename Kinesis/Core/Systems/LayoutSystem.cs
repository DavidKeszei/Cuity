using Kinesis.Core;
using Kinesis.Core.Utils;
using Kinesis.Native;
using Kinesis.UI.Components;
using Kinesis.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Kinesis.Core;

internal record LayoutInfo(Vec2 Scale, bool IsChanged);

/// <summary>
/// This class observes changes in the current console windows dimension.
/// </summary>
internal partial class LayoutSystem: IDynamicSystem {
    #region PREDEFINES
    private const int POOLING_TIME = 1;
    #endregion

    private readonly State<LayoutInfo> m_info = null!;

#if WIN_NT
    private readonly ConsoleReader<WindowsConsoleInfoProvider, ConsoleScaleInfo> m_source = default!;
#elif LINUX
    private readonly ConsoleReader<LinuxConsoleInfoProvider, ConsoleScaleInfo>   m_source = default!;
#endif

    /// <summary>
    /// Behavior of the <see cref="LayoutSystem"/>.
    /// </summary>
    public SystemBehavior Behavior { get => SystemBehavior.DYNAMIC; }

    /// <summary>
    /// Create a new <see cref="LayoutSystem"/> with <paramref name="scale"/>.
    /// </summary>
    /// <param name="scale">Start scale of the application. This is going be the pivot point of the observing.</param>
    public LayoutSystem(PlatformConsoleInfo provider, State<LayoutInfo> state, Vec2 scale) {
        m_info = state;

#if WIN_NT
        m_source = new ConsoleReader<WindowsConsoleInfoProvider, ConsoleScaleInfo>(provider.Windows);
#elif LINUX
        m_source = new ConsoleReader<LinuxConsoleInfoProvider, ConsoleScaleInfo>(provider.Linux);
#endif

        m_info.Value = new LayoutInfo(scale, IsChanged: true);
    }

    /// <summary>
    /// Start watching of changes of the console window.
    /// </summary>
    public void Run() {
        bool isFirst = true;
        ConsoleScaleInfo info = default;

        while (true) {
            if (m_source.Read(out ConsoleScaleInfo current) && (m_info.Value.Scale.X != info.X || m_info.Value.Scale.Y != info.Y)) {
                info = current;
                continue;
            }

            Thread.Sleep(millisecondsTimeout: POOLING_TIME);

            if (!info.Equals(default)) {

                if (!isFirst)
                    m_info.Value = new LayoutInfo(new Vec2(x: info.X, y: info.Y), IsChanged: true);

                isFirst = false;
                info = default;
            }
        }
    }
}