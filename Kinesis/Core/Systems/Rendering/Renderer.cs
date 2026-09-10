using Kinesis.Core.Utils;
using Kinesis.UI;
using Kinesis.UI.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kinesis.Core.Rendering;

/// <summary>
/// Represents a IMGUI/immediate-mode based renderer.
/// </summary>
internal sealed class Renderer {
    #region PREDEFINES

    private const int STRING_BUILDER_STACK_SPACE = 16_384;
    private const float FRAME_TIME_LIMIT         = 16f;
    private const float FPS_CONVERT              = 1000f;
    private const float NS_TO_MS                 = 1000f;

    #endregion

    private ConsoleBuffer m_backbuffer  = default;
    private ConsoleBuffer m_frontbuffer = default;

    private readonly State<LayoutInfo> m_layoutState       = null!;
    private readonly State<WorkerSystemState> m_workState = null!;

    private readonly StreamWriter m_out = null!;
    private float m_delta = .0f;

    /// <summary>
    /// Current frame generation time in ms.
    /// </summary>
    public float Time { get => m_delta / FPS_CONVERT; }

    /// <summary>
    /// Current fps count of the renderer.
    /// </summary>
    public float FPS { get => FPS_CONVERT / m_delta; }

    public Renderer(State<WorkerSystemState> workState, State<LayoutInfo> layoutState) {
        m_workState = workState;
        m_layoutState = layoutState;

        m_backbuffer = new ConsoleBuffer(x: (int)layoutState.Value.Scale.X, (int)layoutState.Value.Scale.Y);
        m_frontbuffer = new ConsoleBuffer(x: (int)layoutState.Value.Scale.X, (int)layoutState.Value.Scale.Y);

        m_out = new StreamWriter(stream: Console.OpenStandardOutput(), encoding: Encoding.UTF8) {
            AutoFlush = false
        };

        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;
    }
        
    public void Run(DrawCalls calls) {
        long start = Stopwatch.GetTimestamp();

        if (m_workState.Value == WorkerSystemState.WAIT_FOR_RENDERER) {
            bool fullRedrawRequested = OnLayoutChange();

            foreach(Entity call in calls) {
                Vec2 scale    = call.Get<Scale>()!.Value;
                Vec2 position = call.Get<Position>()!.Absolute;

                if (!InBuffer(position: position, scale: scale)) continue;

                RenderComponent renderLogic = call.Get<RenderComponent>()!;
                Canvas canvas = ConsoleBuffer.Slice(buffer: ref m_backbuffer, from: position, scale: SetSafeArea(scale, position));

                using StyleEnumerator style = new StyleEnumerator(entity: call);
                renderLogic.Render(buffer: in canvas, version: call.Version, style);
            }

            Diffing(fullRedrawRequested);
            m_workState.Value = WorkerSystemState.OPEN_FOR_PROCESSING;
        }

        float delta = (Stopwatch.GetTimestamp() - start) / NS_TO_MS;

        if (delta <= FRAME_TIME_LIMIT)
            Thread.Sleep(millisecondsTimeout: (int)(FRAME_TIME_LIMIT - delta));

        m_delta = float.Lerp(m_delta, delta, amount: .1f);
    }

    public void Diffing(bool full) {
        Console.Out.Write(value: AnsiCommand.StartBufferLoad);
        ANSIStringBuilder builder = new ANSIStringBuilder(buffer: stackalloc char[STRING_BUILDER_STACK_SPACE]);

        for (int y = 0; y < m_backbuffer.Scale.Y; ++y) {
            for (int x = 0; x < m_backbuffer.Scale.X; ++x) {
                ref vtchar_t backChar = ref m_backbuffer[x, y];
                ref vtchar_t frontChar = ref m_frontbuffer[x, y];

                if (!frontChar.Equals(backChar) || (full && !backChar.Equals(new vtchar_t()))) {
                    builder.WritePosition(x, y)
                           .WriteFontStyles(backChar.Styles)
                                .WriteColor(color: backChar.Background.A == 0 ? null : backChar.Background, isBackground: true)
                                .WriteColor(color: backChar.Foreground.A == 0 ? null : backChar.Foreground, isBackground: false)
                           .WriteCharacter(backChar.Character);

                    frontChar = backChar;
                }

                if (builder.BarrierReached) {
                    builder.Build(destination: m_out);
                    m_out.Flush();
                };
            }

        }

        builder.Build(destination: m_out);

        m_out.Flush();
        m_backbuffer.Clear();

        Console.Out.Write(value: AnsiCommand.ResetStyles);
        Console.Out.Write(value: AnsiCommand.Home);

        Console.Out.Write(value: AnsiCommand.EndBufferLoad);
        Console.Out.Write(value: AnsiCommand.ClearSavedLines);
    }

    private bool OnLayoutChange() {
        if (!m_layoutState.Value.IsChanged)
            return false;

        Vec2 scale = m_layoutState.Value.Scale;

        m_backbuffer = ConsoleBuffer.Reallocate(buffer: ref m_backbuffer, scale);
        m_frontbuffer = ConsoleBuffer.Reallocate(buffer: ref m_frontbuffer, scale);

        m_layoutState.Value = m_layoutState.Value with { IsChanged = false };
        return true;
    }

    private bool InBuffer(Vec2 position, Vec2 scale) {
        if (scale.X <= 0 || scale.Y <= 0) return false;

        return (m_backbuffer.Scale.X > position.X && m_backbuffer.Scale.Y > position.Y) &&
               (position.X + scale.X >= 0 && position.Y + scale.Y >= 0);
    }

    /// <summary>
    /// Create a safe area from the entire scale based on the scale and position.
    /// </summary>
    /// <param name="scale">Scale of the <see cref="Entity"/>.</param>
    /// <param name="position">Position of the <see cref="Entity"/>.</param>
    /// <returns>Returns a safe area scale.</returns>
    private Vec2 SetSafeArea(Vec2 scale, Vec2 position) {
        if ((scale.X + position.X) >= m_backbuffer.Scale.X) 
            scale.X -= (scale.X + position.X) - (m_backbuffer.Scale.X - 1);

        if ((scale.Y + position.Y) >= m_backbuffer.Scale.Y) 
            scale.Y -= (scale.Y + position.Y) - (m_backbuffer.Scale.Y - 1);

        return scale;
    }
}