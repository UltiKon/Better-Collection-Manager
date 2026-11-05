using System;

namespace BetterCollectionManager
{
    /// <summary>Row-major filter placement with full-width section headings.</summary>
    internal static class FilterGridLayout
    {
        internal const int MaxAutomaticRows = 12, MaxAutomaticColumns = 3;
        internal const float AutomaticHeightFraction = 0.78f, AutomaticWidthFraction = 0.52f;
        internal const int ThreeColumnMinimumItems = 100;
        // Keep the complete resized frame outside the viewport before opening,
        // while preserving the native slide direction and minimum travel.
        internal static float HiddenSlideMultiplier(UnityEngine.Rect frame, UnityEngine.Vector3 delta, float width, float height)
        {
            float distance;
            if (System.Math.Abs(delta.x) >= System.Math.Abs(delta.y) && System.Math.Abs(delta.x) > 0.001f)
                distance = delta.x < 0f ? (frame.xMax + 2f) / -delta.x : (width + 2f - frame.xMin) / delta.x;
            else if (System.Math.Abs(delta.y) > 0.001f)
                distance = delta.y < 0f ? (frame.yMax + 2f) / -delta.y : (height + 2f - frame.yMin) / delta.y;
            else return 1f;
            return System.Math.Max(1f, distance);
        }

        internal readonly struct Dimensions
        {
            public readonly int Rows, Columns;
            public Dimensions(int rows, int columns) { Rows = rows; Columns = columns; }
        }

        public static Dimensions Choose(bool[] headers, int requestedRows, int requestedColumns, int rowCapacity, int columnCapacity)
        {
            rowCapacity = Math.Max(1, Math.Min(CollectionQualitySettings.MaxFilterRows, rowCapacity));
            columnCapacity = Math.Max(1, Math.Min(CollectionQualitySettings.MaxFilterColumns, columnCapacity));
            if (requestedRows == 0) rowCapacity = Math.Min(rowCapacity, MaxAutomaticRows);
            if (requestedColumns == 0) columnCapacity = Math.Min(columnCapacity, MaxAutomaticColumns);
            int heading = headers.Length > 0 && headers[0] ? 1 : 0;
            int targetRows = requestedRows > 0 ? requestedRows : rowCapacity;
            // A small amount of scrolling can save an entire column. Keep
            // this allowance exclusive to a fully automatic grid; a fixed
            // row count continues to determine automatic columns directly.
            int columns = requestedColumns;
            int itemCount = 0;
            foreach (bool header in headers) if (!header) itemCount++;
            if (columns <= 0)
            {
                columns = 1;
                while (columns < columnCapacity)
                {
                    // Two columns may scroll. A third is worthwhile only for
                    // very large lists; section headings do not count.
                    if (columns == 2 && itemCount < ThreeColumnMinimumItems) break;
                    int allowance = requestedRows == 0 && columns == 1 ? Math.Max(1, (targetRows + 3) / 4) : 0;
                    if (RowCount(headers, columns) - heading <= targetRows + allowance) break;
                    columns++;
                }
            }
            int needed = Math.Max(1, RowCount(headers, columns) - heading);
            return new Dimensions(requestedRows > 0 ? requestedRows : Math.Min(needed, rowCapacity), columns);
        }

        private static int RowCount(bool[] headers, int columns)
        {
            int rows = 0, pending = 0;
            foreach (bool header in headers)
            {
                if (header)
                {
                    if (pending > 0) { rows++; pending = 0; }
                    rows++;
                }
                else if (++pending == columns) { rows++; pending = 0; }
            }
            return rows + (pending > 0 ? 1 : 0);
        }

        internal struct Cell
        {
            public int Column;
            public float Down, Height;
        }

        // Native scroll height is additive. Parallel buttons contribute one
        // row, not one height per column. Visibility still uses each full item.
        public static float[] ScrollHeights(Cell[] cells)
        {
            var result = new float[cells.Length];
            int first = 0;
            while (first < cells.Length)
            {
                int end = first + 1;
                float height = cells[first].Height;
                while (end < cells.Length && cells[end].Down == cells[first].Down)
                {
                    height = Math.Max(height, cells[end].Height);
                    end++;
                }
                result[first] = height;
                first = end;
            }
            return result;
        }

        public static Cell[] Arrange(float[] heights, bool[] headers, int columns)
        {
            if (heights == null || headers == null || heights.Length != headers.Length)
                throw new ArgumentException("Each filter item needs a height and header flag");
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            var cells = new Cell[heights.Length];
            int column = 0;
            float down = 0f, rowHeight = 0f;
            for (int i = 0; i < cells.Length; i++)
            {
                float height = heights[i] > 0f && !float.IsInfinity(heights[i]) ? heights[i] : 0.45f;
                if (headers[i])
                {
                    if (column > 0) { down += rowHeight; column = 0; rowHeight = 0f; }
                    cells[i] = new Cell { Column = -1, Down = down, Height = height };
                    down += height;
                }
                else
                {
                    cells[i] = new Cell { Column = column++, Down = down, Height = height };
                    rowHeight = Math.Max(rowHeight, height);
                    if (column == columns) { down += rowHeight; column = 0; rowHeight = 0f; }
                }
            }
            return cells;
        }
    }
}
