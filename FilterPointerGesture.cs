namespace BetterCollectionManager
{
    // A release belongs to the press that started it, including releases delivered
    // by the full-screen hide area after dragging the scrollbar out of the tray.
    internal sealed class FilterPointerGesture
    {
        private int m_frame = -1;
        private bool m_tracking, m_insidePress, m_dragged;
        private float m_x, m_y;
        public bool CloseOutside { get; private set; }
        public bool Dragged => m_dragged;

        public void Observe(int frame, bool down, bool held, bool up, float x, float y, bool inside)
        {
            if (m_frame == frame) return;
            m_frame = frame;
            CloseOutside = false;
            if (down)
            {
                m_tracking = true; m_insidePress = inside; m_dragged = false;
                m_x = x; m_y = y;
            }
            if (m_tracking && (held || up))
            {
                float dx = x - m_x, dy = y - m_y;
                m_dragged |= dx * dx + dy * dy > 64f;
            }
            if (up)
            {
                CloseOutside = m_tracking && !m_insidePress && !inside;
                m_tracking = false;
            }
        }

        public void Reset()
        {
            m_frame = -1; m_tracking = m_insidePress = m_dragged = CloseOutside = false;
        }
    }
}
