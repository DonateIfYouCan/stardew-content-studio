using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace CustomContentCore.UI
{
    /// <summary>A page within the editor.</summary>
    public abstract class Screen
    {
        public EditorRoot Root = null!;
        protected readonly List<Widget> Widgets = new();

        /// <summary>Whether the screen below should still be drawn (e.g. for dialogs).</summary>
        public virtual bool IsOverlay => false;

        /// <summary>The area the screen may draw in.</summary>
        public Rectangle Area { get; private set; }

        /// <summary>Position the widgets for the given area.</summary>
        public void Layout(Rectangle area)
        {
            this.Area = area;
            this.OnLayout(area);
        }

        protected abstract void OnLayout(Rectangle area);

        public virtual void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            foreach (Widget widget in this.Widgets)
                widget.Draw(b, mouseX, mouseY);
        }

        public virtual void LeftClick(int x, int y)
        {
            // deselect text fields first, then let the clicked widget handle it
            bool handled = false;
            foreach (Widget widget in this.Widgets.Where(w => w.Visible).ToArray())
            {
                if (widget is TextField field)
                {
                    if (field.Click(x, y))
                        handled = true;
                }
            }
            if (handled)
                return;
            foreach (Widget widget in this.Widgets.Where(w => w.Visible && w is not TextField).ToArray())
            {
                if (widget.Click(x, y))
                    return;
            }
        }

        public virtual void LeftHeld(int x, int y) { }
        public virtual void ReleaseLeft(int x, int y) { }

        /// <summary>Handle a right-click.</summary>
        public virtual void RightClick(int x, int y) { }

        public virtual void Scroll(int x, int y, int direction)
        {
            foreach (Widget widget in this.Widgets)
            {
                if (widget is IScrollable list && list.ScrollBy(x, y, direction))
                    return;
            }
        }

        /// <summary>Handle a key press. Returns whether it was handled.</summary>
        public virtual bool KeyPress(Keys key) => false;

        public virtual void Update(GameTime time) { }

        /// <summary>Called when the screen is removed.</summary>
        public virtual void Dispose() { }

        /// <summary>Called when the screen becomes the top screen again.</summary>
        public virtual void OnResume() { }

        protected T Add<T>(T widget) where T : Widget
        {
            this.Widgets.Add(widget);
            return widget;
        }

        public virtual string? GetTooltip(int x, int y)
        {
            return this.Widgets.LastOrDefault(w => w.Visible && w.Tooltip != null && w.Bounds.Contains(x, y))?.Tooltip;
        }
    }

    /// <summary>The menu that hosts the editor screens (a stack: <see cref="Push"/> opens a screen, <see cref="Pop"/> goes back).</summary>
    public sealed class EditorRoot : IClickableMenu
    {
        private readonly List<Screen> Stack = new();
        private readonly Action OnClosed;
        private bool Closed;

        public EditorRoot(Screen first, Action onClosed)
            : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height)
        {
            this.OnClosed = onClosed;
            this.Push(first);
        }

        private Screen Top => this.Stack[^1];

        /// <summary>Whether the editor has been closed for good.</summary>
        public bool IsClosed => this.Closed;

        /// <summary>Place the screens again, e.g. after coming back from the game.</summary>
        public void Relayout()
        {
            if (this.Stack.Count > 0)
                this.Top.Layout(GetArea());
        }

        public static Rectangle GetArea()
        {
            int w = Math.Min(Game1.uiViewport.Width - 32, 1500);
            int h = Math.Min(Game1.uiViewport.Height - 32, 920);
            return new Rectangle((Game1.uiViewport.Width - w) / 2, (Game1.uiViewport.Height - h) / 2, w, h);
        }

        public void Push(Screen screen)
        {
            ClearTextInput();
            screen.Root = this;
            this.Stack.Add(screen);
            screen.Layout(GetArea());
        }

        /// <summary>Remove the top screen, closing the editor if it was the last one.</summary>
        public void Pop()
        {
            ClearTextInput();
            Screen top = this.Top;
            this.Stack.RemoveAt(this.Stack.Count - 1);
            top.Dispose();
            if (this.Stack.Count == 0)
                this.Close();
            else
            {
                this.Top.Layout(GetArea());
                this.Top.OnResume();
            }
        }

        public void Close()
        {
            if (this.Closed)
                return;
            this.Closed = true;
            ClearTextInput();
            foreach (Screen screen in this.Stack)
                screen.Dispose();
            this.Stack.Clear();
            this.OnClosed();
        }

        private static void ClearTextInput()
        {
            if (Game1.keyboardDispatcher.Subscriber is TextBox box)
                box.Selected = false;
            Game1.keyboardDispatcher.Subscriber = null;
        }

        private static bool IsTyping => Game1.keyboardDispatcher.Subscriber is TextBox { Selected: true };

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);
            this.width = Game1.uiViewport.Width;
            this.height = Game1.uiViewport.Height;
            foreach (Screen screen in this.Stack)
                screen.Layout(GetArea());
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.Stack.Count > 0)
                this.Top.LeftClick(x, y);
        }

        public override void leftClickHeld(int x, int y)
        {
            if (this.Stack.Count > 0)
                this.Top.LeftHeld(x, y);
        }

        public override void releaseLeftClick(int x, int y)
        {
            if (this.Stack.Count > 0)
                this.Top.ReleaseLeft(x, y);
        }

        public override void receiveRightClick(int x, int y, bool playSound = true)
        {
            if (this.Stack.Count > 0)
                this.Top.RightClick(x, y);
        }

        public override void receiveScrollWheelAction(int direction)
        {
            if (this.Stack.Count > 0)
                this.Top.Scroll(Game1.getMouseX(), Game1.getMouseY(), direction);
        }

        public override void receiveKeyPress(Keys key)
        {
            if (this.Stack.Count == 0)
                return;

            if (IsTyping)
            {
                if (key is Keys.Escape or Keys.Tab)
                    ClearTextInput();
                return;
            }

            if (this.Top.KeyPress(key))
                return;

            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
                this.Pop();
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b == Buttons.B && this.Stack.Count > 0)
                this.Pop();
        }

        public override bool readyToClose() => false;

        public override void update(GameTime time)
        {
            if (this.Stack.Count > 0)
                this.Top.Update(time);
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.6f);
            if (this.Stack.Count == 0)
                return;

            int mx = Game1.getMouseX(), my = Game1.getMouseY();
            if (this.Top.IsOverlay && this.Stack.Count > 1)
            {
                this.Stack[^2].Draw(b, -1, -1);
                b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.4f);
            }
            this.Top.Draw(b, mx, my);

            string? tooltip = this.Top.GetTooltip(mx, my);
            if (tooltip != null)
                drawHoverText(b, Game1.parseText(tooltip, Game1.smallFont, 420), Game1.smallFont);

            this.drawMouse(b);
        }
    }
}
