using netDxf;
using netDxf.Entities;

namespace dxfWorker;

/// <summary>
/// Finds bounded faces made from straight LINE segments and arcs, and closed, straight polylines,
/// then searches for a safe horizontal text rectangle inside one of those faces.
/// </summary>
internal static class ClosedContourTextPlacer
{
    private const double EstimatedGlyphWidth = 0.78;
    private const double HorizontalMarginInHeights = 0.20;
    private const double VerticalEnvelopeInHeights = 1.50;

    internal readonly record struct Placement(Vector3 Position, double Height, double Rotation = 0.0, double WidthFactor = 1.0);
    private readonly record struct Point(double X, double Y);
    private readonly record struct PointKey(long X, long Y);
    private readonly record struct Edge(int A, int B);
    private sealed class Node(Point point)
    {
        public Point Point { get; } = point;
        public List<int> Edges { get; } = new();
    }

    public static bool TryFindBestPlacement(
        IEnumerable<Line> lines,
        IEnumerable<Arc> arcs,
        IEnumerable<Circle> circles,
        IEnumerable<Polyline2D> polylines,
        IEnumerable<Polyline3D> polylines3D,
        string text,
        double maximumHeight,
        out int detectedContours,
        out Placement placement)
    {
        placement = default;
        detectedContours = 0;
        if (string.IsNullOrWhiteSpace(text) || maximumHeight <= 0)
            return false;

        var rawSegments = new List<(Point A, Point B)>();
        foreach (Line line in lines)
            rawSegments.Add((ToPoint(line.StartPoint), ToPoint(line.EndPoint)));

        // Approximate circular arcs with short straight chords so they can close a line contour.
        foreach (Arc arc in arcs)
        {
            double start = arc.StartAngle * Math.PI / 180.0;
            double sweep = arc.EndAngle - arc.StartAngle;
            while (sweep < 0) sweep += 360.0;
            if (sweep <= 1e-10) sweep = 360.0;
            int steps = Math.Max(2, (int)Math.Ceiling(sweep / 3.0));
            Point previous = new(
                arc.Center.X + arc.Radius * Math.Cos(start),
                arc.Center.Y + arc.Radius * Math.Sin(start));
            for (int i = 1; i <= steps; i++)
            {
                double angle = start + sweep * i / steps;
                Point next = new(
                    arc.Center.X + arc.Radius * Math.Cos(angle),
                    arc.Center.Y + arc.Radius * Math.Sin(angle));
                rawSegments.Add((previous, next));
                previous = next;
            }
        }

        // Closed 2D polylines without bulge arcs can be treated as straight segments.
        foreach (Polyline2D polyline in polylines)
        {
            if (!polyline.IsClosed || polyline.Vertexes.Count < 3 || polyline.Vertexes.Any(v => Math.Abs(v.Bulge) > 1e-10))
                continue;

            for (int i = 0; i < polyline.Vertexes.Count; i++)
            {
                Point a = ToPoint(polyline.Vertexes[i].Position);
                Point b = ToPoint(polyline.Vertexes[(i + 1) % polyline.Vertexes.Count].Position);
                rawSegments.Add((a, b));
            }
        }

        foreach (Polyline3D polyline in polylines3D)
        {
            if (!polyline.IsClosed || polyline.Vertexes.Count < 3)
                continue;
            double minZ = polyline.Vertexes.Min(v => v.Z);
            double maxZ = polyline.Vertexes.Max(v => v.Z);
            if (maxZ - minZ > 1e-8)
                continue;
            for (int i = 0; i < polyline.Vertexes.Count; i++)
            {
                Point a = ToPoint(polyline.Vertexes[i]);
                Point b = ToPoint(polyline.Vertexes[(i + 1) % polyline.Vertexes.Count]);
                rawSegments.Add((a, b));
            }
        }

        var scanlineSegments = new List<(Point A, Point B)>(rawSegments);
        // Circles are useful as holes to avoid in the scanline fallback, but are not
        // treated as candidate material contours by themselves.
        foreach (Circle circle in circles)
        {
            const int circleSteps = 72;
            Point previous = new(circle.Center.X + circle.Radius, circle.Center.Y);
            for (int n = 1; n <= circleSteps; n++)
            {
                double angle = 2.0 * Math.PI * n / circleSteps;
                Point next = new(
                    circle.Center.X + circle.Radius * Math.Cos(angle),
                    circle.Center.Y + circle.Radius * Math.Sin(angle));
                scanlineSegments.Add((previous, next));
                previous = next;
            }
        }

        if (rawSegments.Count < 3)
            return false;

        double minX = rawSegments.Min(s => Math.Min(s.A.X, s.B.X));
        double maxX = rawSegments.Max(s => Math.Max(s.A.X, s.B.X));
        double minY = rawSegments.Min(s => Math.Min(s.A.Y, s.B.Y));
        double maxY = rawSegments.Max(s => Math.Max(s.A.Y, s.B.Y));
        double tolerance = Math.Max(Math.Max(maxX - minX, maxY - minY) * 1e-9, 1e-8);

        var nodes = new List<Node>();
        var nodeByKey = new Dictionary<PointKey, int>();
        var edges = new List<Edge>();
        var uniqueEdges = new HashSet<(int A, int B)>();

        int GetNode(Point point)
        {
            var key = new PointKey((long)Math.Round((point.X - minX) / tolerance), (long)Math.Round((point.Y - minY) / tolerance));
            if (nodeByKey.TryGetValue(key, out int existing))
                return existing;
            int id = nodes.Count;
            nodes.Add(new Node(point));
            nodeByKey.Add(key, id);
            return id;
        }

        foreach ((Point a, Point b) in rawSegments)
        {
            int first = GetNode(a);
            int second = GetNode(b);
            if (first == second)
                continue;
            var key = first < second ? (first, second) : (second, first);
            if (!uniqueEdges.Add(key))
                continue;
            int edgeId = edges.Count;
            edges.Add(new Edge(first, second));
            nodes[first].Edges.Add(edgeId);
            nodes[second].Edges.Add(edgeId);
        }

        if (edges.Count < 3)
            return false;

        // Sort each vertex's incident edges by angle; walking the next clockwise edge
        // traces the bounded faces in a planar straight-line graph.
        for (int nodeId = 0; nodeId < nodes.Count; nodeId++)
        {
            Node node = nodes[nodeId];
            node.Edges.Sort((left, right) =>
            {
                Point l = nodes[Other(edges[left], nodeId)].Point;
                Point r = nodes[Other(edges[right], nodeId)].Point;
                double la = Math.Atan2(l.Y - node.Point.Y, l.X - node.Point.X);
                double ra = Math.Atan2(r.Y - node.Point.Y, r.X - node.Point.X);
                return la.CompareTo(ra);
            });
        }

        var contours = ExtractBoundedFaces(nodes, edges);
        detectedContours = contours.Count;

        double bestHeight = 0;
        Placement bestPlacement = default;
        foreach (List<Point> contour in contours)
        {
            if (TryFitText(contour, text.Length, maximumHeight, out Placement candidate) && candidate.Height > bestHeight)
            {
                bestHeight = candidate.Height;
                bestPlacement = candidate;
            }
        }

        if (TryFitFromScanlines(scanlineSegments, text.Length, maximumHeight, out Placement scanlinePlacement))
        {
            detectedContours = Math.Max(1, detectedContours);
            placement = scanlinePlacement;
            return true;
        }
        if (bestHeight <= 0)
            return false;
        placement = bestPlacement;
        return true;
    }

    private static List<List<Point>> ExtractBoundedFaces(List<Node> nodes, List<Edge> edges)
    {
        var faces = new List<List<Point>>();
        var visited = new HashSet<(int From, int Edge)>();
        var knownFaces = new HashSet<string>();

        for (int edgeId = 0; edgeId < edges.Count; edgeId++)
        {
            Edge edge = edges[edgeId];
            Visit(edge.A, edgeId);
            Visit(edge.B, edgeId);
        }
        return faces;

        void Visit(int startNode, int startEdge)
        {
            if (visited.Contains((startNode, startEdge)))
                return;

            int from = startNode;
            int edgeId = startEdge;
            var faceWalk = new List<int>();
            bool closed = false;
            int guard = edges.Count * 2 + 1;

            while (guard-- > 0)
            {
                if (!visited.Add((from, edgeId)))
                    break;
                faceWalk.Add(from);
                int to = Other(edges[edgeId], from);
                List<int> outgoing = nodes[to].Edges;
                int reverseIndex = outgoing.IndexOf(edgeId);
                if (reverseIndex < 0 || outgoing.Count == 0)
                    break;

                int nextEdge = outgoing[(reverseIndex - 1 + outgoing.Count) % outgoing.Count];
                from = to;
                edgeId = nextEdge;
                if (from == startNode && edgeId == startEdge)
                {
                    closed = true;
                    break;
                }
            }

            if (!closed || faceWalk.Count < 3)
                return;

            // A connected drawing can make a face-walk revisit a junction. Split that
            // walk into simple loops so one loop does not self-intersect through another.
            var path = new List<int>();
            var pathIndex = new Dictionary<int, int>();
            foreach (int nodeId in faceWalk)
            {
                if (pathIndex.TryGetValue(nodeId, out int loopStart))
                {
                    AddFace(path.Skip(loopStart).ToList());
                    for (int i = path.Count - 1; i > loopStart; i--)
                        pathIndex.Remove(path[i]);
                    path.RemoveRange(loopStart + 1, path.Count - loopStart - 1);
                }
                else
                {
                    pathIndex[nodeId] = path.Count;
                    path.Add(nodeId);
                }
            }
            AddFace(path);
        }

        void AddFace(List<int> faceNodes)
        {
            if (faceNodes.Count < 3)
                return;
            List<Point> polygon = faceNodes.Select(id => nodes[id].Point).ToList();
            double area = SignedArea(polygon);
            // Keep a non-degenerate simple loop regardless of traversal direction.
            if (Math.Abs(area) <= 1e-12)
                return;
            string signature = string.Join(",", faceNodes.OrderBy(id => id));
            if (knownFaces.Add(signature))
                faces.Add(polygon);
        }
    }

    private static bool TryFitFromScanlines(
        List<(Point A, Point B)> segments,
        int characterCount,
        double maximumHeight,
        out Placement placement)
    {
        placement = default;
        double minX = segments.Min(s => Math.Min(s.A.X, s.B.X));
        double maxX = segments.Max(s => Math.Max(s.A.X, s.B.X));
        double minY = segments.Min(s => Math.Min(s.A.Y, s.B.Y));
        double maxY = segments.Max(s => Math.Max(s.A.Y, s.B.Y));
        double drawingWidth = maxX - minX;
        double drawingHeight = maxY - minY;
        double minDimension = Math.Min(drawingWidth, drawingHeight);
        double widthPerHeight = Math.Max(1, characterCount) * EstimatedGlyphWidth;
        double fontHeight = Math.Min(maximumHeight, Math.Min(
            drawingHeight / VerticalEnvelopeInHeights,
            drawingWidth / (widthPerHeight + 2 * HorizontalMarginInHeights)));
        if (fontHeight <= 0 || minDimension <= 0)
            return false;

        double minimumHeight = minDimension / 500.0;
        for (int attempt = 0; attempt < 48 && fontHeight >= minimumHeight; attempt++, fontHeight *= 0.86)
        {
            double textWidth = widthPerHeight * fontHeight;
            double requiredWidth = textWidth + 2 * HorizontalMarginInHeights * fontHeight;
            double boxHeight = VerticalEnvelopeInHeights * fontHeight;
            double lowCenter = minY + boxHeight / 2.0;
            double highCenter = maxY - boxHeight / 2.0;
            double step = Math.Max(fontHeight * 0.5, drawingHeight / 200.0);

            foreach (double centerY in CenterOut(lowCenter, highCenter, step))
            {
                double bottom = centerY - boxHeight / 2.0;
                // Sparse fixed samples can miss a small hole between rows. Probe densely
                // and at geometry vertices inside the text envelope to account for cutouts.
                var sampleYs = new SortedSet<double>();
                const int regularSamples = 40;
                for (int sample = 0; sample <= regularSamples; sample++)
                    sampleYs.Add(bottom + boxHeight * (sample + 0.5) / (regularSamples + 1));
                double yEpsilon = Math.Max(boxHeight * 1e-7, 1e-9);
                foreach ((Point a, Point b) in segments)
                foreach (double vertexY in new[] { a.Y, b.Y })
                {
                    if (vertexY > bottom && vertexY < bottom + boxHeight)
                    {
                        sampleYs.Add(Math.Max(bottom + yEpsilon, vertexY - yEpsilon));
                        sampleYs.Add(Math.Min(bottom + boxHeight - yEpsilon, vertexY + yEpsilon));
                    }
                }

                List<(double Left, double Right)> common = ScanlineIntervals(segments, sampleYs.First());
                foreach (double sampleY in sampleYs.Skip(1))
                {
                    if (common.Count == 0)
                        break;
                    List<(double Left, double Right)> row = ScanlineIntervals(segments, sampleY);                    var intersections = new List<(double Left, double Right)>();
                    foreach ((double leftA, double rightA) in common)
                    foreach ((double leftB, double rightB) in row)
                    {
                        double overlapLeft = Math.Max(leftA, leftB);
                        double overlapRight = Math.Min(rightA, rightB);
                        if (overlapRight > overlapLeft)
                            intersections.Add((overlapLeft, overlapRight));
                    }
                    common = intersections;
                }

                (double Left, double Right)? fit = common
                    .Where(interval => interval.Right - interval.Left >= requiredWidth)
                    .OrderByDescending(interval => interval.Right - interval.Left)
                    .Cast<(double Left, double Right)?>()
                    .FirstOrDefault();
                if (fit is null)
                    continue;

                double left = fit.Value.Left + (fit.Value.Right - fit.Value.Left - textWidth) / 2.0;
                placement = new Placement(
                    new Vector3(left, bottom + 0.45 * fontHeight, 0.0),
                    fontHeight);
                return true;
            }
        }
        return false;
    }

    private static List<(double Left, double Right)> ScanlineIntervals(
        List<(Point A, Point B)> segments,
        double y)
    {
        var intersections = new List<double>();
        foreach ((Point a, Point b) in segments)
        {
            if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
            {
                double x = a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                intersections.Add(x);
            }
        }
        intersections.Sort();
        var intervals = new List<(double Left, double Right)>();
        for (int i = 0; i + 1 < intersections.Count; i += 2)
        {
            if (intersections[i + 1] > intersections[i])
                intervals.Add((intersections[i], intersections[i + 1]));
        }
        return intervals;
    }

    private static bool TryFitText(List<Point> polygon, int characterCount, double maximumHeight, out Placement placement)
    {
        placement = default;
        double minX = polygon.Min(p => p.X);
        double maxX = polygon.Max(p => p.X);
        double minY = polygon.Min(p => p.Y);
        double maxY = polygon.Max(p => p.Y);
        double width = maxX - minX;
        double height = maxY - minY;
        double minDimension = Math.Min(width, height);
        if (minDimension <= 1e-8)
            return false;

        double estimatedTextWidthPerHeight = Math.Max(1, characterCount) * EstimatedGlyphWidth;
        double candidateHeight = Math.Min(maximumHeight, Math.Min(
            height / VerticalEnvelopeInHeights,
            width / (estimatedTextWidthPerHeight + 2 * HorizontalMarginInHeights)));
        double minimumHeight = minDimension / 200.0;
        if (candidateHeight < minimumHeight)
            return false;

        // Try larger fonts first. Each search checks that a conservative text rectangle
        // fits wholly within the contour, including a small margin around the glyphs.
        for (int attempt = 0; attempt < 36 && candidateHeight >= minimumHeight; attempt++, candidateHeight *= 0.86)
        {
            double textWidth = estimatedTextWidthPerHeight * candidateHeight;
            double boxWidth = textWidth + 2 * HorizontalMarginInHeights * candidateHeight;
            double boxHeight = VerticalEnvelopeInHeights * candidateHeight;
            if (boxWidth > width || boxHeight > height)
                continue;

            double xLow = minX + boxWidth / 2.0;
            double xHigh = maxX - boxWidth / 2.0;
            double yLow = minY + boxHeight / 2.0;
            double yHigh = maxY - boxHeight / 2.0;
            double stepX = Math.Max(candidateHeight * 0.65, Math.Max(xHigh - xLow, candidateHeight) / 8.0);
            double stepY = Math.Max(candidateHeight * 0.65, Math.Max(yHigh - yLow, candidateHeight) / 8.0);
            foreach (double centerY in CenterOut(yLow, yHigh, stepY))
            {
                foreach (double centerX in CenterOut(xLow, xHigh, stepX))
                {
                    double boxX = centerX - boxWidth / 2.0;
                    double boxY = centerY - boxHeight / 2.0;
                    if (!RectangleInsidePolygon(polygon, boxX, boxY, boxWidth, boxHeight))
                        continue;

                    double x = boxX + HorizontalMarginInHeights * candidateHeight;
                    // DXF TEXT insertion is on the baseline; reserve room for descenders.
                    double y = boxY + 0.45 * candidateHeight;
                    placement = new Placement(new Vector3(x, y, 0), candidateHeight);
                    return true;
                }
            }
        }
        return false;
    }

    private static IEnumerable<double> CenterOut(double min, double max, double step)
    {
        if (max <= min + 1e-10)
        {
            yield return (min + max) / 2.0;
            yield break;
        }

        double center = (min + max) / 2.0;
        yield return center;
        for (double offset = step; center - offset > min; offset += step)
        {
            yield return center - offset;
            yield return center + offset;
        }
        if (Math.Abs(center - min) > 1e-10)
            yield return min;
        if (Math.Abs(center - max) > 1e-10)
            yield return max;
    }

    private static bool RectangleInsidePolygon(List<Point> polygon, double x, double y, double width, double height)
    {
        Point[] corners =
        {
            new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)
        };
        if (corners.Any(corner => !PointInsidePolygon(polygon, corner)))
            return false;

        for (int i = 0; i < polygon.Count; i++)
        {
            Point a = polygon[i];
            Point b = polygon[(i + 1) % polygon.Count];
            for (int j = 0; j < corners.Length; j++)
            {
                Point c = corners[j];
                Point d = corners[(j + 1) % corners.Length];
                if (SegmentsIntersect(a, b, c, d))
                    return false;
            }
        }
        return true;
    }

    private static bool PointInsidePolygon(List<Point> polygon, Point point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point a = polygon[i];
            Point b = polygon[j];
            bool crosses = (a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X;
            if (crosses)
                inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsIntersect(Point a, Point b, Point c, Point d)
    {
        double abC = Cross(a, b, c);
        double abD = Cross(a, b, d);
        double cdA = Cross(c, d, a);
        double cdB = Cross(c, d, b);
        const double epsilon = 1e-10;
        if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon))
            && ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon)))
            return true;
        return Math.Abs(abC) <= epsilon && OnSegment(a, b, c)
            || Math.Abs(abD) <= epsilon && OnSegment(a, b, d)
            || Math.Abs(cdA) <= epsilon && OnSegment(c, d, a)
            || Math.Abs(cdB) <= epsilon && OnSegment(c, d, b);
    }

    private static bool OnSegment(Point a, Point b, Point p) =>
        p.X >= Math.Min(a.X, b.X) - 1e-10 && p.X <= Math.Max(a.X, b.X) + 1e-10
        && p.Y >= Math.Min(a.Y, b.Y) - 1e-10 && p.Y <= Math.Max(a.Y, b.Y) + 1e-10;

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double SignedArea(List<Point> polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point a = polygon[i];
            Point b = polygon[(i + 1) % polygon.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area / 2.0;
    }

    private static int Other(Edge edge, int node) => edge.A == node ? edge.B : edge.A;
    private static Point ToPoint(Vector3 point) => new(point.X, point.Y);
    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);
}














