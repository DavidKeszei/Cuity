using Kinesis.Core;
using Kinesis.Core.Rendering;
using Kinesis.UI.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Kinesis.UI;

/// <summary>
/// Represents a simple state-machine for rendering different states with different UI elements state.
/// </summary>
/// <typeparam name="TEntity">The renderable entity as <typeparam name="TEntity"/></typeparam>
/// <typeparam name="TData">The state information of current <see cref="Switch{TEntity,TData}"/>.</typeparam>
public sealed class Switch<TEntity, TData>: Island, ICopyable<BuildContext> where TEntity: notnull, Entity {
    private readonly string m_toggleIndicatorName  = null!;
    private readonly Action<TEntity, TData> m_onChange = null!;

    private readonly TData[] m_stateInfos = null!;
    private int m_preStateIndex = -1;

    private int m_stateIndex = 0;
    
    /// <summary>
    /// Visible indicator as <typeparamref name="TEntity"/>.
    /// </summary>
    public TEntity Content { init => Get<RebuildContent>()!.Content = new Viewport { Name = m_toggleIndicatorName, Content = value }; }
    
    /// <summary>
    /// Represents a callback function, which runs at every value change.
    /// </summary>
    public Action<TEntity, TData> OnChange { init => m_onChange = value; }
    
    /// <summary>
    /// All possible state of the current <see cref="Switch{TEntity,TData}"/> instance.
    /// </summary>
    public TData[] Data { init => m_stateInfos = value; }
    
    /// <summary>
    /// Current active state of the <see cref="Switch{TEntity,TData}"/> instance.
    /// </summary>
    public TData Value { get => m_stateInfos[m_stateIndex]; }

    /// <summary>
    /// Create a new <see cref="Switch{TEntity,TData}"/> instance.
    /// </summary>
    public Switch() {
        m_toggleIndicatorName = $"__toggle_{Guid.CreateVersion7()}__";

        _ = Attach<Position>(ComponentPool<Position>.Shared.Rent());
        _ = Attach<Scale>(ComponentPool<Scale>.Shared.Rent(static(scale) => scale.Value = Vec2.Auto with { Y = 1 }));
    }

    public void Copy(ref BuildContext context) {
        context.SetPivot<Scale>(this);
        context.SetPivot<Position>(this);
    }
    
    /// <summary>
    /// Move the internal state to the next possible state.
    /// </summary>
    public void Next()
        => m_stateIndex = ++m_stateIndex % m_stateInfos.Length;

    protected override Entity? Build(ref readonly BuildContext context) {
        return new Viewport {
            Content = new OnUpdate<RenderMessage>(context) {
                Pivot = this,
                On = (_, ref readonly tree) => {
                    TEntity indicator = (TEntity)tree.Visit<Viewport>(name: m_toggleIndicatorName)!
                                                     .Get<Hierarchy>(Hierarchy.ChildrenStart)!
                                                     .Attached!;

                    if (indicator.Get<Scale>() == null) return;

                    if (m_stateInfos.Length > 0 && m_preStateIndex != m_stateIndex) {
                        m_preStateIndex = m_stateIndex;
                        m_onChange?.Invoke(indicator, m_stateInfos[m_stateIndex]);
                    }

                    float width = indicator.Get<Scale>()!.Value.X;
                    this.ClipRenderScale();
                },
                Content = Get<RebuildContent>()?.Content ?? null!
            }
        };
    }
}