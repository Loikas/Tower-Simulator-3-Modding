using System;
using System.Collections.Generic;

namespace TS3Modding.PilotReadiness
{
    internal struct RoutePoint
    {
        public double X, Y;
        public RoutePoint(double x, double y) { X = x; Y = y; }
        public static double Distance(RoutePoint a, RoutePoint b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
        public static RoutePoint Lerp(RoutePoint a, RoutePoint b, double t) => new RoutePoint(a.X+(b.X-a.X)*t, a.Y+(b.Y-a.Y)*t);
    }
    internal static class RouteDashGeometry
    {
        public static bool Project(IReadOnlyList<RoutePoint> route, RoutePoint target, double tolerance, out double along)
        {
            along = 0; double best = tolerance, travelled = 0; bool found = false;
            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i-1]; var b = route[i]; var length = RoutePoint.Distance(a,b);
                if (length < 0.000001) continue;
                var t = Math.Max(0,Math.Min(1,((target.X-a.X)*(b.X-a.X)+(target.Y-a.Y)*(b.Y-a.Y))/(length*length)));
                var distance = RoutePoint.Distance(target,RoutePoint.Lerp(a,b,t));
                if (distance <= best && (!found || distance < best - 0.001)) { best = distance; along = travelled+length*t; found = true; }
                travelled += length;
            }
            return found;
        }
        public static double ScreenSplit(IReadOnlyList<RoutePoint> world, IReadOnlyList<RoutePoint> screen, double worldDistance)
        {
            double w = 0, s = 0;
            for(int i=1;i<world.Count;i++)
            {
                var wl=RoutePoint.Distance(world[i-1],world[i]); var sl=RoutePoint.Distance(screen[i-1],screen[i]);
                if(wl>0 && w+wl>=worldDistance) return s+sl*Math.Max(0,(worldDistance-w)/wl);
                w+=wl; s+=sl;
            }
            return s;
        }
        // Independent endpoint pairs, matching the native UILineRenderer line-list mode.
        public static List<RoutePoint> Build(IReadOnlyList<RoutePoint> points, double split, double dash=8, double gap=6)
        {
            var result=new List<RoutePoint>(); double travelled=0;
            if(double.IsNaN(split) || double.IsInfinity(split) || split<0 || double.IsNaN(dash) || double.IsInfinity(dash) || dash<=0 ||
                double.IsNaN(gap) || double.IsInfinity(gap) || gap<=0) return null;
            var period=dash+gap;
            for(int i=1;i<points.Count;i++)
            {
                var a=points[i-1]; var b=points[i]; var length=RoutePoint.Distance(a,b);
                if(double.IsNaN(length)||double.IsInfinity(length)) return null;
                double at=0;
                while(at<length-0.000001)
                {
                    var absolute=travelled+at;
                    bool solid=absolute<split;
                    var phase=solid?0:(absolute-split)%period;
                    bool draw=solid||phase<dash;
                    var step=Math.Min(length-at,solid?split-absolute:(draw?dash-phase:period-phase));
                    if(step<0.000001) step=Math.Min(0.000001,length-at);
                    if(draw) { result.Add(RoutePoint.Lerp(a,b,at/length)); result.Add(RoutePoint.Lerp(a,b,(at+step)/length)); }
                    if(result.Count>8192) return null;
                    at+=step;
                }
                travelled+=length;
            }
            return result;
        }
    }
}

