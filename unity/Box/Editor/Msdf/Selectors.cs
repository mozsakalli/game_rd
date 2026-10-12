using System;

namespace Digitoy.Box.Editor.Msdf
{
    // edge-selectors.cpp birebir.
    public struct EdgeCache
    {
        public Vector2 point;
        public double absDistance;
        public double aDomainDistance, bDomainDistance;
        public double aPerpendicularDistance, bPerpendicularDistance;
    }

    public struct MultiDistance { public double r, g, b; }
    public struct MultiAndTrueDistance { public double r, g, b, a; }

    public struct PerpendicularDistanceSelectorBase
    {
        public const double DistanceDeltaFactor = 1.001;

        SignedDistance minTrueDistance;
        double minNegativePerpendicularDistance;
        double minPositivePerpendicularDistance;
        EdgeSegment nearEdge;
        double nearEdgeParam;

        // C++ default ctor
        public static PerpendicularDistanceSelectorBase Default()
        {
            var s = new PerpendicularDistanceSelectorBase
            {
                minTrueDistance = SignedDistance.Infinite,
            };
            s.minNegativePerpendicularDistance = -Math.Abs(s.minTrueDistance.distance);
            s.minPositivePerpendicularDistance = Math.Abs(s.minTrueDistance.distance);
            s.nearEdge = null;
            s.nearEdgeParam = 0;
            return s;
        }

        public static bool GetPerpendicularDistance(ref double distance, Vector2 ep, Vector2 edgeDir)
        {
            double ts = Vector2.Dot(ep, edgeDir);
            if (ts > 0)
            {
                double perpendicularDistance = Vector2.Cross(ep, edgeDir);
                if (Math.Abs(perpendicularDistance) < Math.Abs(distance))
                {
                    distance = perpendicularDistance;
                    return true;
                }
            }
            return false;
        }

        public void Reset(double delta)
        {
            minTrueDistance.distance += Arith.NonZeroSign(minTrueDistance.distance) * delta;
            minNegativePerpendicularDistance = -Math.Abs(minTrueDistance.distance);
            minPositivePerpendicularDistance = Math.Abs(minTrueDistance.distance);
            nearEdge = null;
            nearEdgeParam = 0;
        }

        public bool IsEdgeRelevant(in EdgeCache cache, Vector2 p)
        {
            double delta = DistanceDeltaFactor * (p - cache.point).Length();
            return (
                cache.absDistance - delta <= Math.Abs(minTrueDistance.distance) ||
                Math.Abs(cache.aDomainDistance) < delta ||
                Math.Abs(cache.bDomainDistance) < delta ||
                (cache.aDomainDistance > 0 && (cache.aPerpendicularDistance < 0 ?
                    cache.aPerpendicularDistance + delta >= minNegativePerpendicularDistance :
                    cache.aPerpendicularDistance - delta <= minPositivePerpendicularDistance
                )) ||
                (cache.bDomainDistance > 0 && (cache.bPerpendicularDistance < 0 ?
                    cache.bPerpendicularDistance + delta >= minNegativePerpendicularDistance :
                    cache.bPerpendicularDistance - delta <= minPositivePerpendicularDistance
                ))
            );
        }

        public void AddEdgeTrueDistance(EdgeSegment edge, SignedDistance distance, double param)
        {
            if (distance < minTrueDistance)
            {
                minTrueDistance = distance;
                nearEdge = edge;
                nearEdgeParam = param;
            }
        }

        public void AddEdgePerpendicularDistance(double distance)
        {
            if (distance <= 0 && distance > minNegativePerpendicularDistance)
                minNegativePerpendicularDistance = distance;
            if (distance >= 0 && distance < minPositivePerpendicularDistance)
                minPositivePerpendicularDistance = distance;
        }

        public void Merge(in PerpendicularDistanceSelectorBase other)
        {
            if (other.minTrueDistance < minTrueDistance)
            {
                minTrueDistance = other.minTrueDistance;
                nearEdge = other.nearEdge;
                nearEdgeParam = other.nearEdgeParam;
            }
            if (other.minNegativePerpendicularDistance > minNegativePerpendicularDistance)
                minNegativePerpendicularDistance = other.minNegativePerpendicularDistance;
            if (other.minPositivePerpendicularDistance < minPositivePerpendicularDistance)
                minPositivePerpendicularDistance = other.minPositivePerpendicularDistance;
        }

        public double ComputeDistance(Vector2 p)
        {
            double minDistance = minTrueDistance.distance < 0 ? minNegativePerpendicularDistance : minPositivePerpendicularDistance;
            if (nearEdge != null)
            {
                SignedDistance distance = minTrueDistance;
                nearEdge.DistanceToPerpendicularDistance(ref distance, p, nearEdgeParam);
                if (Math.Abs(distance.distance) < Math.Abs(minDistance))
                    minDistance = distance.distance;
            }
            return minDistance;
        }

        public SignedDistance TrueDistance => minTrueDistance;
    }

    // Secici arayuzu: combiner iki secici tipiyle (MultiAndTrue: uretim, Perpendicular: EC mesafe kontrolu) calisir.
    public interface IEdgeSelector<TSelf, TDistance> where TSelf : class, IEdgeSelector<TSelf, TDistance>, new()
    {
        void Reset(Vector2 p);
        void AddEdge(ref EdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge);
        void Merge(TSelf other);
        TDistance Distance();
        void InitDefault(); // C++ default ctor esdegeri (gecici secicilerin yeniden kullanimi icin)
    }

    public sealed class PerpendicularDistanceSelector : IEdgeSelector<PerpendicularDistanceSelector, double>
    {
        PerpendicularDistanceSelectorBase b;
        Vector2 p;

        public PerpendicularDistanceSelector() { InitDefault(); }

        public void InitDefault()
        {
            b = PerpendicularDistanceSelectorBase.Default();
            p = default;
        }

        public void Reset(Vector2 p)
        {
            double delta = PerpendicularDistanceSelectorBase.DistanceDeltaFactor * (p - this.p).Length();
            b.Reset(delta);
            this.p = p;
        }

        public void AddEdge(ref EdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge)
        {
            if (b.IsEdgeRelevant(cache, p))
            {
                SignedDistance distance = edge.SignedDistance(p, out double param);
                b.AddEdgeTrueDistance(edge, distance, param);
                cache.point = p;
                cache.absDistance = Math.Abs(distance.distance);

                Vector2 ap = p - edge.Point(0);
                Vector2 bp = p - edge.Point(1);
                Vector2 aDir = edge.Direction(0).Normalize(true);
                Vector2 bDir = edge.Direction(1).Normalize(true);
                Vector2 prevDir = prevEdge.Direction(1).Normalize(true);
                Vector2 nextDir = nextEdge.Direction(0).Normalize(true);
                double add = Vector2.Dot(ap, (prevDir + aDir).Normalize(true));
                double bdd = -Vector2.Dot(bp, (bDir + nextDir).Normalize(true));
                if (add > 0)
                {
                    double pd = distance.distance;
                    if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, ap, -aDir))
                        b.AddEdgePerpendicularDistance(pd = -pd);
                    cache.aPerpendicularDistance = pd;
                }
                if (bdd > 0)
                {
                    double pd = distance.distance;
                    if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, bp, bDir))
                        b.AddEdgePerpendicularDistance(pd);
                    cache.bPerpendicularDistance = pd;
                }
                cache.aDomainDistance = add;
                cache.bDomainDistance = bdd;
            }
        }

        public void Merge(PerpendicularDistanceSelector other) => b.Merge(other.b);

        public double Distance() => b.ComputeDistance(p);
    }

    public sealed class MultiAndTrueDistanceSelector : IEdgeSelector<MultiAndTrueDistanceSelector, MultiAndTrueDistance>
    {
        Vector2 p;
        PerpendicularDistanceSelectorBase r, g, bl;

        public MultiAndTrueDistanceSelector() { InitDefault(); }

        public void InitDefault()
        {
            p = default;
            r = PerpendicularDistanceSelectorBase.Default();
            g = PerpendicularDistanceSelectorBase.Default();
            bl = PerpendicularDistanceSelectorBase.Default();
        }

        public void Reset(Vector2 p)
        {
            double delta = PerpendicularDistanceSelectorBase.DistanceDeltaFactor * (p - this.p).Length();
            r.Reset(delta);
            g.Reset(delta);
            bl.Reset(delta);
            this.p = p;
        }

        public void AddEdge(ref EdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge)
        {
            int c = (int)edge.color;
            if (
                ((c & 1) != 0 && r.IsEdgeRelevant(cache, p)) ||
                ((c & 2) != 0 && g.IsEdgeRelevant(cache, p)) ||
                ((c & 4) != 0 && bl.IsEdgeRelevant(cache, p))
            )
            {
                SignedDistance distance = edge.SignedDistance(p, out double param);
                if ((c & 1) != 0) r.AddEdgeTrueDistance(edge, distance, param);
                if ((c & 2) != 0) g.AddEdgeTrueDistance(edge, distance, param);
                if ((c & 4) != 0) bl.AddEdgeTrueDistance(edge, distance, param);
                cache.point = p;
                cache.absDistance = Math.Abs(distance.distance);

                Vector2 ap = p - edge.Point(0);
                Vector2 bp = p - edge.Point(1);
                Vector2 aDir = edge.Direction(0).Normalize(true);
                Vector2 bDir = edge.Direction(1).Normalize(true);
                Vector2 prevDir = prevEdge.Direction(1).Normalize(true);
                Vector2 nextDir = nextEdge.Direction(0).Normalize(true);
                double add = Vector2.Dot(ap, (prevDir + aDir).Normalize(true));
                double bdd = -Vector2.Dot(bp, (bDir + nextDir).Normalize(true));
                if (add > 0)
                {
                    double pd = distance.distance;
                    if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, ap, -aDir))
                    {
                        pd = -pd;
                        if ((c & 1) != 0) r.AddEdgePerpendicularDistance(pd);
                        if ((c & 2) != 0) g.AddEdgePerpendicularDistance(pd);
                        if ((c & 4) != 0) bl.AddEdgePerpendicularDistance(pd);
                    }
                    cache.aPerpendicularDistance = pd;
                }
                if (bdd > 0)
                {
                    double pd = distance.distance;
                    if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, bp, bDir))
                    {
                        if ((c & 1) != 0) r.AddEdgePerpendicularDistance(pd);
                        if ((c & 2) != 0) g.AddEdgePerpendicularDistance(pd);
                        if ((c & 4) != 0) bl.AddEdgePerpendicularDistance(pd);
                    }
                    cache.bPerpendicularDistance = pd;
                }
                cache.aDomainDistance = add;
                cache.bDomainDistance = bdd;
            }
        }

        public void Merge(MultiAndTrueDistanceSelector other)
        {
            r.Merge(other.r);
            g.Merge(other.g);
            bl.Merge(other.bl);
        }

        SignedDistance TrueDistance()
        {
            SignedDistance distance = r.TrueDistance;
            if (g.TrueDistance < distance)
                distance = g.TrueDistance;
            if (bl.TrueDistance < distance)
                distance = bl.TrueDistance;
            return distance;
        }

        public MultiAndTrueDistance Distance()
        {
            MultiAndTrueDistance mtd;
            mtd.r = r.ComputeDistance(p);
            mtd.g = g.ComputeDistance(p);
            mtd.b = bl.ComputeDistance(p);
            mtd.a = TrueDistance().distance;
            return mtd;
        }
    }

    // contour-combiners.cpp: OverlappingContourCombiner (overlapSupport=true; shim bunu kullanir).
    public sealed class OverlappingContourCombiner<TSel, TDist> where TSel : class, IEdgeSelector<TSel, TDist>, new()
    {
        readonly int[] windings;
        readonly TSel[] edgeSelectors;
        readonly TSel shapeSel = new(), innerSel = new(), outerSel = new();
        readonly Func<TDist, double> resolve;
        Vector2 p;

        public OverlappingContourCombiner(Shape shape, Func<TDist, double> resolveDistance)
        {
            resolve = resolveDistance;
            windings = new int[shape.contours.Count];
            edgeSelectors = new TSel[shape.contours.Count];
            for (int i = 0; i < windings.Length; i++)
            {
                windings[i] = shape.contours[i].Winding();
                edgeSelectors[i] = new TSel();
            }
        }

        public void Reset(Vector2 p)
        {
            this.p = p;
            foreach (var s in edgeSelectors)
                s.Reset(p);
        }

        public TSel EdgeSelector(int i) => edgeSelectors[i];

        public TDist Distance()
        {
            int contourCount = edgeSelectors.Length;
            shapeSel.InitDefault(); innerSel.InitDefault(); outerSel.InitDefault();
            shapeSel.Reset(p);
            innerSel.Reset(p);
            outerSel.Reset(p);
            for (int i = 0; i < contourCount; ++i)
            {
                TDist edgeDistance = edgeSelectors[i].Distance();
                shapeSel.Merge(edgeSelectors[i]);
                if (windings[i] > 0 && resolve(edgeDistance) >= 0)
                    innerSel.Merge(edgeSelectors[i]);
                if (windings[i] < 0 && resolve(edgeDistance) <= 0)
                    outerSel.Merge(edgeSelectors[i]);
            }

            TDist shapeDistance = shapeSel.Distance();
            TDist innerDistance = innerSel.Distance();
            TDist outerDistance = outerSel.Distance();
            double innerScalarDistance = resolve(innerDistance);
            double outerScalarDistance = resolve(outerDistance);
            TDist distance;

            int winding = 0;
            if (innerScalarDistance >= 0 && Math.Abs(innerScalarDistance) <= Math.Abs(outerScalarDistance))
            {
                distance = innerDistance;
                winding = 1;
                for (int i = 0; i < contourCount; ++i)
                    if (windings[i] > 0)
                    {
                        TDist contourDistance = edgeSelectors[i].Distance();
                        if (Math.Abs(resolve(contourDistance)) < Math.Abs(outerScalarDistance) && resolve(contourDistance) > resolve(distance))
                            distance = contourDistance;
                    }
            }
            else if (outerScalarDistance <= 0 && Math.Abs(outerScalarDistance) < Math.Abs(innerScalarDistance))
            {
                distance = outerDistance;
                winding = -1;
                for (int i = 0; i < contourCount; ++i)
                    if (windings[i] < 0)
                    {
                        TDist contourDistance = edgeSelectors[i].Distance();
                        if (Math.Abs(resolve(contourDistance)) < Math.Abs(innerScalarDistance) && resolve(contourDistance) < resolve(distance))
                            distance = contourDistance;
                    }
            }
            else
                return shapeDistance;

            for (int i = 0; i < contourCount; ++i)
                if (windings[i] != winding)
                {
                    TDist contourDistance = edgeSelectors[i].Distance();
                    if (resolve(contourDistance) * resolve(distance) >= 0 && Math.Abs(resolve(contourDistance)) < Math.Abs(resolve(distance)))
                        distance = contourDistance;
                }
            if (resolve(distance) == resolve(shapeDistance))
                distance = shapeDistance;
            return distance;
        }
    }

    // ShapeDistanceFinder.hpp
    public sealed class ShapeDistanceFinder<TSel, TDist> where TSel : class, IEdgeSelector<TSel, TDist>, new()
    {
        readonly Shape shape;
        readonly OverlappingContourCombiner<TSel, TDist> combiner;
        readonly EdgeCache[] shapeEdgeCache;

        public ShapeDistanceFinder(Shape shape, Func<TDist, double> resolve)
        {
            this.shape = shape;
            combiner = new OverlappingContourCombiner<TSel, TDist>(shape, resolve);
            shapeEdgeCache = new EdgeCache[shape.EdgeCount()];
        }

        public TDist Distance(Vector2 origin)
        {
            combiner.Reset(origin);
            int cacheIdx = 0;
            for (int ci = 0; ci < shape.contours.Count; ci++)
            {
                var edges = shape.contours[ci].edges;
                if (edges.Count == 0)
                    continue;
                var sel = combiner.EdgeSelector(ci);
                EdgeSegment prevEdge = edges.Count >= 2 ? edges[^2] : edges[0];
                EdgeSegment curEdge = edges[^1];
                for (int e = 0; e < edges.Count; e++)
                {
                    EdgeSegment nextEdge = edges[e];
                    sel.AddEdge(ref shapeEdgeCache[cacheIdx++], prevEdge, curEdge, nextEdge);
                    prevEdge = curEdge;
                    curEdge = nextEdge;
                }
            }
            return combiner.Distance();
        }
    }

    public static class Generator
    {
        public static double ResolveMulti(MultiAndTrueDistance d) => Arith.Median(d.r, d.g, d.b);
        public static double ResolveScalar(double d) => d;

        // generateMTSDF (OverlappingContourCombiner<MultiAndTrueDistanceSelector>) + msdfErrorCorrection.
        // Cikti: float[w*h*4], satir 0 ALTTA (msdfgen y-up; inverseYAxis=false).
        public static float[] GenerateMtsdf(Shape shape, int w, int h, in SdfTransformation xf)
        {
            var output = new float[w * h * 4];
            var finder = new ShapeDistanceFinder<MultiAndTrueDistanceSelector, MultiAndTrueDistance>(shape, ResolveMulti);
            int xDirection = 1;
            for (int y = 0; y < h; ++y)
            {
                int x = xDirection < 0 ? w - 1 : 0;
                for (int col = 0; col < w; ++col)
                {
                    Vector2 p = xf.projection.Unproject(new Vector2(x + .5, y + .5));
                    MultiAndTrueDistance d = finder.Distance(p);
                    int o = (y * w + x) * 4;
                    output[o + 0] = (float)xf.distanceMapping.Map(d.r);
                    output[o + 1] = (float)xf.distanceMapping.Map(d.g);
                    output[o + 2] = (float)xf.distanceMapping.Map(d.b);
                    output[o + 3] = (float)xf.distanceMapping.Map(d.a);
                    x += xDirection;
                }
                xDirection = -xDirection;
            }
            ErrorCorrection.Apply(output, w, h, 4, shape, xf);
            return output;
        }
    }
}
