using System.Runtime.InteropServices;

namespace Kinesis.Native;

/// <summary>
/// Represent a simple union of platform specific console info sources.
/// </summary>
[StructLayout(layoutKind: LayoutKind.Explicit)]
internal readonly struct PlatformConsoleInfo {
    private const string ERR_NOT_SUPPORTED_PLATFORM = "The current OS platform not supported.";

    [FieldOffset(offset: 0)] private readonly WindowsConsoleInfoProvider m_windowsSource = null!;

    /// <summary>
    /// Windows platform specific console info provider.
    /// </summary>
    public WindowsConsoleInfoProvider Windows { get => m_windowsSource; }

    /// <summary>
    /// Create a new <see cref="PlatformConsoleInfo"/> instance, which hides the platform specific information from other systems.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException"/>
    public PlatformConsoleInfo() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) m_windowsSource = new WindowsConsoleInfoProvider();
        else throw new PlatformNotSupportedException(message: ERR_NOT_SUPPORTED_PLATFORM);
    }
}
