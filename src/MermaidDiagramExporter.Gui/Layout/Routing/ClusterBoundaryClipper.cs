namespace MermaidDiagramExporter.Gui.Layout;

public sealed class ClusterBoundaryClipper
{
    public Vector2 FindIntersection(Rect bounds, Vector2 fromInside, Vector2 towardPoint)
    {
        if (bounds.width <= 0f || bounds.height <= 0f)
            return fromInside;

        var direction = towardPoint - fromInside;
        if (direction.sqrMagnitude < 0.0001f)
            return fromInside;

        float bestT = float.PositiveInfinity;
        var bestPoint = fromInside;

        TryUpdateIntersection(
            fromInside, direction, bounds.xMin, isVertical: true, bounds.yMin, bounds.yMax,
            ref bestT, ref bestPoint);

        TryUpdateIntersection(
            fromInside, direction, bounds.xMax, isVertical: true, bounds.yMin, bounds.yMax,
            ref bestT, ref bestPoint);

        TryUpdateIntersection(
            fromInside, direction, bounds.yMin, isVertical: false, bounds.xMin, bounds.xMax,
            ref bestT, ref bestPoint);

        TryUpdateIntersection(
            fromInside, direction, bounds.yMax, isVertical: false, bounds.xMin, bounds.xMax,
            ref bestT, ref bestPoint);

        return bestT < float.PositiveInfinity ? bestPoint : fromInside;
    }

    private static void TryUpdateIntersection(
        Vector2 origin,
        Vector2 direction,
        float edgeValue,
        bool isVertical,
        float minOther,
        float maxOther,
        ref float bestT,
        ref Vector2 bestPoint)
    {
        float primary = isVertical ? direction.x : direction.y;
        if (Mathf.Abs(primary) < 0.0001f)
            return;

        float t = (edgeValue - (isVertical ? origin.x : origin.y)) / primary;
        if (t <= 0f || t >= bestT)
            return;

        var point = origin + (direction * t);
        float other = isVertical ? point.y : point.x;
        if (other < minOther - 0.001f || other > maxOther + 0.001f)
            return;

        bestT = t;
        bestPoint = point;
    }
}
