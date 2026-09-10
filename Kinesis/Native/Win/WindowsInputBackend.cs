using Kinesis.Core;
using Kinesis.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kinesis.Native;

/// <summary>
/// Represent a standard input on the Windows platform. 
/// </summary>
internal sealed partial class WindowsInputBackend: IInputBackend {
    #region DEFINES

    private const string KERNEL32_LIB = "kernel32.dll";
    private const string USER32_LIB   = "user32.dll";

    private const uint ENABLE_ECHO_INPUT      = 0x0004;
    private const uint ENABLE_PROCESSED_INPUT = 0x0004;

    private const string DEDICATED_THREAD_NAME = "kinesis.tui::input_thread::windows";

    #endregion
    #region P/INVOKE

    [LibraryImport(libraryName: KERNEL32_LIB, EntryPoint = "SetConsoleMode")]
    [return: MarshalAs(unmanagedType: UnmanagedType.Bool)]
    private static partial bool SetMode(nint handle, uint flags);

    [LibraryImport(libraryName: KERNEL32_LIB, EntryPoint = "GetConsoleMode")]
    [return: MarshalAs(unmanagedType: UnmanagedType.Bool)]
    private static partial bool GetMode(nint handle, out uint flags);

    [LibraryImport(libraryName: USER32_LIB, EntryPoint = "GetAsyncKeyState")]
    private static partial short GetKeyState(int modifier);

    #endregion

    private readonly RingBuffer<InputInfo> m_infoBuffer = null!;
    private readonly ConsoleReader<WindowsConsoleInfoProvider, InputKeyEventInfo> m_source = default!;

    private InputInfo m_info = default;
    private bool m_isPressedLastTime = false;

    private WindowsInputBackend(WindowsConsoleInfoProvider source) {
        m_infoBuffer = new RingBuffer<InputInfo>(capacity: 64);
        m_source = new ConsoleReader<WindowsConsoleInfoProvider, InputKeyEventInfo>(source);
    }

    /// <summary>
    /// Create new <see cref="WindowsInputBackend"/> instance.
    /// </summary>
    /// <returns>Return a fresh <see cref="WindowsInputBackend"/> instance. If something goes wrong, then return <see cref="IInputBackend.ERR"/>.</returns>
    public static IInputBackend Init(WindowsConsoleInfoProvider source) {
        WindowsInputBackend backend = new WindowsInputBackend(source);

        if (!GetMode(handle: StdHandle.Input, out uint flags))
            return IInputBackend.ERR;

        flags &= ~ENABLE_ECHO_INPUT;
        flags &= ~ENABLE_PROCESSED_INPUT;

        if(!SetMode(handle: StdHandle.Input, flags))
            return IInputBackend.ERR;

        return backend;
    }

    public bool ReadInput(out InputInfo input) {
        input = default;
        Span<int> modifiersCodes = stackalloc int[5] {
            0xA0,
            0xA1,
            0xA2,
            0xA3,
            0x12,
        };

        InputModifier modifiers = QueryModifiers(modifiersCodes);
        InputKeyEventInfo info = default!;
        
        bool success = m_source.Read(out info);

        if (success) {
            input = new InputInfo(modifiers, info.Value, info.IsPressed);

            m_isPressedLastTime = info.IsPressed;
            m_info = input;
            return true;
        }

        if (m_isPressedLastTime) {
            input = m_info;
            return true;
        }

        m_info = default;
        return false;
    }

    private InputModifier QueryModifiers(ReadOnlySpan<int> modifiersCodes) {
        InputModifier modifiers = InputModifier.NONE;

        for (byte i = 0; i < modifiersCodes.Length; ++i) {
            if (GetKeyState(modifiersCodes[i]) < 0)
                modifiers |= (InputModifier)modifiersCodes[i];
        }

        return modifiers;
    }
}