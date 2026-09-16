using UnityEngine;

/// <summary>
/// Frame-to-pixel mapping for the ability timeline lanes, kept free of IMGUI so
/// the geometry every drag depends on can be tested without an Editor window.
///
/// Frames are 1-based, matching every authored field on <c>AbilityStep</c>, and
/// a frame owns a cell of the lane rather than a point on it: a marker sits at
/// the centre of its cell, and a window covers its cells edge to edge. That is
/// what makes <see cref="FrameAt"/> the exact inverse of <see cref="MarkerX"/>,
/// so a marker dropped where the cursor is does not land a frame off.
/// </summary>
internal static class AbilityTimelineLayout
{
    /// <summary>Width of one frame cell. Zero for a lane with no frames.</summary>
    internal static float FrameWidth(Rect lane, int frameCount)
    {
        return frameCount <= 0 ? 0f : lane.width / frameCount;
    }

    /// <summary>Centre of the cell owned by <paramref name="frame"/>.</summary>
    internal static float MarkerX(Rect lane, int frameCount, int frame)
    {
        float width = FrameWidth(lane, frameCount);
        return lane.x + (Mathf.Clamp(frame, 1, Mathf.Max(1, frameCount)) - 0.5f) * width;
    }

    /// <summary>
    /// The frame under <paramref name="x"/>, clamped to the lane. A drag that
    /// leaves the lane keeps pinning the marker to the closest legal frame
    /// instead of dropping the input.
    /// </summary>
    internal static int FrameAt(Rect lane, int frameCount, float x)
    {
        if (frameCount <= 0)
        {
            return 0;
        }

        float width = FrameWidth(lane, frameCount);
        int frame = 1 + Mathf.FloorToInt((x - lane.x) / width);
        return Mathf.Clamp(frame, 1, frameCount);
    }

    /// <summary>
    /// The rect covering frames <paramref name="fromFrame"/> to
    /// <paramref name="toFrame"/>, both inclusive. An inverted range collapses
    /// to the width of nothing rather than drawing backwards.
    /// </summary>
    internal static Rect Span(Rect lane, int frameCount, int fromFrame, int toFrame)
    {
        if (frameCount <= 0)
        {
            return new Rect(lane.x, lane.y, 0f, lane.height);
        }

        int first = Mathf.Clamp(fromFrame, 1, frameCount);
        int last = Mathf.Clamp(toFrame, 1, frameCount);
        float width = FrameWidth(lane, frameCount);
        float x = lane.x + (first - 1) * width;
        float right = lane.x + last * width;
        return new Rect(x, lane.y, Mathf.Max(0f, right - x), lane.height);
    }
}
