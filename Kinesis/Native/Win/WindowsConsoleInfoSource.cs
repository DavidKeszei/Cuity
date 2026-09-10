using Kinesis.Core;
using Kinesis.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Kinesis.Native;	

/// <summary>
/// Represents a Windows specific console information source.
/// </summary>
internal sealed partial class WindowsConsoleInfoProvider: IConsoleSource<ConsoleScaleInfo>, IConsoleSource<InputKeyEventInfo> {
    #region DEFINES

    private const int QUEUE_COUNT = 16;
    private const int WAIT = 5;

    private const uint READ_COUNT = 1;

    #endregion
    #region NATIVE

    [LibraryImport(libraryName: "kernel32.dll", EntryPoint = "ReadConsoleInputW")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(unmanagedType: UnmanagedType.Bool)]
    private static partial bool Read(nint handle, ref WindowsConsoleEventMsg message, uint count, out uint _);

    #endregion

    private readonly Queue<InputKeyEventInfo> m_inputs = null!;
    private readonly Queue<ConsoleScaleInfo> m_layouts = null!;

    private bool m_isLocked = false;

    public WindowsConsoleInfoProvider() {
        m_inputs  = new Queue<InputKeyEventInfo>(capacity: QUEUE_COUNT);
        m_layouts = new Queue<ConsoleScaleInfo>(capacity: QUEUE_COUNT);

        _ = Task.Run(async() => await Watch());
    }

    public bool Read(out ConsoleScaleInfo result) {
        result = default;

        if (Interlocked.CompareExchange<bool>(ref m_isLocked, true, false) != false) {
            return false;
        }

        bool success = m_layouts.TryDequeue(out result);
        Interlocked.Exchange<bool>(ref m_isLocked, false);

        return success;
    }

    public bool Read(out InputKeyEventInfo result) {
        result = default;

        if (Interlocked.CompareExchange<bool>(ref m_isLocked, true, false) != false) {
            return false;
        }

        bool success = m_inputs.TryDequeue(out result);
        Interlocked.Exchange<bool>(ref m_isLocked, false);

        return success;
    }

    private async Task Watch() {
        WindowsConsoleEventMsg message = default!;

        while (true) {
            if (Read(handle: StdHandle.Input, ref message, count: READ_COUNT, out _)) {
                while (Interlocked.CompareExchange<bool>(ref m_isLocked, true, false) != false)
                    await Task.Delay(millisecondsDelay: WAIT);

                switch(message.Tag) {
                    case WindowsConsoleMsgTag.INPUT:
                        m_inputs.Enqueue(message.KeyInfo);
                        break;

                    case WindowsConsoleMsgTag.LAYOUT:
                        m_layouts.Enqueue(message.ConsoleWindowScale);
                        break;

                    default:
                        break;
                }

                Interlocked.Exchange<bool>(ref m_isLocked, false);
                continue;
            }
        }
    }
}
