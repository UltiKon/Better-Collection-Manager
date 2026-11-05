using System;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class ComboInspectLayout
    {
        internal sealed class Page
        {
            public Rect[] Cards;
            public Rect Panel, Header, Description, Toolbar;
            public float Font, ActionCenter;
            public int First, Count, TotalPages, Number;
        }
        public static Page Arrange(int width, int height, int count, int requestedPage, bool hasDescription = true, int? requestedFirst = null)
        {
            float margin = Math.Max(8f, Math.Min(width, height) * .04f);
            float font = Math.Max(5f, Math.Min(height / 45f, 24f)), padding = font * 1.2f;
            float availableWidth = Math.Max(1f, width - margin * 2f);
            bool sidePane = hasDescription && width >= height * 1.55f;
            float gap = Math.Max(8f, width * .012f);
            float descriptionWidth = sidePane ? Math.Min(availableWidth * .32f, font * 25f) : Math.Min(availableWidth, font * 25f);
            float descriptionHeight = hasDescription ? (sidePane ? descriptionWidth * .9f : height * .20f) : 0f;
            float cardsWidth = availableWidth - (sidePane ? descriptionWidth + gap : 0f);
            float cardHeight = Math.Min(700f, Math.Max(1f, height * (hasDescription && !sidePane ? .52f : .62f)));
            float cardWidth = cardHeight * .72f;
            if (cardWidth > cardsWidth) { cardWidth = cardsWidth; cardHeight = cardWidth / .72f; }
            int capacity = Math.Max(1, Math.Min(4, (int)((cardsWidth + gap) / (cardWidth + gap))));
            if (count > capacity)
            {
                // Reserve room for the enlarged arrows while retaining a
                // readable card on narrow windows.
                float minimumCardWidth = Math.Min(cardWidth, Math.Min(400f, height * .5f) * .72f);
                float gutters = Math.Min(font * 22.6f, Math.Max(0f, cardsWidth - minimumCardWidth - padding * 2f));
                availableWidth = Math.Max(1f, availableWidth - gutters);
                cardsWidth = Math.Max(1f, cardsWidth - gutters);
                if (cardWidth > cardsWidth) { cardWidth = cardsWidth; cardHeight = cardWidth / .72f; }
                capacity = Math.Max(1, Math.Min(4, (int)((cardsWidth + gap) / (cardWidth + gap))));
            }
            int totalPages = Math.Max(1, (count + capacity - 1) / capacity);
            int page = Math.Max(0, Math.Min(requestedPage, totalPages - 1));
            // Stop at the last full row instead of leaving a short final page.
            // For five cards in pairs the starts are 0, 2, 3 (not 0, 2, 4).
            int first = Math.Min(page * capacity, Math.Max(0, count - capacity));
            if (requestedFirst.HasValue)
            {
                first = Math.Max(0, Math.Min(requestedFirst.Value, Math.Max(0, count - capacity)));
                page = (first + capacity - 1) / capacity;
            }
            int visible = Math.Max(0, Math.Min(capacity, count - first));
            var result = new Page { Cards = new Rect[visible], First = first, Count = visible, TotalPages = totalPages, Number = page };
            float contentWidth = visible * cardWidth + Math.Max(0, visible - 1) * gap;
            float groupWidth = contentWidth + (sidePane ? descriptionWidth + gap : 0f);
            float panelWidth = Math.Min(availableWidth, Math.Max(groupWidth + padding * 2f, font * 36f));
            float panelHeight = Math.Max(cardHeight, sidePane ? descriptionHeight : 0f)
                + (hasDescription && !sidePane ? descriptionHeight : 0f) + font * 6.5f;
            float headerHeight = 0f, bottom = (height - panelHeight) * .5f;
            result.Font = font;
            result.Panel = new Rect((width - panelWidth) * .5f, bottom, panelWidth, panelHeight);
            result.Header = new Rect(result.Panel.xMin, result.Panel.yMax + font * .4f, panelWidth, headerHeight);
            result.Toolbar = new Rect(result.Panel.xMin + padding, bottom + padding, panelWidth - padding * 2f, font * 3.8f);
            float cardBottom = result.Toolbar.yMax + font * 1.5f + (hasDescription && !sidePane ? descriptionHeight : 0f);
            float left = (width - groupWidth) * .5f;
            result.Description = sidePane
                ? new Rect(left + contentWidth + gap, cardBottom + (cardHeight - descriptionHeight) * .5f, descriptionWidth, descriptionHeight)
                : new Rect((width - descriptionWidth) * .5f, result.Toolbar.yMax + font * .6f, descriptionWidth, descriptionHeight);
            for (int i = 0; i < visible; i++) result.Cards[i] = new Rect(left + i * (cardWidth + gap), cardBottom, cardWidth, cardHeight);
            int items = visible + (sidePane ? 1 : 0);
            float Center(int index) => index < visible ? result.Cards[index].center.x : result.Description.center.x;
            result.ActionCenter = items == 0 ? width * .5f : items % 2 == 1 ? Center(items / 2)
                : (Center(items / 2 - 1) + Center(items / 2)) * .5f;

            return result;
        }
    }
}
