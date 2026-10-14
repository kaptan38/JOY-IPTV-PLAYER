using System.Drawing;

namespace JoyIptv;

internal sealed class VirtualChannelList : ScrollableControl
{
    private IReadOnlyList<Channel> items = Array.Empty<Channel>();
    private int selected = -1, height = 62;
    public int Count => items.Count;
    public event EventHandler? SelectedIndexChanged;
    public event EventHandler? ItemActivated;
    public event DrawItemEventHandler? DrawItem;
    public int ItemHeight { get => height; set { height = Math.Max(1, value); UpdateExtent(); } }
    public int SelectedIndex
    {
        get => selected;
        set
        {
            int next = value >= 0 && value < Count ? value : -1;
            if (next == selected) return;
            selected = next;
            if (selected >= 0)
            {
                int y = selected * height, offset = -AutoScrollPosition.Y;
                if (y < offset) AutoScrollPosition = new Point(0, y);
                else if (y + height > offset + ClientSize.Height)
                    AutoScrollPosition = new Point(0, Math.Max(0, y + height - ClientSize.Height));
            }
            Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public int TopIndex { get => Math.Max(0, -AutoScrollPosition.Y / height);
        set => AutoScrollPosition = new Point(0, Math.Clamp(value, 0, Math.Max(0, Count - 1)) * height); }
    public VirtualChannelList()
    {
        AutoScroll = true; DoubleBuffered = true; TabStop = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }
    public void SetItems(IReadOnlyList<Channel> value, int index)
    {
        items = value; selected = -1; UpdateExtent(); SelectedIndex = index; Invalidate();
    }
    private void UpdateExtent() { AutoScrollMinSize = new Size(0, checked(Count * height)); Invalidate(); }
    public int IndexFromPoint(Point point)
    {
        if (!ClientRectangle.Contains(point)) return -1;
        int index = (point.Y - AutoScrollPosition.Y) / height;
        return index >= 0 && index < Count ? index : -1;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int first = TopIndex, last = Math.Min(Count, first + ClientSize.Height / height + 2);
        for (int i = first; i < last; i++)
        {
            var bounds = new Rectangle(0, i * height + AutoScrollPosition.Y, ClientSize.Width, height);
            if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
            DrawItem?.Invoke(this, new DrawItemEventArgs(e.Graphics, Font, bounds, i,
                i == selected ? DrawItemState.Selected | (Focused ? DrawItemState.Focus : 0) : DrawItemState.None));
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    { base.OnMouseDown(e); Focus(); int index = IndexFromPoint(e.Location); if (index >= 0) SelectedIndex = index; }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    { base.OnMouseDoubleClick(e); if (IndexFromPoint(e.Location) >= 0) ItemActivated?.Invoke(this, EventArgs.Empty); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is
        Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int page = Math.Max(1, ClientSize.Height / height);
        int next = e.KeyCode switch { Keys.Home => 0, Keys.End => Count - 1,
            Keys.Up => selected - 1, Keys.Down => selected + 1, Keys.PageUp => selected - page,
            Keys.PageDown => selected + page, _ => selected };
        if (e.KeyCode is Keys.Home or Keys.End or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
        { SelectedIndex = Count == 0 ? -1 : Math.Clamp(next, 0, Count - 1); e.Handled = true; }
        if (e.KeyCode == Keys.Enter && selected >= 0) { ItemActivated?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
