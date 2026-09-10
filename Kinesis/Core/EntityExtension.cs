using Kinesis.Core;
using Kinesis.Core.Rendering;
using Kinesis.UI;
using Kinesis.UI.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kinesis;

public static class EntityExtension {
    extension(Entity entity) {

        /// <summary>
        /// Count of the <typeparamref name="T"/> on the current <see cref="Entity"/> instance.
        /// </summary>
        /// <typeparam name="T">Type of the <see cref="Component"/>.</typeparam>
        /// <param name="comparand">Filter method for the counting.</param>
        /// <returns>Returns a count as <see cref="int"/>.</returns>
        public int CountComponent<T>(Func<T, bool>? comparand = null) where T: Component, IStaticType {
            if (entity == null) return 0;

            int count = 0;
            comparand ??= static(_) => true;

            foreach (Component comp in entity) {
                if (comp == null) continue;

                if (comp.TypeOf(T.TypeName) && comparand((T)comp))
                    ++count;
            }

            return count;
        }

        /// <summary>
        /// Move an <see cref="Entity"/> based on the <paramref name="x"/> and <paramref name="y"/> values.
        /// </summary>
        /// <param name="x">Move amount on the X axis.</param>
        /// <param name="y">Move amount on the Y axis.</param>
        /// <remarks>
        /// <b>Remarks:</b> If the entity not has <see cref="Position"/> or <see cref="Scale"/> component, then the function does nothing.
        /// </remarks>
        public void Move(float x, float y) {
            if (entity.Get<Position>() == null) return;

            Position position = entity.Get<Position>()!;
            position.Relative = new Vec2(x, y);
        }

        /// <summary>
        /// Clipping the current <see cref="Entity"/> based on the parent scale.
        /// </summary>
        public void ClipRenderScale() {
            Scale    scale    = null!;
            Position position = null!;

            if ((position = entity.Get<Position>()!) == null || (scale = entity.Get<Scale>()!) == null) return;
            scale.Inset = Vec2.Zero;

            Vec2 currentScale = scale.Value;
            Vec2 currentPositon = position.Absolute;

            Vec2 parentScale = scale.Maximum?.Value ?? Vec2.Zero;
            Vec2 parentPosition = position.Origin?.Absolute ?? Vec2.Zero;

            Vec2 allCurrent = currentScale + currentPositon;
            Vec2 allParent = parentScale + parentPosition;

            Vec2 limitedScaleWithInset = parentPosition - currentPositon;
            Vec2 inset = Vec2.Zero;

            if (allParent.X < allCurrent.X) inset.X = allCurrent.X - allParent.X;
            if (allParent.Y < allCurrent.Y) inset.Y = allCurrent.Y - allParent.Y;

            scale.Inset = inset;
        }
    }
}