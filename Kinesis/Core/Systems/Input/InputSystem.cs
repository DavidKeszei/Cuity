using Kinesis.Utils;
using Kinesis.Native;
using Kinesis.UI;
using Kinesis.UI.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Kinesis.Core;

namespace Kinesis.Core;

/// <summary>
/// Represent a unified source of the inputs.
/// </summary>
internal sealed class InputSystem: IDynamicSystem {
    private const string DEDICATED_THREAD_NAME = "kinesis.tui::input_thread";

    /// <summary>
    /// Indicates the wait time between two sampling. (10ms)
    /// </summary>
    private const int POOLING_TIME = 10;

    /// <summary>
    /// Minimum time, when we think no input was happened and we fire that. (10ms)
    /// </summary>
    private const int DEAD_ZONE = 10;

    /// <summary>
    /// Minimum time, when a single-press repeated as long-press.
    /// </summary>
    private const int HOLD_ZONE = 250;

    public SystemBehavior Behavior { get => SystemBehavior.DYNAMIC; }

#if WIN_NT
    private readonly WindowsInputBackend m_backend = null!;
#endif

    private (char Key, InputModifier Modifier, TimeSpan When, bool isPress) m_startInputInfo = ('\0', InputModifier.NONE, TimeSpan.Zero, false);

    public InputSystem(PlatformConsoleInfo provider) {
#if WIN_NT
        m_backend = (WindowsInputBackend)WindowsInputBackend.Init(source: provider.Windows);
#endif
        Console.InputEncoding = Encoding.UTF8;
    }

    /// <summary>
    /// Listen inputs from standard input.
    /// </summary>
    public void Run() {
        if (m_backend == IInputBackend.ERR) {
            Console.Error.Write("[WARNING] No input device detected; no input received.\n");
            return;
        }

        Thread.CurrentThread.Name = DEDICATED_THREAD_NAME;
        float deadZoneTime = DEAD_ZONE;

        float holdTime = .0f;
        DateTime lastTime = DateTime.MinValue;

        while (true) {
            DateTime now = DateTime.UtcNow;

            if (m_backend.ReadInput(out InputInfo info) && info.IsPress) {

                /* 1. Check if the key same as before */
                if (m_startInputInfo.Key == info.Key && m_startInputInfo.Modifier == info.Modifiers) {
                    holdTime = (float)(now - lastTime).TotalMilliseconds;

                    if (holdTime >= HOLD_ZONE)
                        JobSystem.Current.AddInputMessage(message: new InputMessage(key: m_startInputInfo.Key, modifiers: m_startInputInfo.Modifier, isPress: m_startInputInfo.isPress));

                    Thread.Sleep(millisecondsTimeout: POOLING_TIME);
                    continue;
                }

                /* 1.1 If not: do fast swap between the new & current keys */
                if (m_startInputInfo.When != TimeSpan.Zero) {

                    JobSystem.Current.AddInputMessage(message: new InputMessage(key: m_startInputInfo.Key, modifiers: m_startInputInfo.Modifier, isPress: m_startInputInfo.isPress));
                    deadZoneTime = DEAD_ZONE;
                }

                lastTime = now;
                m_startInputInfo = (info.Key, info.Modifiers, now.TimeOfDay, info.IsPress);

                Thread.Sleep(millisecondsTimeout: POOLING_TIME);
                continue;
            }


            /* This decrease CPU usage */
            Thread.Sleep(millisecondsTimeout: POOLING_TIME);

            /* 2. Send it after the DEAD_ZONE. (Only, if the action is not HOLD)*/
            if (deadZoneTime <= .0f) {
                JobSystem.Current.AddInputMessage(message: new InputMessage(key: m_startInputInfo.Key, modifiers: m_startInputInfo.Modifier, isPress: m_startInputInfo.isPress));
                m_startInputInfo = ('\0', InputModifier.NONE, TimeSpan.Zero, false);

                deadZoneTime = DEAD_ZONE;
                holdTime = .0f;

                continue;
            }

            if (m_startInputInfo.When != TimeSpan.Zero) {
                float inputTime = (float)(DateTime.UtcNow - now).TotalMilliseconds;
                deadZoneTime -= inputTime;
            }
        }

    }
}
